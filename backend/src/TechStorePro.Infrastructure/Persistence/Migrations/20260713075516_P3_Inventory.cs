using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStorePro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class P3_Inventory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "barcode_print_jobs",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_type = table.Column<short>(type: "smallint", nullable: false),
                source_id = table.Column<Guid>(type: "uuid", nullable: true),
                symbology = table.Column<short>(type: "smallint", nullable: false),
                template = table.Column<short>(type: "smallint", nullable: false),
                label_count = table.Column<int>(type: "integer", nullable: false),
                include_price = table.Column<bool>(type: "boolean", nullable: false),
                include_product_name = table.Column<bool>(type: "boolean", nullable: false),
                printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                table.PrimaryKey("pk_barcode_print_jobs", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "serials",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                status = table.Column<short>(type: "smallint", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                purchase_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                goods_receipt_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                sold_invoice_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                warranty_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                table.PrimaryKey("pk_serials", x => x.id);
                table.ForeignKey(
                    name: "fk_serials_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_serials_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalSchema: "techstorepro",
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_serials_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_adjustments",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                reason = table.Column<short>(type: "smallint", nullable: false),
                explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                adjusted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                stock_count_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("pk_stock_adjustments", x => x.id);
                table.ForeignKey(
                    name: "fk_stock_adjustments_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_adjustments_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_balances",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                reserved_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                average_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_balances", x => x.id);
                table.ForeignKey(
                    name: "fk_stock_balances_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_balances_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_counts",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<short>(type: "smallint", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                counted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                stock_adjustment_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("pk_stock_counts", x => x.id);
                table.ForeignKey(
                    name: "fk_stock_counts_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_counts_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_transfers",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                from_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                to_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<short>(type: "smallint", nullable: false),
                shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                shipped_by = table.Column<Guid>(type: "uuid", nullable: true),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                received_by = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("pk_stock_transfers", x => x.id);
                table.ForeignKey(
                    name: "fk_stock_transfers_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_transfers_warehouses_from_warehouse_id",
                    column: x => x.from_warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_transfers_warehouses_to_warehouse_id",
                    column: x => x.to_warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "serial_events",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                serial_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<short>(type: "smallint", nullable: false),
                status = table.Column<short>(type: "smallint", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                reference_type = table.Column<short>(type: "smallint", nullable: true),
                reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                reference_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_serial_events", x => x.id);
                table.ForeignKey(
                    name: "fk_serial_events_serials_serial_id",
                    column: x => x.serial_id,
                    principalSchema: "techstorepro",
                    principalTable: "serials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "stock_movements",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                serial_id = table.Column<Guid>(type: "uuid", nullable: true),
                type = table.Column<short>(type: "smallint", nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                average_cost_after = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                balance_after = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                reference_type = table.Column<short>(type: "smallint", nullable: false),
                reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                reference_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_movements", x => x.id);
                table.ForeignKey(
                    name: "fk_stock_movements_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_movements_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_movements_serials_serial_id",
                    column: x => x.serial_id,
                    principalSchema: "techstorepro",
                    principalTable: "serials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_movements_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_reservations",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                serial_id = table.Column<Guid>(type: "uuid", nullable: true),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                fulfilled_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                status = table.Column<short>(type: "smallint", nullable: false),
                reference_type = table.Column<short>(type: "smallint", nullable: false),
                reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                reference_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                reserved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                table.PrimaryKey("pk_stock_reservations", x => x.id);
                table.ForeignKey(
                    name: "fk_stock_reservations_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_reservations_serials_serial_id",
                    column: x => x.serial_id,
                    principalSchema: "techstorepro",
                    principalTable: "serials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_reservations_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_adjustment_lines",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                stock_adjustment_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                serial_id = table.Column<Guid>(type: "uuid", nullable: true),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
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
                table.PrimaryKey("pk_stock_adjustment_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_stock_adjustment_lines_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_adjustment_lines_serials_serial_id",
                    column: x => x.serial_id,
                    principalSchema: "techstorepro",
                    principalTable: "serials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_adjustment_lines_stock_adjustments_stock_adjustment_id",
                    column: x => x.stock_adjustment_id,
                    principalSchema: "techstorepro",
                    principalTable: "stock_adjustments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "stock_count_lines",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                stock_count_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                serial_id = table.Column<Guid>(type: "uuid", nullable: true),
                system_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                counted_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
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
                table.PrimaryKey("pk_stock_count_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_stock_count_lines_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_count_lines_serials_serial_id",
                    column: x => x.serial_id,
                    principalSchema: "techstorepro",
                    principalTable: "serials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_count_lines_stock_counts_stock_count_id",
                    column: x => x.stock_count_id,
                    principalSchema: "techstorepro",
                    principalTable: "stock_counts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "stock_transfer_lines",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                stock_transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                serial_id = table.Column<Guid>(type: "uuid", nullable: true),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                received_quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
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
                table.PrimaryKey("pk_stock_transfer_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_stock_transfer_lines_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_transfer_lines_serials_serial_id",
                    column: x => x.serial_id,
                    principalSchema: "techstorepro",
                    principalTable: "serials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_transfer_lines_stock_transfers_stock_transfer_id",
                    column: x => x.stock_transfer_id,
                    principalSchema: "techstorepro",
                    principalTable: "stock_transfers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_barcode_print_jobs_company_id_source_type_source_id",
            schema: "techstorepro",
            table: "barcode_print_jobs",
            columns: new[] { "company_id", "source_type", "source_id" });

        migrationBuilder.CreateIndex(
            name: "ix_serial_events_company_id_serial_id_at",
            schema: "techstorepro",
            table: "serial_events",
            columns: new[] { "company_id", "serial_id", "at" });

        migrationBuilder.CreateIndex(
            name: "ix_serial_events_serial_id",
            schema: "techstorepro",
            table: "serial_events",
            column: "serial_id");

        migrationBuilder.CreateIndex(
            name: "ix_serials_company_id_product_id_status",
            schema: "techstorepro",
            table: "serials",
            columns: new[] { "company_id", "product_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_serials_company_id_serial_number",
            schema: "techstorepro",
            table: "serials",
            columns: new[] { "company_id", "serial_number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_serials_company_id_warehouse_id_status",
            schema: "techstorepro",
            table: "serials",
            columns: new[] { "company_id", "warehouse_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_serials_product_id",
            schema: "techstorepro",
            table: "serials",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_serials_supplier_id",
            schema: "techstorepro",
            table: "serials",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "ix_serials_warehouse_id",
            schema: "techstorepro",
            table: "serials",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_adjustment_lines_product_id",
            schema: "techstorepro",
            table: "stock_adjustment_lines",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_adjustment_lines_serial_id",
            schema: "techstorepro",
            table: "stock_adjustment_lines",
            column: "serial_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_adjustment_lines_stock_adjustment_id",
            schema: "techstorepro",
            table: "stock_adjustment_lines",
            column: "stock_adjustment_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_adjustments_branch_id",
            schema: "techstorepro",
            table: "stock_adjustments",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_adjustments_company_id_number",
            schema: "techstorepro",
            table: "stock_adjustments",
            columns: new[] { "company_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_stock_adjustments_company_id_reason_adjusted_at",
            schema: "techstorepro",
            table: "stock_adjustments",
            columns: new[] { "company_id", "reason", "adjusted_at" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_adjustments_warehouse_id",
            schema: "techstorepro",
            table: "stock_adjustments",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_balances_company_id_product_id",
            schema: "techstorepro",
            table: "stock_balances",
            columns: new[] { "company_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_balances_company_id_warehouse_id_product_id",
            schema: "techstorepro",
            table: "stock_balances",
            columns: new[] { "company_id", "warehouse_id", "product_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_stock_balances_product_id",
            schema: "techstorepro",
            table: "stock_balances",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_balances_warehouse_id",
            schema: "techstorepro",
            table: "stock_balances",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_count_lines_product_id",
            schema: "techstorepro",
            table: "stock_count_lines",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_count_lines_serial_id",
            schema: "techstorepro",
            table: "stock_count_lines",
            column: "serial_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_count_lines_stock_count_id_product_id_serial_id",
            schema: "techstorepro",
            table: "stock_count_lines",
            columns: new[] { "stock_count_id", "product_id", "serial_id" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_stock_counts_branch_id",
            schema: "techstorepro",
            table: "stock_counts",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_counts_company_id_number",
            schema: "techstorepro",
            table: "stock_counts",
            columns: new[] { "company_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_stock_counts_company_id_warehouse_id_status",
            schema: "techstorepro",
            table: "stock_counts",
            columns: new[] { "company_id", "warehouse_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_counts_warehouse_id",
            schema: "techstorepro",
            table: "stock_counts",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_branch_id",
            schema: "techstorepro",
            table: "stock_movements",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_company_id_product_id_occurred_at",
            schema: "techstorepro",
            table: "stock_movements",
            columns: new[] { "company_id", "product_id", "occurred_at" },
            descending: new[] { false, false, true });

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_company_id_reference_type_reference_id",
            schema: "techstorepro",
            table: "stock_movements",
            columns: new[] { "company_id", "reference_type", "reference_id" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_company_id_serial_id",
            schema: "techstorepro",
            table: "stock_movements",
            columns: new[] { "company_id", "serial_id" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_company_id_warehouse_id_product_id_occurred~",
            schema: "techstorepro",
            table: "stock_movements",
            columns: new[] { "company_id", "warehouse_id", "product_id", "occurred_at" },
            descending: new[] { false, false, false, true });

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_product_id",
            schema: "techstorepro",
            table: "stock_movements",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_serial_id",
            schema: "techstorepro",
            table: "stock_movements",
            column: "serial_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_warehouse_id",
            schema: "techstorepro",
            table: "stock_movements",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_reservations_company_id_status_expires_at",
            schema: "techstorepro",
            table: "stock_reservations",
            columns: new[] { "company_id", "status", "expires_at" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_reservations_company_id_warehouse_id_product_id_status",
            schema: "techstorepro",
            table: "stock_reservations",
            columns: new[] { "company_id", "warehouse_id", "product_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_reservations_product_id",
            schema: "techstorepro",
            table: "stock_reservations",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_reservations_serial_id",
            schema: "techstorepro",
            table: "stock_reservations",
            column: "serial_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_reservations_warehouse_id",
            schema: "techstorepro",
            table: "stock_reservations",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfer_lines_product_id",
            schema: "techstorepro",
            table: "stock_transfer_lines",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfer_lines_serial_id",
            schema: "techstorepro",
            table: "stock_transfer_lines",
            column: "serial_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfer_lines_stock_transfer_id",
            schema: "techstorepro",
            table: "stock_transfer_lines",
            column: "stock_transfer_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfers_branch_id",
            schema: "techstorepro",
            table: "stock_transfers",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfers_company_id_number",
            schema: "techstorepro",
            table: "stock_transfers",
            columns: new[] { "company_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfers_company_id_status",
            schema: "techstorepro",
            table: "stock_transfers",
            columns: new[] { "company_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfers_from_warehouse_id",
            schema: "techstorepro",
            table: "stock_transfers",
            column: "from_warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfers_to_warehouse_id",
            schema: "techstorepro",
            table: "stock_transfers",
            column: "to_warehouse_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "barcode_print_jobs",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "serial_events",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "stock_adjustment_lines",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "stock_balances",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "stock_count_lines",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "stock_movements",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "stock_reservations",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "stock_transfer_lines",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "stock_adjustments",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "stock_counts",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "serials",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "stock_transfers",
            schema: "techstorepro");
    }
}
