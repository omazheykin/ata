using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageApi.Data;

public class StatsDbContext : DbContext
{
    public StatsDbContext(DbContextOptions<StatsDbContext> options) : base(options)
    {
    }

    public DbSet<ArbitrageEvent> ArbitrageEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ArbitrageEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Pair).IsRequired();
            entity.Property(e => e.Direction).IsRequired();
            entity.Property(e => e.Spread).HasColumnType("decimal(18,8)");
            entity.Property(e => e.DepthBuy).HasColumnType("decimal(18,8)");
            entity.Property(e => e.DepthSell).HasColumnType("decimal(18,8)");
            entity.Property(e => e.Timestamp).IsRequired();
        });
    }
}
