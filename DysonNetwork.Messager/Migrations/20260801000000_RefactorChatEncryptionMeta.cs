using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Messager.Migrations;

public partial class RefactorChatEncryptionMeta : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "encryption_meta",
            table: "chat_messages",
            type: "jsonb",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE chat_messages
            SET encryption_meta = jsonb_build_object(
                'ciphertext', encode(ciphertext, 'base64'),
                'header', CASE WHEN encryption_header IS NULL THEN NULL ELSE encode(encryption_header, 'base64') END,
                'signature', CASE WHEN encryption_signature IS NULL THEN NULL ELSE encode(encryption_signature, 'base64') END,
                'scheme', encryption_scheme,
                'epoch', encryption_epoch
            )
            WHERE is_encrypted = TRUE;
            """);

        migrationBuilder.DropIndex(
            name: "ix_chat_messages_chat_room_id_is_encrypted_created_at",
            table: "chat_messages");

        migrationBuilder.DropIndex(
            name: "ix_chat_messages_content",
            table: "chat_messages");

        migrationBuilder.DropColumn(name: "is_encrypted", table: "chat_messages");
        migrationBuilder.DropColumn(name: "ciphertext", table: "chat_messages");
        migrationBuilder.DropColumn(name: "encryption_header", table: "chat_messages");
        migrationBuilder.DropColumn(name: "encryption_signature", table: "chat_messages");
        migrationBuilder.DropColumn(name: "encryption_scheme", table: "chat_messages");
        migrationBuilder.DropColumn(name: "encryption_epoch", table: "chat_messages");
        migrationBuilder.DropColumn(name: "encryption_message_type", table: "chat_messages");

        migrationBuilder.CreateIndex(
            name: "ix_chat_messages_content",
            table: "chat_messages",
            column: "content",
            filter: "type = 'text' AND encryption_meta IS NULL AND content IS NOT NULL AND deleted_at IS NULL")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "is_encrypted", table: "chat_messages", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<byte[]>(name: "ciphertext", table: "chat_messages", type: "bytea", nullable: true);
        migrationBuilder.AddColumn<byte[]>(name: "encryption_header", table: "chat_messages", type: "bytea", nullable: true);
        migrationBuilder.AddColumn<byte[]>(name: "encryption_signature", table: "chat_messages", type: "bytea", nullable: true);
        migrationBuilder.AddColumn<string>(name: "encryption_scheme", table: "chat_messages", type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<long>(name: "encryption_epoch", table: "chat_messages", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>(name: "encryption_message_type", table: "chat_messages", type: "character varying(128)", maxLength: 128, nullable: true);

        migrationBuilder.Sql("""
            UPDATE chat_messages
            SET is_encrypted = TRUE,
                ciphertext = decode(encryption_meta->>'ciphertext', 'base64'),
                encryption_header = CASE WHEN encryption_meta->>'header' IS NULL THEN NULL ELSE decode(encryption_meta->>'header', 'base64') END,
                encryption_signature = CASE WHEN encryption_meta->>'signature' IS NULL THEN NULL ELSE decode(encryption_meta->>'signature', 'base64') END,
                encryption_scheme = encryption_meta->>'scheme',
                encryption_epoch = (encryption_meta->>'epoch')::bigint
            WHERE encryption_meta IS NOT NULL;
            """);

        migrationBuilder.DropIndex(name: "ix_chat_messages_content", table: "chat_messages");
        migrationBuilder.CreateIndex(
            name: "ix_chat_messages_content",
            table: "chat_messages",
            column: "content",
            filter: "type = 'text' AND is_encrypted = FALSE AND content IS NOT NULL AND deleted_at IS NULL")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

        migrationBuilder.CreateIndex(
            name: "ix_chat_messages_chat_room_id_is_encrypted_created_at",
            table: "chat_messages",
            columns: new[] { "chat_room_id", "is_encrypted", "created_at" });

        migrationBuilder.DropColumn(name: "encryption_meta", table: "chat_messages");
    }
}
