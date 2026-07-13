using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Purchasing;
using FluentAssertions;
using Xunit;

namespace TechStorePro.Domain.Tests.Purchasing;

/// <summary>
/// The purchase order — which the business may not use at all (requirements §25: "PO is optional").
/// </summary>
public class PurchaseOrderTests
{
    private static PurchaseOrder Order(PurchaseOrderStatus status = PurchaseOrderStatus.Draft, params PurchaseOrderLine[] lines) =>
        new()
        {
            Number = "PO-2026-00001",
            SupplierId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            Status = status,
            OrderedAt = DateTimeOffset.UnixEpoch,
            Lines = lines.Length > 0
                ? [.. lines]
                : [new PurchaseOrderLine { ProductId = Guid.NewGuid(), Quantity = 10, UnitPrice = 100m }]
        };

    [Fact]
    public void An_order_with_no_lines_orders_nothing()
    {
        var order = Order();
        order.Lines.Clear();

        var act = order.Validate;

        act.Should().Throw<DomainException>().WithMessage("*orders nothing*");
    }

    [Fact]
    public void A_line_total_takes_the_discount_off()
    {
        var line = new PurchaseOrderLine { Quantity = 10, UnitPrice = 100m, DiscountPercent = 10m };

        line.LineTotal.Should().Be(900m);
    }

    [Fact]
    public void An_order_that_has_started_arriving_cannot_be_cancelled()
    {
        // The stock is on the shelf and the supplier will invoice for it. Cancelling would leave a
        // receipt pointing at an order that claims it never happened.
        var order = Order(
            PurchaseOrderStatus.PartiallyReceived,
            new PurchaseOrderLine { ProductId = Guid.NewGuid(), Quantity = 10, UnitPrice = 100m, ReceivedQuantity = 3 });

        var act = order.Cancel;

        act.Should().Throw<DomainException>().WithMessage("*already been received*");
    }

    [Fact]
    public void An_untouched_order_can_be_cancelled()
    {
        var order = Order(PurchaseOrderStatus.Approved);

        order.Cancel();

        order.Status.Should().Be(PurchaseOrderStatus.Cancelled);
    }

    [Fact]
    public void Receipt_status_tracks_what_has_actually_turned_up()
    {
        var line = new PurchaseOrderLine { ProductId = Guid.NewGuid(), Quantity = 10, UnitPrice = 100m };
        var order = Order(PurchaseOrderStatus.Approved, line);

        order.RefreshReceiptStatus();
        order.Status.Should().Be(PurchaseOrderStatus.Approved, "nothing has arrived");

        line.ReceivedQuantity = 4;
        order.RefreshReceiptStatus();
        order.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        line.OutstandingQuantity.Should().Be(6);

        line.ReceivedQuantity = 10;
        order.RefreshReceiptStatus();
        order.Status.Should().Be(PurchaseOrderStatus.Received);
        line.OutstandingQuantity.Should().Be(0);
    }

    [Fact]
    public void Over_delivery_does_not_leave_a_negative_outstanding()
    {
        // Suppliers over-ship. "Minus two units outstanding" would read as a debt the supplier owes.
        var line = new PurchaseOrderLine { Quantity = 10, ReceivedQuantity = 12 };

        line.OutstandingQuantity.Should().Be(0);
    }

    [Fact]
    public void An_exchange_rate_of_zero_is_refused()
    {
        // It would value the entire order at nothing, and the stock it receives at nothing after that.
        var order = Order();
        order.ExchangeRate = 0m;

        var act = order.Validate;

        act.Should().Throw<DomainException>().WithMessage("*greater than zero*");
    }
}

/// <summary>The goods receipt: the document that actually moves stock, and works without a PO.</summary>
public class GoodsReceiptTests
{
    private static GoodsReceipt Receipt(decimal exchangeRate = 1m, params GoodsReceiptLine[] lines)
    {
        var receipt = new GoodsReceipt
        {
            Number = "GRN-2026-00001",
            SupplierId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            ExchangeRate = exchangeRate,
            ReceivedAt = DateTimeOffset.UnixEpoch,
            Lines = lines.Length > 0
                ? [.. lines]
                : [new GoodsReceiptLine { ProductId = Guid.NewGuid(), Quantity = 10, UnitPrice = 100m }]
        };

        foreach (var line in receipt.Lines)
        {
            line.GoodsReceipt = receipt;
        }

        return receipt;
    }

    [Fact]
    public void A_receipt_needs_no_purchase_order()
    {
        // Requirements §25's direct-purchase flow. A shop that drives to the wholesaler and comes back
        // with a box has no PO and never will — and forcing one would only produce fake orders raised
        // after the fact, which is worse, because they look real.
        var receipt = Receipt();

        receipt.PurchaseOrderId.Should().BeNull();

        var act = receipt.Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void A_receipt_with_no_lines_receives_nothing()
    {
        var receipt = Receipt();
        receipt.Lines.Clear();

        var act = receipt.Validate;

        act.Should().Throw<DomainException>().WithMessage("*receives nothing*");
    }

    [Fact]
    public void The_landed_unit_cost_is_the_goods_price_plus_its_share_of_the_container()
    {
        // The number D1 warned about: this feeds the moving average, so an error here spreads to every
        // existing unit of the product and never washes out.
        var line = new GoodsReceiptLine { ProductId = Guid.NewGuid(), Quantity = 10, UnitPrice = 1_000m };
        var receipt = Receipt(exchangeRate: 1m, line);

        line.ApportionedCost = 2_000m;   // its share of AED 3,000 of freight, duty and clearing

        line.LandedUnitCost.Should().Be(1_200m);
    }

    [Fact]
    public void A_foreign_currency_receipt_lands_in_base_currency()
    {
        // USD 1,000 a unit at 3.67, plus AED 2,000 of local charges over ten units.
        //   goods:  10 × 1000 × 3.67 = 36,700
        //   plus                        2,000
        //   over ten units           =  3,870 a unit
        var line = new GoodsReceiptLine { ProductId = Guid.NewGuid(), Quantity = 10, UnitPrice = 1_000m };
        var receipt = Receipt(exchangeRate: 3.67m, line);

        line.ApportionedCost = 2_000m;

        receipt.GoodsTotal.Should().Be(10_000m, "in the supplier's money");
        receipt.GoodsTotalBase.Should().Be(36_700m, "in the company's money");
        line.LandedUnitCost.Should().Be(3_870m);
    }

    [Fact]
    public void A_local_purchase_carries_no_apportioned_cost()
    {
        var line = new GoodsReceiptLine { ProductId = Guid.NewGuid(), Quantity = 4, UnitPrice = 25m };
        Receipt(exchangeRate: 1m, line);

        line.ApportionedCost.Should().Be(0m, "nothing was shipped, so nothing was apportioned");
        line.LandedUnitCost.Should().Be(25m);
    }
}

/// <summary>The container, and the fact that goods and their true cost do not arrive together.</summary>
public class ImportShipmentTests
{
    private static ImportShipment Shipment(
        ImportShipmentStatus status = ImportShipmentStatus.Arrived,
        params ImportShipmentCharge[] charges) =>
        new()
        {
            Number = "IMP-2026-00001",
            SupplierId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            Status = status,
            Charges = charges.Length > 0
                ? [.. charges]
                : [new ImportShipmentCharge { Type = ImportChargeType.Freight, Amount = 3_000m }]
        };

    [Fact]
    public void Charges_in_several_currencies_add_up_in_base_currency()
    {
        // Freight billed in USD by the shipping line; duty billed in AED by the customs authority. A
        // container's charges cannot be added up in any other currency than the company's own.
        var shipment = Shipment(
            ImportShipmentStatus.Arrived,
            new ImportShipmentCharge { Type = ImportChargeType.Freight, Amount = 1_000m, CurrencyCode = "USD", ExchangeRate = 3.67m },
            new ImportShipmentCharge { Type = ImportChargeType.Customs, Amount = 500m, CurrencyCode = "AED", ExchangeRate = 1m });

        shipment.TotalChargesBase.Should().Be(4_170m);
    }

    [Fact]
    public void A_shipment_cannot_be_costed_before_the_goods_arrive()
    {
        // There is nothing for the charges to attach to. The revaluation would have no stock to raise.
        var shipment = Shipment(ImportShipmentStatus.InTransit);

        var act = () => shipment.MarkCosted(null, DateTimeOffset.UnixEpoch, 0m);

        act.Should().Throw<DomainException>().WithMessage("*have not been received*");
    }

    [Fact]
    public void A_shipment_cannot_be_costed_twice()
    {
        // The single worst thing this module could do: fold the freight into the moving average a
        // second time, doubling it on every unit — and because the average is moving, it would never
        // wash out.
        var shipment = Shipment();
        shipment.MarkCosted(null, DateTimeOffset.UnixEpoch, 0m);

        var act = () => shipment.MarkCosted(null, DateTimeOffset.UnixEpoch, 0m);

        act.Should().Throw<DomainException>().WithMessage("*already been costed*");
    }

    [Fact]
    public void A_shipment_with_no_charges_has_nothing_to_apportion()
    {
        var shipment = Shipment(ImportShipmentStatus.Arrived);
        shipment.Charges.Clear();

        var act = () => shipment.MarkCosted(null, DateTimeOffset.UnixEpoch, 0m);

        act.Should().Throw<DomainException>().WithMessage("*no charges*");
    }

    [Fact]
    public void A_costed_shipment_cannot_be_cancelled_away()
    {
        // Its money is already inside the moving average. Cancelling the document would not take it
        // back out — it would just remove the explanation.
        var shipment = Shipment();
        shipment.MarkCosted(null, DateTimeOffset.UnixEpoch, 0m);

        var act = shipment.Cancel;

        act.Should().Throw<DomainException>().WithMessage("*already in inventory*");
    }

    [Fact]
    public void Cost_that_no_stock_was_left_to_absorb_is_recorded_rather_than_dropped()
    {
        // The container sold out before the clearing agent invoiced. That money is a real expense; it
        // simply has nowhere in inventory to live. Dropping it would overstate margin, and smearing it
        // over the next container would charge one shipment's freight to another's goods.
        var shipment = Shipment();

        shipment.MarkCosted(null, DateTimeOffset.UnixEpoch, unabsorbed: 250m);

        shipment.UnabsorbedCost.Should().Be(250m);
        shipment.Status.Should().Be(ImportShipmentStatus.Costed);
    }

    [Fact]
    public void A_negative_charge_is_refused()
    {
        var charge = new ImportShipmentCharge { Type = ImportChargeType.Freight, Amount = -1m };

        var act = charge.Validate;

        act.Should().Throw<DomainException>().WithMessage("*cannot be negative*");
    }
}

/// <summary>Supplier invoices, payments, and the FX gain a shop makes by owing money in dollars.</summary>
public class SupplierSettlementTests
{
    private static SupplierInvoice Invoice(decimal total = 1_000m, decimal rate = 1m) =>
        new()
        {
            Number = "SI-2026-00001",
            SupplierReference = "INV-9912",
            SupplierId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            Status = SupplierInvoiceStatus.Posted,
            CurrencyCode = rate == 1m ? "AED" : "USD",
            ExchangeRate = rate,
            InvoicedAt = DateTimeOffset.UnixEpoch,
            Lines = [new SupplierInvoiceLine { Description = "Laptops", Quantity = 1, UnitPrice = total }]
        };

    [Fact]
    public void An_invoice_needs_the_suppliers_own_reference()
    {
        // Without it nobody can match this row to the piece of paper the supplier will chase with.
        var invoice = Invoice();
        invoice.SupplierReference = "  ";

        var act = invoice.Validate;

        act.Should().Throw<DomainException>().WithMessage("*supplier's own reference*");
    }

    [Fact]
    public void Tax_and_discount_compose_in_the_order_an_accountant_expects()
    {
        // Discount first, then tax on the discounted amount. The other order overstates the tax.
        var line = new SupplierInvoiceLine
        {
            Description = "Laptops",
            Quantity = 10,
            UnitPrice = 100m,
            DiscountPercent = 10m,
            TaxPercent = 5m
        };

        line.NetTotal.Should().Be(900m);
        line.TaxAmount.Should().Be(45m);
        line.LineTotal.Should().Be(945m);
    }

    [Fact]
    public void One_payment_can_settle_several_invoices()
    {
        // Which is why a payment is a header plus allocations, and not an invoice_id column.
        var first = Invoice(600m);
        var second = Invoice(400m);

        var payment = new SupplierPayment
        {
            Number = "SPY-2026-00001",
            SupplierId = first.SupplierId,
            BranchId = Guid.NewGuid(),
            PaymentMethodId = Guid.NewGuid(),
            Amount = 1_000m,
            PaidAt = DateTimeOffset.UnixEpoch,
            Allocations =
            [
                new SupplierPaymentAllocation { SupplierInvoiceId = first.Id, Amount = 600m },
                new SupplierPaymentAllocation { SupplierInvoiceId = second.Id, Amount = 400m }
            ]
        };

        payment.Validate();

        payment.AllocatedAmount.Should().Be(1_000m);
        payment.UnallocatedAmount.Should().Be(0m);
    }

    [Fact]
    public void An_invoice_can_be_settled_by_instalments()
    {
        var invoice = Invoice(1_000m);

        invoice.Allocations.Add(new SupplierPaymentAllocation { Amount = 400m });
        invoice.RefreshPaymentStatus();

        invoice.Status.Should().Be(SupplierInvoiceStatus.PartiallyPaid);
        invoice.OutstandingAmount.Should().Be(600m);

        invoice.Allocations.Add(new SupplierPaymentAllocation { Amount = 600m });
        invoice.RefreshPaymentStatus();

        invoice.Status.Should().Be(SupplierInvoiceStatus.Paid);
        invoice.IsSettled.Should().BeTrue();
    }

    [Fact]
    public void A_payment_cannot_allocate_more_than_was_actually_paid()
    {
        // Otherwise the shop would appear to have settled more debt than money left the bank, and the
        // supplier's balance would drift quietly in the shop's favour.
        var payment = new SupplierPayment
        {
            Number = "SPY-2026-00001",
            SupplierId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            PaymentMethodId = Guid.NewGuid(),
            Amount = 500m,
            PaidAt = DateTimeOffset.UnixEpoch,
            Allocations = [new SupplierPaymentAllocation { Amount = 600m }]
        };

        var act = payment.Validate;

        act.Should().Throw<DomainException>().WithMessage("*allocates 600 but only 500*");
    }

    [Fact]
    public void An_advance_to_a_supplier_is_a_real_state_not_an_error()
    {
        // Money paid before the invoice arrived. It becomes a credit the supplier owes back.
        var payment = new SupplierPayment
        {
            Number = "SPY-2026-00001",
            SupplierId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            PaymentMethodId = Guid.NewGuid(),
            Amount = 5_000m,
            PaidAt = DateTimeOffset.UnixEpoch
        };

        payment.Validate();

        payment.UnallocatedAmount.Should().Be(5_000m);
    }

    [Fact]
    public void A_shop_that_owes_dollars_makes_a_gain_when_the_dirham_strengthens()
    {
        // USD 1,000 invoiced at 3.67 is a debt of AED 3,670. Pay it at 3.60 and only AED 3,600 leaves
        // the bank: the company is AED 70 better off. That gain came from the currency moving, not from
        // selling anything — which is exactly why it does NOT go back into the cost of the stock. The
        // laptops did not become cheaper to buy.
        var allocation = new SupplierPaymentAllocation
        {
            Amount = 1_000m,
            InvoiceExchangeRate = 3.67m,
            PaymentExchangeRate = 3.60m
        };

        allocation.ExchangeGainOrLoss.Should().Be(70m);
    }

    [Fact]
    public void And_a_loss_when_it_weakens()
    {
        var allocation = new SupplierPaymentAllocation
        {
            Amount = 1_000m,
            InvoiceExchangeRate = 3.60m,
            PaymentExchangeRate = 3.67m
        };

        allocation.ExchangeGainOrLoss.Should().Be(-70m);
    }

    [Fact]
    public void Paying_in_the_companys_own_currency_can_never_produce_an_fx_result()
    {
        var allocation = new SupplierPaymentAllocation
        {
            Amount = 1_000m,
            InvoiceExchangeRate = 1m,
            PaymentExchangeRate = 1m
        };

        allocation.ExchangeGainOrLoss.Should().Be(0m);
    }

    [Fact]
    public void An_invoice_with_money_against_it_cannot_be_cancelled()
    {
        var invoice = Invoice();
        invoice.Allocations.Add(new SupplierPaymentAllocation { Amount = 100m });

        var act = invoice.Cancel;

        act.Should().Throw<DomainException>().WithMessage("*already been paid*");
    }
}
