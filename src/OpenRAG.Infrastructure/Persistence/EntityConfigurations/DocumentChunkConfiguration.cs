using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Infrastructure.Persistence.EntityConfigurations;

public sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.ToTable("document_chunks");

        builder.HasKey(c => c.Id);
        builder.HasAlternateKey(c => new { c.TenantId, c.DocumentId, c.VersionId, c.Id });

        builder.Property(c => c.TenantId)
            .IsRequired();

        builder.Property(c => c.DocumentId)
            .IsRequired();

        builder.Property(c => c.VersionId)
            .IsRequired();

        builder.Property(c => c.ChunkIndex)
            .IsRequired();

        builder.Property(c => c.PageNumber)
            .IsRequired(false);

        builder.Property(c => c.SectionTitle)
            .IsRequired(false)
            .HasMaxLength(500);

        builder.Property(c => c.Content)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(c => c.ContentHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.TokenCount)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.HasOne<DocumentVersion>()
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.DocumentId, c.VersionId })
            .HasPrincipalKey(v => new { v.TenantId, v.DocumentId, v.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(c => new { c.TenantId, c.DocumentId });
        builder.HasIndex(c => new { c.TenantId, c.VersionId });
        builder.HasIndex(c => new { c.TenantId, c.DocumentId, c.VersionId });
        builder.HasIndex(c => new { c.TenantId, c.DocumentId, c.VersionId, c.ChunkIndex })
            .IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.ContentHash });
    }
}
