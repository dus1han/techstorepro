using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStorePro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class P6_Repairs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "repair_tickets",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                device_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                device_serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                device_serial_id = table.Column<Guid>(type: "uuid", nullable: true),
                reported_fault = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                accessories = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                condition_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                status = table.Column<short>(type: "smallint", nullable: false),
                estimated_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                warranty_type = table.Column<short>(type: "smallint", nullable: false),
                warranty_invoice_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                technician_id = table.Column<Guid>(type: "uuid", nullable: true),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                promised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                cancelled_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                table.PrimaryKey("pk_repair_tickets", x => x.id);
                table.ForeignKey(
                    name: "fk_repair_tickets_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_repair_tickets_customers_customer_id",
                    column: x => x.customer_id,
                    principalSchema: "techstorepro",
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_repair_tickets_products_device_product_id",
                    column: x => x.device_product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_repair_tickets_sales_invoice_lines_warranty_invoice_line_id",
                    column: x => x.warranty_invoice_line_id,
                    principalSchema: "techstorepro",
                    principalTable: "sales_invoice_lines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_repair_tickets_serials_device_serial_id",
                    column: x => x.device_serial_id,
                    principalSchema: "techstorepro",
                    principalTable: "serials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "warranties",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                warranty_type = table.Column<short>(type: "smallint", nullable: false),
                source_type = table.Column<short>(type: "smallint", nullable: false),
                source_id = table.Column<Guid>(type: "uuid", nullable: true),
                serial_id = table.Column<Guid>(type: "uuid", nullable: true),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                terms = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                table.PrimaryKey("pk_warranties", x => x.id);
                table.ForeignKey(
                    name: "fk_warranties_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_warranties_serials_serial_id",
                    column: x => x.serial_id,
                    principalSchema: "techstorepro",
                    principalTable: "serials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "repair_charges",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                repair_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                sales_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                charged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                table.PrimaryKey("pk_repair_charges", x => x.id);
                table.ForeignKey(
                    name: "fk_repair_charges_repair_tickets_repair_ticket_id",
                    column: x => x.repair_ticket_id,
                    principalSchema: "techstorepro",
                    principalTable: "repair_tickets",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_repair_charges_sales_invoices_sales_invoice_id",
                    column: x => x.sales_invoice_id,
                    principalSchema: "techstorepro",
                    principalTable: "sales_invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "repair_diagnoses",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                repair_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                technician_id = table.Column<Guid>(type: "uuid", nullable: true),
                findings = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                recommended_action = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                estimated_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                diagnosed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                table.PrimaryKey("pk_repair_diagnoses", x => x.id);
                table.ForeignKey(
                    name: "fk_repair_diagnoses_repair_tickets_repair_ticket_id",
                    column: x => x.repair_ticket_id,
                    principalSchema: "techstorepro",
                    principalTable: "repair_tickets",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "repair_labour",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                repair_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                technician_id = table.Column<Guid>(type: "uuid", nullable: true),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                hours = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                hourly_rate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                is_chargeable = table.Column<bool>(type: "boolean", nullable: false),
                worked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                table.PrimaryKey("pk_repair_labour", x => x.id);
                table.ForeignKey(
                    name: "fk_repair_labour_repair_tickets_repair_ticket_id",
                    column: x => x.repair_ticket_id,
                    principalSchema: "techstorepro",
                    principalTable: "repair_tickets",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "repair_outsourcing",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                repair_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                vendor_supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<short>(type: "smallint", nullable: false),
                sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                exchange_rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
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
                table.PrimaryKey("pk_repair_outsourcing", x => x.id);
                table.ForeignKey(
                    name: "fk_repair_outsourcing_repair_tickets_repair_ticket_id",
                    column: x => x.repair_ticket_id,
                    principalSchema: "techstorepro",
                    principalTable: "repair_tickets",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_repair_outsourcing_suppliers_vendor_supplier_id",
                    column: x => x.vendor_supplier_id,
                    principalSchema: "techstorepro",
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "repair_parts",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                repair_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                serial_id = table.Column<Guid>(type: "uuid", nullable: true),
                quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                is_chargeable = table.Column<bool>(type: "boolean", nullable: false),
                is_returned = table.Column<bool>(type: "boolean", nullable: false),
                returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                table.PrimaryKey("pk_repair_parts", x => x.id);
                table.ForeignKey(
                    name: "fk_repair_parts_products_product_id",
                    column: x => x.product_id,
                    principalSchema: "techstorepro",
                    principalTable: "products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_repair_parts_repair_tickets_repair_ticket_id",
                    column: x => x.repair_ticket_id,
                    principalSchema: "techstorepro",
                    principalTable: "repair_tickets",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_repair_parts_serials_serial_id",
                    column: x => x.serial_id,
                    principalSchema: "techstorepro",
                    principalTable: "serials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_repair_parts_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalSchema: "techstorepro",
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "repair_status_history",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                repair_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                from_status = table.Column<short>(type: "smallint", nullable: false),
                to_status = table.Column<short>(type: "smallint", nullable: false),
                changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                table.PrimaryKey("pk_repair_status_history", x => x.id);
                table.ForeignKey(
                    name: "fk_repair_status_history_repair_tickets_repair_ticket_id",
                    column: x => x.repair_ticket_id,
                    principalSchema: "techstorepro",
                    principalTable: "repair_tickets",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "warranty_claims",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                warranty_id = table.Column<Guid>(type: "uuid", nullable: false),
                repair_ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<short>(type: "smallint", nullable: false),
                claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                outcome = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                table.PrimaryKey("pk_warranty_claims", x => x.id);
                table.ForeignKey(
                    name: "fk_warranty_claims_repair_tickets_repair_ticket_id",
                    column: x => x.repair_ticket_id,
                    principalSchema: "techstorepro",
                    principalTable: "repair_tickets",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_warranty_claims_warranties_warranty_id",
                    column: x => x.warranty_id,
                    principalSchema: "techstorepro",
                    principalTable: "warranties",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_repair_charges_company_id_repair_ticket_id",
            schema: "techstorepro",
            table: "repair_charges",
            columns: new[] { "company_id", "repair_ticket_id" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_charges_company_id_sales_invoice_id",
            schema: "techstorepro",
            table: "repair_charges",
            columns: new[] { "company_id", "sales_invoice_id" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_repair_charges_repair_ticket_id",
            schema: "techstorepro",
            table: "repair_charges",
            column: "repair_ticket_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_charges_sales_invoice_id",
            schema: "techstorepro",
            table: "repair_charges",
            column: "sales_invoice_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_diagnoses_company_id_repair_ticket_id",
            schema: "techstorepro",
            table: "repair_diagnoses",
            columns: new[] { "company_id", "repair_ticket_id" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_diagnoses_repair_ticket_id",
            schema: "techstorepro",
            table: "repair_diagnoses",
            column: "repair_ticket_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_labour_company_id_repair_ticket_id",
            schema: "techstorepro",
            table: "repair_labour",
            columns: new[] { "company_id", "repair_ticket_id" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_labour_company_id_technician_id",
            schema: "techstorepro",
            table: "repair_labour",
            columns: new[] { "company_id", "technician_id" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_labour_repair_ticket_id",
            schema: "techstorepro",
            table: "repair_labour",
            column: "repair_ticket_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_outsourcing_company_id_repair_ticket_id",
            schema: "techstorepro",
            table: "repair_outsourcing",
            columns: new[] { "company_id", "repair_ticket_id" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_outsourcing_company_id_status",
            schema: "techstorepro",
            table: "repair_outsourcing",
            columns: new[] { "company_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_outsourcing_company_id_vendor_supplier_id",
            schema: "techstorepro",
            table: "repair_outsourcing",
            columns: new[] { "company_id", "vendor_supplier_id" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_outsourcing_repair_ticket_id",
            schema: "techstorepro",
            table: "repair_outsourcing",
            column: "repair_ticket_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_outsourcing_vendor_supplier_id",
            schema: "techstorepro",
            table: "repair_outsourcing",
            column: "vendor_supplier_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_parts_company_id_product_id",
            schema: "techstorepro",
            table: "repair_parts",
            columns: new[] { "company_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_parts_company_id_repair_ticket_id",
            schema: "techstorepro",
            table: "repair_parts",
            columns: new[] { "company_id", "repair_ticket_id" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_parts_product_id",
            schema: "techstorepro",
            table: "repair_parts",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_parts_repair_ticket_id",
            schema: "techstorepro",
            table: "repair_parts",
            column: "repair_ticket_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_parts_serial_id",
            schema: "techstorepro",
            table: "repair_parts",
            column: "serial_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_parts_warehouse_id",
            schema: "techstorepro",
            table: "repair_parts",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_status_history_company_id_repair_ticket_id_changed_at",
            schema: "techstorepro",
            table: "repair_status_history",
            columns: new[] { "company_id", "repair_ticket_id", "changed_at" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_status_history_repair_ticket_id",
            schema: "techstorepro",
            table: "repair_status_history",
            column: "repair_ticket_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_tickets_branch_id",
            schema: "techstorepro",
            table: "repair_tickets",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_tickets_company_id_customer_id_received_at",
            schema: "techstorepro",
            table: "repair_tickets",
            columns: new[] { "company_id", "customer_id", "received_at" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_tickets_company_id_device_serial_number",
            schema: "techstorepro",
            table: "repair_tickets",
            columns: new[] { "company_id", "device_serial_number" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_tickets_company_id_number",
            schema: "techstorepro",
            table: "repair_tickets",
            columns: new[] { "company_id", "number" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_repair_tickets_company_id_promised_at",
            schema: "techstorepro",
            table: "repair_tickets",
            columns: new[] { "company_id", "promised_at" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_tickets_company_id_status",
            schema: "techstorepro",
            table: "repair_tickets",
            columns: new[] { "company_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_tickets_company_id_technician_id",
            schema: "techstorepro",
            table: "repair_tickets",
            columns: new[] { "company_id", "technician_id" });

        migrationBuilder.CreateIndex(
            name: "ix_repair_tickets_customer_id",
            schema: "techstorepro",
            table: "repair_tickets",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_tickets_device_product_id",
            schema: "techstorepro",
            table: "repair_tickets",
            column: "device_product_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_tickets_device_serial_id",
            schema: "techstorepro",
            table: "repair_tickets",
            column: "device_serial_id");

        migrationBuilder.CreateIndex(
            name: "ix_repair_tickets_warranty_invoice_line_id",
            schema: "techstorepro",
            table: "repair_tickets",
            column: "warranty_invoice_line_id");

        migrationBuilder.CreateIndex(
            name: "ix_warranties_company_id_product_id",
            schema: "techstorepro",
            table: "warranties",
            columns: new[] { "company_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_warranties_company_id_serial_id",
            schema: "techstorepro",
            table: "warranties",
            columns: new[] { "company_id", "serial_id" });

        migrationBuilder.CreateIndex(
            name: "ix_warranties_company_id_serial_number_ends_on",
            schema: "techstorepro",
            table: "warranties",
            columns: new[] { "company_id", "serial_number", "ends_on" });

        migrationBuilder.CreateIndex(
            name: "ix_warranties_product_id",
            schema: "techstorepro",
            table: "warranties",
            column: "product_id");

        migrationBuilder.CreateIndex(
            name: "ix_warranties_serial_id",
            schema: "techstorepro",
            table: "warranties",
            column: "serial_id");

        migrationBuilder.CreateIndex(
            name: "ix_warranty_claims_company_id_repair_ticket_id",
            schema: "techstorepro",
            table: "warranty_claims",
            columns: new[] { "company_id", "repair_ticket_id" });

        migrationBuilder.CreateIndex(
            name: "ix_warranty_claims_company_id_status",
            schema: "techstorepro",
            table: "warranty_claims",
            columns: new[] { "company_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_warranty_claims_company_id_warranty_id",
            schema: "techstorepro",
            table: "warranty_claims",
            columns: new[] { "company_id", "warranty_id" });

        migrationBuilder.CreateIndex(
            name: "ix_warranty_claims_repair_ticket_id",
            schema: "techstorepro",
            table: "warranty_claims",
            column: "repair_ticket_id");

        migrationBuilder.CreateIndex(
            name: "ix_warranty_claims_warranty_id",
            schema: "techstorepro",
            table: "warranty_claims",
            column: "warranty_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "repair_charges",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "repair_diagnoses",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "repair_labour",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "repair_outsourcing",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "repair_parts",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "repair_status_history",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "warranty_claims",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "repair_tickets",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "warranties",
            schema: "techstorepro");
    }
}
