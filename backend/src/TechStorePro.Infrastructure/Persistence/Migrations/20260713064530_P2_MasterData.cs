using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStorePro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class P2_MasterData : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "brands",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                table.PrimaryKey("pk_brands", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "currencies",
            schema: "techstorepro",
            columns: table => new
            {
                code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                decimal_places = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_currencies", x => x.code);
            });

        migrationBuilder.CreateTable(
            name: "payment_methods",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                kind = table.Column<short>(type: "smallint", nullable: false),
                requires_reference = table.Column<bool>(type: "boolean", nullable: false),
                valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                table.PrimaryKey("pk_payment_methods", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "price_tiers",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                is_default = table.Column<bool>(type: "boolean", nullable: false),
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
                table.PrimaryKey("pk_price_tiers", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "product_categories",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                parent_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("pk_product_categories", x => x.id);
                table.ForeignKey(
                    name: "fk_product_categories_product_categories_parent_id",
                    column: x => x.parent_id,
                    principalSchema: "techstorepro",
                    principalTable: "product_categories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "suppliers",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                type = table.Column<short>(type: "smallint", nullable: false),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                address = table.Column<string>(type: "text", nullable: true),
                country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                tax_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                default_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                payment_term_days = table.Column<int>(type: "integer", nullable: false),
                lead_time_days = table.Column<int>(type: "integer", nullable: false),
                balance = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
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
                table.PrimaryKey("pk_suppliers", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "tax_rates",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                percent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                is_default = table.Column<bool>(type: "boolean", nullable: false),
                valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                table.PrimaryKey("pk_tax_rates", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "fx_rates",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                rate_to_base = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                rate_date = table.Column<DateOnly>(type: "date", nullable: false),
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
                table.PrimaryKey("pk_fx_rates", x => x.id);
                table.ForeignKey(
                    name: "fk_fx_rates_currencies_currency_code",
                    column: x => x.currency_code,
                    principalSchema: "techstorepro",
                    principalTable: "currencies",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "customers",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                type = table.Column<short>(type: "smallint", nullable: false),
                company_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                address = table.Column<string>(type: "text", nullable: true),
                tax_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                credit_limit = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                payment_term_days = table.Column<int>(type: "integer", nullable: false),
                price_tier_id = table.Column<Guid>(type: "uuid", nullable: true),
                balance = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
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
                table.PrimaryKey("pk_customers", x => x.id);
                table.ForeignKey(
                    name: "fk_customers_price_tiers_price_tier_id",
                    column: x => x.price_tier_id,
                    principalSchema: "techstorepro",
                    principalTable: "price_tiers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "price_lists",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                price_tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                table.PrimaryKey("pk_price_lists", x => x.id);
                table.ForeignKey(
                    name: "fk_price_lists_price_tiers_price_tier_id",
                    column: x => x.price_tier_id,
                    principalSchema: "techstorepro",
                    principalTable: "price_tiers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "products",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                description = table.Column<string>(type: "text", nullable: true),
                category_id = table.Column<Guid>(type: "uuid", nullable: true),
                brand_id = table.Column<Guid>(type: "uuid", nullable: true),
                model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                specifications = table.Column<string>(type: "jsonb", nullable: true),
                kind = table.Column<short>(type: "smallint", nullable: false),
                condition = table.Column<short>(type: "smallint", nullable: false),
                tracking_mode = table.Column<short>(type: "smallint", nullable: false),
                unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                purchase_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                selling_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                tax_rate_id = table.Column<Guid>(type: "uuid", nullable: true),
                warranty_months = table.Column<int>(type: "integer", nullable: false),
                reorder_level = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
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
                table.PrimaryKey("pk_products", x => x.id);
                table.ForeignKey(
                    name: "fk_products_brands_brand_id",
                    column: x => x.brand_id,
                    principalSchema: "techstorepro",
                    principalTable: "brands",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_products_product_categories_category_id",
                    column: x => x.category_id,
                    principalSchema: "techstorepro",
                    principalTable: "product_categories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_products_tax_rates_tax_rate_id",
                    column: x => x.tax_rate_id,
                    principalSchema: "techstorepro",
                    principalTable: "tax_rates",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "discounts",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: true),
                customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                method = table.Column<short>(type: "smallint", nullable: false),
                value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                max_value = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                table.PrimaryKey("pk_discounts", x => x.id);
                table.ForeignKey(
                    name: "fk_discounts_customers_customer_id",
                    column: x => x.customer_id,
                    principalSchema: "techstorepro",
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_discounts_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "price_history",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<short>(type: "smallint", nullable: false),
                old_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                new_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                changed_by = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("pk_price_history", x => x.id);
                table.ForeignKey(
                    name: "fk_price_history_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "price_list_items",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                minimum_price = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
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
                table.PrimaryKey("pk_price_list_items", x => x.id);
                table.ForeignKey(
                    name: "fk_price_list_items_price_lists_price_list_id",
                    column: x => x.price_list_id,
                    principalSchema: "techstorepro",
                    principalTable: "price_lists",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_price_list_items_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_brands_company_id_name",
            schema: "techstorepro",
            table: "brands",
            columns: new[] { "company_id", "name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_customers_company_id_code",
            schema: "techstorepro",
            table: "customers",
            columns: new[] { "company_id", "code" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_customers_company_id_name",
            schema: "techstorepro",
            table: "customers",
            columns: new[] { "company_id", "name" });

        migrationBuilder.CreateIndex(
            name: "ix_customers_company_id_phone",
            schema: "techstorepro",
            table: "customers",
            columns: new[] { "company_id", "phone" });

        migrationBuilder.CreateIndex(
            name: "ix_customers_price_tier_id",
            schema: "techstorepro",
            table: "customers",
            column: "price_tier_id");

        migrationBuilder.CreateIndex(
            name: "ix_discounts_company_id_customer_id",
            schema: "techstorepro",
            table: "discounts",
            columns: new[] { "company_id", "customer_id" });

        migrationBuilder.CreateIndex(
            name: "ix_discounts_company_id_product_id",
            schema: "techstorepro",
            table: "discounts",
            columns: new[] { "company_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_discounts_customer_id",
            schema: "techstorepro",
            table: "discounts",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_discounts_product_id",
            schema: "techstorepro",
            table: "discounts",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_fx_rates_company_id_currency_code_rate_date",
            schema: "techstorepro",
            table: "fx_rates",
            columns: new[] { "company_id", "currency_code", "rate_date" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_fx_rates_currency_code",
            schema: "techstorepro",
            table: "fx_rates",
            column: "currency_code");

        migrationBuilder.CreateIndex(
            name: "ix_payment_methods_company_id_name",
            schema: "techstorepro",
            table: "payment_methods",
            columns: new[] { "company_id", "name" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_price_history_company_id_product_id_changed_at",
            schema: "techstorepro",
            table: "price_history",
            columns: new[] { "company_id", "product_id", "changed_at" },
            descending: new[] { false, false, true });

        migrationBuilder.CreateIndex(
            name: "ix_price_history_product_id",
            schema: "techstorepro",
            table: "price_history",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_price_list_items_price_list_id_product_id",
            schema: "techstorepro",
            table: "price_list_items",
            columns: new[] { "price_list_id", "product_id" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_price_list_items_product_id",
            schema: "techstorepro",
            table: "price_list_items",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_price_lists_company_id_price_tier_id_valid_from",
            schema: "techstorepro",
            table: "price_lists",
            columns: new[] { "company_id", "price_tier_id", "valid_from" });

        migrationBuilder.CreateIndex(
            name: "ix_price_lists_price_tier_id",
            schema: "techstorepro",
            table: "price_lists",
            column: "price_tier_id");

        migrationBuilder.CreateIndex(
            name: "ix_price_tiers_company_id_name",
            schema: "techstorepro",
            table: "price_tiers",
            columns: new[] { "company_id", "name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_product_categories_company_id_name",
            schema: "techstorepro",
            table: "product_categories",
            columns: new[] { "company_id", "name" });

        migrationBuilder.CreateIndex(
            name: "ix_product_categories_parent_id",
            schema: "techstorepro",
            table: "product_categories",
            column: "parent_id");

        migrationBuilder.CreateIndex(
            name: "ix_products_brand_id",
            schema: "techstorepro",
            table: "products",
            column: "brand_id");

        migrationBuilder.CreateIndex(
            name: "ix_products_category_id",
            schema: "techstorepro",
            table: "products",
            column: "category_id");

        migrationBuilder.CreateIndex(
            name: "ix_products_company_id_barcode",
            schema: "techstorepro",
            table: "products",
            columns: new[] { "company_id", "barcode" });

        migrationBuilder.CreateIndex(
            name: "ix_products_company_id_category_id",
            schema: "techstorepro",
            table: "products",
            columns: new[] { "company_id", "category_id" });

        migrationBuilder.CreateIndex(
            name: "ix_products_company_id_item_code",
            schema: "techstorepro",
            table: "products",
            columns: new[] { "company_id", "item_code" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_products_company_id_sku",
            schema: "techstorepro",
            table: "products",
            columns: new[] { "company_id", "sku" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_products_tax_rate_id",
            schema: "techstorepro",
            table: "products",
            column: "tax_rate_id");

        migrationBuilder.CreateIndex(
            name: "ix_suppliers_company_id_code",
            schema: "techstorepro",
            table: "suppliers",
            columns: new[] { "company_id", "code" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_suppliers_company_id_name",
            schema: "techstorepro",
            table: "suppliers",
            columns: new[] { "company_id", "name" });

        migrationBuilder.CreateIndex(
            name: "ix_tax_rates_company_id_valid_from",
            schema: "techstorepro",
            table: "tax_rates",
            columns: new[] { "company_id", "valid_from" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "discounts",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "fx_rates",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "payment_methods",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "price_history",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "price_list_items",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "suppliers",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "customers",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "currencies",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "price_lists",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "products",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "price_tiers",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "brands",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "product_categories",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "tax_rates",
            schema: "techstorepro");
    }
}
