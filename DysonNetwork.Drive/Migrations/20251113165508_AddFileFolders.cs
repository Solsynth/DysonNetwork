using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class AddFileFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_file_indexes_path_account_id",
                table: "file_indexes");

            migrationBuilder.AlterColumn<string>(
                name: "path",
                table: "file_indexes",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8192)",
                oldMaxLength: 8192);

            migrationBuilder.AddColumn<Guid>(
                name: "folder_id",
                table: "file_indexes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "folders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    path = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    parent_folder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    folder_meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_folders", x => x.id);
                    table.ForeignKey(
                        name: "fk_folders_folders_parent_folder_id",
                        column: x => x.parent_folder_id,
                        principalTable: "folders",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_file_indexes_folder_id_account_id",
                table: "file_indexes",
                columns: new[] { "folder_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_folders_parent_folder_id",
                table: "folders",
                column: "parent_folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_folders_path_account_id",
                table: "folders",
                columns: new[] { "path", "account_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_file_indexes_folders_folder_id",
                table: "file_indexes",
                column: "folder_id",
                principalTable: "folders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_file_indexes_folders_folder_id",
                table: "file_indexes");

            migrationBuilder.DropTable(
                name: "folders");

            migrationBuilder.DropIndex(
                name: "ix_file_indexes_folder_id_account_id",
                table: "file_indexes");

            migrationBuilder.DropColumn(
                name: "folder_id",
                table: "file_indexes");

            migrationBuilder.AlterColumn<string>(
                name: "path",
                table: "file_indexes",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.CreateIndex(
                name: "ix_file_indexes_path_account_id",
                table: "file_indexes",
                columns: new[] { "path", "account_id" });
        }
    }
}
