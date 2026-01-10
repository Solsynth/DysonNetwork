using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class NullableReplicaPoolId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_file_replicas_pools_pool_id",
                table: "file_replicas");

            migrationBuilder.AlterColumn<Guid>(
                name: "pool_id",
                table: "file_replicas",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "fk_file_replicas_pools_pool_id",
                table: "file_replicas",
                column: "pool_id",
                principalTable: "pools",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_file_replicas_pools_pool_id",
                table: "file_replicas");

            migrationBuilder.AlterColumn<Guid>(
                name: "pool_id",
                table: "file_replicas",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_file_replicas_pools_pool_id",
                table: "file_replicas",
                column: "pool_id",
                principalTable: "pools",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
