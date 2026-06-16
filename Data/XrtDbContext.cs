using AfbGenerator.Api.Entities.Xrt;
using Microsoft.EntityFrameworkCore;

namespace AfbGenerator.Api.Data;

public class XrtDbContext : DbContext
{
    public XrtDbContext(DbContextOptions<XrtDbContext> options) : base(options)
    {
    }

    public DbSet<XrtFlow> XrtFlows => Set<XrtFlow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<XrtFlow>(entity =>
        {
            entity.ToTable("XRT_FLOW", "dbo");
            entity.HasKey(e => e.FlowCode);
            entity.Property(e => e.FlowCode).HasColumnName("FLOW_CODE").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Description).HasColumnName("DESCRIPTION").HasMaxLength(500);
            entity.Property(e => e.FlowsctsId).HasColumnName("FLOWSCTS_ID");
            entity.Property(e => e.Direction).HasColumnName("DIRECTION");
        });
    }
}
