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

        // TODO: Replace bytea storage with pgvector Vector type when
        // Pgvector.EntityFrameworkCore supports EF Core 10 / Npgsql 10.
        // Map float[] to bytea using a value converter.
        builder.Property(e => e.Vector)
            .IsRequired()
            .HasColumnType("bytea")
            .HasConversion(
                v => SerializeVector(v),
                v => DeserializeVector(v));

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

    private static byte[] SerializeVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] DeserializeVector(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }
}
