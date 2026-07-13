using TechStorePro.Domain.Configuration;

namespace TechStorePro.Application.Common.Interfaces;

/// <summary>
/// Issues gapless, per-branch document numbers (requirements §5, §11).
///
/// The caller must already be inside a transaction — the sequence row is locked with
/// <c>SELECT … FOR UPDATE</c> and held until that transaction commits, so a document that fails to
/// save hands its number back instead of burning it. Numbering that skips INV-2026-00042 is
/// something an auditor will ask about, and "the save failed" is not an answer they accept.
/// </summary>
public interface IDocumentNumberGenerator
{
    Task<string> NextAsync(
        DocumentType documentType,
        Guid branchId,
        CancellationToken cancellationToken = default);
}
