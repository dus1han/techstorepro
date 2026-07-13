using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStorePro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class P1_Identity_Configuration_Audit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "techstorepro");

        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:PostgresExtension:citext", ",,");

        migrationBuilder.CreateTable(
            name: "audit_log",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: true),
                user_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                action = table.Column<short>(type: "smallint", nullable: false),
                old_values = table.Column<string>(type: "jsonb", nullable: true),
                new_values = table.Column<string>(type: "jsonb", nullable: true),
                summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_audit_log", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "companies",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                tax_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                registration_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                base_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                address = table.Column<string>(type: "text", nullable: true),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                website = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                bank_details = table.Column<string>(type: "text", nullable: true),
                logo_storage_key = table.Column<string>(type: "text", nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_companies", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "features",
            schema: "techstorepro",
            columns: table => new
            {
                code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                display_order = table.Column<int>(type: "integer", nullable: false),
                supported_actions = table.Column<short[]>(type: "smallint[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_features", x => x.code);
            });

        migrationBuilder.CreateTable(
            name: "setting_definitions",
            schema: "techstorepro",
            columns: table => new
            {
                key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                data_type = table.Column<short>(type: "smallint", nullable: false),
                scope = table.Column<short>(type: "smallint", nullable: false),
                default_value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_setting_definitions", x => x.key);
            });

        migrationBuilder.CreateTable(
            name: "setting_values",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
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
                table.PrimaryKey("pk_setting_values", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "users",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "citext", maxLength: 256, nullable: false),
                password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                failed_login_count = table.Column<int>(type: "integer", nullable: false),
                locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_users", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "company_users",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_default = table.Column<bool>(type: "boolean", nullable: false),
                is_owner = table.Column<bool>(type: "boolean", nullable: false),
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
                table.PrimaryKey("pk_company_users", x => x.id);
                table.ForeignKey(
                    name: "fk_company_users_companies_company_id",
                    column: x => x.company_id,
                    principalSchema: "techstorepro",
                    principalTable: "companies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_company_users_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "techstorepro",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "login_history",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: true),
                email = table.Column<string>(type: "citext", maxLength: 256, nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: true),
                result = table.Column<short>(type: "smallint", nullable: false),
                failure_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                device_info = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_login_history", x => x.id);
                table.ForeignKey(
                    name: "fk_login_history_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "techstorepro",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "refresh_tokens",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                device_info = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_refresh_tokens", x => x.id);
                table.ForeignKey(
                    name: "fk_refresh_tokens_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "techstorepro",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_permissions",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                feature_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                action = table.Column<short>(type: "smallint", nullable: false),
                granted = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_permissions", x => x.id);
                table.ForeignKey(
                    name: "fk_user_permissions_company_users_company_user_id",
                    column: x => x.company_user_id,
                    principalSchema: "techstorepro",
                    principalTable: "company_users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "branch_warehouses",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                can_issue = table.Column<bool>(type: "boolean", nullable: false),
                can_receive = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_branch_warehouses", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "branches",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                address = table.Column<string>(type: "text", nullable: true),
                phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                default_warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("pk_branches", x => x.id);
                table.ForeignKey(
                    name: "fk_branches_companies_company_id",
                    column: x => x.company_id,
                    principalSchema: "techstorepro",
                    principalTable: "companies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "company_user_branches",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_company_user_branches", x => x.id);
                table.ForeignKey(
                    name: "fk_company_user_branches_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_company_user_branches_company_users_company_user_id",
                    column: x => x.company_user_id,
                    principalSchema: "techstorepro",
                    principalTable: "company_users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "document_number_sequences",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                document_type = table.Column<short>(type: "smallint", nullable: false),
                year = table.Column<int>(type: "integer", nullable: false),
                prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                next_number = table.Column<long>(type: "bigint", nullable: false),
                padding = table.Column<int>(type: "integer", nullable: false),
                resets_annually = table.Column<bool>(type: "boolean", nullable: false),
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
                table.PrimaryKey("pk_document_number_sequences", x => x.id);
                table.ForeignKey(
                    name: "fk_document_number_sequences_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "warehouses",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                type = table.Column<short>(type: "smallint", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                address = table.Column<string>(type: "text", nullable: true),
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
                table.PrimaryKey("pk_warehouses", x => x.id);
                table.ForeignKey(
                    name: "fk_warehouses_branches_branch_id",
                    column: x => x.branch_id,
                    principalSchema: "techstorepro",
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_warehouses_companies_company_id",
                    column: x => x.company_id,
                    principalSchema: "techstorepro",
                    principalTable: "companies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_audit_log_company_id_at",
            schema: "techstorepro",
            table: "audit_log",
            columns: new[] { "company_id", "at" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "ix_audit_log_company_id_entity_type_entity_id_at",
            schema: "techstorepro",
            table: "audit_log",
            columns: new[] { "company_id", "entity_type", "entity_id", "at" },
            descending: new[] { false, false, false, true });

        migrationBuilder.CreateIndex(
            name: "ix_branch_warehouses_branch_id",
            schema: "techstorepro",
            table: "branch_warehouses",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_branch_warehouses_company_id_branch_id_warehouse_id",
            schema: "techstorepro",
            table: "branch_warehouses",
            columns: new[] { "company_id", "branch_id", "warehouse_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_branch_warehouses_warehouse_id",
            schema: "techstorepro",
            table: "branch_warehouses",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_branches_company_id_code",
            schema: "techstorepro",
            table: "branches",
            columns: new[] { "company_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_branches_default_warehouse_id",
            schema: "techstorepro",
            table: "branches",
            column: "default_warehouse_id");

        migrationBuilder.CreateIndex(
            name: "ix_companies_is_active",
            schema: "techstorepro",
            table: "companies",
            column: "is_active");

        migrationBuilder.CreateIndex(
            name: "ix_company_user_branches_branch_id",
            schema: "techstorepro",
            table: "company_user_branches",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_company_user_branches_company_user_id_branch_id",
            schema: "techstorepro",
            table: "company_user_branches",
            columns: new[] { "company_user_id", "branch_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_company_users_company_id_user_id",
            schema: "techstorepro",
            table: "company_users",
            columns: new[] { "company_id", "user_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_company_users_user_id",
            schema: "techstorepro",
            table: "company_users",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_document_number_sequences_branch_id",
            schema: "techstorepro",
            table: "document_number_sequences",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_document_number_sequences_company_id_branch_id_document_typ~",
            schema: "techstorepro",
            table: "document_number_sequences",
            columns: new[] { "company_id", "branch_id", "document_type", "year" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_login_history_email_at",
            schema: "techstorepro",
            table: "login_history",
            columns: new[] { "email", "at" });

        migrationBuilder.CreateIndex(
            name: "ix_login_history_user_id_at",
            schema: "techstorepro",
            table: "login_history",
            columns: new[] { "user_id", "at" });

        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_token_hash",
            schema: "techstorepro",
            table: "refresh_tokens",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_user_id_company_id",
            schema: "techstorepro",
            table: "refresh_tokens",
            columns: new[] { "user_id", "company_id" });

        migrationBuilder.CreateIndex(
            name: "ix_setting_values_lookup",
            schema: "techstorepro",
            table: "setting_values",
            columns: new[] { "company_id", "key", "branch_id", "valid_from" },
            descending: new[] { false, false, false, true });

        migrationBuilder.CreateIndex(
            name: "ix_user_permissions_company_user_id_feature_code_action",
            schema: "techstorepro",
            table: "user_permissions",
            columns: new[] { "company_user_id", "feature_code", "action" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_users_email",
            schema: "techstorepro",
            table: "users",
            column: "email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_warehouses_branch_id",
            schema: "techstorepro",
            table: "warehouses",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_warehouses_company_id_branch_id",
            schema: "techstorepro",
            table: "warehouses",
            columns: new[] { "company_id", "branch_id" });

        migrationBuilder.CreateIndex(
            name: "ix_warehouses_company_id_code",
            schema: "techstorepro",
            table: "warehouses",
            columns: new[] { "company_id", "code" },
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "fk_branch_warehouses_branches_branch_id",
            schema: "techstorepro",
            table: "branch_warehouses",
            column: "branch_id",
            principalSchema: "techstorepro",
            principalTable: "branches",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_branch_warehouses_warehouses_warehouse_id",
            schema: "techstorepro",
            table: "branch_warehouses",
            column: "warehouse_id",
            principalSchema: "techstorepro",
            principalTable: "warehouses",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_branches_warehouses_default_warehouse_id",
            schema: "techstorepro",
            table: "branches",
            column: "default_warehouse_id",
            principalSchema: "techstorepro",
            principalTable: "warehouses",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_warehouses_branches_branch_id",
            schema: "techstorepro",
            table: "warehouses");

        migrationBuilder.DropTable(
            name: "audit_log",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "branch_warehouses",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "company_user_branches",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "document_number_sequences",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "features",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "login_history",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "refresh_tokens",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "setting_definitions",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "setting_values",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "user_permissions",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "company_users",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "users",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "branches",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "warehouses",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "companies",
            schema: "techstorepro");
    }
}
