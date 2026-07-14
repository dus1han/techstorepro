using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Finance.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Finance;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Finance.Expenses;

// ================================================================================================
// Categories (§34)
// ================================================================================================

public record ExpenseCategoryDto(Guid Id, string Name, string? Description, bool IsActive, int ExpenseCount);

[RequiresPermission(FeatureCatalog.ExpenseCategories, PermissionAction.View)]
public record GetExpenseCategoriesQuery : IRequest<IReadOnlyCollection<ExpenseCategoryDto>>;

public class GetExpenseCategoriesQueryHandler
    : IRequestHandler<GetExpenseCategoriesQuery, IReadOnlyCollection<ExpenseCategoryDto>>
{
    private readonly IApplicationDbContext _db;

    public GetExpenseCategoriesQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<ExpenseCategoryDto>> Handle(
        GetExpenseCategoriesQuery request,
        CancellationToken cancellationToken) =>
        await _db.ExpenseCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ExpenseCategoryDto(
                c.Id,
                c.Name,
                c.Description,
                c.IsActive,
                _db.Expenses.Count(e => e.ExpenseCategoryId == c.Id)))
            .ToListAsync(cancellationToken);
}

[RequiresPermission(FeatureCatalog.ExpenseCategories, PermissionAction.Create)]
public record CreateExpenseCategoryCommand(string Name, string? Description = null) : IRequest<Guid>;

public class CreateExpenseCategoryCommandValidator : AbstractValidator<CreateExpenseCategoryCommand>
{
    public CreateExpenseCategoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class CreateExpenseCategoryCommandHandler : IRequestHandler<CreateExpenseCategoryCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CreateExpenseCategoryCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> Handle(CreateExpenseCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = new ExpenseCategory { Name = request.Name, Description = request.Description };

        category.Validate();

        _db.ExpenseCategories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);

        return category.Id;
    }
}

[RequiresPermission(FeatureCatalog.ExpenseCategories, PermissionAction.Edit)]
public record UpdateExpenseCategoryCommand(
    Guid Id,
    string Name,
    string? Description = null,
    bool IsActive = true) : IRequest;

public class UpdateExpenseCategoryCommandValidator : AbstractValidator<UpdateExpenseCategoryCommand>
{
    public UpdateExpenseCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class UpdateExpenseCategoryCommandHandler : IRequestHandler<UpdateExpenseCategoryCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdateExpenseCategoryCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateExpenseCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _db.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Expense category", request.Id);

        category.Name = request.Name;
        category.Description = request.Description;
        category.IsActive = request.IsActive;

        category.Validate();

        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ================================================================================================
// Recording what the shop spent (§34)
// ================================================================================================

/// <summary>
/// Record money the shop has spent that bought no stock — the rent, the courier, the clearing agent.
///
/// It is <b>recorded and paid in one act</b>: this writes the expense and takes the money out of
/// <see cref="FinancialAccountId"/>, in one transaction. There is no draft, because there is no general
/// ledger to accrue into (§45 D3) and an expense that had not left an account would be a bill — and a bill
/// from somebody the shop knows is a <c>SupplierInvoice</c>, which already exists and already ages.
///
/// <b>The amount is in the account's currency.</b> Not the supplier's, not the base — see <see cref="Expense"/>.
/// </summary>
[RequiresPermission(FeatureCatalog.Expenses, PermissionAction.Create)]
public record RecordExpenseCommand(
    Guid ExpenseCategoryId,
    Guid BranchId,
    Guid FinancialAccountId,
    decimal Amount,
    string Description,
    Guid? SupplierId = null,
    DateTimeOffset? ExpenseDate = null,
    string? Reference = null,
    string? Notes = null) : IRequest<Guid>;

public class RecordExpenseCommandValidator : AbstractValidator<RecordExpenseCommand>
{
    public RecordExpenseCommandValidator()
    {
        RuleFor(x => x.ExpenseCategoryId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.FinancialAccountId).NotEmpty()
            .WithMessage("An expense must say which account the money came out of.");

        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class RecordExpenseCommandHandler : IRequestHandler<RecordExpenseCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountLedger _ledger;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public RecordExpenseCommandHandler(
        IApplicationDbContext db,
        IAccountLedger ledger,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _ledger = ledger;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(RecordExpenseCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var category = await _db.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == request.ExpenseCategoryId, cancellationToken)
            ?? throw new NotFoundException("Expense category", request.ExpenseCategoryId);

        if (!category.IsActive)
        {
            throw new DomainException($"'{category.Name}' has been retired and cannot take new expenses.");
        }

        var account = await _db.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == request.FinancialAccountId, cancellationToken)
            ?? throw new NotFoundException("Account", request.FinancialAccountId);

        if (request.SupplierId is { } supplierId
            && !await _db.Suppliers.AnyAsync(s => s.Id == supplierId, cancellationToken))
        {
            throw new NotFoundException("Supplier", supplierId);
        }

        var expenseDate = request.ExpenseDate ?? _clock.UtcNow;

        var expense = new Expense
        {
            Number = await _numbers.NextAsync(DocumentType.Expense, request.BranchId, cancellationToken),
            ExpenseCategoryId = category.Id,
            BranchId = request.BranchId,
            FinancialAccountId = account.Id,
            SupplierId = request.SupplierId,
            Description = request.Description,
            Amount = request.Amount,
            CurrencyCode = account.CurrencyCode,
            ExpenseDate = expenseDate,
            Reference = request.Reference,
            Notes = request.Notes
        };

        expense.Validate();

        _db.Expenses.Add(expense);

        // The money leaves. Negative, because it is going out — and through the ledger, which is what
        // refuses to let a till pay out more than it holds.
        var movement = await _ledger.PostAsync(
            new AccountPosting(
                account.Id,
                -expense.Amount,
                AccountTransactionSource.Expense,
                $"{category.Name} — {expense.Description}",
                BranchId: request.BranchId,
                SourceId: expense.Id,
                SourceNumber: expense.Number,
                Reference: request.Reference,
                OccurredAt: expenseDate),
            cancellationToken);

        // The rate the ledger resolved for the account, carried onto the document so the P&L can total an
        // expense paid out of a dollar account without re-reading a rate that may have moved since.
        expense.ExchangeRate = movement.ExchangeRate;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return expense.Id;
    }
}

/// <summary>
/// Undo an expense that should not have been recorded.
///
/// It is a cancellation, not a delete and not an edit: the money comes back into the account it left, as
/// a movement of its own, and both rows stay on the statement. A paid expense whose amount could be edited
/// in place would silently restate a bank balance that has already been reconciled — and nothing would
/// record that it had happened.
/// </summary>
[RequiresPermission(FeatureCatalog.Expenses, PermissionAction.Delete)]
public record CancelExpenseCommand(Guid Id, string Reason) : IRequest;

public class CancelExpenseCommandValidator : AbstractValidator<CancelExpenseCommand>
{
    public CancelExpenseCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500)
            .WithMessage("Cancelling an expense needs a reason — it is money.");
    }
}

public class CancelExpenseCommandHandler : IRequestHandler<CancelExpenseCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountLedger _ledger;
    private readonly IDateTime _clock;

    public CancelExpenseCommandHandler(IApplicationDbContext db, IAccountLedger ledger, IDateTime clock)
    {
        _db = db;
        _ledger = ledger;
        _clock = clock;
    }

    public async Task Handle(CancelExpenseCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var expense = await _db.Expenses
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Expense", request.Id);

        var at = _clock.UtcNow;

        expense.Cancel(request.Reason, at);

        await _ledger.PostAsync(
            new AccountPosting(
                expense.FinancialAccountId,
                expense.Amount,   // positive: the money comes back
                AccountTransactionSource.ExpenseCancellation,
                $"Cancelled {expense.Number} — {request.Reason}",
                BranchId: expense.BranchId,
                SourceId: expense.Id,
                SourceNumber: expense.Number,
                OccurredAt: at),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
