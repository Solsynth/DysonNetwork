using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPostChaining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "chained_post_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_posts_chained_post_id",
                table: "posts",
                column: "chained_post_id");

            migrationBuilder.AddForeignKey(
                name: "fk_posts_posts_chained_post_id",
                table: "posts",
                column: "chained_post_id",
                principalTable: "posts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_posts_posts_chained_post_id",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_posts_chained_post_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "chained_post_id",
                table: "posts");
        }
    }
}
