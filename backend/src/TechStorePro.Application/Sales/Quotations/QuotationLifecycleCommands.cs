using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Sales;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Quotations;

/// <summary>Mark a quotation as sent to the customer.</summary>
[RequiresPermission(FeatureCatalog.Quotations, PermissionAction.Edit)]
public record SendQuotationCommand(Guid QuotationId) : IRequest<Unit>;

public class SendQuotationCommandHandler : IRequestHandler<SendQuotationCommand, Unit>
{
    private readonly IApplicationDbContext _db;

    public SendQuotationCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(SendQuotationCommand request, CancellationToken cancellationToken)
    {
        var quotation = await _db.Quotations
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == request.QuotationId, cancellationToken)
            ?? throw new NotFoundException("Quotation", request.QuotationId);

        quotation.Send();

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

/// <summary>The customer said yes. Still no stock: that happens when the order is confirmed.</summary>
[RequiresPermission(FeatureCatalog.Quotations, PermissionAction.Edit)]
public record AcceptQuotationCommand(Guid QuotationId) : IRequest<Unit>;

public class AcceptQuotationCommandHandler : IRequestHandler<AcceptQuotationCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public AcceptQuotationCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(AcceptQuotationCommand request, CancellationToken cancellationToken)
    {
        var quotation = await _db.Quotations
            .FirstOrDefaultAsync(q => q.Id == request.QuotationId, cancellationToken)
            ?? throw new NotFoundException("Quotation", request.QuotationId);

        quotation.Accept(_clock.UtcNow);

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

[RequiresPermission(FeatureCatalog.Quotations, PermissionAction.Edit)]
public record RejectQuotationCommand(Guid QuotationId, string? Reason = null) : IRequest<Unit>;

public class RejectQuotationCommandHandler : IRequestHandler<RejectQuotationCommand, Unit>
{
    private readonly IApplicationDbContext _db;

    public RejectQuotationCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(RejectQuotationCommand request, CancellationToken cancellationToken)
    {
        var quotation = await _db.Quotations
            .FirstOrDefaultAsync(q => q.Id == request.QuotationId, cancellationToken)
            ?? throw new NotFoundException("Quotation", request.QuotationId);

        quotation.Reject();

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            quotation.Notes = string.IsNullOrWhiteSpace(quotation.Notes)
                ? $"Rejected: {request.Reason}"
                : $"{quotation.Notes}\nRejected: {request.Reason}";
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

/// <summary>
/// Turn an accepted quotation into a sales order (requirements §22, "convert to invoice" — via the
/// order, because that is what reserves the stock).
///
/// The order is raised as a <b>draft</b>, and the lines are copied <em>as quoted</em>: the promised
/// price does not re-resolve against today's price list. That promise is the entire point of a
/// quotation, and re-pricing it here would make the document a suggestion.
/// </summary>
[RequiresPermission(FeatureCatalog.SalesOrders, PermissionAction.Create)]
public record ConvertQuotationCommand(
    Guid QuotationId,
    Guid WarehouseId,
    DateTimeOffset? ExpectedAt = null) : IRequest<Guid>;

public class ConvertQuotationCommandValidator : AbstractValidator<ConvertQuotationCommand>
{
    public ConvertQuotationCommandValidator()
    {
        RuleFor(x => x.QuotationId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
    }
}

public class ConvertQuotationCommandHandler : IRequestHandler<ConvertQuotationCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public ConvertQuotationCommandHandler(
        IApplicationDbContext db,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(ConvertQuotationCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var quotation = await _db.Quotations
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == request.QuotationId, cancellationToken)
            ?? throw new NotFoundException("Quotation", request.QuotationId);

        if (quotation.CustomerId is not { } customerId)
        {
            // A quotation may be raised for an enquiry with no customer on file. An order cannot: there
            // would be nobody to deliver to, nobody to bill, and no account to carry the balance.
            throw new DomainException(
                "This quotation has no customer. Add one before turning it into an order — an order "
                + "with nobody to bill is not an order.");
        }

        var order = new SalesOrder
        {
            Number = await _numbers.NextAsync(DocumentType.SalesOrder, quotation.BranchId, cancellationToken),
            CustomerId = customerId,
            BranchId = quotation.BranchId,
            WarehouseId = request.WarehouseId,
            QuotationId = quotation.Id,
            Status = SalesOrderStatus.Draft,
            CurrencyCode = quotation.CurrencyCode,
            OrderedAt = _clock.UtcNow,
            ExpectedAt = request.ExpectedAt,
            Notes = quotation.Notes
        };

        _db.SalesOrders.Add(order);

        foreach (var line in quotation.Lines)
        {
            _db.SalesOrderLines.Add(new SalesOrderLine
            {
                SalesOrderId = order.Id,
                ProductId = line.ProductId,
                Description = line.Description,
                Quantity = line.Quantity,
                DeliveredQuantity = 0m,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                DiscountAmount = line.DiscountAmount,
                TaxPercent = line.TaxPercent,
                PriceSource = line.PriceSource
            });
        }

        order.Validate();
        quotation.MarkConverted();

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return order.Id;
    }
}
