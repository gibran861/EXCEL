using AfbGenerator.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AfbGenerator.Api.Configuration;

public class FluxConfiguration : IEntityTypeConfiguration<Flux>
{
    public void Configure(EntityTypeBuilder<Flux> builder)
    {
        builder.ToTable("Fluxes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FluxCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.FluxLabel)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.BankCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Cib1)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Cib2)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasMany(x => x.Libelles)
            .WithOne(x => x.Flux)
            .HasForeignKey(x => x.FluxId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
