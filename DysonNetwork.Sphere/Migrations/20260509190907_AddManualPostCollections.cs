using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddManualPostCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "post_collection_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_collection_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_collection_items_post_collections_collection_id",
                        column: x => x.collection_id,
                        principalTable: "post_collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_collection_items_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                @"INSERT INTO post_collection_items (id, collection_id, post_id, ""order"", created_at, updated_at, deleted_at)
SELECT gen_random_uuid(),
       link.collections_id,
       link.posts_id,
       ROW_NUMBER() OVER (PARTITION BY link.collections_id ORDER BY link.posts_id) - 1,
       NOW(),
       NOW(),
       NULL
FROM post_collection_links AS link;"
            );

            migrationBuilder.DropTable(
                name: "post_collection_links");

            migrationBuilder.DropIndex(
                name: "ix_post_collections_publisher_id",
                table: "post_collections");

            migrationBuilder.CreateIndex(
                name: "ix_post_collections_publisher_id_slug_deleted_at",
                table: "post_collections",
                columns: new[] { "publisher_id", "slug", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_collection_items_collection_id_order",
                table: "post_collection_items",
                columns: new[] { "collection_id", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_post_collection_items_collection_id_post_id_deleted_at",
                table: "post_collection_items",
                columns: new[] { "collection_id", "post_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_collection_items_post_id",
                table: "post_collection_items",
                column: "post_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "post_collection_items");

            migrationBuilder.DropIndex(
                name: "ix_post_collections_publisher_id_slug_deleted_at",
                table: "post_collections");

            migrationBuilder.CreateTable(
                name: "post_collection_links",
                columns: table => new
                {
                    collections_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posts_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_collection_links", x => new { x.collections_id, x.posts_id });
                    table.ForeignKey(
                        name: "fk_post_collection_links_post_collections_collections_id",
                        column: x => x.collections_id,
                        principalTable: "post_collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_collection_links_posts_posts_id",
                        column: x => x.posts_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_post_collections_publisher_id",
                table: "post_collections",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_collection_links_posts_id",
                table: "post_collection_links",
                column: "posts_id");
        }
    }
}
