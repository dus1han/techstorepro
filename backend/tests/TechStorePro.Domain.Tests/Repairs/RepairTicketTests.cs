using FluentAssertions;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Repairs;
using Xunit;

namespace TechStorePro.Domain.Tests.Repairs;

/// <summary>
/// The workshop state machine (requirements §28) and the money that falls out of it.
///
/// These are the rules that cost the shop real money when they are wrong, and none of them needs a
/// database to prove.
/// </summary>
public class RepairTicketTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);

    private static RepairTicket Ticket(RepairWarrantyType warranty = RepairWarrantyType.None) => new()
    {
        Number = "REP-00001",
        ReportedFault = "Will not power on",
        Status = RepairTicketStatus.Received,
        WarrantyType = warranty,
        ReceivedAt = Now
    };

    [Fact]
    public void The_workflow_runs_received_to_delivered()
    {
        var ticket = Ticket();

        ticket.BeginDiagnosis(null, Now);
        ticket.Status.Should().Be(RepairTicketStatus.Diagnosing);

        ticket.RecordDiagnosis(400m, Now, null);
        ticket.Status.Should().Be(RepairTicketStatus.AwaitingApproval, "a chargeable job waits for the customer");

        ticket.ApproveByCustomer(null, Now);
        ticket.Status.Should().Be(RepairTicketStatus.InRepair);

        ticket.BeginTesting(null, Now);
        ticket.MarkReady(null, Now);
        ticket.Deliver(null, Now);

        ticket.Status.Should().Be(RepairTicketStatus.Delivered);
        ticket.DeliveredAt.Should().Be(Now);

        // Every transition left a trail. A state machine whose history is written by whoever remembers to
        // write it is a state machine with no history.
        ticket.StatusHistory.Should().HaveCount(6);
        ticket.StatusHistory.Last().ToStatus.Should().Be(RepairTicketStatus.Delivered);
    }

    [Fact]
    public void Nothing_may_be_fitted_before_the_customer_approves_the_estimate()
    {
        // The gate, and the reason the approval step exists at all. Parts consumed against a job the
        // customer then declines are parts the shop has paid for and cannot bill.
        var ticket = Ticket();

        ticket.BeginDiagnosis(null, Now);
        ticket.RecordDiagnosis(400m, Now, null);

        ticket.Status.Should().Be(RepairTicketStatus.AwaitingApproval);

        var fit = () => ticket.EnsureWorkAllowed();

        fit.Should().Throw<DomainException>().WithMessage("*not approved the estimate*");

        ticket.ApproveByCustomer(null, Now);

        ticket.Invoking(t => t.EnsureWorkAllowed()).Should().NotThrow("the customer has now agreed to pay");
    }

    [Fact]
    public void A_warranty_job_skips_the_approval_step()
    {
        // There is no price on a warranty job, so there is nobody to agree to it. Parking it at
        // AwaitingApproval would leave a free repair waiting for a decision nobody was ever asked to make.
        var ticket = Ticket(RepairWarrantyType.Shop);

        ticket.BeginDiagnosis(null, Now);
        ticket.RecordDiagnosis(null, Now, null);

        ticket.Status.Should().Be(RepairTicketStatus.InRepair, "a warranty job goes straight to the bench");
        ticket.Invoking(t => t.EnsureWorkAllowed()).Should().NotThrow();
    }

    [Fact]
    public void A_part_may_still_be_fitted_while_the_machine_is_being_tested()
    {
        // A machine that fails its test bench needs another part. Forcing the technician to reopen the job
        // is a workflow people route around by never moving the job to Testing in the first place.
        var ticket = Ticket();

        ticket.BeginDiagnosis(null, Now);
        ticket.RecordDiagnosis(400m, Now, null);
        ticket.ApproveByCustomer(null, Now);
        ticket.BeginTesting(null, Now);

        ticket.Invoking(t => t.EnsureWorkAllowed()).Should().NotThrow();
    }

    [Fact]
    public void A_job_with_parts_in_it_cannot_simply_be_cancelled()
    {
        var ticket = Ticket();

        ticket.BeginDiagnosis(null, Now);
        ticket.RecordDiagnosis(400m, Now, null);
        ticket.ApproveByCustomer(null, Now);

        ticket.Parts.Add(new RepairPart { Quantity = 1m, UnitCost = 120m, UnitPrice = 200m });

        var cancel = () => ticket.Cancel("Customer changed their mind", null, Now);

        // The screen is inside the customer's laptop. Cancelling the job would lose the shop the screen and
        // leave no document accounting for where it went.
        cancel.Should().Throw<DomainException>().WithMessage("*Parts have already been fitted*");
    }

    [Fact]
    public void Cancelling_needs_a_reason()
    {
        var ticket = Ticket();

        ticket.Invoking(t => t.Cancel("  ", null, Now))
            .Should().Throw<DomainException>().WithMessage("*needs a reason*");
    }

    [Fact]
    public void A_device_cannot_be_delivered_before_it_is_ready()
    {
        var ticket = Ticket();

        ticket.BeginDiagnosis(null, Now);
        ticket.RecordDiagnosis(400m, Now, null);
        ticket.ApproveByCustomer(null, Now);

        ticket.Invoking(t => t.Deliver(null, Now))
            .Should().Throw<DomainException>().WithMessage("*Mark it ready first*");
    }

    [Fact]
    public void A_chargeable_job_earns_its_margin_from_parts_and_labour()
    {
        var ticket = Ticket();

        // A screen that cost 120 on the shelf, sold at 200. Two hours at 150.
        ticket.Parts.Add(new RepairPart { Quantity = 1m, UnitCost = 120m, UnitPrice = 200m, IsChargeable = true });
        ticket.Labour.Add(new RepairLabour { Hours = 2m, HourlyRate = 150m, IsChargeable = true });

        ticket.PartsCost.Should().Be(120m);
        ticket.ChargeableTotal.Should().Be(500m, "200 for the screen + 300 of labour");
        ticket.TotalCost.Should().Be(120m, "labour has no cost side — the wage is a payroll expense, not a job cost");
        ticket.GrossProfit.Should().Be(380m);
    }

    [Fact]
    public void A_warranty_job_makes_a_loss_and_that_is_the_point()
    {
        // §45 D10. The parts still left the shelf and the vendor still charged for the board; only the
        // customer's bill is zero. A warranty repair that booked no cost would make warranty look free, and
        // the shop would never learn which product line is eating it.
        var ticket = Ticket(RepairWarrantyType.Shop);

        ticket.Parts.Add(new RepairPart { Quantity = 1m, UnitCost = 120m, UnitPrice = 200m, IsChargeable = false });
        ticket.Labour.Add(new RepairLabour { Hours = 2m, HourlyRate = 150m, IsChargeable = false });

        ticket.ChargeableTotal.Should().Be(0m, "the customer is not billed for a warranty repair");
        ticket.PartsCost.Should().Be(120m, "but the screen is still gone from the shelf");
        ticket.GrossProfit.Should().Be(-120m, "and the shop is 120 down, which is exactly what it should show");
    }

    [Fact]
    public void An_outsourced_vendor_charge_is_a_cost_of_the_job()
    {
        var ticket = Ticket();

        ticket.Labour.Add(new RepairLabour { Hours = 1m, HourlyRate = 150m, IsChargeable = true });

        var outsourcing = new RepairOutsourcing
        {
            Status = OutsourcingStatus.Sent,
            CurrencyCode = "USD",
            ExchangeRate = 3.67m,
            SentAt = Now
        };

        ticket.Outsourcings.Add(outsourcing);

        outsourcing.Receive(100m, Now);

        // The vendor billed USD 100 at 3.67. The margin has to be in the money the shop keeps its books in.
        outsourcing.CostInBaseCurrency.Should().Be(367m);
        ticket.OutsourcingCost.Should().Be(367m);
        ticket.TotalCost.Should().Be(367m);
        ticket.GrossProfit.Should().Be(-217m, "150 of labour against a 367 vendor bill is a loss on this job");
    }

    [Fact]
    public void A_cancelled_outsourcing_costs_nothing_but_a_completed_one_cannot_be_cancelled()
    {
        var sent = new RepairOutsourcing { Status = OutsourcingStatus.Sent, Cost = 300m, ExchangeRate = 1m };

        sent.Cancel();
        sent.CostInBaseCurrency.Should().Be(0m, "the vendor never did the work");

        var done = new RepairOutsourcing { Status = OutsourcingStatus.Sent, ExchangeRate = 1m };
        done.Receive(300m, Now);

        done.Invoking(o => o.Cancel())
            .Should().Throw<DomainException>().WithMessage("*already done the work*");
    }

    [Fact]
    public void A_returned_part_stops_counting_toward_cost_and_charge()
    {
        var ticket = Ticket();

        var part = new RepairPart { Quantity = 1m, UnitCost = 120m, UnitPrice = 200m, IsChargeable = true };
        ticket.Parts.Add(part);

        ticket.PartsCost.Should().Be(120m);

        part.IsReturned = true;

        ticket.PartsCost.Should().Be(0m, "it went back on the shelf, so it is not a cost of this job");
        ticket.ChargeableTotal.Should().Be(0m, "and the customer is not billed for a part they did not get");
    }
}
