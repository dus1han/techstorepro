using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStorePro.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class P4_Identity_UsernameLogin_PlatformAdmin : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ========================================================================================
        // Identity is being turned inside out: a user used to be a platform-wide person who held
        // memberships in many companies, and is now a person who belongs to exactly one and signs
        // in as username@COMPANYCODE.
        //
        // The scaffolded version of this migration dropped company_users FIRST and then added
        // users.company_id with a default of the all-zeros GUID. Run against a populated database
        // that silently orphans every user: a company nobody belongs to, a blank username, and a
        // unique index that collides the moment a second user exists. So the order here is
        // deliberate and the backfill is hand-written:
        //
        //   1. add the new columns, nullable
        //   2. copy the data out of company_users while it still exists
        //   3. make the columns NOT NULL and add the indexes
        //   4. only then drop the old tables
        //
        // A person who held memberships in two companies cannot be represented any more. They keep
        // exactly one account — their default membership, or their first — and the other membership
        // is dropped. That is a real loss of information, and it is the honest consequence of the
        // model change rather than something to paper over.
        // ========================================================================================

        // --- 1. New columns, nullable for now ---------------------------------------------------

        migrationBuilder.AddColumn<string>(
            name: "code",
            schema: "techstorepro",
            table: "companies",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "company_id",
            schema: "techstorepro",
            table: "users",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "username",
            schema: "techstorepro",
            table: "users",
            type: "citext",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "is_owner",
            schema: "techstorepro",
            table: "users",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "login",
            schema: "techstorepro",
            table: "login_history",
            type: "citext",
            maxLength: 360,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "username",
            schema: "techstorepro",
            table: "audit_log",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        // Email stops being the login, so it stops being required.
        migrationBuilder.AlterColumn<string>(
            name: "email",
            schema: "techstorepro",
            table: "users",
            type: "citext",
            maxLength: 256,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "citext",
            oldMaxLength: 256);

        // The column keeps its values (they are company_user ids) — step 2 rewrites them into user
        // ids while company_users is still there to join against.
        migrationBuilder.DropForeignKey(
            name: "fk_user_permissions_company_users_company_user_id",
            schema: "techstorepro",
            table: "user_permissions");

        migrationBuilder.RenameColumn(
            name: "company_user_id",
            schema: "techstorepro",
            table: "user_permissions",
            newName: "user_id");

        migrationBuilder.RenameIndex(
            name: "ix_user_permissions_company_user_id_feature_code_action",
            schema: "techstorepro",
            table: "user_permissions",
            newName: "ix_user_permissions_user_id_feature_code_action");

        // user_branches has to exist before company_user_branches is drained into it.
        migrationBuilder.CreateTable(
            name: "user_branches",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_branches", x => x.id);
            });

        // --- 2. Backfill, while company_users still exists ---------------------------------------

        migrationBuilder.Sql(
            """
            -- A company code, derived from the name: letters and digits only, upper-cased, capped
            -- at 12 characters, and suffixed with a number if two companies collide. It is half of
            -- every login, so it must be unique and it must be typeable.
            WITH derived AS (
                SELECT
                    id,
                    LEFT(UPPER(REGEXP_REPLACE(COALESCE(NULLIF(name, ''), 'COMPANY'), '[^a-zA-Z0-9]', '', 'g')), 12) AS base,
                    ROW_NUMBER() OVER (
                        PARTITION BY LEFT(UPPER(REGEXP_REPLACE(COALESCE(NULLIF(name, ''), 'COMPANY'), '[^a-zA-Z0-9]', '', 'g')), 12)
                        ORDER BY created_at, id
                    ) AS n
                FROM techstorepro.companies
            )
            UPDATE techstorepro.companies c
            SET code = CASE
                WHEN d.base = '' THEN 'CO' || SUBSTRING(REPLACE(c.id::text, '-', ''), 1, 8)
                WHEN d.n = 1 THEN d.base
                ELSE LEFT(d.base, 9) || d.n::text
            END
            FROM derived d
            WHERE c.id = d.id AND c.code IS NULL;
            """);

        migrationBuilder.Sql(
            """
            -- Pin each user to one company: their default membership, else the oldest one. A user
            -- with memberships in two companies keeps one account; the other membership is gone.
            WITH chosen AS (
                SELECT DISTINCT ON (cu.user_id)
                    cu.user_id,
                    cu.company_id,
                    cu.is_owner
                FROM techstorepro.company_users cu
                WHERE cu.is_deleted = false
                ORDER BY cu.user_id, cu.is_default DESC, cu.created_at
            )
            UPDATE techstorepro.users u
            SET company_id = ch.company_id,
                is_owner  = ch.is_owner
            FROM chosen ch
            WHERE u.id = ch.user_id;
            """);

        migrationBuilder.Sql(
            """
            -- A username from the email's local part ('maryam@gulf.ae' -> 'maryam'), de-duplicated
            -- within the company, since the old email was unique platform-wide but the new username
            -- only has to be unique per company — and two companies' users could collide.
            WITH derived AS (
                SELECT
                    id,
                    company_id,
                    LOWER(SPLIT_PART(email, '@', 1)) AS base,
                    ROW_NUMBER() OVER (
                        PARTITION BY company_id, LOWER(SPLIT_PART(email, '@', 1))
                        ORDER BY created_at, id
                    ) AS n
                FROM techstorepro.users
                WHERE username IS NULL
            )
            UPDATE techstorepro.users u
            SET username = CASE
                WHEN d.base = '' THEN 'user' || SUBSTRING(REPLACE(u.id::text, '-', ''), 1, 8)
                WHEN d.n = 1 THEN d.base
                ELSE d.base || d.n::text
            END
            FROM derived d
            WHERE u.id = d.id;
            """);

        migrationBuilder.Sql(
            """
            -- Permission grants pointed at company_user rows; repoint them at the user directly.
            UPDATE techstorepro.user_permissions p
            SET user_id = cu.user_id
            FROM techstorepro.company_users cu
            WHERE p.user_id = cu.id;
            """);

        migrationBuilder.Sql(
            """
            INSERT INTO techstorepro.user_branches (id, company_id, user_id, branch_id, created_at)
            SELECT gen_random_uuid(), cu.company_id, cu.user_id, cub.branch_id, now()
            FROM techstorepro.company_user_branches cub
            JOIN techstorepro.company_users cu ON cu.id = cub.company_user_id
            WHERE cu.is_deleted = false
            ON CONFLICT DO NOTHING;
            """);

        migrationBuilder.Sql(
            """
            UPDATE techstorepro.login_history SET login = email WHERE login IS NULL;
            UPDATE techstorepro.audit_log SET username = user_email WHERE username IS NULL;
            """);

        migrationBuilder.Sql(
            """
            -- A user with no membership at all cannot be pinned to a company, and a user row with
            -- no company has no meaning in the new model — it could never log in and nothing could
            -- ever see it. Retire it rather than leave it dangling with a null tenant.
            UPDATE techstorepro.users
            SET is_deleted = true,
                deleted_at = now(),
                deleted_reason = 'No company membership at the username-login migration.'
            WHERE company_id IS NULL AND is_deleted = false;

            DELETE FROM techstorepro.users WHERE company_id IS NULL;
            """);

        // --- 3. Now the columns can be made NOT NULL --------------------------------------------

        migrationBuilder.AlterColumn<string>(
            name: "code",
            schema: "techstorepro",
            table: "companies",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20,
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "company_id",
            schema: "techstorepro",
            table: "users",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "username",
            schema: "techstorepro",
            table: "users",
            type: "citext",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "citext",
            oldMaxLength: 100,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "login",
            schema: "techstorepro",
            table: "login_history",
            type: "citext",
            maxLength: 360,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "citext",
            oldMaxLength: 360,
            oldNullable: true);

        // --- 4. The old world can go now ---------------------------------------------------------

        migrationBuilder.DropTable(
            name: "company_user_branches",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "company_users",
            schema: "techstorepro");

        migrationBuilder.DropIndex(
            name: "ix_users_email",
            schema: "techstorepro",
            table: "users");

        migrationBuilder.DropIndex(
            name: "ix_refresh_tokens_user_id_company_id",
            schema: "techstorepro",
            table: "refresh_tokens");

        migrationBuilder.DropIndex(
            name: "ix_login_history_email_at",
            schema: "techstorepro",
            table: "login_history");

        migrationBuilder.DropColumn(
            name: "company_id",
            schema: "techstorepro",
            table: "refresh_tokens");

        migrationBuilder.DropColumn(
            name: "email",
            schema: "techstorepro",
            table: "login_history");

        migrationBuilder.DropColumn(
            name: "user_email",
            schema: "techstorepro",
            table: "audit_log");

        migrationBuilder.CreateTable(
            name: "platform_admins",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                username = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                email = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
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
                table.PrimaryKey("pk_platform_admins", x => x.id);
            });

        // The foreign keys for user_branches, added now rather than at CreateTable: the table had to
        // exist before the backfill above could drain company_user_branches into it.
        migrationBuilder.AddForeignKey(
            name: "fk_user_branches_branches_branch_id",
            schema: "techstorepro",
            table: "user_branches",
            column: "branch_id",
            principalSchema: "techstorepro",
            principalTable: "branches",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_user_branches_users_user_id",
            schema: "techstorepro",
            table: "user_branches",
            column: "user_id",
            principalSchema: "techstorepro",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.CreateTable(
            name: "platform_refresh_tokens",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                platform_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("pk_platform_refresh_tokens", x => x.id);
                table.ForeignKey(
                    name: "fk_platform_refresh_tokens_platform_admins_platform_admin_id",
                    column: x => x.platform_admin_id,
                    principalSchema: "techstorepro",
                    principalTable: "platform_admins",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_users_company_id_username",
            schema: "techstorepro",
            table: "users",
            columns: new[] { "company_id", "username" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_user_id",
            schema: "techstorepro",
            table: "refresh_tokens",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_login_history_login_at",
            schema: "techstorepro",
            table: "login_history",
            columns: new[] { "login", "at" });

        migrationBuilder.CreateIndex(
            name: "ix_companies_code",
            schema: "techstorepro",
            table: "companies",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_platform_admins_username",
            schema: "techstorepro",
            table: "platform_admins",
            column: "username",
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "ix_platform_refresh_tokens_platform_admin_id",
            schema: "techstorepro",
            table: "platform_refresh_tokens",
            column: "platform_admin_id");

        migrationBuilder.CreateIndex(
            name: "ix_platform_refresh_tokens_token_hash",
            schema: "techstorepro",
            table: "platform_refresh_tokens",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_user_branches_branch_id",
            schema: "techstorepro",
            table: "user_branches",
            column: "branch_id");

        migrationBuilder.CreateIndex(
            name: "ix_user_branches_company_id_user_id_branch_id",
            schema: "techstorepro",
            table: "user_branches",
            columns: new[] { "company_id", "user_id", "branch_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_user_branches_user_id",
            schema: "techstorepro",
            table: "user_branches",
            column: "user_id");

        migrationBuilder.AddForeignKey(
            name: "fk_user_permissions_users_user_id",
            schema: "techstorepro",
            table: "user_permissions",
            column: "user_id",
            principalSchema: "techstorepro",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_users_companies_company_id",
            schema: "techstorepro",
            table: "users",
            column: "company_id",
            principalSchema: "techstorepro",
            principalTable: "companies",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_user_permissions_users_user_id",
            schema: "techstorepro",
            table: "user_permissions");

        migrationBuilder.DropForeignKey(
            name: "fk_users_companies_company_id",
            schema: "techstorepro",
            table: "users");

        migrationBuilder.DropTable(
            name: "platform_refresh_tokens",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "user_branches",
            schema: "techstorepro");

        migrationBuilder.DropTable(
            name: "platform_admins",
            schema: "techstorepro");

        migrationBuilder.DropIndex(
            name: "ix_users_company_id_username",
            schema: "techstorepro",
            table: "users");

        migrationBuilder.DropIndex(
            name: "ix_refresh_tokens_user_id",
            schema: "techstorepro",
            table: "refresh_tokens");

        migrationBuilder.DropIndex(
            name: "ix_login_history_login_at",
            schema: "techstorepro",
            table: "login_history");

        migrationBuilder.DropIndex(
            name: "ix_companies_code",
            schema: "techstorepro",
            table: "companies");

        migrationBuilder.DropColumn(
            name: "company_id",
            schema: "techstorepro",
            table: "users");

        migrationBuilder.DropColumn(
            name: "is_owner",
            schema: "techstorepro",
            table: "users");

        migrationBuilder.DropColumn(
            name: "username",
            schema: "techstorepro",
            table: "users");

        migrationBuilder.DropColumn(
            name: "login",
            schema: "techstorepro",
            table: "login_history");

        migrationBuilder.DropColumn(
            name: "code",
            schema: "techstorepro",
            table: "companies");

        migrationBuilder.DropColumn(
            name: "username",
            schema: "techstorepro",
            table: "audit_log");

        migrationBuilder.RenameColumn(
            name: "user_id",
            schema: "techstorepro",
            table: "user_permissions",
            newName: "company_user_id");

        migrationBuilder.RenameIndex(
            name: "ix_user_permissions_user_id_feature_code_action",
            schema: "techstorepro",
            table: "user_permissions",
            newName: "ix_user_permissions_company_user_id_feature_code_action");

        migrationBuilder.AlterColumn<string>(
            name: "email",
            schema: "techstorepro",
            table: "users",
            type: "citext",
            maxLength: 256,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "citext",
            oldMaxLength: 256,
            oldNullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "company_id",
            schema: "techstorepro",
            table: "refresh_tokens",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<string>(
            name: "email",
            schema: "techstorepro",
            table: "login_history",
            type: "citext",
            maxLength: 256,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "user_email",
            schema: "techstorepro",
            table: "audit_log",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "company_users",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                deleted_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                is_default = table.Column<bool>(type: "boolean", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                is_owner = table.Column<bool>(type: "boolean", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
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
            name: "company_user_branches",
            schema: "techstorepro",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
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

        migrationBuilder.CreateIndex(
            name: "ix_users_email",
            schema: "techstorepro",
            table: "users",
            column: "email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_user_id_company_id",
            schema: "techstorepro",
            table: "refresh_tokens",
            columns: new[] { "user_id", "company_id" });

        migrationBuilder.CreateIndex(
            name: "ix_login_history_email_at",
            schema: "techstorepro",
            table: "login_history",
            columns: new[] { "email", "at" });

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

        migrationBuilder.AddForeignKey(
            name: "fk_user_permissions_company_users_company_user_id",
            schema: "techstorepro",
            table: "user_permissions",
            column: "company_user_id",
            principalSchema: "techstorepro",
            principalTable: "company_users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
    }
}
