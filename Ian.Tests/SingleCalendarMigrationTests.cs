using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
public sealed class SingleCalendarMigrationTests : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    // A deliberately refused upgrade must not contaminate another class's migration setup.
    public Task DisposeAsync() => PostgresTestDatabase.ResetAsync();

    internal const string Legacy = "20261110155523_TelegramInboxProcessing";
    internal const string Preparation = "20261111132422_PrepararAgendaUnica";

    [PostgresFact]
    public async Task EmptyDatabaseUpgradesWithoutInventingTeamOrCalendar()
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.MigrateAsync();
        Assert.Equal(6, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(0, await db.Agendamentos.CountAsync());
        Assert.Equal(0, await Scalar("SELECT count(*) FROM legado_mvp.equipes"));
        Assert.Equal(0, await Scalar("SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_name IN ('equipes','bloqueios_agenda','excecoes_expediente','agenda','agendas')"));
        Assert.Equal(3, await Scalar("SELECT count(*) FROM information_schema.tables WHERE table_schema='legado_mvp' AND table_name IN ('equipes','bloqueios_agenda','excecoes_expediente')"));
    }

    [PostgresFact]
    public async Task PopulatedUpgradePreservesRowsMappingsTechnicalRecordsAndIdentitySequences()
    {
        await SeedLegacy();
        var before = await Fingerprints(false);
        var sequencesBefore = await SequenceStates();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(Preparation);

        Assert.Equal(1, await Scalar("SELECT count(*) FROM public.equipes WHERE \"Id\"=1"));
        Assert.Equal(3, await Scalar("SELECT count(*) FROM legado_mvp.agendamentos_equipes"));
        Assert.Equal(8, await Scalar("SELECT count(*) FROM legado_mvp.registros_preparacao"));

        // An old writer can still change team after preparation. Final map must refresh under lock.
        await Sql("UPDATE public.agendamentos SET \"EquipeId\"=2 WHERE \"Id\"=1;");
        await db.Database.MigrateAsync();

        Assert.Equal(before, await Fingerprints(true));
        Assert.Equal(sequencesBefore, await SequenceStates());
        Assert.Equal(2, await Scalar("SELECT \"EquipeId\" FROM legado_mvp.agendamentos_equipes WHERE \"AgendamentoId\"=1"));
        Assert.Equal(1, await Scalar("SELECT (\"Dados\"->>'EquipeId')::bigint FROM legado_mvp.registros_preparacao WHERE \"Tabela\"='agendamentos' AND \"Id\"=1"));
        Assert.Equal(3, await Scalar("SELECT count(*) FROM public.agendamentos WHERE \"Versao\"=1"));
        Assert.Equal(9, await Scalar("SELECT count(*) FROM pg_constraint WHERE contype='f' AND connamespace IN ('public'::regnamespace,'legado_mvp'::regnamespace)"));
        Assert.Equal(0, await Scalar("SELECT count(*) FROM pg_constraint WHERE NOT convalidated AND connamespace IN ('public'::regnamespace,'legado_mvp'::regnamespace)"));
        Assert.Equal(2, await Scalar("SELECT count(*) FROM legado_mvp.manifesto"));
        Assert.Equal(1, await Scalar("SELECT count(*) FROM public.telegram_updates_inbox WHERE \"Estado\"='Completed'"));
        Assert.Equal(1, await Scalar("SELECT count(*) FROM public.ian_t08_test_effects"));

        var foreignKey = await Assert.ThrowsAsync<PostgresException>(() =>
            Sql("INSERT INTO legado_mvp.agendamentos_equipes (\"AgendamentoId\",\"EquipeId\") VALUES (999999,1)"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreignKey.SqlState);

        // No automatic Down can invent a team for a new row.
        await Sql("""
            INSERT INTO public.agendamentos ("ClienteId","TipoServicoId","Modalidade","Duracao","UnidadeDuracao",
                "InicioPrevistoUtc","FimPrevistoUtc","Estado")
            VALUES (1,1,'Interno',60,'Minutos','2030-02-01 11:00Z','2030-02-01 12:00Z','Agendado');
            """);
        Assert.Equal(301, await Scalar("SELECT max(\"Id\") FROM public.agendamentos"));
        await Assert.ThrowsAsync<NotSupportedException>(() => db.GetService<IMigrator>().MigrateAsync(Preparation));
        Assert.Equal(4, await Scalar("SELECT count(*) FROM public.agendamentos"));
    }

    [PostgresTheory]
    [InlineData("overlap")]
    [InlineData("partial")]
    [InlineData("block")]
    [InlineData("exception")]
    [InlineData("missing-archive")]
    [InlineData("changed-archive")]
    public async Task ConsolidationRefusesUnreconciledDataAndRollsBack(string scenario)
    {
        await SeedLegacy();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(Preparation);
        var sql = scenario switch
        {
            "overlap" => "UPDATE public.agendamentos SET \"Estado\"='Agendado' WHERE \"Id\"=2;",
            "partial" => "UPDATE public.periodos_ocupacao SET \"InicioUtc\"='2030-01-07 13:00Z' WHERE \"Id\"=1;",
            "block" => "UPDATE public.bloqueios_agenda SET \"InicioUtc\"='2030-01-07 11:00Z',\"FimUtc\"='2030-01-07 12:00Z' WHERE \"Id\"=1;",
            "exception" => "UPDATE public.excecoes_expediente SET \"DataLocal\"='2030-01-07' WHERE \"Id\"=1;",
            "changed-archive" => "UPDATE legado_mvp.registros_preparacao SET \"Dados\"='{}' WHERE \"Tabela\"='equipes' AND \"Id\"=1;",
            _ => "DELETE FROM legado_mvp.registros_preparacao WHERE \"Tabela\"='equipes' AND \"Id\"=1;"
        };
        await Sql(sql);
        var before = await Fingerprints(false);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
        Assert.Equal("P0001", error.SqlState);
        Assert.Equal(before, await Fingerprints(false));
        Assert.Equal(5, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(2, await Scalar("SELECT count(*) FROM public.equipes"));
        Assert.Equal(1, await Scalar("SELECT count(*) FROM public.telegram_updates_inbox"));
        Assert.Equal(0, await Scalar("SELECT count(*) FROM information_schema.tables WHERE table_schema='legado_mvp' AND table_name='equipes'"));
    }

    private static async Task SeedLegacy()
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(Legacy);
        await Sql("""
            INSERT INTO clientes ("Nome","TelefoneNormalizado") VALUES ('Cliente sintetico','synthetic-79');
            INSERT INTO equipes ("Nome","Ativa","TelegramChatId") VALUES ('Equipe sintetica A',true,-79),('Equipe sintetica B',true,NULL);
            INSERT INTO tipos_servico ("Nome","ModalidadePadrao","DuracaoPadrao","UnidadeDuracaoPadrao")
                VALUES ('Servico sintetico','Interno',1,'DiasCorridos');
            INSERT INTO agendamentos ("ClienteId","EquipeId","TipoServicoId","Modalidade","Duracao","UnidadeDuracao","InicioPrevistoUtc","FimPrevistoUtc","Estado","Observacoes")
                VALUES (1,1,1,'Interno',1,'DiasCorridos','2030-01-07 11:00Z','2030-01-07 21:00Z','Agendado','synthetic'),
                       (1,2,1,'Interno',1,'DiasCorridos','2030-01-07 13:00Z','2030-01-07 21:00Z','Concluido','historical partial'),
                       (1,1,1,'Interno',60,'Minutos','2030-01-08 11:00Z','2030-01-08 12:00Z','Cancelado',NULL);
            INSERT INTO periodos_ocupacao ("AgendamentoId","InicioUtc","FimUtc")
                SELECT "Id","InicioPrevistoUtc","FimPrevistoUtc" FROM agendamentos ORDER BY "Id";
            INSERT INTO excecoes_expediente ("EquipeId","DataLocal","FolgaIntegral") VALUES (1,'2029-12-01',true);
            INSERT INTO bloqueios_agenda ("EquipeId","InicioUtc","FimUtc")
                VALUES (NULL,'2029-12-01 11:00Z','2029-12-01 12:00Z'),(2,'2029-12-02 11:00Z','2029-12-02 12:00Z');
            INSERT INTO registros_auditoria ("AgendamentoId","Operacao","Resultado","CorrelacaoId","RegistradoEmUtc")
                VALUES (2,'synthetic','success','synthetic-79','2030-01-07 21:00Z');
            INSERT INTO solicitacoes_pendentes ("TelegramUserId","Operacao","Estado","CriadaEmUtc","ExpiraEmUtc")
                VALUES (79,'synthetic','Pendente','2030-01-01 00:00Z','2030-01-02 00:00Z');
            INSERT INTO envios_telegram ("DestinoChatId","TipoMensagem","Estado","Tentativas","CriadoEmUtc")
                VALUES (-79,'synthetic','Pendente',0,'2030-01-01 00:00Z');
            INSERT INTO telegram_updates_inbox ("UpdateId","PayloadJson","Estado","RecebidoEmUtc","Tentativas")
                VALUES (79,'{"update_id":79}','Completed','2030-01-01 00:00Z',1);
            INSERT INTO telegram_business_operations ("UpdateId","OperationKey","ExecutadoEmUtc")
                VALUES (79,'synthetic','2030-01-01 00:00Z');
            CREATE TABLE ian_t08_test_effects ("UpdateId" bigint PRIMARY KEY);
            INSERT INTO ian_t08_test_effects VALUES (79);
            SELECT setval('public."agendamentos_Id_seq"',300,true);
            SELECT setval('public."equipes_Id_seq"',40,false);
            """);
    }

    private static async Task<string[]> Fingerprints(bool migrated)
    {
        var tables = new[] { "clientes", "tipos_servico", "equipes", "agendamentos", "periodos_ocupacao",
            "excecoes_expediente", "bloqueios_agenda", "registros_auditoria", "solicitacoes_pendentes",
            "envios_telegram", "telegram_updates_inbox", "telegram_business_operations", "ian_t08_test_effects" };
        var result = new List<string>();
        await using var connection = new NpgsqlConnection(PostgresTestDatabase.ConnectionString);
        await connection.OpenAsync();
        foreach (var table in tables)
        {
            var schema = migrated && table is "equipes" or "excecoes_expediente" or "bloqueios_agenda" ? "legado_mvp" : "public";
            var json = table == "agendamentos" ? "(to_jsonb(t) - 'EquipeId' - 'Versao')" : "to_jsonb(t)";
            await using var command = new NpgsqlCommand(
                $"SELECT md5(COALESCE(string_agg(({json})::text, ',' ORDER BY ({json})::text),'')) FROM {schema}.{table} t", connection);
            result.Add(table + ":" + await command.ExecuteScalarAsync());
        }
        return result.ToArray();
    }

    private static async Task<string[]> SequenceStates()
    {
        await using var connection = new NpgsqlConnection(PostgresTestDatabase.ConnectionString);
        await connection.OpenAsync();
        var sequences = new List<(string Schema, string Name)>();
        await using (var command = new NpgsqlCommand("""
            SELECT n.nspname,c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE c.relkind='S' AND n.nspname IN ('public','legado_mvp') ORDER BY c.relname
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) sequences.Add((reader.GetString(0), reader.GetString(1)));
        var result = new List<string>();
        foreach (var (schema, name) in sequences)
        {
            await using var command = new NpgsqlCommand($"SELECT last_value::text || ':' || is_called::text FROM {schema}.\"{name}\"", connection);
            result.Add(name + ":" + await command.ExecuteScalarAsync());
        }
        return result.ToArray();
    }

    internal static async Task Sql(string sql)
    {
        await using var connection = new NpgsqlConnection(PostgresTestDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task<long> Scalar(string sql)
    {
        await using var connection = new NpgsqlConnection(PostgresTestDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IAN_TEST_POSTGRES_CONNECTION_STRING")))
            Skip = "Requer banco PostgreSQL descartável explicitamente confirmado.";
    }
}
