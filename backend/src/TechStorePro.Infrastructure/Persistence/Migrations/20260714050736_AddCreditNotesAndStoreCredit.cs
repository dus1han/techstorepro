using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStorePro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCreditNotesAndStoreCredit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "credit_notes",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                sales_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<short>(type: "smallint", nullable: false),
                refund_method = table.Column<short>(type: "smallint", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
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
                table.PrimaryKey("pk_credit_notes", x => x.id);
                table.ForeignKey(
                    name: "fk_credit_notes_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_credit_notes_customers_customer_id",
                    column: x => x.customer_id,
                    principalSchema: "techstorepro",
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_credit_notes_sales_invoices_sales_invoice_id",
                    column: x => x.sales_invoice_id,
                    principalSchema: "techstorepro",
                    principalTable: "sales_invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_credit_notes_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "credit_note_lines",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                sales_invoice_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: true),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                discount_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                discount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                tax_percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                restocked_to_shelf = table.Column<bool>(type: "boolean", nullable: false),
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
                table.PrimaryKey("pk_credit_note_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_credit_note_lines_credit_notes_credit_note_id",
                    column: x => x.credit_note_id,
                    principalSchema: "techstorepro",
                    principalTable: "credit_notes",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_credit_note_lines_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_credit_note_lines_sales_invoice_lines_sales_invoice_line_id",
                    column: x => x.sales_invoice_line_id,
                    principalSchema: "techstorepro",
                    principalTable: "sales_invoice_lines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "store_credit_entries",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                credit_note_id = table.Column<Guid>(type: "uuid", nullable: true),
                customer_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
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
                table.PrimaryKey("pk_store_credit_entries", x => x.id);
                table.ForeignKey(
                    name: "fk_store_credit_entries_credit_notes_credit_note_id",
                    column: x => x.credit_note_id,
                    principalSchema: "techstorepro",
                    principalTable: "credit_notes",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_store_credit_entries_customer_payments_customer_payment_id",
                    column: x => x.customer_payment_id,
                    principalSchema: "techstorepro",
                    principalTable: "customer_payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_store_credit_entries_customers_customer_id",
                    column: x => x.customer_id,
                    principalSchema: "techstorepro",
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_credit_note_lines_company_id_credit_note_id",
            schema: "techstorepro",
            table: "credit_note_lines",
            columns: new[] { "company_id", "credit_note_id" });

        migrationBuilder.CreateIndex(
            name: "ix_credit_note_lines_company_id_sales_invoice_line_id",
            schema: "techstorepro",
            table: "credit_note_lines",
            columns: new[] { "company_id", "sales_invoice_line_id" });

        migrationBuilder.CreateIndex(
            name: "ix_credit_note_lines_credit_note_id",
            schema: "techstorepro",
            table: "credit_note_lines",
            column: "credit_note_id");

        migrationBuilder.CreateIndex(
            name: "ix_credit_note_lines_product_id",
            schema: "techstorepro",
            table: "credit_note_lines",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_credit_note_lines_sales_invoice_line_id",
            schema: "techstorepro",
            table: "credit_note_lines",
            column: "sales_invoice_line_id");

        migrationBuilder.CreateIndex(
            name: "ix_credit_notes_branch_id",
            schema: "techstorepro",
            table: "credit_notes",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_credit_notes_company_id_customer_id_issued_at",
            schema: "techstorepro",
            table: "credit_notes",
            columns: new[] { "company_id", "customer_id", "issued_at" });

        migrationBuilder.CreateIndex(
            name: "ix_credit_notes_company_id_number",
            schema: "techstorepro",
            table: "credit_notes",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_credit_notes_company_id_sales_invoice_id",
            schema: "techstorepro",
            table: "credit_notes",
            columns: new[] { "company_id", "sales_invoice_id" });

        migrationBuilder.CreateIndex(
            name: "ix_credit_notes_customer_id",
            schema: "techstorepro",
            table: "credit_notes",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_credit_notes_sales_invoice_id",
            schema: "techstorepro",
            table: "credit_notes",
            column: "sales_invoice_id");

        migrationBuilder.CreateIndex(
            name: "ix_credit_notes_warehouse_id",
            schema: "techstorepro",
            table: "credit_notes",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_store_credit_entries_company_id_customer_id_occurred_at",
            schema: "techstorepro",
            table: "store_credit_entries",
            columns: new[] { "company_id", "customer_id", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "ix_store_credit_entries_credit_note_id",
            schema: "techstorepro",
            table: "store_credit_entries",
            column: "credit_note_id");

        migrationBuilder.CreateIndex(
            name: "ix_store_credit_entries_customer_id",
            schema: "techstorepro",
            table: "store_credit_entries",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_store_credit_entries_customer_payment_id",
            schema: "techstorepro",
            table: "store_credit_entries",
            column: "customer_payment_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "credit_note_lines",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "store_credit_entries",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "credit_notes",
            schema: "techstorepro");
    }
}
