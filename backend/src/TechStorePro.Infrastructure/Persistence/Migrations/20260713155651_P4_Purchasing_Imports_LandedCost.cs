using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStorePro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class P4_Purchasing_Imports_LandedCost : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "value_adjustment",
            schema: "techstorepro",
            table: "stock_movements",
            type: "numeric(18,4)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.CreateTable(
            name: "import_shipments",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<short>(type: "smallint", nullable: false),
                transport_document = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                vessel_or_flight = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                port_of_loading = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                port_of_discharge = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                expected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                arrived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                costed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                costed_by = table.Column<Guid>(type: "uuid", nullable: true),
                unabsorbed_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_import_shipments", x => x.id);
                table.ForeignKey(
                    name: "fk_import_shipments_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_import_shipments_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalSchema: "techstorepro",
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "purchase_orders",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<short>(type: "smallint", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                exchange_rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                ordered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_purchase_orders", x => x.id);
                table.ForeignKey(
                    name: "fk_purchase_orders_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_purchase_orders_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalSchema: "techstorepro",
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_purchase_orders_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "supplier_payments",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_method_id = table.Column<Guid>(type: "uuid", nullable: false),
                reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                exchange_rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_supplier_payments", x => x.id);
                table.ForeignKey(
                    name: "fk_supplier_payments_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_supplier_payments_payment_methods_payment_method_id",
                    column: x => x.payment_method_id,
                    principalSchema: "techstorepro",
                    principalTable: "payment_methods",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_supplier_payments_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalSchema: "techstorepro",
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "import_shipment_charges",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                import_shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<short>(type: "smallint", nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                vendor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                exchange_rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                incurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_import_shipment_charges", x => x.id);
                table.ForeignKey(
                    name: "fk_import_shipment_charges_import_shipments_import_shipment_id",
                    column: x => x.import_shipment_id,
                    principalSchema: "techstorepro",
                    principalTable: "import_shipments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "goods_receipts",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                purchase_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                import_shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                exchange_rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                supplier_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_goods_receipts", x => x.id);
                table.ForeignKey(
                    name: "fk_goods_receipts_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_goods_receipts_import_shipments_import_shipment_id",
                    column: x => x.import_shipment_id,
                    principalSchema: "techstorepro",
                    principalTable: "import_shipments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_goods_receipts_purchase_orders_purchase_order_id",
                    column: x => x.purchase_order_id,
                    principalSchema: "techstorepro",
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_goods_receipts_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalSchema: "techstorepro",
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_goods_receipts_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "purchase_order_lines",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                discount_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                received_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_purchase_order_lines_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_purchase_order_lines_purchase_orders_purchase_order_id",
                    column: x => x.purchase_order_id,
                    principalSchema: "techstorepro",
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "supplier_invoices",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                supplier_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                goods_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<short>(type: "smallint", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                exchange_rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                invoiced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_supplier_invoices", x => x.id);
                table.ForeignKey(
                    name: "fk_supplier_invoices_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_supplier_invoices_goods_receipts_goods_receipt_id",
                    column: x => x.goods_receipt_id,
                    principalSchema: "techstorepro",
                    principalTable: "goods_receipts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_supplier_invoices_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalSchema: "techstorepro",
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "goods_receipt_lines",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                goods_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                purchase_order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                discount_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                apportioned_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_goods_receipt_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_goods_receipt_lines_goods_receipts_goods_receipt_id",
                    column: x => x.goods_receipt_id,
                    principalSchema: "techstorepro",
                    principalTable: "goods_receipts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_goods_receipt_lines_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_goods_receipt_lines_purchase_order_lines_purchase_order_lin~",
                    column: x => x.purchase_order_line_id,
                    principalSchema: "techstorepro",
                    principalTable: "purchase_order_lines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "supplier_invoice_lines",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                supplier_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: true),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                discount_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                tax_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_supplier_invoice_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_supplier_invoice_lines_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_supplier_invoice_lines_supplier_invoices_supplier_invoice_id",
                    column: x => x.supplier_invoice_id,
                    principalSchema: "techstorepro",
                    principalTable: "supplier_invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "supplier_payment_allocations",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                supplier_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                supplier_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                invoice_exchange_rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                payment_exchange_rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_supplier_payment_allocations", x => x.id);
                table.ForeignKey(
                    name: "fk_supplier_payment_allocations_supplier_invoices_supplier_inv~",
                    column: x => x.supplier_invoice_id,
                    principalSchema: "techstorepro",
                    principalTable: "supplier_invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_supplier_payment_allocations_supplier_payments_supplier_pay~",
                    column: x => x.supplier_payment_id,
                    principalSchema: "techstorepro",
                    principalTable: "supplier_payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "goods_receipt_serials",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                goods_receipt_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                serial_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_goods_receipt_serials", x => x.id);
                table.ForeignKey(
                    name: "fk_goods_receipt_serials_goods_receipt_lines_goods_receipt_lin~",
                    column: x => x.goods_receipt_line_id,
                    principalSchema: "techstorepro",
                    principalTable: "goods_receipt_lines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipt_lines_company_id_goods_receipt_id",
            schema: "techstorepro",
            table: "goods_receipt_lines",
            columns: new[] { "company_id", "goods_receipt_id" });

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipt_lines_company_id_product_id",
            schema: "techstorepro",
            table: "goods_receipt_lines",
            columns: new[] { "company_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipt_lines_goods_receipt_id",
            schema: "techstorepro",
            table: "goods_receipt_lines",
            column: "goods_receipt_id");

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipt_lines_product_id",
            schema: "techstorepro",
            table: "goods_receipt_lines",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipt_lines_purchase_order_line_id",
            schema: "techstorepro",
            table: "goods_receipt_lines",
            column: "purchase_order_line_id");

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipt_serials_company_id_goods_receipt_line_id",
            schema: "techstorepro",
            table: "goods_receipt_serials",
            columns: new[] { "company_id", "goods_receipt_line_id" });

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipt_serials_goods_receipt_line_id",
            schema: "techstorepro",
            table: "goods_receipt_serials",
            column: "goods_receipt_line_id");

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipts_branch_id",
            schema: "techstorepro",
            table: "goods_receipts",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipts_company_id_import_shipment_id",
            schema: "techstorepro",
            table: "goods_receipts",
            columns: new[] { "company_id", "import_shipment_id" });

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipts_company_id_number",
            schema: "techstorepro",
            table: "goods_receipts",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipts_company_id_supplier_id_received_at",
            schema: "techstorepro",
            table: "goods_receipts",
            columns: new[] { "company_id", "supplier_id", "received_at" });

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipts_import_shipment_id",
            schema: "techstorepro",
            table: "goods_receipts",
            column: "import_shipment_id");

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipts_purchase_order_id",
            schema: "techstorepro",
            table: "goods_receipts",
            column: "purchase_order_id");

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipts_supplier_id",
            schema: "techstorepro",
            table: "goods_receipts",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipts_warehouse_id",
            schema: "techstorepro",
            table: "goods_receipts",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_import_shipment_charges_company_id_import_shipment_id",
            schema: "techstorepro",
            table: "import_shipment_charges",
            columns: new[] { "company_id", "import_shipment_id" });

        migrationBuilder.CreateIndex(
            name: "ix_import_shipment_charges_import_shipment_id",
            schema: "techstorepro",
            table: "import_shipment_charges",
            column: "import_shipment_id");

        migrationBuilder.CreateIndex(
            name: "ix_import_shipments_branch_id",
            schema: "techstorepro",
            table: "import_shipments",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_import_shipments_company_id_number",
            schema: "techstorepro",
            table: "import_shipments",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_import_shipments_company_id_status",
            schema: "techstorepro",
            table: "import_shipments",
            columns: new[] { "company_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_import_shipments_supplier_id",
            schema: "techstorepro",
            table: "import_shipments",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "ix_purchase_order_lines_company_id_purchase_order_id",
            schema: "techstorepro",
            table: "purchase_order_lines",
            columns: new[] { "company_id", "purchase_order_id" });

        migrationBuilder.CreateIndex(
            name: "ix_purchase_order_lines_product_id",
            schema: "techstorepro",
            table: "purchase_order_lines",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_purchase_order_lines_purchase_order_id",
            schema: "techstorepro",
            table: "purchase_order_lines",
            column: "purchase_order_id");

        migrationBuilder.CreateIndex(
            name: "ix_purchase_orders_branch_id",
            schema: "techstorepro",
            table: "purchase_orders",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_purchase_orders_company_id_number",
            schema: "techstorepro",
            table: "purchase_orders",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_purchase_orders_company_id_status",
            schema: "techstorepro",
            table: "purchase_orders",
            columns: new[] { "company_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_purchase_orders_company_id_supplier_id_ordered_at",
            schema: "techstorepro",
            table: "purchase_orders",
            columns: new[] { "company_id", "supplier_id", "ordered_at" });

        migrationBuilder.CreateIndex(
            name: "ix_purchase_orders_supplier_id",
            schema: "techstorepro",
            table: "purchase_orders",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "ix_purchase_orders_warehouse_id",
            schema: "techstorepro",
            table: "purchase_orders",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoice_lines_company_id_supplier_invoice_id",
            schema: "techstorepro",
            table: "supplier_invoice_lines",
            columns: new[] { "company_id", "supplier_invoice_id" });

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoice_lines_product_id",
            schema: "techstorepro",
            table: "supplier_invoice_lines",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoice_lines_supplier_invoice_id",
            schema: "techstorepro",
            table: "supplier_invoice_lines",
            column: "supplier_invoice_id");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoices_branch_id",
            schema: "techstorepro",
            table: "supplier_invoices",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoices_company_id_number",
            schema: "techstorepro",
            table: "supplier_invoices",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoices_company_id_status_due_at",
            schema: "techstorepro",
            table: "supplier_invoices",
            columns: new[] { "company_id", "status", "due_at" });

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoices_company_id_supplier_id_supplier_reference",
            schema: "techstorepro",
            table: "supplier_invoices",
            columns: new[] { "company_id", "supplier_id", "supplier_reference" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoices_goods_receipt_id",
            schema: "techstorepro",
            table: "supplier_invoices",
            column: "goods_receipt_id");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoices_supplier_id",
            schema: "techstorepro",
            table: "supplier_invoices",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_payment_allocations_company_id_supplier_invoice_id",
            schema: "techstorepro",
            table: "supplier_payment_allocations",
            columns: new[] { "company_id", "supplier_invoice_id" });

        migrationBuilder.CreateIndex(
            name: "ix_supplier_payment_allocations_company_id_supplier_payment_id~",
            schema: "techstorepro",
            table: "supplier_payment_allocations",
            columns: new[] { "company_id", "supplier_payment_id", "supplier_invoice_id" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_payment_allocations_supplier_invoice_id",
            schema: "techstorepro",
            table: "supplier_payment_allocations",
            column: "supplier_invoice_id");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_payment_allocations_supplier_payment_id",
            schema: "techstorepro",
            table: "supplier_payment_allocations",
            column: "supplier_payment_id");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_payments_branch_id",
            schema: "techstorepro",
            table: "supplier_payments",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_payments_company_id_number",
            schema: "techstorepro",
            table: "supplier_payments",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_payments_company_id_supplier_id_paid_at",
            schema: "techstorepro",
            table: "supplier_payments",
            columns: new[] { "company_id", "supplier_id", "paid_at" });

        migrationBuilder.CreateIndex(
            name: "ix_supplier_payments_payment_method_id",
            schema: "techstorepro",
            table: "supplier_payments",
            column: "payment_method_id");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_payments_supplier_id",
            schema: "techstorepro",
            table: "supplier_payments",
            column: "supplier_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "goods_receipt_serials",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "import_shipment_charges",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "supplier_invoice_lines",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "supplier_payment_allocations",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "goods_receipt_lines",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "supplier_invoices",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "supplier_payments",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "purchase_order_lines",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "goods_receipts",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "import_shipments",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "purchase_orders",
            schema: "techstorepro");

        migrationBuilder.DropColumn(
            name: "value_adjustment",
            schema: "techstorepro",
            table: "stock_movements");
    }
}
