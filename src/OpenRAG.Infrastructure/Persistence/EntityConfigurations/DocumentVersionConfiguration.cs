using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Infrastructure.Persistence.EntityConfigurations;

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_versions");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.TenantId)
            .IsRequired();

        builder.Property(v => v.DocumentId)
            .IsRequired();

        builder.Property(v => v.VersionNumber)
            .IsRequired();

        builder.Property(v => v.OriginalObjectKey)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(v => v.OriginalContentType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(v => v.OriginalSizeBytes)
            .IsRequired();

        builder.Property(v => v.OriginalSha256)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(v => v.DoclingMarkdownObjectKey)
            .IsRequired(false)
            .HasMaxLength(2000);

        builder.Property(v => v.DoclingJsonObjectKey)
            .IsRequired(false)
            .HasMaxLength(2000);

        builder.Property(v => v.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(v => v.CreatedAt)
            .IsRequired();

        // Indexes
        builder.HasIndex(v => new { v.TenantId, v.DocumentId });
        builder.HasIndex(v => new { v.TenantId, v.DocumentId, v.VersionNumber })
            .IsUnique();
        builder.HasIndex(v => new { v.TenantId, v.DocumentId, v.Id });
        builder.HasIndex(v => new { v.TenantId, v.Status });
    }
}
