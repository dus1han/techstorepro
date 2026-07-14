using TechStorePro.Application.Reports.Queries;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

/// <summary>
/// The finance reports (§33, §35). Read-only, every one of them: they explain documents that already
/// exist and write nothing back. The permission is demanded on the query itself, as everywhere else.
/// </summary>
[Route("api/v1/reports")]
public class ReportsController : ApiControllerBase
{
    /// <summary>What every customer owes, aged by how far past due it is.</summary>
    [HttpGet("receivables-ageing")]
    public async Task<ActionResult<ReceivablesAgeingDto>> ReceivablesAgeing(
        [FromQuery] DateTimeOffset? asOf = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] Guid? branchId = null) =>
        Ok(await Mediator.Send(new GetReceivablesAgeingQuery(asOf, customerId, branchId)));

    /// <summary>What the shop owes every supplier, aged, and valued at the rate each invoice was booked at.</summary>
    [HttpGet("payables-ageing")]
    public async Task<ActionResult<PayablesAgeingDto>> PayablesAgeing(
        [FromQuery] DateTimeOffset? asOf = null,
        [FromQuery] Guid? supplierId = null) =>
        Ok(await Mediator.Send(new GetPayablesAgeingQuery(asOf, supplierId)));

    /// <summary>One customer's account: what they owed, what moved it, what they owe.</summary>
    [HttpGet("customer-statement/{customerId:guid}")]
    public async Task<ActionResult<CustomerStatementDto>> CustomerStatement(
        Guid customerId,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null) =>
        Ok(await Mediator.Send(new GetCustomerStatementQuery(customerId, from, to)));

    /// <summary>One supplier's account, in the shop's own money.</summary>
    [HttpGet("supplier-statement/{supplierId:guid}")]
    public async Task<ActionResult<SupplierStatementDto>> SupplierStatement(
        Guid supplierId,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null) =>
        Ok(await Mediator.Send(new GetSupplierStatementQuery(supplierId, from, to)));
}
