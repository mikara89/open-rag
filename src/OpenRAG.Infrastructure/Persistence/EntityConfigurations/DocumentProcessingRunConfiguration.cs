using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Infrastructure.Persistence.EntityConfigurations;

public sealed class DocumentProcessingRunConfiguration : IEntityTypeConfiguration<DocumentProcessingRun>
{
    public void Configure(EntityTypeBuilder<DocumentProcessingRun> builder)
    {
        builder.ToTable("document_processing_runs");

        builder.HasKey(r => r.Id);
        builder.HasAlternateKey(r => new { r.TenantId, r.DocumentId, r.VersionId, r.Id });

        builder.Property(r => r.TenantId)
            .IsRequired();

        builder.Property(r => r.DocumentId)
            .IsRequired();

        builder.Property(r => r.VersionId)
            .IsRequired();

        builder.Property(r => r.RunReason)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(r => r.StartedAt)
            .IsRequired();

        builder.Property(r => r.CompletedAt)
            .IsRequired(false);

        builder.Property(r => r.CorrelationId)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasOne<OpenRAG.Domain.Documents.DocumentVersion>()
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.DocumentId, r.VersionId })
            .HasPrincipalKey(v => new { v.TenantId, v.DocumentId, v.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(r => new { r.TenantId, r.DocumentId });
        builder.HasIndex(r => new { r.TenantId, r.VersionId });
        builder.HasIndex(r => new { r.TenantId, r.Status });
        builder.HasIndex(r => new { r.TenantId, r.CorrelationId });
    }
}
