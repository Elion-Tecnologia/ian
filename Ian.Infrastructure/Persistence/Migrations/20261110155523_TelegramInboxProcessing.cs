using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TelegramInboxProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_telegram_updates_inbox_Estado_RecebidoEmUtc",
                table: "telegram_updates_inbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_telegram_inbox_estado",
                table: "telegram_updates_inbox");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConcluidoEmUtc",
                table: "telegram_updates_inbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IniciadoEmUtc",
                table: "telegram_updates_inbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockAteUtc",
                table: "telegram_updates_inbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LockToken",
                table: "telegram_updates_inbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProximaTentativaEmUtc",
                table: "telegram_updates_inbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Resultado",
                table: "telegram_updates_inbox",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Tentativas",
                table: "telegram_updates_inbox",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UltimoErroCodigo",
                table: "telegram_updates_inbox",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "telegram_business_operations",
                columns: table => new
                {
                    UpdateId = table.Column<long>(type: "bigint", nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExecutadoEmUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_business_operations", x => new { x.UpdateId, x.OperationKey });
                    table.ForeignKey(
                        name: "FK_telegram_business_operations_telegram_updates_inbox_UpdateId",
                        column: x => x.UpdateId,
                        principalTable: "telegram_updates_inbox",
                        principalColumn: "UpdateId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_updates_inbox_Estado_LockAteUtc",
                table: "telegram_updates_inbox",
                columns: new[] { "Estado", "LockAteUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_updates_inbox_Estado_ProximaTentativaEmUtc_Recebid~",
                table: "telegram_updates_inbox",
                columns: new[] { "Estado", "ProximaTentativaEmUtc", "RecebidoEmUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_telegram_inbox_estado",
                table: "telegram_updates_inbox",
                sql: "\"Estado\" IN (\r\n    'Received',\r\n    'Processing',\r\n    'Completed',\r\n    'FailedRetryable',\r\n    'FailedPaused'\r\n)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_telegram_inbox_lease",
                table: "telegram_updates_inbox",
                sql: "(\r\n    \"Estado\" = 'Processing'\r\n    AND \"LockToken\" IS NOT NULL\r\n    AND \"LockAteUtc\" IS NOT NULL\r\n)\r\nOR\r\n(\r\n    \"Estado\" <> 'Processing'\r\n    AND \"LockToken\" IS NULL\r\n    AND \"LockAteUtc\" IS NULL\r\n)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_telegram_inbox_retry",
                table: "telegram_updates_inbox",
                sql: "(\"Estado\" = 'FailedRetryable' AND \"ProximaTentativaEmUtc\" IS NOT NULL)\r\nOR\r\n(\"Estado\" <> 'FailedRetryable' AND \"ProximaTentativaEmUtc\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_telegram_inbox_tentativas",
                table: "telegram_updates_inbox",
                sql: "\"Tentativas\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_business_operations");

            migrationBuilder.DropIndex(
                name: "IX_telegram_updates_inbox_Estado_LockAteUtc",
                table: "telegram_updates_inbox");

            migrationBuilder.DropIndex(
                name: "IX_telegram_updates_inbox_Estado_ProximaTentativaEmUtc_Recebid~",
                table: "telegram_updates_inbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_telegram_inbox_estado",
                table: "telegram_updates_inbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_telegram_inbox_lease",
                table: "telegram_updates_inbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_telegram_inbox_retry",
                table: "telegram_updates_inbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_telegram_inbox_tentativas",
                table: "telegram_updates_inbox");

            migrationBuilder.DropColumn(
                name: "ConcluidoEmUtc",
                table: "telegram_updates_inbox");

            migrationBuilder.DropColumn(
                name: "IniciadoEmUtc",
                table: "telegram_updates_inbox");

            migrationBuilder.DropColumn(
                name: "LockAteUtc",
                table: "telegram_updates_inbox");

            migrationBuilder.DropColumn(
                name: "LockToken",
                table: "telegram_updates_inbox");

            migrationBuilder.DropColumn(
                name: "ProximaTentativaEmUtc",
                table: "telegram_updates_inbox");

            migrationBuilder.DropColumn(
                name: "Resultado",
                table: "telegram_updates_inbox");

            migrationBuilder.DropColumn(
                name: "Tentativas",
                table: "telegram_updates_inbox");

            migrationBuilder.DropColumn(
                name: "UltimoErroCodigo",
                table: "telegram_updates_inbox");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_updates_inbox_Estado_RecebidoEmUtc",
                table: "telegram_updates_inbox",
                columns: new[] { "Estado", "RecebidoEmUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_telegram_inbox_estado",
                table: "telegram_updates_inbox",
                sql: "\"Estado\" IN ('Received')");
        }
    }
}
