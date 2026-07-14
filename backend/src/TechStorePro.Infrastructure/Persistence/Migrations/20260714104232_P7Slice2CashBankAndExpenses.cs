using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStorePro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class P7Slice2CashBankAndExpenses : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "financial_account_id",
            schema: "techstorepro",
            table: "payment_methods",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "expense_categories",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                table.PrimaryKey("pk_expense_categories", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "financial_accounts",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                kind = table.Column<short>(type: "smallint", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                bank_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                account_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                allows_overdraft = table.Column<bool>(type: "boolean", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                table.PrimaryKey("pk_financial_accounts", x => x.id);
                table.ForeignKey(
                    name: "fk_financial_accounts_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "account_transactions",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                financial_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                source = table.Column<short>(type: "smallint", nullable: false),
                source_id = table.Column<Guid>(type: "uuid", nullable: true),
                source_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                exchange_rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
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
                table.PrimaryKey("pk_account_transactions", x => x.id);
                table.ForeignKey(
                    name: "fk_account_transactions_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_account_transactions_financial_accounts_financial_account_id",
                    column: x => x.financial_account_id,
                    principalSchema: "techstorepro",
                    principalTable: "financial_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "expenses",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                expense_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                financial_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                exchange_rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                expense_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                status = table.Column<short>(type: "smallint", nullable: false),
                cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cancelled_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                table.PrimaryKey("pk_expenses", x => x.id);
                table.ForeignKey(
                    name: "fk_expenses_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_expenses_expense_categories_expense_category_id",
                    column: x => x.expense_category_id,
                    principalSchema: "techstorepro",
                    principalTable: "expense_categories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_expenses_financial_accounts_financial_account_id",
                    column: x => x.financial_account_id,
                    principalSchema: "techstorepro",
                    principalTable: "financial_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_expenses_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalSchema: "techstorepro",
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_payment_methods_financial_account_id",
            schema: "techstorepro",
            table: "payment_methods",
            column: "financial_account_id");

        migrationBuilder.CreateIndex(
            name: "ix_account_transactions_branch_id",
            schema: "techstorepro",
            table: "account_transactions",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_account_transactions_company_id_financial_account_id_occurr~",
            schema: "techstorepro",
            table: "account_transactions",
            columns: new[] { "company_id", "financial_account_id", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "ix_account_transactions_company_id_source_source_id",
            schema: "techstorepro",
            table: "account_transactions",
            columns: new[] { "company_id", "source", "source_id" });

        migrationBuilder.CreateIndex(
            name: "ix_account_transactions_financial_account_id",
            schema: "techstorepro",
            table: "account_transactions",
            column: "financial_account_id");

        migrationBuilder.CreateIndex(
            name: "ix_expense_categories_company_id_name",
            schema: "techstorepro",
            table: "expense_categories",
            columns: new[] { "company_id", "name" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_expenses_branch_id",
            schema: "techstorepro",
            table: "expenses",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_expenses_company_id_expense_date_expense_category_id",
            schema: "techstorepro",
            table: "expenses",
            columns: new[] { "company_id", "expense_date", "expense_category_id" });

        migrationBuilder.CreateIndex(
            name: "ix_expenses_company_id_number",
            schema: "techstorepro",
            table: "expenses",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_expenses_expense_category_id",
            schema: "techstorepro",
            table: "expenses",
            column: "expense_category_id");

        migrationBuilder.CreateIndex(
            name: "ix_expenses_financial_account_id",
            schema: "techstorepro",
            table: "expenses",
            column: "financial_account_id");

        migrationBuilder.CreateIndex(
            name: "ix_expenses_supplier_id",
            schema: "techstorepro",
            table: "expenses",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "ix_financial_accounts_branch_id",
            schema: "techstorepro",
            table: "financial_accounts",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_financial_accounts_company_id_kind_is_active",
            schema: "techstorepro",
            table: "financial_accounts",
            columns: new[] { "company_id", "kind", "is_active" });

        migrationBuilder.CreateIndex(
            name: "ix_financial_accounts_company_id_name",
            schema: "techstorepro",
            table: "financial_accounts",
            columns: new[] { "company_id", "name" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.AddForeignKey(
            name: "fk_payment_methods_financial_accounts_financial_account_id",
            schema: "techstorepro",
            table: "payment_methods",
            column: "financial_account_id",
            principalSchema: "techstorepro",
            principalTable: "financial_accounts",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_payment_methods_financial_accounts_financial_account_id",
            schema: "techstorepro",
            table: "payment_methods");

        migrationBuilder.DropTable(
            name: "account_transactions",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "expenses",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "expense_categories",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "financial_accounts",
            schema: "techstorepro");

        migrationBuilder.DropIndex(
            name: "ix_payment_methods_financial_account_id",
            schema: "techstorepro",
            table: "payment_methods");

        migrationBuilder.DropColumn(
            name: "financial_account_id",
            schema: "techstorepro",
            table: "payment_methods");
    }
}
