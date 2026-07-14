using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Sales;
using FluentAssertions;
using Xunit;

namespace TechStorePro.Domain.Tests.Sales;

public class CreditNoteTests
{
    private static CreditNote Note(string? reason = "Faulty on arrival", params CreditNoteLine[] lines)
    {
        var note = new CreditNote
        {
            Number = "CN-2026-00001",
            CustomerId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            SalesInvoiceId = Guid.NewGuid(),
            IssuedAt = DateTimeOffset.UtcNow,
            RefundMethod = RefundMethod.StoreCredit,
            Reason = reason!
        };

        foreach (var line in lines)
        {
            note.Lines.Add(line);
        }

        return note;
    }

    private static CreditNoteLine Line(decimal quantity = 1m, decimal unitPrice = 1_000m, decimal tax = 5m) =>
        new()
        {
            SalesInvoiceLineId = Guid.NewGuid(),
            Description = "Laptop",
            Quantity = quantity,
            UnitPrice = unitPrice,
            TaxPercent = tax,
            UnitCost = 800m
        };

    [Fact]
    public void A_credit_note_needs_a_reason()
    {
        // Money is going back to a customer. "Why" is the first question anyone will ask of it, and a
        // blank is not an answer a shop can give an auditor.
        var act = Note(reason: " ", Line()).Validate;

        act.Should().Throw<DomainException>().WithMessage("*reason*");
    }

    [Fact]
    public void A_credit_note_with_no_lines_credits_nothing()
    {
        var act = Note().Validate;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void The_refund_gives_back_the_tax_that_was_charged()
    {
        // The customer paid 1,050 for the laptop — 1,000 plus 5% tax. They get 1,050 back, not 1,000: the
        // shop collected that tax from them and is giving it back, not keeping it.
        var note = Note("Returned", Line());

        note.NetTotal.Should().Be(1_000m);
        note.TaxTotal.Should().Be(50m);
        note.Total.Should().Be(1_050m);
    }

    [Fact]
    public void The_cost_of_the_returned_goods_comes_back_with_them()
    {
        // What the goods cost the shop when they left. Restocking them at today's average instead would
        // move the average by money the shop never spent.
        var note = Note("Returned", Line(quantity: 2m));

        note.CostTotal.Should().Be(1_600m, "2 × the 800 they left at");
    }
}

public class StoreCreditTests
{
    private static StoreCreditEntry Entry(decimal amount, string reason = "Credit note CN-1") =>
        new()
        {
            CustomerId = Guid.NewGuid(),
            Amount = amount,
            OccurredAt = DateTimeOffset.UtcNow,
            Reason = reason
        };

    [Fact]
    public void The_balance_is_the_sum_of_the_entries()
    {
        // A ledger, not a column. "Why do I have 240 credit?" has an answer only if every issue and every
        // redemption is a row — the same reasoning that makes stock_balances a cache of stock_movements
        // and not the truth itself.
        var entries = new[] { Entry(300m), Entry(-100m), Entry(40m) };

        entries.Sum(e => e.Amount).Should().Be(240m);
    }

    [Fact]
    public void An_entry_of_nothing_moves_nothing()
    {
        var act = Entry(0m).Validate;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void An_entry_needs_a_reason_because_it_is_money()
    {
        var act = Entry(100m, reason: "").Validate;

        act.Should().Throw<DomainException>();
    }
}
