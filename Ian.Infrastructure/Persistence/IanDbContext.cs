using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Infrastructure.Persistence;

public sealed class IanDbContext(DbContextOptions<IanDbContext> options)
    : DbContext(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Equipe> Equipes => Set<Equipe>();
    public DbSet<TipoServico> TiposServico => Set<TipoServico>();
    public DbSet<Agendamento> Agendamentos => Set<Agendamento>();
    public DbSet<PeriodoOcupacao> PeriodosOcupacao => Set<PeriodoOcupacao>();
    public DbSet<ExcecaoExpediente> ExcecoesExpediente => Set<ExcecaoExpediente>();
    public DbSet<BloqueioAgenda> BloqueiosAgenda => Set<BloqueioAgenda>();
    public DbSet<SolicitacaoPendente> SolicitacoesPendentes => Set<SolicitacaoPendente>();
    public DbSet<RegistroAuditoria> RegistrosAuditoria => Set<RegistroAuditoria>();
    public DbSet<EnvioTelegram> EnviosTelegram => Set<EnvioTelegram>();
    public DbSet<TelegramUpdateInbox> TelegramUpdatesInbox => Set<TelegramUpdateInbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IanDbContext).Assembly);
    }
}