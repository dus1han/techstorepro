using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Infrastructure.Configuration;

/// <summary>
/// Issues gapless per-branch document numbers (requirements §5, §11).
///
/// The sequence row is locked with <c>SELECT … FOR UPDATE</c> and stays locked until the caller's
/// transaction commits. That is what makes the guarantee real in both directions:
///
/// <list type="bullet">
/// <item>two clerks invoicing at the same instant serialise, so nobody gets a duplicate number;</item>
/// <item>a transaction that rolls back gives its number back, so nobody gets a gap.</item>
/// </list>
///
/// Issuing the number outside the document's transaction would satisfy neither, and "why does the
/// invoice book jump from 41 to 43?" is a question an auditor genuinely asks.
/// </summary>
public class DocumentNumberGenerator : IDocumentNumberGenerator
{
    private readonly ApplicationDbContextAccessor _accessor;
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDateTime _clock;

    public DocumentNumberGenerator(
        ApplicationDbContextAccessor accessor,
        IApplicationDbContext db,
        ITenantContext tenant,
        IDateTime clock)
    {
        _accessor = accessor;
        _db = db;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<string> NextAsync(
        DocumentType documentType,
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        var companyId = _tenant.CompanyId
            ?? throw new DomainException("A document number cannot be issued without a company.");

        var context = _accessor.Context;

        if (context.Database.CurrentTransaction is null)
        {
            // Without an ambient transaction the FOR UPDATE lock would be released the moment this
            // statement finished, and two concurrent callers could both read the same NextNumber.
            // Failing loudly beats issuing duplicate invoice numbers and discovering it at year end.
            throw new DomainException(
                "A document number must be issued inside a transaction. "
                + "Call IApplicationDbContext.BeginTransactionAsync first.");
        }

        var year = _clock.UtcNow.Year;

        // Locked for the rest of the caller's transaction. Any other session asking for the same
        // sequence waits here.
        var sequence = await context.DocumentNumberSequences
            .FromSqlRaw(
                """
                SELECT * FROM techstorepro.document_number_sequences
                WHERE company_id = {0}
                  AND branch_id = {1}
                  AND document_type = {2}
                  AND year = {3}
                  AND is_deleted = false
                FOR UPDATE
                """,
                companyId, branchId, (short)documentType, year)
            .FirstOrDefaultAsync(cancellationToken);

        // A sequence for a year that has not been seeded yet — the company rolled into January, or
        // this document type was added after the company registered.
        sequence ??= await CreateSequenceAsync(companyId, branchId, documentType, year, cancellationToken);

        var number = sequence.Take();

        await _db.SaveChangesAsync(cancellationToken);

        return number;
    }

    private async Task<DocumentNumberSequence> CreateSequenceAsync(
        Guid companyId,
        Guid branchId,
        DocumentType documentType,
        int year,
        CancellationToken cancellationToken)
    {
        // Carry the prefix and padding forward from the previous year's sequence if there is one, so
        // that a company's numbering conventions survive the new year rather than silently resetting
        // to the defaults.
        var previous = await _db.DocumentNumberSequences
            .Where(s => s.BranchId == branchId && s.DocumentType == documentType)
            .OrderByDescending(s => s.Year)
            .FirstOrDefaultAsync(cancellationToken);

        var sequence = new DocumentNumberSequence
        {
            CompanyId = companyId,
            BranchId = branchId,
            DocumentType = documentType,
            Year = year,
            Prefix = previous?.Prefix ?? documentType.ToString()[..3].ToUpperInvariant(),
            Padding = previous?.Padding ?? 5,
            ResetsAnnually = previous?.ResetsAnnually ?? true,
            NextNumber = 1
        };

        _db.DocumentNumberSequences.Add(sequence);

        return sequence;
    }
}

/// <summary>
/// Hands the concrete DbContext to the few Infrastructure services that need raw SQL — the
/// <c>FOR UPDATE</c> lock above cannot be expressed through <see cref="IApplicationDbContext"/>,
/// and widening that interface with an ExecuteSql method would hand every feature handler a way
/// around the tenant filter.
/// </summary>
public class ApplicationDbContextAccessor
{
    public ApplicationDbContextAccessor(Persistence.ApplicationDbContext context)
    {
        Context = context;
    }

    public Persistence.ApplicationDbContext Context { get; }
}
