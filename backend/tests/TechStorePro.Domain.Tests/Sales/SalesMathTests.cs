using TechStorePro.Domain.Sales;
using FluentAssertions;
using Xunit;

namespace TechStorePro.Domain.Tests.Sales;

/// <summary>
/// The arithmetic of decision <b>D7</b>: prices are tax-exclusive, and the discount comes off before the
/// tax goes on.
///
/// This is the file that pins the decision. Get the order of operations wrong and every invoice the shop
/// ever issues is wrong by the tax on the discount — a small number, on every line, forever, and one the
/// tax authority eventually asks about.
/// </summary>
public class SalesMathTests
{
    [Fact]
    public void Tax_is_charged_on_the_discounted_net_and_not_on_the_gross()
    {
        // One laptop at 1,000, 10% off, 5% tax.
        //   net = 1,000 − 100 = 900
        //   tax = 900 × 5%    =  45      ← not 50, which is what taxing the gross would give
        var net = SalesMath.Net(quantity: 1, unitPrice: 1_000m, discountPercent: 10m, discountAmount: 0m);
        var tax = SalesMath.Tax(net, taxPercent: 5m);

        net.Should().Be(900m);
        tax.Should().Be(45m, "the customer never paid the 100 that was discounted, so it cannot be taxed");
        (net + tax).Should().Be(945m);
    }

    [Fact]
    public void A_price_is_exclusive_of_tax_so_the_line_total_is_higher_than_the_price()
    {
        // The whole of D7 in one assertion. If prices were inclusive, this would be 1,000.
        var net = SalesMath.Net(1, 1_000m, 0m, 0m);
        var tax = SalesMath.Tax(net, 5m);

        (net + tax).Should().Be(1_050m);
    }

    [Fact]
    public void No_tax_configured_means_no_tax_charged()
    {
        // A company in a jurisdiction with no sales tax configures no rate. That is a legitimate answer,
        // not a misconfiguration — nothing here assumes a country.
        var net = SalesMath.Net(3, 100m, 0m, 0m);

        SalesMath.Tax(net, taxPercent: 0m).Should().Be(0m);
    }

    [Fact]
    public void The_percentage_comes_off_first_and_then_the_fixed_amount()
    {
        // 2 × 500 = 1,000; 10% off → 900; then 50 haggled off → 850.
        var net = SalesMath.Net(quantity: 2, unitPrice: 500m, discountPercent: 10m, discountAmount: 50m);

        net.Should().Be(850m);
    }

    [Fact]
    public void A_discount_can_never_take_a_line_below_zero()
    {
        // Otherwise the customer would be owed money for buying something — the P2 rule, held here too.
        var net = SalesMath.Net(quantity: 1, unitPrice: 100m, discountPercent: 0m, discountAmount: 250m);

        net.Should().Be(0m);
    }

    [Fact]
    public void Money_is_rounded_at_the_line_not_at_the_total()
    {
        // 3 × 33.333 = 99.999 → 100.00. A total assembled from unrounded lines can disagree with the sum
        // of the lines as printed, which is the "invoice is off by one fils" nobody can ever explain.
        SalesMath.Gross(3, 33.333m).Should().Be(100.00m);

        // And tax rounds the same way: 5% of 99.99 is 4.9995 → 5.00.
        SalesMath.Tax(99.99m, 5m).Should().Be(5.00m);
    }

    [Fact]
    public void Half_a_fils_rounds_away_from_zero_not_to_even()
    {
        // Banker's rounding would give 0.02 here. The customer is charged what the shop printed, and
        // shops round up at the half.
        SalesMath.Tax(0.50m, 5m).Should().Be(0.03m);   // 0.025 → 0.03
    }
}
