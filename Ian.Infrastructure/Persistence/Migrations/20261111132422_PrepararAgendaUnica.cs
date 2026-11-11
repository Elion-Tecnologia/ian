using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrepararAgendaUnica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SELECT pg_advisory_xact_lock(790, 1);
                LOCK TABLE public.agendamentos, public.bloqueios_agenda,
                    public.equipes, public.excecoes_expediente IN ACCESS EXCLUSIVE MODE;
                CREATE SCHEMA legado_mvp;
                REVOKE ALL ON SCHEMA legado_mvp FROM PUBLIC;
                CREATE TABLE legado_mvp.registros_preparacao (
                    "Tabela" text NOT NULL,
                    "Id" bigint NOT NULL,
                    "Dados" jsonb NOT NULL,
                    PRIMARY KEY ("Tabela", "Id")
                );
                INSERT INTO legado_mvp.registros_preparacao
                    SELECT 'agendamentos', "Id", to_jsonb(a) FROM public.agendamentos a
                    UNION ALL SELECT 'equipes', "Id", to_jsonb(e) FROM public.equipes e
                    UNION ALL SELECT 'excecoes_expediente', "Id", to_jsonb(e) FROM public.excecoes_expediente e
                    UNION ALL SELECT 'bloqueios_agenda', "Id", to_jsonb(b) FROM public.bloqueios_agenda b;
                CREATE TABLE legado_mvp.agendamentos_equipes (
                    "AgendamentoId" bigint PRIMARY KEY REFERENCES public.agendamentos("Id") ON DELETE RESTRICT,
                    "EquipeId" bigint NOT NULL REFERENCES public.equipes("Id") ON DELETE RESTRICT,
                    "ArquivadoEmUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                INSERT INTO legado_mvp.agendamentos_equipes ("AgendamentoId", "EquipeId")
                    SELECT "Id", "EquipeId" FROM public.agendamentos;
                CREATE TABLE legado_mvp.manifesto (
                    "Etapa" text PRIMARY KEY,
                    "RegistradoEmUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    "Contagens" jsonb NOT NULL,
                    "Hashes" jsonb NOT NULL DEFAULT '{}'::jsonb
                );
                INSERT INTO legado_mvp.manifesto ("Etapa", "Contagens")
                    SELECT 'PrepararAgendaUnica', jsonb_build_object(
                        'agendamentos', (SELECT count(*) FROM public.agendamentos),
                        'equipes', (SELECT count(*) FROM public.equipes),
                        'excecoes_expediente', (SELECT count(*) FROM public.excecoes_expediente),
                        'bloqueios_agenda', (SELECT count(*) FROM public.bloqueios_agenda));
                UPDATE legado_mvp.manifesto SET "Hashes" = (
                    SELECT COALESCE(jsonb_object_agg("Tabela", digest), '{}'::jsonb)
                    FROM (
                        SELECT "Tabela", md5(string_agg("Dados"::text, ',' ORDER BY "Id")) digest
                        FROM legado_mvp.registros_preparacao GROUP BY "Tabela"
                    ) hashes
                ) WHERE "Etapa" = 'PrepararAgendaUnica';
                """);
            migrationBuilder.AddColumn<long>(
                name: "Versao",
                table: "agendamentos",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddCheckConstraint(
                name: "ck_agendamentos_versao",
                table: "agendamentos",
                sql: "\"Versao\" >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Retorno exige restauração ou reconciliação revisada; o arquivo legado não deve ser apagado.");
        }
    }
}
