using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class CleanCloudFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "file_meta",
                table: "files");

            migrationBuilder.DropColumn(
                name: "has_compression",
                table: "files");

            migrationBuilder.DropColumn(
                name: "has_thumbnail",
                table: "files");

            migrationBuilder.DropColumn(
                name: "hash",
                table: "files");

            migrationBuilder.DropColumn(
                name: "is_encrypted",
                table: "files");

            migrationBuilder.DropColumn(
                name: "mime_type",
                table: "files");

            migrationBuilder.DropColumn(
                name: "size",
                table: "files");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Dictionary<string, object>>(
                name: "file_meta",
                table: "files",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_compression",
                table: "files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_thumbnail",
                table: "files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "hash",
                table: "files",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_encrypted",
                table: "files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "mime_type",
                table: "files",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "size",
                table: "files",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
