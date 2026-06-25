using AfbGenerator.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AfbGenerator.Api.Configuration;

public class LibelleConfiguration : IEntityTypeConfiguration<Libelle>
{
    public void Configure(EntityTypeBuilder<Libelle> builder)
    {
        builder.ToTable("Libelles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Keyword)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Priority)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.FluxId)
            .IsRequired(false);
    }
}
