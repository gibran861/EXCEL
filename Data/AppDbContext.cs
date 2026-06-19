using AfbGenerator.Api.Configuration;
using AfbGenerator.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace AfbGenerator.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Flux> Fluxes => Set<Flux>();
    public DbSet<Libelle> Libelles => Set<Libelle>();
    public DbSet<Banque> Banques { get; set; }
    public DbSet<NEWFlux> Flux { get; set; }
    public DbSet<Cib> Cib { get; set; }
    public DbSet<FluxMapping> FluxMappings { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new FluxConfiguration());
        modelBuilder.ApplyConfiguration(new LibelleConfiguration());
       
    }
}
