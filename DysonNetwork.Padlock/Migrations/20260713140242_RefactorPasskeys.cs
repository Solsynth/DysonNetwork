using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class RefactorPasskeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_passkeys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    credential_id = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    credential = table.Column<string>(type: "character varying(8196)", maxLength: 8196, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_passkeys", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_passkeys_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_passkeys_account_id",
                table: "account_passkeys",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_passkeys_credential_id",
                table: "account_passkeys",
                column: "credential_id",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.Sql("""
                INSERT INTO account_passkeys (id, account_id, label, credential_id, credential, created_at, updated_at, deleted_at)
                SELECT gen_random_uuid(), account_id, 'Passkey',
                       COALESCE(secret::jsonb ->> 'CredentialId', secret::jsonb ->> 'credentialId'),
                       secret, created_at, updated_at, deleted_at
                FROM account_auth_factors
                WHERE type = 7
                  AND secret IS NOT NULL
                  AND COALESCE(secret::jsonb ->> 'CredentialId', secret::jsonb ->> 'credentialId') IS NOT NULL
                ON CONFLICT (credential_id) DO NOTHING;
                """);

            migrationBuilder.Sql("""
                DELETE FROM account_auth_factors
                WHERE id IN (
                    SELECT id
                    FROM (
                        SELECT id, ROW_NUMBER() OVER (PARTITION BY account_id ORDER BY created_at, id) AS row_number
                        FROM account_auth_factors
                        WHERE type = 7
                    ) passkey_factors
                    WHERE row_number > 1
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_passkeys");
        }
    }
}
