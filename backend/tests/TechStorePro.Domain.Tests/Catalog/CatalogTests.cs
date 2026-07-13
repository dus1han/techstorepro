using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace TechStorePro.Domain.Tests.Catalog;

public class ProductTests
{
    [Fact]
    public void A_service_cannot_be_serial_tracked()
    {
        // A stock ledger entry for "one hour of labour" is meaningless, and a serial number for it
        // is worse — P3's ledger would carry rows it can never reconcile against a physical shelf.
        var service = new Product
        {
            Name = "Diagnostic fee",
            Kind = ProductKind.Service,
            TrackingMode = TrackingMode.Serial
        };

        var act = service.Validate;

        act.Should().Throw<DomainException>().WithMessage("*service cannot be serial*");
    }

    [Fact]
    public void A_service_with_no_tracking_is_fine()
    {
        var service = new Product { Name = "Labour", Kind = ProductKind.Service, TrackingMode = TrackingMode.None };

        var act = service.Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void Negative_prices_are_rejected()
    {
        var product = new Product { Name = "Laptop", SellingPrice = -1 };

        var act = product.Validate;

        act.Should().Throw<DomainException>().WithMessage("*negative*");
    }

    [Fact]
    public void Margin_is_computed_from_the_selling_price()
    {
        var product = new Product { PurchasePrice = 750m, SellingPrice = 1000m };

        product.DefaultMarginPercent.Should().Be(25m);
    }

    [Fact]
    public void Margin_is_negative_when_selling_below_cost()
    {
        // Selling below cost is a decision, not an error — clearing old stock is a real thing. But it
        // must be visible, so the margin goes negative rather than clamping to zero.
        var product = new Product { PurchasePrice = 1000m, SellingPrice = 800m };

        product.DefaultMarginPercent.Should().Be(-25m);
    }

    [Fact]
    public void Margin_is_undefined_rather_than_infinite_when_the_selling_price_is_zero()
    {
        var freebie = new Product { PurchasePrice = 100m, SellingPrice = 0m };

        freebie.DefaultMarginPercent.Should().BeNull();
    }
}

public class CustomerCreditTests
{
    [Fact]
    public void An_order_within_the_credit_limit_is_allowed()
    {
        var customer = new Customer { CreditLimit = 10_000m, Balance = 2_000m };

        customer.WouldExceedCreditLimit(5_000m).Should().BeFalse();
    }

    [Fact]
    public void An_order_that_would_breach_the_limit_is_flagged()
    {
        var customer = new Customer { CreditLimit = 10_000m, Balance = 8_000m };

        customer.WouldExceedCreditLimit(3_000m).Should().BeTrue();
    }

    [Fact]
    public void A_zero_credit_limit_means_cash_only_not_unlimited_credit()
    {
        // The dangerous reading. Zero must mean "no credit", never "no ceiling" — the latter would
        // hand unlimited credit to every walk-in by default.
        var walkIn = new Customer { CreditLimit = 0m, Balance = 0m };

        walkIn.WouldExceedCreditLimit(1m).Should().BeFalse(
            "a zero limit is enforced by refusing credit sales outright, not by the limit check");
    }
}

public class TaxRateTests
{
    private static readonly DateTimeOffset March = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset July = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_rate_is_in_force_only_within_its_window()
    {
        var rate = new TaxRate { Percent = 5m, ValidFrom = March, ValidTo = July, IsActive = true };

        rate.IsInForceAt(March.AddDays(-1)).Should().BeFalse();
        rate.IsInForceAt(April).Should().BeTrue();
        rate.IsInForceAt(July).Should().BeFalse();
    }

    [Fact]
    public void Changing_the_rate_does_not_change_what_applied_in_the_past()
    {
        // General Rule 3 again, now for tax. An invoice raised in April is 5%; the July change to 9%
        // must not restate it. This is why documents snapshot the percentage onto the line.
        var old = new TaxRate { Percent = 5m, ValidFrom = March, ValidTo = July, IsActive = true };
        var current = new TaxRate { Percent = 9m, ValidFrom = July, ValidTo = null, IsActive = true };

        old.IsInForceAt(April).Should().BeTrue();
        current.IsInForceAt(April).Should().BeFalse();
    }

    [Fact]
    public void A_rate_outside_zero_to_one_hundred_percent_is_rejected()
    {
        var act = () => new TaxRate { Percent = 101m, ValidFrom = March }.Validate();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_window_that_ends_before_it_begins_is_rejected()
    {
        var act = () => new TaxRate { Percent = 5m, ValidFrom = July, ValidTo = March }.Validate();

        act.Should().Throw<DomainException>().WithMessage("*end after it begins*");
    }

    private static readonly DateTimeOffset April = new(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
}

public class DiscountTests
{
    [Fact]
    public void A_percentage_discount_takes_that_percentage_off_the_line()
    {
        var discount = new Discount { Method = DiscountMethod.Percentage, Value = 10m };

        discount.AmountOff(1_000m).Should().Be(100m);
    }

    [Fact]
    public void A_fixed_discount_can_never_exceed_the_line_it_is_applied_to()
    {
        // Otherwise a 500 discount on a 300 line produces a negative total — the customer would be
        // owed money for buying something.
        var discount = new Discount { Method = DiscountMethod.FixedAmount, Value = 500m };

        discount.AmountOff(300m).Should().Be(300m);
    }

    [Fact]
    public void A_discount_beyond_its_ceiling_needs_approval()
    {
        // Requirements §32: discount limits with manager approval.
        var discount = new Discount { Method = DiscountMethod.Percentage, Value = 5m, MaxValue = 10m };

        discount.RequiresApproval(8m).Should().BeFalse();
        discount.RequiresApproval(15m).Should().BeTrue();
    }

    [Fact]
    public void A_discount_with_no_ceiling_never_needs_approval()
    {
        var discount = new Discount { Method = DiscountMethod.Percentage, Value = 5m, MaxValue = null };

        discount.RequiresApproval(99m).Should().BeFalse();
    }

    [Fact]
    public void A_percentage_over_one_hundred_is_rejected()
    {
        var act = () => new Discount { Method = DiscountMethod.Percentage, Value = 120m }.Validate();

        act.Should().Throw<DomainException>().WithMessage("*exceed 100*");
    }

    [Fact]
    public void A_ceiling_below_the_discount_itself_is_rejected()
    {
        // It would mean every single use of the rule needs approval, which is not a rule — it is a
        // misconfiguration that would silently stall every sale that used it.
        var act = () => new Discount { Method = DiscountMethod.Percentage, Value = 20m, MaxValue = 10m }.Validate();

        act.Should().Throw<DomainException>().WithMessage("*ceiling cannot be below*");
    }
}

public class PriceListTests
{
    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jun = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Dec = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Two_lists_covering_the_same_period_overlap()
    {
        var first = new PriceList { ValidFrom = Jan, ValidTo = Dec };
        var second = new PriceList { ValidFrom = Jun, ValidTo = null };

        // Overlapping lists for one tier would make "what does this customer pay today?" have two
        // answers, and the system would silently pick one.
        first.OverlapsWith(second).Should().BeTrue();
    }

    [Fact]
    public void Consecutive_lists_do_not_overlap()
    {
        // Half-open intervals: [Jan, Jun) then [Jun, Dec). The changeover instant belongs to exactly
        // one of them, so handing over on 1 June is clean rather than ambiguous.
        var first = new PriceList { ValidFrom = Jan, ValidTo = Jun };
        var second = new PriceList { ValidFrom = Jun, ValidTo = Dec };

        first.OverlapsWith(second).Should().BeFalse();
        second.OverlapsWith(first).Should().BeFalse();
    }
}

public class FxRateTests
{
    [Fact]
    public void A_zero_or_negative_rate_is_rejected()
    {
        var act = () => new FxRate { CurrencyCode = "USD", RateToBase = 0m }.Validate();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Conversion_to_base_multiplies_by_the_rate()
    {
        var rate = new FxRate { CurrencyCode = "USD", RateToBase = 3.6725m };

        rate.ToBase(100m).Should().Be(367.25m);
    }
}
