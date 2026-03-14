using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddRealmIdentityBoostAndLeveling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "boost_points",
                table: "realms",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "bio",
                table: "realm_members",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "experience",
                table: "realm_members",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "label_id",
                table: "realm_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nick",
                table: "realm_members",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "realm_boost_contributions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realm_boost_contributions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "realm_experience_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_type = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realm_experience_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "realm_labels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    color = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    icon = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realm_labels", x => x.id);
                    table.ForeignKey(
                        name: "fk_realm_labels_realms_realm_id",
                        column: x => x.realm_id,
                        principalTable: "realms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_realm_members_label_id",
                table: "realm_members",
                column: "label_id");

            migrationBuilder.CreateIndex(
                name: "ix_realm_labels_realm_id",
                table: "realm_labels",
                column: "realm_id");

            migrationBuilder.AddForeignKey(
                name: "fk_realm_members_realm_labels_label_id",
                table: "realm_members",
                column: "label_id",
                principalTable: "realm_labels",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_realm_members_realm_labels_label_id",
                table: "realm_members");

            migrationBuilder.DropTable(
                name: "realm_boost_contributions");

            migrationBuilder.DropTable(
                name: "realm_experience_records");

            migrationBuilder.DropTable(
                name: "realm_labels");

            migrationBuilder.DropIndex(
                name: "ix_realm_members_label_id",
                table: "realm_members");

            migrationBuilder.DropColumn(
                name: "boost_points",
                table: "realms");

            migrationBuilder.DropColumn(
                name: "bio",
                table: "realm_members");

            migrationBuilder.DropColumn(
                name: "experience",
                table: "realm_members");

            migrationBuilder.DropColumn(
                name: "label_id",
                table: "realm_members");

            migrationBuilder.DropColumn(
                name: "nick",
                table: "realm_members");
        }
    }
}
