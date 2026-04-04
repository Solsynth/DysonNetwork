using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class QuoteAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "quote_authorization_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sn_quote_authorization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fediverse_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interacting_object_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    interaction_target_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    target_post_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quote_post_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_valid = table.Column<bool>(type: "boolean", nullable: false),
                    revoked_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_quote_authorization", x => x.id);
                    table.ForeignKey(
                        name: "fk_sn_quote_authorization_fediverse_actors_author_id",
                        column: x => x.author_id,
                        principalTable: "fediverse_actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sn_quote_authorization_posts_quote_post_id",
                        column: x => x.quote_post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_sn_quote_authorization_posts_target_post_id",
                        column: x => x.target_post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_posts_quote_authorization_id",
                table: "posts",
                column: "quote_authorization_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_quote_authorization_author_id",
                table: "sn_quote_authorization",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_quote_authorization_quote_post_id",
                table: "sn_quote_authorization",
                column: "quote_post_id");

            migrationBuilder.CreateIndex(
                name: "ix_sn_quote_authorization_target_post_id",
                table: "sn_quote_authorization",
                column: "target_post_id");

            migrationBuilder.AddForeignKey(
                name: "fk_posts_sn_quote_authorization_quote_authorization_id",
                table: "posts",
                column: "quote_authorization_id",
                principalTable: "sn_quote_authorization",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_posts_sn_quote_authorization_quote_authorization_id",
                table: "posts");

            migrationBuilder.DropTable(
                name: "sn_quote_authorization");

            migrationBuilder.DropIndex(
                name: "ix_posts_quote_authorization_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "quote_authorization_id",
                table: "posts");
        }
    }
}
