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
                name: "posts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    content = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    views_unique = table.Column<int>(type: "integer", nullable: false),
                    views_total = table.Column<int>(type: "integer", nullable: false),
                    upvotes = table.Column<int>(type: "integer", nullable: false),
                    downvotes = table.Column<int>(type: "integer", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "ix_files_post_id",
                table: "files",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_reactions_account_id",
                table: "post_reactions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_reactions_post_id",
                table: "post_reactions",
                column: "post_id");

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
                name: "post_reactions");

            migrationBuilder.DropTable(
                name: "publisher_members");

            migrationBuilder.DropTable(
                name: "posts");

            migrationBuilder.DropTable(
                name: "publishers");

            migrationBuilder.DropIndex(
                name: "ix_files_post_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "post_id",
                table: "files");
        }
    }
}
