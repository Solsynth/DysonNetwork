using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddAppleWalletPasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "apple_passes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pass_type_identifier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    serial_number = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    authentication_token = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    last_updated_tag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_apple_passes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "apple_pass_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pass_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_library_identifier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    push_token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_apple_pass_registrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_apple_pass_registrations_apple_passes_pass_id",
                        column: x => x.pass_id,
                        principalTable: "apple_passes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_apple_pass_registrations_device_library_identifier_pass_id",
                table: "apple_pass_registrations",
                columns: new[] { "device_library_identifier", "pass_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_apple_pass_registrations_pass_id",
                table: "apple_pass_registrations",
                column: "pass_id");

            migrationBuilder.CreateIndex(
                name: "ix_apple_passes_account_id_pass_type_identifier",
                table: "apple_passes",
                columns: new[] { "account_id", "pass_type_identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_apple_passes_pass_type_identifier_serial_number",
                table: "apple_passes",
                columns: new[] { "pass_type_identifier", "serial_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "apple_pass_registrations");

            migrationBuilder.DropTable(
                name: "apple_passes");
        }
    }
}
