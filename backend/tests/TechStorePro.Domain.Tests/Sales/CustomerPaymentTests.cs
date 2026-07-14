using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Sales;
using FluentAssertions;
using Xunit;

namespace TechStorePro.Domain.Tests.Sales;

/// <summary>
/// A payment is a header, its tender and its allocations — and the rules that hold the three together.
/// </summary>
public class CustomerPaymentTests
{
    private static CustomerPayment Payment(params decimal[] tender)
    {
        var payment = new CustomerPayment
        {
            Number = "PAY-2026-00001",
            CustomerId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            PaidAt = DateTimeOffset.UtcNow
        };

        foreach (var amount in tender)
        {
            payment.Methods.Add(new CustomerPaymentMethod
            {
                PaymentMethodId = Guid.NewGuid(),
                Amount = amount
            });
        }

        return payment;
    }

    [Fact]
    public void One_sale_can_be_settled_by_cash_and_card_together()
    {
        // Requirements §23 asks for exactly this. The workaround — two payments — would make one sale
        // look like two.
        var payment = Payment(500m, 1_075m);

        payment.Amount.Should().Be(1_575m);
        payment.Methods.Should().HaveCount(2);
    }

    [Fact]
    public void The_total_is_the_tender_and_cannot_disagree_with_it()
    {
        // There is no settable Amount on the header. A header that could disagree with its method lines
        // would be a till that does not balance, and nobody could say which figure was the truth.
        var payment = Payment(100m, 50m);

        payment.Amount.Should().Be(150m);
    }

    [Fact]
    public void A_payment_cannot_settle_more_debt_than_the_money_it_carries()
    {
        // Otherwise the shop appears to have collected more than it holds, and the customer's balance
        // drifts quietly in the customer's favour.
        var payment = Payment(100m);

        payment.Allocations.Add(new CustomerPaymentAllocation
        {
            SalesInvoiceId = Guid.NewGuid(),
            Amount = 150m
        });

        var act = payment.Validate;

        act.Should().Throw<DomainException>().WithMessage("*allocates 150*only 100 was received*");
    }

    [Fact]
    public void Money_received_before_the_invoice_is_a_credit_and_not_an_error()
    {
        // A deposit on an order, or a customer paying down their account. It is a real state.
        var payment = Payment(1_000m);

        payment.Allocations.Add(new CustomerPaymentAllocation
        {
            SalesInvoiceId = Guid.NewGuid(),
            Amount = 400m
        });

        payment.Validate();

        payment.UnallocatedAmount.Should().Be(600m);
    }

    [Fact]
    public void A_payment_with_no_tender_is_not_a_payment()
    {
        var act = Payment().Validate;

        act.Should().Throw<DomainException>();
    }
}

public class InvoiceSettlementTests
{
    private static SalesInvoice Invoice(decimal unitPrice = 1_000m, decimal taxPercent = 5m)
    {
        var invoice = new SalesInvoice
        {
            Number = "INV-2026-00001",
            CustomerId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            Status = SalesInvoiceStatus.Posted,
            InvoicedAt = DateTimeOffset.UtcNow
        };

        invoice.Lines.Add(new SalesInvoiceLine
        {
            Description = "Laptop",
            Quantity = 1,
            UnitPrice = unitPrice,
            TaxPercent = taxPercent
        });

        return invoice;
    }

    [Fact]
    public void Paying_part_of_an_invoice_leaves_it_partially_paid()
    {
        var invoice = Invoice();   // 1,050 with tax

        invoice.Allocations.Add(new CustomerPaymentAllocation { Amount = 500m });
        invoice.RefreshPaymentStatus();

        invoice.Status.Should().Be(SalesInvoiceStatus.PartiallyPaid);
        invoice.OutstandingAmount.Should().Be(550m);
        invoice.IsSettled.Should().BeFalse();
    }

    [Fact]
    public void Paying_an_invoice_in_two_instalments_settles_it()
    {
        // The reason allocations are a table and not a column: one invoice, two payments.
        var invoice = Invoice();

        invoice.Allocations.Add(new CustomerPaymentAllocation { Amount = 500m });
        invoice.RefreshPaymentStatus();

        invoice.Allocations.Add(new CustomerPaymentAllocation { Amount = 550m });
        invoice.RefreshPaymentStatus();

        invoice.Status.Should().Be(SalesInvoiceStatus.Paid);
        invoice.OutstandingAmount.Should().Be(0m);
    }

    [Fact]
    public void A_draft_invoice_is_not_marked_paid_by_money_arriving()
    {
        // Status is derived from the allocations, but only once the bill has actually been issued. An
        // invoice marked Paid that was never posted would be a receivable that never existed.
        var invoice = Invoice();
        invoice.Status = SalesInvoiceStatus.Draft;

        invoice.Allocations.Add(new CustomerPaymentAllocation { Amount = 1_050m });
        invoice.RefreshPaymentStatus();

        invoice.Status.Should().Be(SalesInvoiceStatus.Draft);
    }
}
