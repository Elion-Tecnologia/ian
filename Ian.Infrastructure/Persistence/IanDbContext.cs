using Microsoft.EntityFrameworkCore;

namespace Ian.Infrastructure.Persistence;

public sealed class IanDbContext(DbContextOptions<IanDbContext> options)
    : DbContext(options)
{
}