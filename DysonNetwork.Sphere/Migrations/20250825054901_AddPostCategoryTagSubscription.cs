using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPostCategoryTagSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "post_category_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_category_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_category_subscriptions_post_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "post_categories",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_post_category_subscriptions_post_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "post_tags",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_post_category_subscriptions_category_id",
                table: "post_category_subscriptions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_category_subscriptions_tag_id",
                table: "post_category_subscriptions",
                column: "tag_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "post_category_subscriptions");
        }
    }
}
