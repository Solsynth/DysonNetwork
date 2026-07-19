using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPublisherSubscriptionReadIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_publisher_subscriptions_account_id_publisher_id_ended_at",
                table: "publisher_subscriptions",
                columns: new[] { "account_id", "publisher_id", "ended_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_publisher_subscriptions_account_id_publisher_id_ended_at",
                table: "publisher_subscriptions");
        }
    }
}
