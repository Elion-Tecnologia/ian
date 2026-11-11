using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Infrastructure.Persistence;

public sealed class IanDbContext(DbContextOptions<IanDbContext> options)
    : DbContext(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<TipoServico> TiposServico => Set<TipoServico>();
    public DbSet<Agendamento> Agendamentos => Set<Agendamento>();
    public DbSet<PeriodoOcupacao> PeriodosOcupacao => Set<PeriodoOcupacao>();
    public DbSet<SolicitacaoPendente> SolicitacoesPendentes => Set<SolicitacaoPendente>();
    public DbSet<RegistroAuditoria> RegistrosAuditoria => Set<RegistroAuditoria>();
    public DbSet<EnvioTelegram> EnviosTelegram => Set<EnvioTelegram>();
    public DbSet<TelegramUpdateInbox> TelegramUpdatesInbox => Set<TelegramUpdateInbox>();
    public DbSet<TelegramBusinessOperation> TelegramBusinessOperations => Set<TelegramBusinessOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IanDbContext).Assembly);
    }
}