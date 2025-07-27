using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotaRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quota_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    quota = table.Column<long>(type: "bigint", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quota_records", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quota_records");
        }
    }
}
