using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ian.Infrastructure.Persistence.Migrations;

public partial class ConsolidarAgendaUnica : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SELECT pg_advisory_xact_lock(790, 1);
            LOCK TABLE public.agendamentos, public.bloqueios_agenda, public.equipes,
                public.excecoes_expediente, public.periodos_ocupacao IN ACCESS EXCLUSIVE MODE;
            DO $preflight$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM legado_mvp.manifesto WHERE "Etapa" = 'PrepararAgendaUnica')
                    OR (SELECT "Hashes" FROM legado_mvp.manifesto WHERE "Etapa" = 'PrepararAgendaUnica')
                        IS DISTINCT FROM (
                            SELECT COALESCE(jsonb_object_agg("Tabela", digest), '{}'::jsonb)
                            FROM (
                                SELECT "Tabela", md5(string_agg("Dados"::text, ',' ORDER BY "Id")) digest
                                FROM legado_mvp.registros_preparacao GROUP BY "Tabela"
                            ) hashes
                        )
                    OR EXISTS (
                        SELECT 1 FROM legado_mvp.manifesto m,
                            jsonb_each_text(m."Contagens") expected
                        WHERE m."Etapa" = 'PrepararAgendaUnica'
                        AND expected.value::bigint <> (
                            SELECT count(*) FROM legado_mvp.registros_preparacao r
                            WHERE r."Tabela" = expected.key)
                    ) THEN
                    RAISE EXCEPTION 'AgendaUnica: arquivo de preparacao incompleto';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM public.periodos_ocupacao p
                    JOIN public.agendamentos a ON a."Id" = p."AgendamentoId"
                    JOIN public.periodos_ocupacao q ON p."Id" < q."Id"
                        AND p."InicioUtc" < q."FimUtc" AND q."InicioUtc" < p."FimUtc"
                    JOIN public.agendamentos b ON b."Id" = q."AgendamentoId"
                    WHERE a."Estado" = 'Agendado' AND b."Estado" = 'Agendado'
                ) THEN
                    RAISE EXCEPTION 'AgendaUnica: sobreposicoes ativas exigem reconciliacao';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM public.agendamentos a WHERE a."Estado" = 'Agendado'
                    AND (
                        NOT EXISTS (SELECT 1 FROM public.periodos_ocupacao p WHERE p."AgendamentoId" = a."Id")
                        OR EXISTS (
                            SELECT 1 FROM public.periodos_ocupacao p WHERE p."AgendamentoId" = a."Id"
                            AND (p."InicioUtc" < a."InicioPrevistoUtc" OR p."FimUtc" > a."FimPrevistoUtc"
                                OR (p."InicioUtc" AT TIME ZONE 'America/Sao_Paulo')::date
                                    <> (p."FimUtc" AT TIME ZONE 'America/Sao_Paulo')::date
                                OR (p."InicioUtc" AT TIME ZONE 'America/Sao_Paulo')::time < time '08:00'
                                OR (p."FimUtc" AT TIME ZONE 'America/Sao_Paulo')::time > time '18:00')
                        )
                        OR (a."UnidadeDuracao" = 'Minutos' AND a."Duracao" <>
                            (SELECT sum(extract(epoch FROM p."FimUtc" - p."InicioUtc") / 60)
                             FROM public.periodos_ocupacao p WHERE p."AgendamentoId" = a."Id"))
                        OR (a."UnidadeDuracao" = 'DiasCorridos' AND (
                            (a."InicioPrevistoUtc" AT TIME ZONE 'America/Sao_Paulo')::time <> time '08:00'
                            OR (SELECT count(*) FROM public.periodos_ocupacao p WHERE p."AgendamentoId" = a."Id") <> a."Duracao"
                            OR EXISTS (
                                SELECT 1 FROM generate_series(0, a."Duracao" - 1) d
                                WHERE NOT EXISTS (
                                    SELECT 1 FROM public.periodos_ocupacao p WHERE p."AgendamentoId" = a."Id"
                                    AND p."InicioUtc" = (((a."InicioPrevistoUtc" AT TIME ZONE 'America/Sao_Paulo')::date + d)
                                        + time '08:00') AT TIME ZONE 'America/Sao_Paulo'
                                    AND p."FimUtc" = (((a."InicioPrevistoUtc" AT TIME ZONE 'America/Sao_Paulo')::date + d)
                                        + time '18:00') AT TIME ZONE 'America/Sao_Paulo'
                                )
                            )
                        ))
                    )
                ) THEN
                    RAISE EXCEPTION 'AgendaUnica: ocupacoes ativas incompativeis exigem reconciliacao';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM public.bloqueios_agenda b
                    JOIN public.agendamentos a ON a."Estado" = 'Agendado'
                        AND (b."EquipeId" IS NULL OR b."EquipeId" = a."EquipeId")
                    JOIN public.periodos_ocupacao p ON p."AgendamentoId" = a."Id"
                        AND p."InicioUtc" < b."FimUtc" AND b."InicioUtc" < p."FimUtc"
                ) OR EXISTS (
                    SELECT 1 FROM public.excecoes_expediente e
                    JOIN public.agendamentos a ON a."Estado" = 'Agendado' AND a."EquipeId" = e."EquipeId"
                        AND e."DataLocal" >= (a."InicioPrevistoUtc" AT TIME ZONE 'America/Sao_Paulo')::date
                        AND e."DataLocal" <= ((a."FimPrevistoUtc" - interval '1 microsecond')
                            AT TIME ZONE 'America/Sao_Paulo')::date
                ) THEN
                    RAISE EXCEPTION 'AgendaUnica: efeitos de bloqueios ou excecoes exigem revisao';
                END IF;
            END
            $preflight$;

            -- Refresh under lock; the full preparation snapshot remains immutable.
            INSERT INTO legado_mvp.agendamentos_equipes ("AgendamentoId", "EquipeId")
                SELECT "Id", "EquipeId" FROM public.agendamentos
            ON CONFLICT ("AgendamentoId") DO UPDATE
                SET "EquipeId" = EXCLUDED."EquipeId", "ArquivadoEmUtc" = CURRENT_TIMESTAMP;
            DO $verify$
            BEGIN
                IF EXISTS (
                    (SELECT "Id", "EquipeId" FROM public.agendamentos
                     EXCEPT SELECT "AgendamentoId", "EquipeId" FROM legado_mvp.agendamentos_equipes)
                    UNION ALL
                    (SELECT "AgendamentoId", "EquipeId" FROM legado_mvp.agendamentos_equipes
                     EXCEPT SELECT "Id", "EquipeId" FROM public.agendamentos)
                ) THEN
                    RAISE EXCEPTION 'AgendaUnica: mapa legado incompleto';
                END IF;
            END
            $verify$;

            INSERT INTO legado_mvp.manifesto ("Etapa", "Contagens")
                SELECT 'ConsolidarAgendaUnica', jsonb_build_object(
                    'agendamentos', (SELECT count(*) FROM public.agendamentos),
                    'equipes', (SELECT count(*) FROM public.equipes),
                    'excecoes_expediente', (SELECT count(*) FROM public.excecoes_expediente),
                    'bloqueios_agenda', (SELECT count(*) FROM public.bloqueios_agenda),
                    'periodos_ocupacao', (SELECT count(*) FROM public.periodos_ocupacao),
                    'registros_auditoria', (SELECT count(*) FROM public.registros_auditoria),
                    'telegram_updates_inbox', (SELECT count(*) FROM public.telegram_updates_inbox),
                    'telegram_business_operations', (SELECT count(*) FROM public.telegram_business_operations));
            ALTER TABLE public.agendamentos DROP CONSTRAINT "FK_agendamentos_equipes_EquipeId";
            DROP INDEX public."IX_agendamentos_EquipeId_Estado";
            ALTER TABLE public.agendamentos DROP COLUMN "EquipeId";
            -- Moving the original tables preserves rows, OIDs, FKs and owned identity sequences.
            ALTER TABLE public.equipes SET SCHEMA legado_mvp;
            ALTER TABLE public.excecoes_expediente SET SCHEMA legado_mvp;
            ALTER TABLE public.bloqueios_agenda SET SCHEMA legado_mvp;
            REVOKE ALL ON ALL TABLES IN SCHEMA legado_mvp FROM PUBLIC;
            REVOKE ALL ON ALL SEQUENCES IN SCHEMA legado_mvp FROM PUBLIC;
            CREATE INDEX "IX_agendamentos_Estado_InicioPrevistoUtc"
                ON public.agendamentos ("Estado", "InicioPrevistoUtc");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Novos agendamentos nao possuem equipe antiga. Retorno exige reconciliacao ou restauracao revisada.");
}
