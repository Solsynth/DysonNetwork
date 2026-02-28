using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AdjustE2eeEnvelopeIdempotencyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_e2ee_envelopes_recipient_id_client_message_id",
                table: "e2ee_envelopes");

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_envelopes_recipient_id_sender_id_client_message_id",
                table: "e2ee_envelopes",
                columns: new[] { "recipient_id", "sender_id", "client_message_id" },
                unique: true,
                filter: "client_message_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_e2ee_envelopes_recipient_id_sender_id_client_message_id",
                table: "e2ee_envelopes");

            migrationBuilder.CreateIndex(
                name: "ix_e2ee_envelopes_recipient_id_client_message_id",
                table: "e2ee_envelopes",
                columns: new[] { "recipient_id", "client_message_id" },
                unique: true,
                filter: "client_message_id IS NOT NULL");
        }
    }
}
