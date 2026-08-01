using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Infrastructure.Persistence.EntityConfigurations;

public sealed class DocumentIntelligenceConfiguration : IEntityTypeConfiguration<DocumentIntelligence>
{
    public void Configure(EntityTypeBuilder<DocumentIntelligence> builder)
    {
        builder.ToTable("document_intelligence");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.Property(e => e.DocumentId)
            .IsRequired();

        builder.Property(e => e.VersionId)
            .IsRequired();

        builder.Property(e => e.Classification)
            .IsRequired(false)
            .HasMaxLength(500);

        builder.Property(e => e.Summary)
            .IsRequired(false)
            .HasColumnType("text");

        builder.Property(e => e.KeywordsJson)
            .IsRequired(false)
            .HasColumnType("jsonb");

        builder.Property(e => e.EntitiesJson)
            .IsRequired(false)
            .HasColumnType("jsonb");

        builder.Property(e => e.ExtractedMetadataJson)
            .IsRequired(false)
            .HasColumnType("jsonb");

        builder.Property(e => e.Provider)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Model)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .IsRequired();

        builder.HasOne<DocumentVersion>()
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.DocumentId, e.VersionId })
            .HasPrincipalKey(v => new { v.TenantId, v.DocumentId, v.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // One active intelligence record per version
        builder.HasIndex(e => new { e.TenantId, e.VersionId })
            .IsUnique();
    }
}
