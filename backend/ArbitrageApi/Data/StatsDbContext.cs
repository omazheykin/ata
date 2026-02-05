using ArbitrageApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageApi.Data;

public class StatsDbContext : DbContext
{
    public StatsDbContext(DbContextOptions<StatsDbContext> options) : base(options)
    {
    }

    public DbSet<ArbitrageEvent> ArbitrageEvents { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<HeatmapCell> HeatmapCells { get; set; }
    public DbSet<AggregatedMetric> AggregatedMetrics { get; set; }

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

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Asset).IsRequired();
            entity.Property(t => t.Exchange).IsRequired();
            entity.Property(t => t.Amount).HasColumnType("decimal(18,8)");
            entity.Property(t => t.Price).HasColumnType("decimal(18,8)");
            entity.Property(t => t.Fee).HasColumnType("decimal(18,8)");
            entity.Property(t => t.Profit).HasColumnType("decimal(18,8)");
            
            // New PnL Fields
            entity.Property(t => t.RealizedProfit).HasColumnType("decimal(18,8)");
            entity.Property(t => t.TotalFees).HasColumnType("decimal(18,8)");
            entity.Property(t => t.BuyCost).HasColumnType("decimal(18,8)");
            entity.Property(t => t.SellProceeds).HasColumnType("decimal(18,8)");

            entity.Property(t => t.Timestamp).IsRequired();
        });

        modelBuilder.Entity<AggregatedMetric>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.SumSpread).HasColumnType("decimal(18,8)");
            entity.Property(m => m.MaxSpread).HasColumnType("decimal(18,8)");
            entity.Property(m => m.SumDepth).HasColumnType("decimal(18,8)");
        });
    }
}
