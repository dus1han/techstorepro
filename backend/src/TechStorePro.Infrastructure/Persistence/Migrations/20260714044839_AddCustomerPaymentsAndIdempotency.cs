using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStorePro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCustomerPaymentsAndIdempotency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customer_payments",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
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
                table.PrimaryKey("pk_customer_payments", x => x.id);
                table.ForeignKey(
                    name: "fk_customer_payments_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_customer_payments_customers_customer_id",
                    column: x => x.customer_id,
                    principalSchema: "techstorepro",
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                endpoint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                status_code = table.Column<int>(type: "integer", nullable: true),
                response_body = table.Column<string>(type: "text", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_idempotency_records", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "customer_payment_allocations",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                sales_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
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
                table.PrimaryKey("pk_customer_payment_allocations", x => x.id);
                table.ForeignKey(
                    name: "fk_customer_payment_allocations_customer_payments_customer_pay~",
                    column: x => x.customer_payment_id,
                    principalSchema: "techstorepro",
                    principalTable: "customer_payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_customer_payment_allocations_sales_invoices_sales_invoice_id",
                    column: x => x.sales_invoice_id,
                    principalSchema: "techstorepro",
                    principalTable: "sales_invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "customer_payment_methods",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_method_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                table.PrimaryKey("pk_customer_payment_methods", x => x.id);
                table.ForeignKey(
                    name: "fk_customer_payment_methods_customer_payments_customer_payment~",
                    column: x => x.customer_payment_id,
                    principalSchema: "techstorepro",
                    principalTable: "customer_payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_customer_payment_methods_payment_methods_payment_method_id",
                    column: x => x.payment_method_id,
                    principalSchema: "techstorepro",
                    principalTable: "payment_methods",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_customer_payment_allocations_company_id_customer_payment_id~",
            schema: "techstorepro",
            table: "customer_payment_allocations",
            columns: new[] { "company_id", "customer_payment_id", "sales_invoice_id" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_customer_payment_allocations_company_id_sales_invoice_id",
            schema: "techstorepro",
            table: "customer_payment_allocations",
            columns: new[] { "company_id", "sales_invoice_id" });

        migrationBuilder.CreateIndex(
            name: "ix_customer_payment_allocations_customer_payment_id",
            schema: "techstorepro",
            table: "customer_payment_allocations",
            column: "customer_payment_id");

        migrationBuilder.CreateIndex(
            name: "ix_customer_payment_allocations_sales_invoice_id",
            schema: "techstorepro",
            table: "customer_payment_allocations",
            column: "sales_invoice_id");

        migrationBuilder.CreateIndex(
            name: "ix_customer_payment_methods_company_id_customer_payment_id",
            schema: "techstorepro",
            table: "customer_payment_methods",
            columns: new[] { "company_id", "customer_payment_id" });

        migrationBuilder.CreateIndex(
            name: "ix_customer_payment_methods_customer_payment_id",
            schema: "techstorepro",
            table: "customer_payment_methods",
            column: "customer_payment_id");

        migrationBuilder.CreateIndex(
            name: "ix_customer_payment_methods_payment_method_id",
            schema: "techstorepro",
            table: "customer_payment_methods",
            column: "payment_method_id");

        migrationBuilder.CreateIndex(
            name: "ix_customer_payments_branch_id",
            schema: "techstorepro",
            table: "customer_payments",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_customer_payments_company_id_customer_id_paid_at",
            schema: "techstorepro",
            table: "customer_payments",
            columns: new[] { "company_id", "customer_id", "paid_at" });

        migrationBuilder.CreateIndex(
            name: "ix_customer_payments_company_id_number",
            schema: "techstorepro",
            table: "customer_payments",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_customer_payments_customer_id",
            schema: "techstorepro",
            table: "customer_payments",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_idempotency_records_company_id_key_endpoint",
            schema: "techstorepro",
            table: "idempotency_records",
            columns: new[] { "company_id", "key", "endpoint" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "customer_payment_allocations",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "customer_payment_methods",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "idempotency_records",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "customer_payments",
            schema: "techstorepro");
    }
}
