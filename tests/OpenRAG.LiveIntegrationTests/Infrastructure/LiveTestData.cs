using OpenRAG.Domain.Documents;

namespace OpenRAG.LiveIntegrationTests.Infrastructure;

internal static class LiveTestData
{
    public static LiveTestDocumentSeed TenantA1(
        LiveTestIds ids,
        DocumentStatus status = DocumentStatus.Ready) =>
        new(
            LiveTestConstants.TenantA,
            LiveTestConstants.UserA,
            ids.DocumentA1,
            ids.VersionA1,
            ids.ChunkA1,
            ids.EmbeddingA1,
            ids.IntelligenceA1,
            ids.RunA1,
            ids.StepA1,
            "Tenant A document one",
            "tenant-a-one.txt",
            LiveTestConstants.TenantAMarker,
            [1f, 0f, 0f],
            status);

    public static LiveTestDocumentSeed TenantA2(
        LiveTestIds ids,
        DocumentStatus status = DocumentStatus.Ready) =>
        new(
            LiveTestConstants.TenantA,
            LiveTestConstants.UserA,
            ids.DocumentA2,
            ids.VersionA2,
            ids.ChunkA2,
            ids.EmbeddingA2,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Tenant A document two",
            "tenant-a-two.txt",
            LiveTestConstants.TenantAMarker,
            [0.9f, 0.1f, 0f],
            status);

    public static LiveTestDocumentSeed TenantB1(
        LiveTestIds ids,
        DocumentStatus status = DocumentStatus.Ready) =>
        new(
            LiveTestConstants.TenantB,
            LiveTestConstants.UserB,
            ids.DocumentB1,
            ids.VersionB1,
            ids.ChunkB1,
            ids.EmbeddingB1,
            ids.IntelligenceB1,
            ids.RunB1,
            ids.StepB1,
            "Tenant B document one",
            "tenant-b-one.txt",
            LiveTestConstants.TenantBMarker,
            [0f, 1f, 0f],
            status);
}
