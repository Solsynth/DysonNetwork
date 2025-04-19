using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "post_id",
                table: "files",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "post_categories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "post_tags",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "publishers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    publisher_type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    nick = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    bio = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    picture_id = table.Column<string>(type: "text", nullable: true),
                    background_id = table.Column<string>(type: "text", nullable: true),
                    account_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publishers", x => x.id);
                    table.ForeignKey(
                        name: "fk_publishers_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_publishers_files_background_id",
                        column: x => x.background_id,
                        principalTable: "files",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_publishers_files_picture_id",
                        column: x => x.picture_id,
                        principalTable: "files",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "post_collections",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    publisher_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_collections", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_collections_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "posts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    language = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    edited_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    visibility = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    views_unique = table.Column<int>(type: "integer", nullable: false),
                    views_total = table.Column<int>(type: "integer", nullable: false),
                    upvotes = table.Column<int>(type: "integer", nullable: false),
                    downvotes = table.Column<int>(type: "integer", nullable: false),
                    threaded_post_id = table.Column<long>(type: "bigint", nullable: true),
                    replied_post_id = table.Column<long>(type: "bigint", nullable: true),
                    forwarded_post_id = table.Column<long>(type: "bigint", nullable: true),
                    publisher_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posts", x => x.id);
                    table.ForeignKey(
                        name: "fk_posts_posts_forwarded_post_id",
                        column: x => x.forwarded_post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_posts_posts_replied_post_id",
                        column: x => x.replied_post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_posts_posts_threaded_post_id",
                        column: x => x.threaded_post_id,
                        principalTable: "posts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_posts_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "publisher_members",
                columns: table => new
                {
                    publisher_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    joined_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publisher_members", x => new { x.publisher_id, x.account_id });
                    table.ForeignKey(
                        name: "fk_publisher_members_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_publisher_members_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_category_links",
                columns: table => new
                {
                    categories_id = table.Column<long>(type: "bigint", nullable: false),
                    posts_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_category_links", x => new { x.categories_id, x.posts_id });
                    table.ForeignKey(
                        name: "fk_post_category_links_post_categories_categories_id",
                        column: x => x.categories_id,
                        principalTable: "post_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_category_links_posts_posts_id",
                        column: x => x.posts_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_collection_links",
                columns: table => new
                {
                    collections_id = table.Column<long>(type: "bigint", nullable: false),
                    posts_id = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "post_reactions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    symbol = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    attitude = table.Column<int>(type: "integer", nullable: false),
                    post_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_reactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_reactions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_reactions_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_tag_links",
                columns: table => new
                {
                    posts_id = table.Column<long>(type: "bigint", nullable: false),
                    tags_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_tag_links", x => new { x.posts_id, x.tags_id });
                    table.ForeignKey(
                        name: "fk_post_tag_links_post_tags_tags_id",
                        column: x => x.tags_id,
                        principalTable: "post_tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_tag_links_posts_posts_id",
                        column: x => x.posts_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_files_post_id",
                table: "files",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_name",
                table: "accounts",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_category_links_posts_id",
                table: "post_category_links",
                column: "posts_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_collection_links_posts_id",
                table: "post_collection_links",
                column: "posts_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_collections_publisher_id",
                table: "post_collections",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_reactions_account_id",
                table: "post_reactions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_reactions_post_id",
                table: "post_reactions",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_tag_links_tags_id",
                table: "post_tag_links",
                column: "tags_id");

            migrationBuilder.CreateIndex(
                name: "ix_posts_forwarded_post_id",
                table: "posts",
                column: "forwarded_post_id");

            migrationBuilder.CreateIndex(
                name: "ix_posts_publisher_id",
                table: "posts",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_posts_replied_post_id",
                table: "posts",
                column: "replied_post_id");

            migrationBuilder.CreateIndex(
                name: "ix_posts_threaded_post_id",
                table: "posts",
                column: "threaded_post_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publisher_members_account_id",
                table: "publisher_members",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_publishers_account_id",
                table: "publishers",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_publishers_background_id",
                table: "publishers",
                column: "background_id");

            migrationBuilder.CreateIndex(
                name: "ix_publishers_name",
                table: "publishers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publishers_picture_id",
                table: "publishers",
                column: "picture_id");

            migrationBuilder.AddForeignKey(
                name: "fk_files_posts_post_id",
                table: "files",
                column: "post_id",
                principalTable: "posts",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_posts_post_id",
                table: "files");

            migrationBuilder.DropTable(
                name: "post_category_links");

            migrationBuilder.DropTable(
                name: "post_collection_links");

            migrationBuilder.DropTable(
                name: "post_reactions");

            migrationBuilder.DropTable(
                name: "post_tag_links");

            migrationBuilder.DropTable(
                name: "publisher_members");

            migrationBuilder.DropTable(
                name: "post_categories");

            migrationBuilder.DropTable(
                name: "post_collections");

            migrationBuilder.DropTable(
                name: "post_tags");

            migrationBuilder.DropTable(
                name: "posts");

            migrationBuilder.DropTable(
                name: "publishers");

            migrationBuilder.DropIndex(
                name: "ix_files_post_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_accounts_name",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "post_id",
                table: "files");
        }
    }
}
