using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Sales.Common;
using TechStorePro.Application.Sales.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Sales;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Quotations;

/// <param name="UnitPrice">
/// Null means "price it from the list". A figure means the salesperson typed one, and it is checked
/// against the floor exactly as a discount would be.
/// </param>
public record QuoteLine(
    Guid ProductId,
    decimal Quantity,
    decimal? UnitPrice = null,
    decimal DiscountPercent = 0m,
    decimal DiscountAmount = 0m,
    string? Description = null);

/// <summary>
/// Quote a customer a price (requirements §22).
///
/// <b>No stock is reserved.</b> See <see cref="Quotation"/> — an offer is not a claim on the shelf.
/// </summary>
[RequiresPermission(FeatureCatalog.Quotations, PermissionAction.Create)]
public record CreateQuotationCommand(
    Guid BranchId,
    IReadOnlyCollection<QuoteLine> Lines,
    Guid? CustomerId = null,
    DateTimeOffset? QuotedAt = null,
    DateTimeOffset? ValidUntil = null,
    string? CurrencyCode = null,
    string? Notes = null) : IRequest<Guid>;

public class CreateQuotationCommandValidator : AbstractValidator<CreateQuotationCommand>
{
    public CreateQuotationCommandValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("A quotation must quote at least one line.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0).When(l => l.UnitPrice is not null);
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0, 100);
            line.RuleFor(l => l.DiscountAmount).GreaterThanOrEqualTo(0);
        });
    }
}

public class CreateQuotationCommandHandler : IRequestHandler<CreateQuotationCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISalesLinePricer _pricer;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public CreateQuotationCommandHandler(
        IApplicationDbContext db,
        ITenantContext tenant,
        ISalesLinePricer pricer,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _tenant = tenant;
        _pricer = pricer;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreateQuotationCommand request, CancellationToken cancellationToken)
    {
        // The transaction is for the number, not for stock: NextAsync holds a row lock across
        // statements, and a rolled-back quotation must give its number back rather than burn it.
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var quotedAt = request.QuotedAt ?? _clock.UtcNow;

        var currency = await CompanyCurrency.EnsureAsync(_db, _tenant, request.CurrencyCode, cancellationToken);

        if (request.CustomerId is { } customerId
            && !await _db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken))
        {
            throw new NotFoundException("Customer", customerId);
        }

        var quotation = new Quotation
        {
            Number = await _numbers.NextAsync(DocumentType.Quotation, request.BranchId, cancellationToken),
            CustomerId = request.CustomerId,
            BranchId = request.BranchId,
            Status = QuotationStatus.Draft,
            CurrencyCode = currency,
            QuotedAt = quotedAt,
            ValidUntil = request.ValidUntil,
            Notes = request.Notes
        };

        _db.Quotations.Add(quotation);

        foreach (var line in request.Lines)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == line.ProductId, cancellationToken)
                ?? throw new NotFoundException("Product", line.ProductId);

            // Priced as of the quotation's own date, not today's: a quote backdated to last week must
            // carry last week's price list and last week's tax rate.
            var priced = await _pricer.PriceAsync(
                line.ProductId,
                request.CustomerId,
                line.Quantity,
                line.UnitPrice,
                line.DiscountPercent,
                line.DiscountAmount,
                quotedAt,
                cancellationToken);

            // Through the DbSet, and only the DbSet — EF's fixup adds it to quotation.Lines by itself,
            // and adding it there by hand as well would count every total twice (the P4 bug).
            _db.QuotationLines.Add(new QuotationLine
            {
                QuotationId = quotation.Id,
                ProductId = line.ProductId,
                Description = line.Description ?? product.Name,
                Quantity = line.Quantity,
                UnitPrice = priced.UnitPrice,
                DiscountPercent = priced.DiscountPercent,
                DiscountAmount = priced.DiscountAmount,
                TaxPercent = priced.TaxPercent,
                PriceSource = priced.PriceSource
            });
        }

        quotation.Validate();

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return quotation.Id;
    }
}
