using Microsoft.EntityFrameworkCore;

namespace Server.Infrastructure.Persistence;

public sealed class SessionGuardDbContext(DbContextOptions<SessionGuardDbContext> options) : DbContext(options)
{
    public DbSet<ChildEntity> Children => Set<ChildEntity>();
    public DbSet<AgentEntity> Agents => Set<AgentEntity>();
    public DbSet<UsageReportEntity> UsageReports => Set<UsageReportEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChildEntity>(entity =>
        {
            entity.ToTable("Children");
            entity.HasKey(x => x.ChildId);
            entity.Property(x => x.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<AgentEntity>(entity =>
        {
            entity.ToTable("Agents");
            entity.HasKey(x => x.AgentId);
            entity.Property(x => x.Hostname).HasMaxLength(200);
            entity.HasOne(x => x.Child)
                .WithMany(x => x.Agents)
                .HasForeignKey(x => x.ChildId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<UsageReportEntity>(entity =>
        {
            entity.ToTable("UsageReports");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.AgentId, x.UsageDateUtc }).IsUnique();
            entity.HasOne(x => x.Agent)
                .WithMany(x => x.UsageReports)
                .HasForeignKey(x => x.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
