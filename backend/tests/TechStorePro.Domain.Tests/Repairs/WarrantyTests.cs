using FluentAssertions;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Repairs;
using Xunit;

namespace TechStorePro.Domain.Tests.Repairs;

public class WarrantyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);

    private static Warranty Warranty() => new()
    {
        WarrantyType = RepairWarrantyType.Manufacturer,
        SourceType = WarrantySourceType.Serial,
        SerialNumber = "SN-A",
        StartsOn = new DateOnly(2026, 1, 1),
        EndsOn = new DateOnly(2027, 1, 1)
    };

    [Fact]
    public void Cover_is_inclusive_of_its_last_day()
    {
        var warranty = Warranty();

        warranty.CoversAt(Now).Should().BeTrue();

        // The last day is covered. Off by one here and a customer whose warranty "expires on 1 January" is
        // turned away on 1 January, which is the day they were told they were covered until.
        warranty.CoversAt(new DateTimeOffset(2027, 1, 1, 23, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        warranty.CoversAt(new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero)).Should().BeFalse();
        warranty.CoversAt(new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void A_warranty_cannot_end_before_it_starts()
    {
        var warranty = Warranty();
        warranty.EndsOn = new DateOnly(2025, 1, 1);

        warranty.Invoking(w => w.Validate())
            .Should().Throw<DomainException>().WithMessage("*cannot end before it starts*");
    }

    [Fact]
    public void A_warranty_that_covers_nobody_is_not_a_warranty()
    {
        var warranty = Warranty();
        warranty.WarrantyType = RepairWarrantyType.None;

        warranty.Invoking(w => w.Validate()).Should().Throw<DomainException>();
    }

    [Fact]
    public void Rejecting_a_claim_needs_a_reason()
    {
        var claim = new WarrantyClaim { Status = WarrantyClaimStatus.Open, ClaimedAt = Now };

        // The customer is about to be charged for a repair they believed was free. "Because we said so" is
        // how that becomes a dispute.
        claim.Invoking(c => c.Reject(Now, "  "))
            .Should().Throw<DomainException>().WithMessage("*needs a reason*");

        claim.Reject(Now, "Liquid damage — not covered.");

        claim.Status.Should().Be(WarrantyClaimStatus.Rejected);
        claim.Outcome.Should().Be("Liquid damage — not covered.");
        claim.ResolvedAt.Should().Be(Now);
    }

    [Fact]
    public void A_settled_claim_cannot_be_settled_twice()
    {
        var claim = new WarrantyClaim { Status = WarrantyClaimStatus.Open, ClaimedAt = Now };

        claim.Accept(Now, "Repaired under warranty.");

        claim.Invoking(c => c.Reject(Now, "Actually, no."))
            .Should().Throw<DomainException>().WithMessage("*already been settled*");
    }
}
