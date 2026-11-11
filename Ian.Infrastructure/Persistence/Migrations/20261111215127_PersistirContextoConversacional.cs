using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistirContextoConversacional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AtualizadaEmUtc",
                table: "solicitacoes_pendentes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CamposFaltantesJson",
                table: "solicitacoes_pendentes",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "CandidatosJson",
                table: "solicitacoes_pendentes",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "DadosColetadosJson",
                table: "solicitacoes_pendentes",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            // Previous placeholder requests have no recoverable field data; keep their rows but expire them.
            migrationBuilder.Sql(
                "UPDATE solicitacoes_pendentes SET \"Estado\" = 'Expirada', \"AtualizadaEmUtc\" = now() WHERE \"Estado\" = 'Pendente';");

            migrationBuilder.CreateIndex(
                name: "IX_solicitacoes_pendentes_TelegramUserId",
                table: "solicitacoes_pendentes",
                column: "TelegramUserId",
                unique: true,
                filter: "\"Estado\" = 'Pendente'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_solicitacoes_candidatos_objeto",
                table: "solicitacoes_pendentes",
                sql: "jsonb_typeof(\"CandidatosJson\") = 'object'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_solicitacoes_dados_objeto",
                table: "solicitacoes_pendentes",
                sql: "jsonb_typeof(\"DadosColetadosJson\") = 'object'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_solicitacoes_faltantes_array",
                table: "solicitacoes_pendentes",
                sql: "jsonb_typeof(\"CamposFaltantesJson\") = 'array'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM solicitacoes_pendentes
                        WHERE "DadosColetadosJson" <> '{}'::jsonb
                           OR "CamposFaltantesJson" <> '[]'::jsonb
                           OR "CandidatosJson" <> '{}'::jsonb
                    ) THEN
                        RAISE EXCEPTION 'Rollback recusado: existem contextos conversacionais que seriam perdidos.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_solicitacoes_pendentes_TelegramUserId",
                table: "solicitacoes_pendentes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_solicitacoes_candidatos_objeto",
                table: "solicitacoes_pendentes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_solicitacoes_dados_objeto",
                table: "solicitacoes_pendentes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_solicitacoes_faltantes_array",
                table: "solicitacoes_pendentes");

            migrationBuilder.DropColumn(
                name: "AtualizadaEmUtc",
                table: "solicitacoes_pendentes");

            migrationBuilder.DropColumn(
                name: "CamposFaltantesJson",
                table: "solicitacoes_pendentes");

            migrationBuilder.DropColumn(
                name: "CandidatosJson",
                table: "solicitacoes_pendentes");

            migrationBuilder.DropColumn(
                name: "DadosColetadosJson",
                table: "solicitacoes_pendentes");
        }
    }
}
