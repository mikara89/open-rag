using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Infrastructure.Persistence.EntityConfigurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.TenantId)
            .IsRequired();

        builder.Property(d => d.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(d => d.OriginalFileName)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(d => d.CurrentVersionId)
            .IsRequired(false);

        builder.Property(d => d.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(d => d.CreatedByUserId)
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .IsRequired();

        builder.Property(d => d.UpdatedAt)
            .IsRequired();

        builder.Property(d => d.DeletedAt)
            .IsRequired(false);

        // Navigation: Document has many DocumentVersions via backing field _versions
        builder.HasMany<DocumentVersion>("_versions")
            .WithOne()
            .HasForeignKey(v => v.DocumentId)
            .HasPrincipalKey(d => d.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(d => d.Versions);

        // Indexes
        builder.HasIndex(d => d.TenantId);
        builder.HasIndex(d => new { d.TenantId, d.Id });
        builder.HasIndex(d => new { d.TenantId, d.Status });
        builder.HasIndex(d => new { d.TenantId, d.CurrentVersionId });
    }
}
