using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Sales;
using FluentAssertions;
using Xunit;

namespace TechStorePro.Domain.Tests.Sales;

/// <summary>
/// The rules the sales documents enforce on themselves, so that no code path can dodge them.
/// </summary>
public class QuotationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);

    private static Quotation Quote(QuotationStatus status = QuotationStatus.Draft, DateTimeOffset? validUntil = null)
    {
        var quotation = new Quotation
        {
            Number = "QT-2026-00001",
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = status,
            QuotedAt = Now.AddDays(-1),
            ValidUntil = validUntil
        };

        quotation.Lines.Add(new QuotationLine
        {
            ProductId = Guid.NewGuid(),
            Description = "Laptop",
            Quantity = 1,
            UnitPrice = 1_000m,
            TaxPercent = 5m
        });

        return quotation;
    }

    [Fact]
    public void An_expired_quotation_cannot_be_accepted()
    {
        // The price was promised until a date and that date has passed. Honouring it now would sell at a
        // price the shop has withdrawn — possibly below what the goods now cost it.
        var quotation = Quote(QuotationStatus.Sent, validUntil: Now.AddDays(-1));

        var act = () => quotation.Accept(Now);

        act.Should().Throw<DomainException>().WithMessage("*expired*");
    }

    [Fact]
    public void A_quotation_cannot_become_an_order_twice()
    {
        // The second order would sell the same goods again at the same promised price, and it would look
        // exactly as legitimate as the first.
        var quotation = Quote(QuotationStatus.Accepted);

        quotation.MarkConverted();

        var act = quotation.MarkConverted;

        act.Should().Throw<DomainException>().WithMessage("*already become an order*");
    }

    [Fact]
    public void A_quotation_with_no_lines_quotes_nothing()
    {
        var quotation = new Quotation { Number = "QT-1", BranchId = Guid.NewGuid(), QuotedAt = Now };

        var act = quotation.Validate;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void The_total_is_the_net_plus_the_tax()
    {
        var quotation = Quote();

        quotation.NetTotal.Should().Be(1_000m);
        quotation.TaxTotal.Should().Be(50m);
        quotation.Total.Should().Be(1_050m);
    }
}

public class SalesOrderTests
{
    private static SalesOrder Order(SalesOrderStatus status = SalesOrderStatus.Draft, decimal delivered = 0m)
    {
        var order = new SalesOrder
        {
            Number = "SO-2026-00001",
            CustomerId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            Status = status,
            OrderedAt = DateTimeOffset.UtcNow
        };

        order.Lines.Add(new SalesOrderLine
        {
            ProductId = Guid.NewGuid(),
            Description = "Laptop",
            Quantity = 2,
            DeliveredQuantity = delivered,
            UnitPrice = 1_000m,
            TaxPercent = 5m
        });

        return order;
    }

    [Fact]
    public void An_order_that_has_started_shipping_cannot_be_cancelled()
    {
        // Cancelling the paperwork would leave stock off the shelf with no document explaining where it
        // went. Getting it back is a credit note.
        var order = Order(SalesOrderStatus.PartiallyDelivered, delivered: 1m);

        var act = order.Cancel;

        act.Should().Throw<DomainException>().WithMessage("*already been delivered*");
    }

    [Fact]
    public void Delivering_every_line_completes_the_order()
    {
        var order = Order(SalesOrderStatus.Confirmed);

        order.Lines.First().DeliveredQuantity = 2m;
        order.RefreshDeliveryStatus();

        order.Status.Should().Be(SalesOrderStatus.Delivered);
        order.IsFullyDelivered.Should().BeTrue();
    }

    [Fact]
    public void Delivering_some_of_a_line_leaves_the_order_partially_delivered()
    {
        var order = Order(SalesOrderStatus.Confirmed);

        order.Lines.First().DeliveredQuantity = 1m;
        order.RefreshDeliveryStatus();

        order.Status.Should().Be(SalesOrderStatus.PartiallyDelivered);
        order.Lines.First().OutstandingQuantity.Should().Be(1m);
    }

    [Fact]
    public void A_draft_order_cannot_be_confirmed_twice()
    {
        // The second confirmation would reserve the stock a second time — the same units promised twice.
        var order = Order();

        order.Confirm();

        var act = order.Confirm;

        act.Should().Throw<DomainException>();
    }
}

public class CreditLimitTests
{
    [Fact]
    public void An_order_that_would_break_the_credit_limit_is_refused()
    {
        // Checked at confirmation, because that is the moment the shop commits goods to someone who has
        // not paid. Discovering it at delivery is discovering it too late.
        var customer = new Customer
        {
            Code = "C-1",
            Name = "Omar",
            Type = CustomerType.Corporate,
            CreditLimit = 10_000m,
            Balance = 9_500m
        };

        customer.WouldExceedCreditLimit(1_000m).Should().BeTrue();
        customer.WouldExceedCreditLimit(400m).Should().BeFalse();
    }
}

public class SalesInvoiceTests
{
    private static SalesInvoice Invoice(SalesInvoiceStatus status = SalesInvoiceStatus.Draft)
    {
        var invoice = new SalesInvoice
        {
            Number = "INV-2026-00001",
            CustomerId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            Status = status,
            InvoicedAt = DateTimeOffset.UtcNow
        };

        invoice.Lines.Add(new SalesInvoiceLine
        {
            ProductId = Guid.NewGuid(),
            Description = "Laptop",
            Quantity = 1,
            UnitPrice = 1_500m,
            TaxPercent = 5m,
            UnitCost = 1_200m
        });

        return invoice;
    }

    [Fact]
    public void An_invoice_that_has_been_paid_cannot_be_cancelled()
    {
        // The payment would be left pointing at nothing. A credit note is how money goes back.
        var invoice = Invoice(SalesInvoiceStatus.Paid);

        var act = invoice.Cancel;

        act.Should().Throw<DomainException>().WithMessage("*credit note*");
    }

    [Fact]
    public void Margin_is_revenue_minus_cost_and_excludes_the_tax()
    {
        // The tax is the government's money, not the shop's. Counting it as profit would overstate every
        // margin report in P7 by exactly the tax rate.
        var invoice = Invoice();

        invoice.NetTotal.Should().Be(1_500m);
        invoice.TaxTotal.Should().Be(75m);
        invoice.Total.Should().Be(1_575m);

        invoice.CostTotal.Should().Be(1_200m);
        invoice.GrossProfit.Should().Be(300m, "1,500 net − 1,200 cost — the 75 of tax is not profit");
    }

    [Fact]
    public void An_invoice_with_no_lines_bills_nothing()
    {
        var invoice = new SalesInvoice
        {
            Number = "INV-1",
            CustomerId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            InvoicedAt = DateTimeOffset.UtcNow
        };

        var act = invoice.Post;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_posted_invoice_cannot_be_posted_again()
    {
        // It would raise the customer's balance a second time — the same debt, recorded twice.
        var invoice = Invoice();

        invoice.Post();

        var act = invoice.Post;

        act.Should().Throw<DomainException>();
    }
}
