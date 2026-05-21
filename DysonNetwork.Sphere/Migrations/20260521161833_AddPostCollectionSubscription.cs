using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPostCollectionSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "collection_id",
                table: "post_category_subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_category_subscriptions_collection_id",
                table: "post_category_subscriptions",
                column: "collection_id");

            migrationBuilder.AddForeignKey(
                name: "fk_post_category_subscriptions_post_collections_collection_id",
                table: "post_category_subscriptions",
                column: "collection_id",
                principalTable: "post_collections",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_post_category_subscriptions_post_collections_collection_id",
                table: "post_category_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_post_category_subscriptions_collection_id",
                table: "post_category_subscriptions");

            migrationBuilder.DropColumn(
                name: "collection_id",
                table: "post_category_subscriptions");
        }
    }
}
