using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TechStorePro.Application.Catalog.Services;
using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Sales.Common;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Repairs;
using TechStorePro.Domain.Sales;

namespace TechStorePro.Application.Repairs.Invoicing;

/// <summary>
/// Bill the customer for the repair (requirements §28).
///
/// <b>It raises an ordinary <see cref="SalesInvoice"/>, not a document of its own</b> (§45 D11). A
/// `repair_invoices` table would need its own tax arithmetic, its own payment allocations, its own credit
/// notes and its own place in the receivables report — and every one of those already exists, is proven,
/// and is the thing P7 will report on. A repair bill is a bill. The customer does not care which department
/// raised it, and neither does their balance.
///
/// <b>The invoice moves no stock</b>, for the same reason a sales invoice does not: the goods already went.
/// The parts left the shelf when they were fitted (§45 D9), so the lines here carry the cost the ledger
/// reported <em>then</em> rather than asking it for a new one now — by which time the moving average will
/// have moved, and the margin on this job would be quietly restated.
/// </summary>
[RequiresPermission(FeatureCatalog.RepairTickets, PermissionAction.Create)]
[RequiresPermission(FeatureCatalog.SalesInvoices, PermissionAction.Create)]
public record BillRepairCommand(
    Guid RepairTicketId,
    DateTimeOffset? InvoicedAt = null,
    DateTimeOffset? DueAt = null,
    string? Notes = null) : IRequest<Guid>;

public class BillRepairCommandValidator : AbstractValidator<BillRepairCommand>
{
    public BillRepairCommandValidator()
    {
        RuleFor(x => x.RepairTicketId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class BillRepairCommandHandler : IRequestHandler<BillRepairCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ITaxResolver _taxes;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public BillRepairCommandHandler(
        IApplicationDbContext db,
        ITenantContext tenant,
        ITaxResolver taxes,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _tenant = tenant;
        _taxes = taxes;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(BillRepairCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var invoicedAt = request.InvoicedAt ?? _clock.UtcNow;

        var currency = await CompanyCurrency.ResolveAsync(_db, _tenant, cancellationToken);

        var ticket = await _db.RepairTickets
            .Include(t => t.Customer)
            .Include(t => t.Parts).ThenInclude(p => p.Product)
            .Include(t => t.Labour)
            .Include(t => t.Charges)
            .FirstOrDefaultAsync(t => t.Id == request.RepairTicketId, cancellationToken)
            ?? throw new NotFoundException("Repair ticket", request.RepairTicketId);

        if (ticket.Charges.Count > 0)
        {
            // Billing the same job twice would double the customer's debt and double the revenue, and the
            // second invoice would look exactly as legitimate as the first.
            throw new DomainException(
                $"Job {ticket.Number} has already been invoiced. Bill it again and the customer owes for "
                + "the same repair twice.");
        }

        if (ticket.Status == RepairTicketStatus.Cancelled)
        {
            throw new DomainException("That job was cancelled. There is no repair to bill for.");
        }

        var parts = ticket.Parts.Where(p => p is { IsChargeable: true, IsReturned: false }).ToList();
        var labour = ticket.Labour.Where(l => l.IsChargeable).ToList();

        if (parts.Count == 0 && labour.Count == 0)
        {
            // A wholly-warranty job, or one where the fault turned out to be nothing. There is genuinely
            // nothing to bill, and an invoice for zero is not a bill — it is a document that will sit in
            // the receivables report for ever being worth nothing.
            //
            // The job still has a cost (the parts left the shelf; the vendor charged for the board). That
            // cost is on the ticket, where the profitability report reads it — which is the whole of D10.
            throw new DomainException(
                ticket.IsWarranty
                    ? $"Job {ticket.Number} is under {ticket.WarrantyType} warranty, so there is nothing to "
                      + "bill the customer for. The parts and the vendor's charge are still costs, and they "
                      + "are on the job."
                    : $"Job {ticket.Number} has no chargeable parts or labour on it, so there is nothing to bill.");
        }

        var invoice = new SalesInvoice
        {
            Number = await _numbers.NextAsync(DocumentType.Invoice, ticket.BranchId, cancellationToken),
            CustomerId = ticket.CustomerId,
            BranchId = ticket.BranchId,

            // No delivery: this bill is for a repair, and the parts in it left the shelf against the job
            // sheet rather than against a delivery note. SalesInvoice.DeliveryId is nullable precisely for
            // a service invoice like this one.
            DeliveryId = null,
            SalesOrderId = null,

            Status = SalesInvoiceStatus.Draft,
            CurrencyCode = currency,
            InvoicedAt = invoicedAt,
            DueAt = request.DueAt
                ?? (ticket.Customer.PaymentTermDays > 0
                    ? invoicedAt.AddDays(ticket.Customer.PaymentTermDays)
                    : null),
            Notes = request.Notes ?? $"Repair job {ticket.Number}"
        };

        _db.SalesInvoices.Add(invoice);

        foreach (var part in parts)
        {
            var tax = await _taxes.ResolveAsync(part.ProductId, invoicedAt, cancellationToken);

            _db.SalesInvoiceLines.Add(new SalesInvoiceLine
            {
                SalesInvoiceId = invoice.Id,
                DeliveryLineId = null,
                ProductId = part.ProductId,
                Description = part.Product.Name,
                Quantity = part.Quantity,
                UnitPrice = part.UnitPrice,
                TaxPercent = tax.Percent,
                PriceSource = "Repair part",

                // The cost the ledger reported when the part was fitted. Not recomputed — see the class
                // comment, and SalesInvoiceLine.UnitCost, which says the same thing about a sale.
                UnitCost = part.UnitCost
            });
        }

        // Labour has no product, so it has no product tax rate. It takes the company's default — and it is
        // resolved rather than passed in, because a client that can choose the tax rate on a line is a
        // client that can choose zero.
        var labourTax = await _taxes.ResolveDefaultAsync(invoicedAt, cancellationToken);

        foreach (var hours in labour)
        {
            _db.SalesInvoiceLines.Add(new SalesInvoiceLine
            {
                SalesInvoiceId = invoice.Id,
                ProductId = null,
                Description = hours.Description,
                Quantity = hours.Hours,
                UnitPrice = hours.HourlyRate,
                TaxPercent = labourTax.Percent,
                PriceSource = "Repair labour",

                // No stock consumed, so no cost of goods. The technician's wage is a payroll expense (§34),
                // not a cost this job caused — see RepairLabour.
                UnitCost = 0m
            });
        }

        invoice.Post();

        // The debt, recorded. Posting the bill and recording what the customer owes are one act.
        ticket.Customer.Balance += invoice.Total;

        _db.RepairCharges.Add(new RepairCharge
        {
            RepairTicketId = ticket.Id,
            SalesInvoiceId = invoice.Id,
            ChargedAt = invoicedAt
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return invoice.Id;
    }
}
