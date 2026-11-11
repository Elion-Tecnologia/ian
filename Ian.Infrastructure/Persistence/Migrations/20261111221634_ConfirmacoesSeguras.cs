using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmacoesSeguras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfirmacaoJson",
                table: "solicitacoes_pendentes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_solicitacoes_confirmacao_objeto",
                table: "solicitacoes_pendentes",
                sql: "\"ConfirmacaoJson\" IS NULL OR jsonb_typeof(\"ConfirmacaoJson\") = 'object'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM solicitacoes_pendentes WHERE "ConfirmacaoJson" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Rollback recusado: existem confirmações persistidas.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_solicitacoes_confirmacao_objeto",
                table: "solicitacoes_pendentes");

            migrationBuilder.DropColumn(
                name: "ConfirmacaoJson",
                table: "solicitacoes_pendentes");
        }
    }
}
