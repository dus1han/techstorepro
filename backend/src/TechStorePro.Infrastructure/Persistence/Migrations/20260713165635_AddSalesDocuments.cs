using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStorePro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSalesDocuments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "quotations",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<short>(type: "smallint", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                quoted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                table.PrimaryKey("pk_quotations", x => x.id);
                table.ForeignKey(
                    name: "fk_quotations_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_quotations_customers_customer_id",
                    column: x => x.customer_id,
                    principalSchema: "techstorepro",
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "quotation_lines",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                quotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                discount_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                discount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                tax_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                price_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                table.PrimaryKey("pk_quotation_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_quotation_lines_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_quotation_lines_quotations_quotation_id",
                    column: x => x.quotation_id,
                    principalSchema: "techstorepro",
                    principalTable: "quotations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sales_orders",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                quotation_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<short>(type: "smallint", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                ordered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                table.PrimaryKey("pk_sales_orders", x => x.id);
                table.ForeignKey(
                    name: "fk_sales_orders_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_orders_customers_customer_id",
                    column: x => x.customer_id,
                    principalSchema: "techstorepro",
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_orders_quotations_quotation_id",
                    column: x => x.quotation_id,
                    principalSchema: "techstorepro",
                    principalTable: "quotations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_orders_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "deliveries",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                sales_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<short>(type: "smallint", nullable: false),
                delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                delivered_to = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                table.PrimaryKey("pk_deliveries", x => x.id);
                table.ForeignKey(
                    name: "fk_deliveries_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_deliveries_customers_customer_id",
                    column: x => x.customer_id,
                    principalSchema: "techstorepro",
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_deliveries_sales_orders_sales_order_id",
                    column: x => x.sales_order_id,
                    principalSchema: "techstorepro",
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_deliveries_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sales_order_lines",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                sales_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                delivered_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                discount_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                discount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                tax_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                price_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                stock_reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("pk_sales_order_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_sales_order_lines_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_order_lines_sales_orders_sales_order_id",
                    column: x => x.sales_order_id,
                    principalSchema: "techstorepro",
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sales_invoices",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                sales_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                delivery_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<short>(type: "smallint", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
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
                table.PrimaryKey("pk_sales_invoices", x => x.id);
                table.ForeignKey(
                    name: "fk_sales_invoices_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_invoices_customers_customer_id",
                    column: x => x.customer_id,
                    principalSchema: "techstorepro",
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_invoices_deliveries_delivery_id",
                    column: x => x.delivery_id,
                    principalSchema: "techstorepro",
                    principalTable: "deliveries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_invoices_sales_orders_sales_order_id",
                    column: x => x.sales_order_id,
                    principalSchema: "techstorepro",
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "delivery_lines",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                sales_order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
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
                table.PrimaryKey("pk_delivery_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_delivery_lines_deliveries_delivery_id",
                    column: x => x.delivery_id,
                    principalSchema: "techstorepro",
                    principalTable: "deliveries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_delivery_lines_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_delivery_lines_sales_order_lines_sales_order_line_id",
                    column: x => x.sales_order_line_id,
                    principalSchema: "techstorepro",
                    principalTable: "sales_order_lines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "delivery_serials",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                delivery_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                serial_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("pk_delivery_serials", x => x.id);
                table.ForeignKey(
                    name: "fk_delivery_serials_delivery_lines_delivery_line_id",
                    column: x => x.delivery_line_id,
                    principalSchema: "techstorepro",
                    principalTable: "delivery_lines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sales_invoice_lines",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                sales_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                delivery_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                product_id = table.Column<Guid>(type: "uuid", nullable: true),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                discount_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                discount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                tax_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                price_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                discount_approved_by = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("pk_sales_invoice_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_sales_invoice_lines_delivery_lines_delivery_line_id",
                    column: x => x.delivery_line_id,
                    principalSchema: "techstorepro",
                    principalTable: "delivery_lines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_invoice_lines_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_invoice_lines_sales_invoices_sales_invoice_id",
                    column: x => x.sales_invoice_id,
                    principalSchema: "techstorepro",
                    principalTable: "sales_invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_branch_id",
            schema: "techstorepro",
            table: "deliveries",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_company_id_customer_id_delivered_at",
            schema: "techstorepro",
            table: "deliveries",
            columns: new[] { "company_id", "customer_id", "delivered_at" });

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_company_id_number",
            schema: "techstorepro",
            table: "deliveries",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_company_id_sales_order_id",
            schema: "techstorepro",
            table: "deliveries",
            columns: new[] { "company_id", "sales_order_id" });

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_company_id_status",
            schema: "techstorepro",
            table: "deliveries",
            columns: new[] { "company_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_customer_id",
            schema: "techstorepro",
            table: "deliveries",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_sales_order_id",
            schema: "techstorepro",
            table: "deliveries",
            column: "sales_order_id");

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_warehouse_id",
            schema: "techstorepro",
            table: "deliveries",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_delivery_lines_company_id_delivery_id",
            schema: "techstorepro",
            table: "delivery_lines",
            columns: new[] { "company_id", "delivery_id" });

        migrationBuilder.CreateIndex(
            name: "ix_delivery_lines_company_id_product_id",
            schema: "techstorepro",
            table: "delivery_lines",
            columns: new[] { "company_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_delivery_lines_delivery_id",
            schema: "techstorepro",
            table: "delivery_lines",
            column: "delivery_id");

        migrationBuilder.CreateIndex(
            name: "ix_delivery_lines_product_id",
            schema: "techstorepro",
            table: "delivery_lines",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_delivery_lines_sales_order_line_id",
            schema: "techstorepro",
            table: "delivery_lines",
            column: "sales_order_line_id");

        migrationBuilder.CreateIndex(
            name: "ix_delivery_serials_company_id_delivery_line_id",
            schema: "techstorepro",
            table: "delivery_serials",
            columns: new[] { "company_id", "delivery_line_id" });

        migrationBuilder.CreateIndex(
            name: "ix_delivery_serials_company_id_serial_id",
            schema: "techstorepro",
            table: "delivery_serials",
            columns: new[] { "company_id", "serial_id" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_delivery_serials_delivery_line_id",
            schema: "techstorepro",
            table: "delivery_serials",
            column: "delivery_line_id");

        migrationBuilder.CreateIndex(
            name: "ix_quotation_lines_company_id_quotation_id",
            schema: "techstorepro",
            table: "quotation_lines",
            columns: new[] { "company_id", "quotation_id" });

        migrationBuilder.CreateIndex(
            name: "ix_quotation_lines_product_id",
            schema: "techstorepro",
            table: "quotation_lines",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_quotation_lines_quotation_id",
            schema: "techstorepro",
            table: "quotation_lines",
            column: "quotation_id");

        migrationBuilder.CreateIndex(
            name: "ix_quotations_branch_id",
            schema: "techstorepro",
            table: "quotations",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_quotations_company_id_customer_id_quoted_at",
            schema: "techstorepro",
            table: "quotations",
            columns: new[] { "company_id", "customer_id", "quoted_at" });

        migrationBuilder.CreateIndex(
            name: "ix_quotations_company_id_number",
            schema: "techstorepro",
            table: "quotations",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_quotations_company_id_status",
            schema: "techstorepro",
            table: "quotations",
            columns: new[] { "company_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_quotations_customer_id",
            schema: "techstorepro",
            table: "quotations",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoice_lines_company_id_product_id",
            schema: "techstorepro",
            table: "sales_invoice_lines",
            columns: new[] { "company_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoice_lines_company_id_sales_invoice_id",
            schema: "techstorepro",
            table: "sales_invoice_lines",
            columns: new[] { "company_id", "sales_invoice_id" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoice_lines_delivery_line_id",
            schema: "techstorepro",
            table: "sales_invoice_lines",
            column: "delivery_line_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoice_lines_product_id",
            schema: "techstorepro",
            table: "sales_invoice_lines",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoice_lines_sales_invoice_id",
            schema: "techstorepro",
            table: "sales_invoice_lines",
            column: "sales_invoice_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoices_branch_id",
            schema: "techstorepro",
            table: "sales_invoices",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoices_company_id_customer_id_invoiced_at",
            schema: "techstorepro",
            table: "sales_invoices",
            columns: new[] { "company_id", "customer_id", "invoiced_at" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoices_company_id_delivery_id",
            schema: "techstorepro",
            table: "sales_invoices",
            columns: new[] { "company_id", "delivery_id" },
            unique: true,
            filter: "is_deleted = false AND delivery_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoices_company_id_number",
            schema: "techstorepro",
            table: "sales_invoices",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoices_company_id_status_due_at",
            schema: "techstorepro",
            table: "sales_invoices",
            columns: new[] { "company_id", "status", "due_at" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoices_customer_id",
            schema: "techstorepro",
            table: "sales_invoices",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoices_delivery_id",
            schema: "techstorepro",
            table: "sales_invoices",
            column: "delivery_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_invoices_sales_order_id",
            schema: "techstorepro",
            table: "sales_invoices",
            column: "sales_order_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_order_lines_company_id_product_id",
            schema: "techstorepro",
            table: "sales_order_lines",
            columns: new[] { "company_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_order_lines_company_id_sales_order_id",
            schema: "techstorepro",
            table: "sales_order_lines",
            columns: new[] { "company_id", "sales_order_id" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_order_lines_product_id",
            schema: "techstorepro",
            table: "sales_order_lines",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_order_lines_sales_order_id",
            schema: "techstorepro",
            table: "sales_order_lines",
            column: "sales_order_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_branch_id",
            schema: "techstorepro",
            table: "sales_orders",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_company_id_customer_id_ordered_at",
            schema: "techstorepro",
            table: "sales_orders",
            columns: new[] { "company_id", "customer_id", "ordered_at" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_company_id_number",
            schema: "techstorepro",
            table: "sales_orders",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_company_id_status",
            schema: "techstorepro",
            table: "sales_orders",
            columns: new[] { "company_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_customer_id",
            schema: "techstorepro",
            table: "sales_orders",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_quotation_id",
            schema: "techstorepro",
            table: "sales_orders",
            column: "quotation_id");

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_warehouse_id",
            schema: "techstorepro",
            table: "sales_orders",
            column: "warehouse_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "delivery_serials",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "quotation_lines",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "sales_invoice_lines",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "delivery_lines",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "sales_invoices",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "sales_order_lines",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "deliveries",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "sales_orders",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "quotations",
            schema: "techstorepro");
    }
}
