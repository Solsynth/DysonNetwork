using System;
using System.Collections.Generic;
using DysonNetwork.Drive.Storage;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudFilePool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Dictionary<string, object>>(
                name: "file_meta",
                table: "files",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(Dictionary<string, object>),
                oldType: "jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "has_thumbnail",
                table: "files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_encrypted",
                table: "files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "pool_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pools",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    storage_config = table.Column<RemoteStorageConfig>(type: "jsonb", nullable: false),
                    billing_config = table.Column<BillingConfig>(type: "jsonb", nullable: false),
                    policy_config = table.Column<PolicyConfig>(type: "jsonb", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pools", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_files_pool_id",
                table: "files",
                column: "pool_id");

            migrationBuilder.AddForeignKey(
                name: "fk_files_pools_pool_id",
                table: "files",
                column: "pool_id",
                principalTable: "pools",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_pools_pool_id",
                table: "files");

            migrationBuilder.DropTable(
                name: "pools");

            migrationBuilder.DropIndex(
                name: "ix_files_pool_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "has_thumbnail",
                table: "files");

            migrationBuilder.DropColumn(
                name: "is_encrypted",
                table: "files");

            migrationBuilder.DropColumn(
                name: "pool_id",
                table: "files");

            migrationBuilder.AlterColumn<Dictionary<string, object>>(
                name: "file_meta",
                table: "files",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(Dictionary<string, object>),
                oldType: "jsonb",
                oldNullable: true);
        }
    }
}
