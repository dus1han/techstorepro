using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Payments;

public record PaymentMethodLineDto(
    Guid Id,
    Guid PaymentMethodId,
    string PaymentMethodName,
    decimal Amount,
    string? Reference);

public record PaymentAllocationDto(
    Guid Id,
    Guid SalesInvoiceId,
    string InvoiceNumber,
    decimal Amount);

public record CustomerPaymentDto(
    Guid Id,
    string Number,
    Guid CustomerId,
    string CustomerName,
    Guid BranchId,
    string CurrencyCode,
    DateTimeOffset PaidAt,
    string? Reference,
    decimal Amount,
    decimal AllocatedAmount,
    decimal UnallocatedAmount,
    string? Notes,
    IReadOnlyCollection<PaymentMethodLineDto> Methods,
    IReadOnlyCollection<PaymentAllocationDto> Allocations);

[RequiresPermission(FeatureCatalog.CustomerPayments, PermissionAction.View)]
public record GetPaymentsQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    Guid? CustomerId = null) : IRequest<PagedResult<CustomerPaymentDto>>;

public class GetPaymentsQueryHandler : IRequestHandler<GetPaymentsQuery, PagedResult<CustomerPaymentDto>>
{
    private readonly IApplicationDbContext _db;

    public GetPaymentsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<CustomerPaymentDto>> Handle(
        GetPaymentsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.CustomerPayments.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(p =>
                p.Number.ToLower().Contains(term)
                || p.Customer.Name.ToLower().Contains(term)
                || (p.Reference != null && p.Reference.ToLower().Contains(term)));
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(p => p.CustomerId == customerId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        // Materialised, then mapped — Amount and UnallocatedAmount are computed from the tender in C#,
        // so they do not exist as columns. See the note in SalesQueries.cs.
        var payments = await query
            .Include(p => p.Customer)
            .Include(p => p.Methods).ThenInclude(m => m.PaymentMethod)
            .Include(p => p.Allocations).ThenInclude(a => a.SalesInvoice)
            .OrderByDescending(p => p.PaidAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = payments
            .Select(p => new CustomerPaymentDto(
                p.Id,
                p.Number,
                p.CustomerId,
                p.Customer.Name,
                p.BranchId,
                p.CurrencyCode,
                p.PaidAt,
                p.Reference,
                p.Amount,
                p.AllocatedAmount,
                p.UnallocatedAmount,
                p.Notes,
                p.Methods.Select(m => new PaymentMethodLineDto(
                    m.Id,
                    m.PaymentMethodId,
                    m.PaymentMethod.Name,
                    m.Amount,
                    m.Reference)).ToList(),
                p.Allocations.Select(a => new PaymentAllocationDto(
                    a.Id,
                    a.SalesInvoiceId,
                    a.SalesInvoice.Number,
                    a.Amount)).ToList()))
            .ToList();

        return new PagedResult<CustomerPaymentDto>(items, total, page, pageSize);
    }
}
