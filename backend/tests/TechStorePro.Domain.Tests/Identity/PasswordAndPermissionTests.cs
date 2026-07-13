using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace TechStorePro.Domain.Tests.Identity;

public class UserLockoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Failed_logins_below_the_threshold_do_not_lock_the_account()
    {
        var user = new User();

        user.RegisterFailedLogin(Now, maxAttempts: 5, lockoutFor: TimeSpan.FromMinutes(15));
        user.RegisterFailedLogin(Now, maxAttempts: 5, lockoutFor: TimeSpan.FromMinutes(15));

        user.IsLockedOut(Now).Should().BeFalse();
        user.FailedLoginCount.Should().Be(2);
    }

    [Fact]
    public void Reaching_the_threshold_locks_the_account_for_the_configured_period()
    {
        var user = new User();

        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(Now, maxAttempts: 5, lockoutFor: TimeSpan.FromMinutes(15));
        }

        user.IsLockedOut(Now).Should().BeTrue();
        user.IsLockedOut(Now.AddMinutes(14)).Should().BeTrue();

        // The lock expires on its own. An account that stays locked until an admin intervenes turns
        // a forgotten password into a support ticket.
        user.IsLockedOut(Now.AddMinutes(16)).Should().BeFalse();
    }

    [Fact]
    public void A_successful_login_clears_the_lock_and_the_counter()
    {
        var user = new User();
        user.RegisterFailedLogin(Now, maxAttempts: 2, lockoutFor: TimeSpan.FromMinutes(15));

        user.RegisterSuccessfulLogin(Now);

        user.FailedLoginCount.Should().Be(0);
        user.LockedUntil.Should().BeNull();
        user.LastLoginAt.Should().Be(Now);
    }

    [Fact]
    public void The_threshold_is_configurable_not_hardcoded()
    {
        // Requirements §11: a password policy is configuration. Proving the domain honours whatever
        // it is handed, rather than a constant of its own.
        var strict = new User();
        strict.RegisterFailedLogin(Now, maxAttempts: 1, lockoutFor: TimeSpan.FromHours(1));

        strict.IsLockedOut(Now).Should().BeTrue();
        strict.LockedUntil.Should().Be(Now.AddHours(1));
    }
}

public class WarehouseAccessTests
{
    private static Warehouse BranchOwned(Guid branchId) => new() { BranchId = branchId };

    private static Warehouse Shared(params BranchWarehouse[] access) =>
        new() { BranchId = null, AccessibleToBranches = access };

    [Fact]
    public void A_branch_owned_warehouse_is_usable_only_by_its_owner()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();

        var warehouse = BranchOwned(owner);

        warehouse.IsShared.Should().BeFalse();
        warehouse.IsAccessibleTo(owner, forIssue: true).Should().BeTrue();
        warehouse.IsAccessibleTo(owner, forIssue: false).Should().BeTrue();

        // The point of branch-owned stock: another branch cannot reach into it.
        warehouse.IsAccessibleTo(other, forIssue: true).Should().BeFalse();
        warehouse.IsAccessibleTo(other, forIssue: false).Should().BeFalse();
    }

    [Fact]
    public void A_shared_warehouse_is_reachable_only_by_branches_explicitly_granted_access()
    {
        var granted = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        var warehouse = Shared(new BranchWarehouse { BranchId = granted, CanIssue = true, CanReceive = true });

        warehouse.IsShared.Should().BeTrue();
        warehouse.IsAccessibleTo(granted, forIssue: true).Should().BeTrue();

        // "Shared" must not silently mean "any branch may drain it" — that is the whole reason the
        // access list exists rather than a bare nullable branch id.
        warehouse.IsAccessibleTo(stranger, forIssue: true).Should().BeFalse();
    }

    [Fact]
    public void Issue_and_receive_are_granted_separately()
    {
        var branch = Guid.NewGuid();

        // A returns warehouse a shop may put stock into but not sell from.
        var warehouse = Shared(new BranchWarehouse { BranchId = branch, CanIssue = false, CanReceive = true });

        warehouse.IsAccessibleTo(branch, forIssue: false).Should().BeTrue();
        warehouse.IsAccessibleTo(branch, forIssue: true).Should().BeFalse();
    }
}

public class DocumentNumberSequenceTests
{
    [Fact]
    public void Numbers_are_formatted_with_prefix_year_and_padding()
    {
        var sequence = new DocumentNumberSequence
        {
            Prefix = "INV",
            Year = 2026,
            NextNumber = 42,
            Padding = 5,
            ResetsAnnually = true
        };

        sequence.Take().Should().Be("INV-2026-00042");
    }

    [Fact]
    public void Taking_a_number_advances_the_counter_by_exactly_one()
    {
        var sequence = new DocumentNumberSequence
        {
            Prefix = "GRN",
            Year = 2026,
            NextNumber = 1,
            Padding = 5,
            ResetsAnnually = true
        };

        var issued = new[] { sequence.Take(), sequence.Take(), sequence.Take() };

        // Gapless: consecutive, no duplicates. An auditor will check exactly this.
        issued.Should().Equal("GRN-2026-00001", "GRN-2026-00002", "GRN-2026-00003");
        sequence.NextNumber.Should().Be(4);
    }

    [Fact]
    public void A_sequence_that_does_not_reset_annually_omits_the_year()
    {
        var sequence = new DocumentNumberSequence
        {
            Prefix = "REP",
            Year = 2026,
            NextNumber = 7,
            Padding = 4,
            ResetsAnnually = false
        };

        sequence.Take().Should().Be("REP-0007");
    }
}

public class SettingValueTests
{
    private static readonly DateTimeOffset March = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_version_is_in_force_only_within_its_validity_window()
    {
        var value = new SettingValue { ValidFrom = March, ValidTo = July, IsActive = true };

        value.IsInForceAt(March.AddDays(-1)).Should().BeFalse();
        value.IsInForceAt(March).Should().BeTrue();
        value.IsInForceAt(March.AddMonths(2)).Should().BeTrue();

        // Exclusive upper bound: the moment the next version starts, this one stops. Otherwise two
        // versions would both be "in force" on the changeover instant.
        value.IsInForceAt(July).Should().BeFalse();
    }

    [Fact]
    public void An_open_ended_version_stays_in_force()
    {
        var value = new SettingValue { ValidFrom = March, ValidTo = null, IsActive = true };

        value.IsInForceAt(July.AddYears(5)).Should().BeTrue();
    }

    [Fact]
    public void Superseding_a_setting_does_not_change_what_was_in_force_in_the_past()
    {
        // General Rule 3, expressed as a test: an invoice raised in April must still resolve April's
        // tax rate after the rate is changed in July. This is why settings are versioned rather than
        // updated in place.
        var old = new SettingValue { Key = "tax.rate", Value = "5", ValidFrom = March, ValidTo = July, IsActive = true };
        var current = new SettingValue { Key = "tax.rate", Value = "9", ValidFrom = July, ValidTo = null, IsActive = true };

        var april = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);

        old.IsInForceAt(april).Should().BeTrue();
        current.IsInForceAt(april).Should().BeFalse();

        var august = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);

        old.IsInForceAt(august).Should().BeFalse();
        current.IsInForceAt(august).Should().BeTrue();
    }
}

public class FeatureCatalogTests
{
    [Fact]
    public void Every_feature_supports_at_least_the_view_action()
    {
        // A feature nobody can view is a feature nobody can reach.
        FeatureCatalog.All.Should().OnlyContain(f => f.SupportedActions.Contains(PermissionAction.View));
    }

    [Fact]
    public void Feature_codes_are_unique()
    {
        FeatureCatalog.All.Select(f => f.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Supports_rejects_an_action_the_feature_does_not_declare()
    {
        // The audit log is read-only. Granting Create on it would be a permission that nothing can
        // ever check — the grid must refuse it.
        FeatureCatalog.Supports(FeatureCatalog.AuditLog, PermissionAction.View).Should().BeTrue();
        FeatureCatalog.Supports(FeatureCatalog.AuditLog, PermissionAction.Create).Should().BeFalse();
        FeatureCatalog.Supports("not.a.feature", PermissionAction.View).Should().BeFalse();
    }
}
