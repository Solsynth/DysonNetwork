using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPostPublishNotifiedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "publish_notified_at",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            // Every post that is already published had its publish side effects
            // dispatched by the create/update path, so mark them as claimed.
            // Otherwise the scheduled publish job would replay them. Posts that
            // are still scheduled for the future stay unclaimed so the job can
            // announce them when their time comes.
            migrationBuilder.Sql(
                """
                UPDATE posts
                SET publish_notified_at = published_at
                WHERE published_at IS NOT NULL
                  AND drafted_at IS NULL
                  AND published_at <= now();
                """);

            migrationBuilder.CreateIndex(
                name: "ix_posts_publish_notified_at_published_at",
                table: "posts",
                columns: new[] { "publish_notified_at", "published_at" },
                filter: "publish_notified_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_posts_publish_notified_at_published_at",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "publish_notified_at",
                table: "posts");
        }
    }
}
