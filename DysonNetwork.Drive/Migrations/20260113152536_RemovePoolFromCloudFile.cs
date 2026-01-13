using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class RemovePoolFromCloudFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_pools_pool_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_pool_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "pool_id",
                table: "files");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pool_id",
                table: "files",
                type: "uuid",
                nullable: true);

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
    }
}
