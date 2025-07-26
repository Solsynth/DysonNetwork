using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class FilePoolAuthorize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                table: "pools",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "public_indexable",
                table: "pools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "public_usable",
                table: "pools",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "account_id",
                table: "pools");

            migrationBuilder.DropColumn(
                name: "public_indexable",
                table: "pools");

            migrationBuilder.DropColumn(
                name: "public_usable",
                table: "pools");
        }
    }
}
