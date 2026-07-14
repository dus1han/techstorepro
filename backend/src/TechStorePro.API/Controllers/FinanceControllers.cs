using TechStorePro.Application.Finance.Accounts;
using TechStorePro.Application.Finance.Expenses;
using TechStorePro.Application.Finance.Queries;
using TechStorePro.Domain.Finance;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

/// <summary>
/// Cash and bank accounts (requirements §33).
///
/// Note what is absent: there is no endpoint that sets a balance. There cannot be — an account holds no
/// balance to set (see <c>FinancialAccount</c>). Money arrives through a payment, an expense or a transfer,
/// and every one of those writes a movement through <c>IAccountLedger</c>. An "adjust the balance" endpoint
/// would be a licence to conjure cash out of nothing, and the statement would show it as nobody's decision.
/// </summary>
[Route("api/v1/finance/accounts")]
public class FinancialAccountsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<AccountDto>>> List(
        [FromQuery] bool includeInactive = false) =>
        Ok(await Mediator.Send(new GetAccountsQuery(includeInactive)));

    /// <summary>One account's history: what it held, what moved it, what it holds.</summary>
    [HttpGet("{id:guid}/statement")]
    public async Task<ActionResult<AccountStatementDto>> Statement(
        Guid id,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null) =>
        Ok(await Mediator.Send(new GetAccountStatementQuery(id, from, to)));

    [HttpPost]
    public async Task<ActionResult<Guid>> Open(OpenAccountCommand command) =>
        Ok(await Mediator.Send(command));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateAccountCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest();
        }

        await Mediator.Send(command);
        return NoContent();
    }

    /// <summary>Bank the till; take a float out to the second shop. Two movements, never one.</summary>
    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer(TransferBetweenAccountsCommand command)
    {
        await Mediator.Send(command);
        return NoContent();
    }
}

/// <summary>What the shop spends (requirements §34).</summary>
[Route("api/v1/finance/expenses")]
public class ExpensesController : ApiControllerBase
{
    // The query parameters are named for the fields they filter on — expenseCategoryId, not categoryId —
    // because a client reading ExpenseDto should not have to guess that the two are the same thing.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<ExpenseDto>>> List(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] Guid? expenseCategoryId = null,
        [FromQuery] Guid? branchId = null,
        [FromQuery] Guid? financialAccountId = null,
        [FromQuery] ExpenseStatus? status = null) =>
        Ok(await Mediator.Send(
            new GetExpensesQuery(from, to, expenseCategoryId, branchId, financialAccountId, status)));

    /// <summary>Expenses by category over a period — and the figure the computed P&amp;L will subtract.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ExpenseSummaryDto>> Summary(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] Guid? branchId = null) =>
        Ok(await Mediator.Send(new GetExpenseSummaryQuery(from, to, branchId)));

    /// <summary>Record it and pay it — one act, one transaction. The money leaves the named account.</summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> Record(RecordExpenseCommand command) =>
        Ok(await Mediator.Send(command));

    /// <summary>
    /// Cancel it. Not a delete and not an edit: the money comes back as a movement of its own, and the
    /// mistake stays on the statement next to its reversal.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelExpenseCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest();
        }

        await Mediator.Send(command);
        return NoContent();
    }
}

[Route("api/v1/finance/expense-categories")]
public class ExpenseCategoriesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<ExpenseCategoryDto>>> List() =>
        Ok(await Mediator.Send(new GetExpenseCategoriesQuery()));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateExpenseCategoryCommand command) =>
        Ok(await Mediator.Send(command));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateExpenseCategoryCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest();
        }

        await Mediator.Send(command);
        return NoContent();
    }
}
