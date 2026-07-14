using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Application.Repairs.Invoicing;
using TechStorePro.Application.Repairs.Queries;
using TechStorePro.Application.Repairs.Tickets;
using TechStorePro.Application.Repairs.Warranties;
using TechStorePro.Application.Repairs.Work;
using TechStorePro.Application.Sales.Deliveries;
using TechStorePro.Application.Sales.Invoices;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Repairs;
using TechStorePro.Domain.Sales;
using TechStorePro.Infrastructure.Catalog;
using TechStorePro.Infrastructure.Configuration;
using TechStorePro.Infrastructure.Inventory;
using TechStorePro.Infrastructure.Persistence;
using TechStorePro.Infrastructure.Repairs;
using TechStorePro.Infrastructure.Sales;
using Testcontainers.PostgreSql;
using Xunit;

namespace TechStorePro.Application.Tests;

/// <summary>
/// Repairs, against a real PostgreSQL.
///
/// The workshop's arithmetic is unit-tested in <c>Domain.Tests/Repairs</c>. What needs a database to be
/// true is everything else: that the parts really left the shelf when they were fitted, that the machine on
/// the counter can be traced back to the sale that put it in the customer's hands, that a warranty job
/// costs the shop money and says so — and that <b>the P3 balance audit still reconciles afterwards</b>,
/// which is what proves the repairs module wrote no stock outside the ledger.
/// </summary>
public class RepairsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("techstorepro_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private Guid _companyA;
    private Guid _branchOfA;
    private Guid _warehouseOfA;
    private Guid _customer;

    private Guid _laptop;   // serial-tracked, sells at 1,500, 12 months of shop warranty
    private Guid _screen;   // a spare part: costs 120, sells at 200
    private Guid _vendor;   // a repair vendor — the board-level shop down the road

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var seed = CreateContext(null);
        await seed.Database.MigrateAsync();

        var company = new Company { Name = "Gulf Computers", Code = "GULF01", BaseCurrency = "AED", TimeZone = "Asia/Dubai" };
        seed.Companies.Add(company);

        var branch = new Branch { CompanyId = company.Id, Name = "Main", Code = "MAIN", IsDefault = true };
        seed.Branches.Add(branch);

        var warehouse = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Name = "Main Warehouse",
            Code = "MAIN"
        };
        seed.Warehouses.Add(warehouse);

        var vat = new TaxRate
        {
            CompanyId = company.Id,
            Name = "Standard VAT",
            Percent = 5m,
            IsDefault = true,
            ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        seed.TaxRates.Add(vat);

        var customer = new Customer
        {
            CompanyId = company.Id,
            Code = "C-1",
            Name = "Omar Trading",
            Type = CustomerType.Corporate,
            CreditLimit = 50_000m,
            PaymentTermDays = 30
        };
        seed.Customers.Add(customer);

        var vendor = new Supplier
        {
            CompanyId = company.Id,
            Code = "S-1",
            Name = "BoardFix LLC",
            Type = SupplierType.RepairVendor
        };
        seed.Suppliers.Add(vendor);

        // Twelve months of shop warranty, sold with the machine. P5 stamps the expiry onto the serial at
        // the moment of sale; P6 reads it back. Nothing registers it anywhere.
        var laptop = new Product
        {
            CompanyId = company.Id,
            ItemCode = "LAPTOP",
            Sku = "LAPTOP",
            Name = "Laptop",
            Unit = "each",
            TrackingMode = TrackingMode.Serial,
            SellingPrice = 1_500m,
            WarrantyMonths = 12,
            TaxRateId = vat.Id
        };

        var screen = new Product
        {
            CompanyId = company.Id,
            ItemCode = "SCREEN",
            Sku = "SCREEN",
            Name = "Replacement screen",
            Unit = "each",
            Kind = ProductKind.SparePart,
            TrackingMode = TrackingMode.None,
            SellingPrice = 200m,
            TaxRateId = vat.Id
        };

        seed.Products.AddRange(laptop, screen);

        foreach (var (type, prefix) in new[]
                 {
                     (DocumentType.DeliveryNote, "DLV"),
                     (DocumentType.Invoice, "INV"),
                     (DocumentType.RepairTicket, "REP")
                 })
        {
            seed.DocumentNumberSequences.Add(new DocumentNumberSequence
            {
                CompanyId = company.Id,
                BranchId = branch.Id,
                DocumentType = type,
                Prefix = prefix,
                Year = 2026,
                NextNumber = 1,
                Padding = 5,
                ResetsAnnually = true
            });
        }

        await seed.SaveChangesAsync();

        _companyA = company.Id;
        _branchOfA = branch.Id;
        _warehouseOfA = warehouse.Id;
        _customer = customer.Id;
        _laptop = laptop.Id;
        _screen = screen.Id;
        _vendor = vendor.Id;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    // --- The money path ----------------------------------------------------------------------------

    [Fact]
    public async Task Repairing_a_machine_end_to_end_and_billing_it()
    {
        // The flow §28 describes: received → diagnosis → approval → repair → testing → ready → delivered,
        // with a screen fitted and two hours booked, and a bill at the end of it.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _screen, quantity: 3m, unitCost: 120m);

        var ticketId = await BookInAsync(db, "Cracked screen");

        await BeginDiagnosisAsync(db, ticketId);
        await DiagnoseAsync(db, ticketId, "Screen is cracked. Replace it.", estimate: 500m);

        // The gate. Nothing may be fitted before the customer agrees to the price.
        var early = async () => await ConsumePartAsync(db, ticketId, _screen, 1m);

        await early.Should().ThrowAsync<DomainException>()
            .WithMessage("*not approved the estimate*");

        await ApproveAsync(db, ticketId);

        await ConsumePartAsync(db, ticketId, _screen, 1m);
        await LogLabourAsync(db, ticketId, "Screen replacement", hours: 2m, rate: 150m);

        await TestAsync(db, ticketId);
        await ReadyAsync(db, ticketId);

        var invoiceId = await BillAsync(db, ticketId);

        await DeliverAsync(db, ticketId);

        await using var fresh = CreateContext(_companyA);

        // 1. The part left the shelf — when it was fitted, not when it was billed (§45 D9).
        var balance = await fresh.StockBalances.SingleAsync(b => b.ProductId == _screen);

        balance.Quantity.Should().Be(2m, "one of the three screens is now inside a customer's laptop");
        balance.AverageCost.Should().Be(120m, "issuing stock does not change what the rest of it cost");

        // 2. It went through the ledger, as a RepairConsumption against the job.
        var movement = await fresh.StockMovements
            .SingleAsync(m => m.Type == MovementType.RepairConsumption);

        movement.ReferenceType.Should().Be(StockReferenceType.RepairTicket);
        movement.Quantity.Should().Be(-1m, "the ledger decides the sign; the caller passes a magnitude");

        // 3. The job's money. A screen that cost 120 and sold at 200, plus 2 hours at 150.
        var ticket = await fresh.RepairTickets
            .Include(t => t.Parts)
            .Include(t => t.Labour)
            .Include(t => t.Outsourcings)
            .FirstAsync(t => t.Id == ticketId);

        ticket.Status.Should().Be(RepairTicketStatus.Delivered);
        ticket.PartsCost.Should().Be(120m, "COGS, snapshotted at the moment the screen left the shelf");
        ticket.ChargeableTotal.Should().Be(500m, "200 for the screen + 300 of labour");
        ticket.GrossProfit.Should().Be(380m, "labour has no cost side — the wage is a payroll expense");

        // 4. The bill is an ordinary sales invoice (§45 D11), with no delivery behind it.
        var invoice = await fresh.SalesInvoices
            .Include(i => i.Lines)
            .FirstAsync(i => i.Id == invoiceId);

        invoice.DeliveryId.Should().BeNull("a repair bill has no delivery note — the parts went against the job");
        invoice.Status.Should().Be(SalesInvoiceStatus.Posted);
        invoice.Lines.Should().HaveCount(2);

        // Tax-exclusive, tax after discount (D7): 500 net, 5%, 525.
        invoice.NetTotal.Should().Be(500m);
        invoice.TaxTotal.Should().Be(25m);
        invoice.Total.Should().Be(525m);

        // The labour line has no product and no cost of goods.
        var labourLine = invoice.Lines.Single(l => l.ProductId is null);
        labourLine.UnitCost.Should().Be(0m);
        labourLine.TaxPercent.Should().Be(5m, "labour takes the company's default rate, not zero");

        // The part line carries the cost the ledger reported when it was fitted.
        var partLine = invoice.Lines.Single(l => l.ProductId == _screen);
        partLine.UnitCost.Should().Be(120m);

        // 5. The customer owes for it.
        var customer = await fresh.Customers.FirstAsync(c => c.Id == _customer);
        customer.Balance.Should().Be(525m, "posting the bill and recording the debt are one act");

        // 6. And the ledger still reconciles — which is what proves repairs wrote no stock behind its back.
        await AssertBalanceAuditCleanAsync(fresh);
    }

    [Fact]
    public async Task A_warranty_claim_finds_the_invoice_line_that_sold_the_machine()
    {
        // <b>The back-edge into Sales</b>, and the reason P5 binds the serial at delivery. Two years from
        // now, a laptop on the counter has to be traceable to the sale that put it there.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, quantity: 1m, unitCost: 1_200m, serials: ["SN-A"]);

        // Sell it. P5 stamps the 12-month shop warranty onto the serial as it goes.
        var deliveryId = await DeliverDirectAsync(db, (_laptop, 1m, new[] { "SN-A" }));
        var saleInvoiceId = await RaiseInvoiceAsync(db, deliveryId);

        // It comes back a month later with a fault.
        var ticketId = await BookInAsync(db, "Screen flickers", serialNumber: "SN-A");

        await using var fresh = CreateContext(_companyA);

        var ticket = await fresh.RepairTickets.FirstAsync(t => t.Id == ticketId);

        // The intake found the warranty by itself. Nobody ticked a box — which is exactly how a shop ends
        // up billing a customer for a repair it had already promised to do for free.
        ticket.WarrantyType.Should().Be(RepairWarrantyType.Shop);
        ticket.IsWarranty.Should().BeTrue();

        var saleInvoice = await fresh.SalesInvoices
            .Include(i => i.Lines)
            .FirstAsync(i => i.Id == saleInvoiceId);

        ticket.WarrantyInvoiceLineId.Should().Be(
            saleInvoice.Lines.Single().Id,
            "the claim walked Serial.SoldInvoiceLineId back to the sale that put the machine in the customer's hands");

        // The unit is in the workshop — but it is still the customer's, and it has NOT gone back into stock.
        var serial = await fresh.Serials.FirstAsync(s => s.SerialNumber == "SN-A");

        serial.Status.Should().Be(SerialStatus.InRepair);
        serial.WarehouseId.Should().BeNull("a machine in for repair is not on anybody's shelf");
    }

    [Fact]
    public async Task A_warranty_repair_costs_the_shop_money_and_says_so()
    {
        // §45 D10, and the whole reason warranty work is costed rather than free. The parts still left the
        // shelf; only the customer's bill is zero.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, quantity: 1m, unitCost: 1_200m, serials: ["SN-A"]);
        await ReceiveAsync(db, _screen, quantity: 2m, unitCost: 120m);

        var deliveryId = await DeliverDirectAsync(db, (_laptop, 1m, new[] { "SN-A" }));
        await RaiseInvoiceAsync(db, deliveryId);

        var ticketId = await BookInAsync(db, "Screen flickers", serialNumber: "SN-A");

        await BeginDiagnosisAsync(db, ticketId);

        // A warranty job goes straight to the bench: there is no price, so there is nobody to agree to it.
        await DiagnoseAsync(db, ticketId, "Faulty panel. Covered.", estimate: null);

        await using (var diagnosed = CreateContext(_companyA))
        {
            var mid = await diagnosed.RepairTickets.FirstAsync(t => t.Id == ticketId);
            mid.Status.Should().Be(RepairTicketStatus.InRepair, "a warranty job skips the approval step");
        }

        await ConsumePartAsync(db, ticketId, _screen, 1m);
        await LogLabourAsync(db, ticketId, "Panel replacement", hours: 2m, rate: 150m);

        await ReadyAsync(db, ticketId);
        await DeliverAsync(db, ticketId);

        await using var fresh = CreateContext(_companyA);

        var ticket = await fresh.RepairTickets
            .Include(t => t.Parts)
            .Include(t => t.Labour)
            .Include(t => t.Outsourcings)
            .FirstAsync(t => t.Id == ticketId);

        ticket.ChargeableTotal.Should().Be(0m, "the customer is not billed for a warranty repair");
        ticket.PartsCost.Should().Be(120m, "but the screen is still gone from the shelf");
        ticket.GrossProfit.Should().Be(-120m, "and the shop is 120 down — which is exactly what it should show");

        // The stock really moved, warranty or not.
        var balance = await fresh.StockBalances.SingleAsync(b => b.ProductId == _screen);
        balance.Quantity.Should().Be(1m);

        // The customer owes nothing.
        var customer = await fresh.Customers.FirstAsync(c => c.Id == _customer);
        customer.Balance.Should().Be(1_575m, "the original sale — the repair added nothing to it");

        // Billing it is refused: there is genuinely nothing to bill, and an invoice for zero is not a bill.
        var bill = async () => await BillAsync(db, ticketId);

        await bill.Should().ThrowAsync<DomainException>().WithMessage("*nothing to bill*");

        await AssertBalanceAuditCleanAsync(fresh);
    }

    [Fact]
    public async Task Rejecting_a_warranty_claim_makes_the_job_chargeable()
    {
        // The fault turned out to be liquid damage. The job carries on; only the free ride ends — and the
        // parts already fitted on the strength of the warranty stop being a gift.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, quantity: 1m, unitCost: 1_200m, serials: ["SN-A"]);
        await ReceiveAsync(db, _screen, quantity: 2m, unitCost: 120m);

        var deliveryId = await DeliverDirectAsync(db, (_laptop, 1m, new[] { "SN-A" }));
        await RaiseInvoiceAsync(db, deliveryId);

        // A manufacturer's warranty, registered by hand — the kind nobody can compute.
        var warrantyId = await new RegisterWarrantyCommandHandler(db).Handle(
            new RegisterWarrantyCommand(
                WarrantyType: RepairWarrantyType.Manufacturer,
                ProductId: _laptop,
                StartsOn: new DateOnly(2026, 1, 1),
                EndsOn: new DateOnly(2027, 1, 1),
                SerialNumber: "SN-A"),
            CancellationToken.None);

        var ticketId = await BookInAsync(db, "Will not power on", serialNumber: "SN-A");

        await using (var booked = CreateContext(_companyA))
        {
            var ticket = await booked.RepairTickets.FirstAsync(t => t.Id == ticketId);

            // The manufacturer's warranty wins over the shop's own: if someone else will pay for the board,
            // the shop should not be eating it out of its own provision.
            ticket.WarrantyType.Should().Be(RepairWarrantyType.Manufacturer);

            var claim = await booked.WarrantyClaims.SingleAsync(c => c.RepairTicketId == ticketId);
            claim.WarrantyId.Should().Be(warrantyId);
            claim.Status.Should().Be(WarrantyClaimStatus.Open);
        }

        // The technician opens it up and finds liquid damage.
        var claimId = await db.WarrantyClaims
            .Where(c => c.RepairTicketId == ticketId)
            .Select(c => c.Id)
            .FirstAsync();

        await new RejectWarrantyClaimCommandHandler(db, new StubClock()).Handle(
            new RejectWarrantyClaimCommand(claimId, "Liquid damage — not covered by the manufacturer."),
            CancellationToken.None);

        await using var fresh = CreateContext(_companyA);

        var rejected = await fresh.WarrantyClaims.FirstAsync(c => c.Id == claimId);
        rejected.Status.Should().Be(WarrantyClaimStatus.Rejected);

        var chargeable = await fresh.RepairTickets.FirstAsync(t => t.Id == ticketId);

        chargeable.WarrantyType.Should().Be(
            RepairWarrantyType.None,
            "the claim was refused, so the customer is paying after all");
        chargeable.IsWarranty.Should().BeFalse();
    }

    [Fact]
    public async Task An_outsourced_repair_moves_no_stock_but_costs_real_money()
    {
        // §29. The device belongs to the customer; sending it to a vendor does not make it the vendor's,
        // and it was never the shop's to move. What lands on the ticket is a cost.
        await using var db = CreateContext(_companyA);

        var ticketId = await BookInAsync(db, "Motherboard fault");

        await BeginDiagnosisAsync(db, ticketId);
        await DiagnoseAsync(db, ticketId, "Board-level. Send it out.", estimate: 800m);
        await ApproveAsync(db, ticketId);

        var outsourcingId = await new SendToVendorCommandHandler(db, Tenant(), new StubClock()).Handle(
            new SendToVendorCommand(
                RepairTicketId: ticketId,
                VendorSupplierId: _vendor,
                EstimatedCost: 100m,
                CurrencyCode: "USD",
                ExchangeRate: 3.67m),
            CancellationToken.None);

        // The vendor does the work and bills USD 100.
        await new ReceiveFromVendorCommandHandler(db, new StubClock()).Handle(
            new ReceiveFromVendorCommand(outsourcingId, Cost: 100m),
            CancellationToken.None);

        await LogLabourAsync(db, ticketId, "Reassembly and testing", hours: 1m, rate: 150m);

        await using var fresh = CreateContext(_companyA);

        var ticket = await fresh.RepairTickets
            .Include(t => t.Parts)
            .Include(t => t.Labour)
            .Include(t => t.Outsourcings)
            .FirstAsync(t => t.Id == ticketId);

        // USD 100 at 3.67. The margin has to be in the money the shop keeps its books in.
        ticket.OutsourcingCost.Should().Be(367m);
        ticket.TotalCost.Should().Be(367m);
        ticket.ChargeableTotal.Should().Be(150m, "one hour of labour");
        ticket.GrossProfit.Should().Be(-217m, "the shop quoted 800 and outsourced it for 367 — but only billed the labour");

        // Nothing moved on any shelf.
        var movements = await fresh.StockMovements.CountAsync();
        movements.Should().Be(0, "an outsourced repair touches no inventory at all");
    }

    [Fact]
    public async Task A_part_taken_back_out_goes_back_on_the_shelf()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _screen, quantity: 2m, unitCost: 120m);

        var ticketId = await BookInAsync(db, "Cracked screen");

        await BeginDiagnosisAsync(db, ticketId);
        await DiagnoseAsync(db, ticketId, "Replace the screen.", estimate: 500m);
        await ApproveAsync(db, ticketId);

        var partId = await ConsumePartAsync(db, ticketId, _screen, 1m);

        await using (var mid = CreateContext(_companyA))
        {
            var balance = await mid.StockBalances.SingleAsync(b => b.ProductId == _screen);
            balance.Quantity.Should().Be(1m);
        }

        // Wrong part. Take it back out.
        await new ReturnPartCommandHandler(db, Ledger(db), new StubClock()).Handle(
            new ReturnPartCommand(partId, "Wrong panel — ordered the other one."),
            CancellationToken.None);

        await using var fresh = CreateContext(_companyA);

        var after = await fresh.StockBalances.SingleAsync(b => b.ProductId == _screen);
        after.Quantity.Should().Be(2m, "it is back on the shelf");

        // A real movement, not an UPDATE undoing the first one: the ledger is append-only, and a shop that
        // could erase a consumption could erase the evidence that a part ever went missing.
        var movements = await fresh.StockMovements
            .Where(m => m.ProductId == _screen)
            .ToListAsync();

        movements.Should().HaveCount(3, "receipt, consumption, return");
        movements.Should().ContainSingle(m => m.Type == MovementType.RepairReturn);

        var part = await fresh.RepairParts.FirstAsync(p => p.Id == partId);

        part.IsReturned.Should().BeTrue();
        part.CostTotal.Should().Be(0m, "it went back, so it is not a cost of this job");

        await AssertBalanceAuditCleanAsync(fresh);
    }

    [Fact]
    public async Task A_job_cannot_be_billed_twice()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _screen, quantity: 2m, unitCost: 120m);

        var ticketId = await BookInAsync(db, "Cracked screen");

        await BeginDiagnosisAsync(db, ticketId);
        await DiagnoseAsync(db, ticketId, "Replace the screen.", estimate: 500m);
        await ApproveAsync(db, ticketId);
        await ConsumePartAsync(db, ticketId, _screen, 1m);

        await BillAsync(db, ticketId);

        var again = async () => await BillAsync(db, ticketId);

        // Bill it twice and the customer owes for the same repair twice — and the second invoice would look
        // exactly as legitimate as the first.
        await again.Should().ThrowAsync<DomainException>().WithMessage("*already been invoiced*");

        await using var fresh = CreateContext(_companyA);

        var invoices = await fresh.SalesInvoices.CountAsync();
        invoices.Should().Be(1);
    }

    [Fact]
    public async Task A_declined_estimate_sends_the_machine_home_untouched()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, quantity: 1m, unitCost: 1_200m, serials: ["SN-A"]);

        var deliveryId = await DeliverDirectAsync(db, (_laptop, 1m, new[] { "SN-A" }));
        await RaiseInvoiceAsync(db, deliveryId);

        // Two years on, out of warranty.
        var ticketId = await BookInAsync(db, "Battery dead", serialNumber: "SN-A", at: new DateTimeOffset(2028, 7, 13, 9, 0, 0, TimeSpan.Zero));

        await BeginDiagnosisAsync(db, ticketId);
        await DiagnoseAsync(db, ticketId, "New battery needed.", estimate: 400m);

        await new DeclineEstimateCommandHandler(db, new StubUser(), new StubClock()).Handle(
            new DeclineEstimateCommand(ticketId, "Too expensive — customer will live with it."),
            CancellationToken.None);

        await using var fresh = CreateContext(_companyA);

        var ticket = await fresh.RepairTickets.FirstAsync(t => t.Id == ticketId);

        ticket.WarrantyType.Should().Be(RepairWarrantyType.None, "the shop warranty ran out a year ago");
        ticket.Status.Should().Be(
            RepairTicketStatus.Cancelled,
            "a job that ended in a shrug is not a job that ended in a fix");
        ticket.CancelledReason.Should().Contain("Too expensive");

        // The machine goes back to the customer — to Sold, where it was, and emphatically not to InStock,
        // which would put a customer's own laptop back on the shelf to be sold to someone else.
        var serial = await fresh.Serials.FirstAsync(s => s.SerialNumber == "SN-A");
        serial.Status.Should().Be(SerialStatus.Sold);

        var movements = await fresh.StockMovements
            .CountAsync(m => m.Type == MovementType.RepairConsumption);

        movements.Should().Be(0, "nothing was ever fitted to it");
    }

    [Fact]
    public async Task The_read_model_reports_the_job_and_its_margin()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _screen, quantity: 2m, unitCost: 120m);

        var ticketId = await BookInAsync(db, "Cracked screen");

        await BeginDiagnosisAsync(db, ticketId);
        await DiagnoseAsync(db, ticketId, "Replace the screen.", estimate: 500m);
        await ApproveAsync(db, ticketId);
        await ConsumePartAsync(db, ticketId, _screen, 1m);
        await LogLabourAsync(db, ticketId, "Screen replacement", hours: 2m, rate: 150m);

        await using var fresh = CreateContext(_companyA);

        var dto = await new GetRepairTicketByIdQueryHandler(fresh).Handle(
            new GetRepairTicketByIdQuery(ticketId), CancellationToken.None);

        // The prefix, not the sequence: every test in this class shares one database, so the numbers keep
        // climbing. Pinning "REP-00001" here would make this test depend on the order the others ran in.
        dto.Number.Should().StartWith("REP-");
        dto.CustomerName.Should().Be("Omar Trading");
        dto.Status.Should().Be(RepairTicketStatus.InRepair);
        dto.Parts.Should().HaveCount(1);
        dto.Labour.Should().HaveCount(1);
        dto.Diagnoses.Should().HaveCount(1);

        dto.PartsCost.Should().Be(120m);
        dto.ChargeableTotal.Should().Be(500m);
        dto.GrossProfit.Should().Be(380m);
        dto.SalesInvoiceId.Should().BeNull("it has not been billed yet");

        // The pending-repairs report (§35): everything not yet delivered or cancelled.
        var open = await new GetRepairTicketsQueryHandler(fresh).Handle(
            new GetRepairTicketsQuery(OpenOnly: true), CancellationToken.None);

        open.Items.Should().ContainSingle(t => t.Id == ticketId);
    }

    [Fact]
    public async Task A_serial_the_shop_never_sold_is_simply_not_under_warranty()
    {
        // A customer may bring in anything. Refusing the job because the machine is not in the catalogue
        // would turn the catalogue into a gatekeeper it was never meant to be.
        await using var db = CreateContext(_companyA);

        var ticketId = await BookInAsync(db, "Fan noise", serialNumber: "NOT-OURS-123");

        await using var fresh = CreateContext(_companyA);

        var ticket = await fresh.RepairTickets.FirstAsync(t => t.Id == ticketId);

        ticket.WarrantyType.Should().Be(RepairWarrantyType.None);
        ticket.DeviceSerialId.Should().BeNull("the shop has never seen this machine");
        ticket.DeviceSerialNumber.Should().Be("NOT-OURS-123", "but the number is written down anyway");
        ticket.Status.Should().Be(RepairTicketStatus.Received, "the job is booked in regardless");
    }

    [Fact]
    public async Task Company_B_cannot_see_company_As_repair_jobs()
    {
        // The cross-tenant gate, which runs at every phase boundary. One database holds every company's
        // data; this test is the only thing standing between them.
        await using var db = CreateContext(_companyA);

        var ticketId = await BookInAsync(db, "Cracked screen");

        var companyB = Guid.NewGuid();

        await using var seed = CreateContext(null);

        seed.Companies.Add(new Company
        {
            Id = companyB,
            Name = "Rival Computers",
            Code = "RIVAL",
            BaseCurrency = "AED",
            TimeZone = "Asia/Dubai"
        });

        await seed.SaveChangesAsync();

        await using var asB = CreateContext(companyB);

        // Holding A's exact id.
        var byId = await asB.RepairTickets.FirstOrDefaultAsync(t => t.Id == ticketId);
        byId.Should().BeNull("company B cannot read company A's repair job even knowing its id");

        var all = await asB.RepairTickets.CountAsync();
        all.Should().Be(0);

        var parts = await asB.RepairParts.CountAsync();
        parts.Should().Be(0);

        var claims = await asB.WarrantyClaims.CountAsync();
        claims.Should().Be(0);

        var history = await asB.RepairStatusChanges.CountAsync();
        history.Should().Be(0);

        // And the query handler refuses too — the filter is not merely on the DbSet.
        var query = async () => await new GetRepairTicketByIdQueryHandler(asB).Handle(
            new GetRepairTicketByIdQuery(ticketId), CancellationToken.None);

        await query.Should().ThrowAsync<Common.Exceptions.NotFoundException>();
    }

    // --- Helpers -----------------------------------------------------------------------------------

    /// <summary>
    /// Recomputes <c>stock_balances</c> from <c>stock_movements</c> — the P3 audit. If repairs wrote stock
    /// anywhere but through the ledger, this is what catches it.
    /// </summary>
    private async Task AssertBalanceAuditCleanAsync(ApplicationDbContext db)
    {
        var audit = await new BalanceAuditor(db).AuditAsync();

        audit.Agrees.Should().BeTrue(
            "the balance cache must still agree with the ledger — a disagreement means something wrote "
            + "stock outside IStockLedger");

        audit.Discrepancies.Should().BeEmpty();
    }

    private async Task ReceiveAsync(
        ApplicationDbContext db,
        Guid productId,
        decimal quantity,
        decimal unitCost,
        IReadOnlyCollection<string>? serials = null)
    {
        await using var transaction = await db.BeginTransactionAsync();

        await Ledger(db).PostAsync(new StockPosting(
            WarehouseId: _warehouseOfA,
            BranchId: _branchOfA,
            ProductId: productId,
            Type: MovementType.Receipt,
            Quantity: quantity,
            ReferenceType: StockReferenceType.GoodsReceipt,
            UnitCost: unitCost,
            SerialNumbers: serials));

        await transaction.CommitAsync();
    }

    private async Task<Guid> DeliverDirectAsync(
        ApplicationDbContext db,
        params (Guid ProductId, decimal Quantity, string[]? Serials)[] lines) =>
        await new DeliverGoodsCommandHandler(db, Ledger(db), Numbers(db), new StubClock())
            .Handle(
                new DeliverGoodsCommand(
                    BranchId: _branchOfA,
                    WarehouseId: _warehouseOfA,
                    Lines: lines.Select(l => new DeliverLine(l.ProductId, l.Quantity, SerialNumbers: l.Serials)).ToList(),
                    CustomerId: _customer),
                CancellationToken.None);

    private async Task<Guid> RaiseInvoiceAsync(ApplicationDbContext db, Guid deliveryId) =>
        await new RaiseInvoiceCommandHandler(
                db, Tenant(), Pricer(db), Discounts(db), Numbers(db), new StubClock())
            .Handle(new RaiseInvoiceCommand(deliveryId), CancellationToken.None);

    private async Task<Guid> BookInAsync(
        ApplicationDbContext db,
        string fault,
        string? serialNumber = null,
        DateTimeOffset? at = null) =>
        await new BookInDeviceCommandHandler(db, new WarrantyLookup(db), Numbers(db), new StubClock())
            .Handle(
                new BookInDeviceCommand(
                    CustomerId: _customer,
                    BranchId: _branchOfA,
                    ReportedFault: fault,
                    DeviceSerialNumber: serialNumber,
                    ReceivedAt: at),
                CancellationToken.None);

    private async Task BeginDiagnosisAsync(ApplicationDbContext db, Guid ticketId) =>
        await new BeginDiagnosisCommandHandler(db, new StubClock())
            .Handle(new BeginDiagnosisCommand(ticketId), CancellationToken.None);

    private async Task DiagnoseAsync(
        ApplicationDbContext db,
        Guid ticketId,
        string findings,
        decimal? estimate) =>
        await new RecordDiagnosisCommandHandler(db, new StubClock())
            .Handle(
                new RecordDiagnosisCommand(ticketId, findings, EstimatedCost: estimate),
                CancellationToken.None);

    private async Task ApproveAsync(ApplicationDbContext db, Guid ticketId) =>
        await new ApproveEstimateCommandHandler(db, new StubUser(), new StubClock())
            .Handle(new ApproveEstimateCommand(ticketId), CancellationToken.None);

    private async Task<Guid> ConsumePartAsync(
        ApplicationDbContext db,
        Guid ticketId,
        Guid productId,
        decimal quantity) =>
        await new ConsumePartCommandHandler(db, Ledger(db), Pricer(db), new StubClock())
            .Handle(
                new ConsumePartCommand(ticketId, productId, _warehouseOfA, quantity),
                CancellationToken.None);

    private async Task<Guid> LogLabourAsync(
        ApplicationDbContext db,
        Guid ticketId,
        string description,
        decimal hours,
        decimal rate) =>
        await new LogLabourCommandHandler(db, new StubUser(), new StubClock())
            .Handle(
                new LogLabourCommand(ticketId, description, hours, rate),
                CancellationToken.None);

    private async Task TestAsync(ApplicationDbContext db, Guid ticketId) =>
        await new BeginTestingCommandHandler(db, new StubUser(), new StubClock())
            .Handle(new BeginTestingCommand(ticketId), CancellationToken.None);

    private async Task ReadyAsync(ApplicationDbContext db, Guid ticketId) =>
        await new MarkReadyCommandHandler(db, new StubUser(), new StubClock())
            .Handle(new MarkReadyCommand(ticketId), CancellationToken.None);

    private async Task DeliverAsync(ApplicationDbContext db, Guid ticketId) =>
        await new DeliverDeviceCommandHandler(db, new StubUser(), new StubClock())
            .Handle(new DeliverDeviceCommand(ticketId), CancellationToken.None);

    private async Task<Guid> BillAsync(ApplicationDbContext db, Guid ticketId) =>
        await new BillRepairCommandHandler(
                db, Tenant(), new TaxResolver(db, new StubClock()), Numbers(db), new StubClock())
            .Handle(new BillRepairCommand(ticketId), CancellationToken.None);

    private SalesLinePricer Pricer(ApplicationDbContext db) =>
        new(new PriceResolver(db, new StubClock()), new TaxResolver(db, new StubClock()));

    private static DiscountAuthorizer Discounts(ApplicationDbContext db, bool mayApprove = true) =>
        new(db, new StubPermissions(mayApprove), new StubUser());

    private StockLedger Ledger(ApplicationDbContext db) =>
        new(
            new ApplicationDbContextAccessor(db),
            db,
            new StubTenant(_companyA),
            new StubClock(),
            new WeightedAverageCosting());

    private DocumentNumberGenerator Numbers(ApplicationDbContext db) =>
        new(new ApplicationDbContextAccessor(db), db, new StubTenant(_companyA), new StubClock());

    private StubTenant Tenant() => new(_companyA);

    private ApplicationDbContext CreateContext(Guid? companyId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations", "techstorepro"))
            .Options;

        return new ApplicationDbContext(options, new StubTenant(companyId), new StubUser(), new StubClock());
    }

    private sealed class StubTenant(Guid? companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool HasTenant => CompanyId.HasValue;
    }

    private sealed class StubPermissions(bool mayApprove) : IPermissionService
    {
        public Task<IReadOnlyCollection<PermissionGrant>> GetGrantsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<PermissionGrant>>([]);

        public Task<bool> HasPermissionAsync(string feature, PermissionAction action, CancellationToken ct = default) =>
            Task.FromResult(action != PermissionAction.Approve || mayApprove);

        public Task DemandAsync(string feature, PermissionAction action, CancellationToken ct = default) =>
            Task.CompletedTask;

        public void InvalidateCache(Guid companyUserId)
        {
        }
    }

    private sealed class StubUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? Username => "tests";
        public bool IsAuthenticated => true;
        public bool IsPlatformAdmin => false;
        public string? IpAddress => "203.0.113.7";
        public string? UserAgent => "tests";
    }

    /// <summary>Fixed at 2026 so the document-number sequences seeded for that year are the ones used.</summary>
    private sealed class StubClock : IDateTime
    {
        public DateTimeOffset UtcNow => new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);
    }
}
