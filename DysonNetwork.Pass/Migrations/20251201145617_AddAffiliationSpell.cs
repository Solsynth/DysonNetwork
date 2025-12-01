using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AddAffiliationSpell : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "affiliation_spells",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    spell = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    affected_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_affiliation_spells", x => x.id);
                    table.ForeignKey(
                        name: "fk_affiliation_spells_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "affiliation_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_identifier = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    spell_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_affiliation_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_affiliation_results_affiliation_spells_spell_id",
                        column: x => x.spell_id,
                        principalTable: "affiliation_spells",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_affiliation_results_spell_id",
                table: "affiliation_results",
                column: "spell_id");

            migrationBuilder.CreateIndex(
                name: "ix_affiliation_spells_account_id",
                table: "affiliation_spells",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_affiliation_spells_spell",
                table: "affiliation_spells",
                column: "spell",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "affiliation_results");

            migrationBuilder.DropTable(
                name: "affiliation_spells");
        }
    }
}
