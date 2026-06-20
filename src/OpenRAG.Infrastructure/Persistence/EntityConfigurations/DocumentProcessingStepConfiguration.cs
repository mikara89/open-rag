using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Infrastructure.Persistence.EntityConfigurations;

public sealed class DocumentProcessingStepConfiguration : IEntityTypeConfiguration<DocumentProcessingStep>
{
    public void Configure(EntityTypeBuilder<DocumentProcessingStep> builder)
    {
        builder.ToTable("document_processing_steps");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.TenantId)
            .IsRequired();

        builder.Property(s => s.DocumentId)
            .IsRequired();

        builder.Property(s => s.VersionId)
            .IsRequired();

        builder.Property(s => s.ProcessingRunId)
            .IsRequired();

        builder.Property(s => s.StepName)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(s => s.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(s => s.AttemptCount)
            .IsRequired();

        builder.Property(s => s.MaxAttempts)
            .IsRequired();

        builder.Property(s => s.InputHash)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(s => s.OutputHash)
            .IsRequired(false)
            .HasMaxLength(2000);

        builder.Property(s => s.ProcessorName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.ProcessorVersion)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.StartedAt)
            .IsRequired(false);

        builder.Property(s => s.CompletedAt)
            .IsRequired(false);

        builder.Property(s => s.LastErrorCode)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(s => s.LastErrorMessage)
            .IsRequired(false)
            .HasMaxLength(2000);

        // Indexes
        builder.HasIndex(s => new { s.TenantId, s.ProcessingRunId });
        builder.HasIndex(s => new { s.TenantId, s.DocumentId });
        builder.HasIndex(s => new { s.TenantId, s.VersionId });
        builder.HasIndex(s => new { s.TenantId, s.StepName });
        builder.HasIndex(s => new { s.TenantId, s.ProcessingRunId, s.StepName })
            .IsUnique();
        builder.HasIndex(s => new { s.TenantId, s.Status });
    }
}
