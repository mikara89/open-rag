using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Infrastructure.Persistence.EntityConfigurations;

public sealed class DocumentEmbeddingConfiguration : IEntityTypeConfiguration<DocumentEmbedding>
{
    public void Configure(EntityTypeBuilder<DocumentEmbedding> builder)
    {
        builder.ToTable("document_embeddings");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.Property(e => e.DocumentId)
            .IsRequired();

        builder.Property(e => e.VersionId)
            .IsRequired();

        builder.Property(e => e.ChunkId)
            .IsRequired();

        // Pgvector Vector type — float[] ⟷ Pgvector.Vector ⟷ PostgreSQL vector.
        // Pgvector.EntityFrameworkCore registers the Vector ⟷ vector mapping via UseVector().
        // We convert float[] (domain) to Pgvector.Vector (EF model) here.
        builder.Property(e => e.Vector)
            .IsRequired()
            .HasConversion(
                v => new Pgvector.Vector(v),
                v => v.ToArray());

        builder.Property(e => e.EmbeddingProvider)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.EmbeddingModel)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.EmbeddingDimensions)
            .IsRequired();

        builder.Property(e => e.EmbeddingVersion)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        // Indexes
        builder.HasIndex(e => new { e.TenantId, e.DocumentId });
        builder.HasIndex(e => new { e.TenantId, e.VersionId });
        builder.HasIndex(e => new { e.TenantId, e.ChunkId });
        builder.HasIndex(e => new { e.TenantId, e.VersionId, e.EmbeddingModel });
        builder.HasIndex(e => new { e.TenantId, e.ChunkId, e.EmbeddingModel })
            .IsUnique();
    }
}
