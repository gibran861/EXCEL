using AfbGenerator.Api.Entities.Xrt;
using Microsoft.EntityFrameworkCore;

namespace AfbGenerator.Api.Data;

public class XrtDbContext : DbContext
{
    public XrtDbContext(DbContextOptions<XrtDbContext> options) : base(options)
    {
    }

    public DbSet<XrtFlow> XrtFlows => Set<XrtFlow>();
    public DbSet<FlowViewDto> FlowViews { get; set; }
    public DbSet<RibAccountView> RibAccountViews { get; set; }
    public DbSet<GS_CUR> GS_CUR { get; set; }
    public DbSet<FluxCibView> FluxCibViews { get; set; }
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
        modelBuilder.Entity<FlowViewDto>(eb =>
        {
            eb.HasNoKey(); // Car c'est une vue sans clé primaire directe
            eb.ToView("Flux_sens"); // 🔥 Remplacez par le nom réel de votre vue dans la BDD
        });

        // Configuration pour entité sans clé (Vue SQL)
        modelBuilder.Entity<FluxCibView>()
            .HasNoKey()
            .ToView("Flux_Cib");

        modelBuilder.Entity<GS_CUR>(entity =>
            {
                entity.ToTable("GS_CUR", "dbo"); // Spécifie le nom de la table et le schéma
                entity.HasKey(e => e.CUR_ID);    // Définit CUR_ID comme clé primaire
                
                
            });
    }
}
