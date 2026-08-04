using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Infrastructure.Persistence;
using OpenRAG.Infrastructure.Persistence.Repositories;
using OpenRAG.LiveIntegrationTests.Infrastructure;

namespace OpenRAG.LiveIntegrationTests.Repositories;

[Collection(OpenRagLiveInfrastructureTestGroup.Name)]
[Trait("Category", "LiveIntegration")]
[Trait("Security", "CrossTenant")]
public sealed class LiveRepositoryIsolationTests
{
    private readonly OpenRagLiveInfrastructureFixture _fixture;

    public LiveRepositoryIsolationTests(OpenRagLiveInfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Real_repositories_scope_all_reads_lists_counts_and_mutations_by_tenant()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(200);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        await _fixture.SeedDocumentAsync(LiveTestData.TenantB1(ids));

        using var scope = _fixture.ApiFactory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var documents = services.GetRequiredService<IDocumentRepository>();
        var authorization = services.GetRequiredService<IDocumentAuthorizationRepository>();
        var chunks = services.GetRequiredService<IDocumentChunkRepository>();
        var embeddings = services.GetRequiredService<IDocumentEmbeddingRepository>();
        var intelligence = services.GetRequiredService<IDocumentIntelligenceRepository>();
        var processing = services.GetRequiredService<IProcessingRunRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        Assert.IsType<EfDocumentRepository>(documents);
        Assert.IsType<EfDocumentRepository>(authorization);
        Assert.IsType<EfDocumentChunkRepository>(chunks);
        Assert.IsType<EfDocumentEmbeddingRepository>(embeddings);
        Assert.IsType<EfDocumentIntelligenceRepository>(intelligence);
        Assert.IsType<EfProcessingRunRepository>(processing);
        Assert.IsType<UnitOfWork>(unitOfWork);

        Assert.NotNull(await documents.GetByIdAsync(LiveTestConstants.TenantA, ids.DocumentA1));
        Assert.NotNull(await documents.GetByIdAsync(LiveTestConstants.TenantB, ids.DocumentB1));
        Assert.Null(await documents.GetByIdAsync(LiveTestConstants.TenantB, ids.DocumentA1));
        Assert.Null(await documents.GetByIdAsync(LiveTestConstants.TenantB, Guid.NewGuid()));
        Assert.Null(await documents.GetByIdWithVersionsAsync(LiveTestConstants.TenantB, ids.DocumentA1));
        Assert.Null(await documents.GetByIdForUpdateAsync(LiveTestConstants.TenantB, ids.DocumentA1));
        Assert.False(await documents.ExistsAsync(LiveTestConstants.TenantB, ids.DocumentA1));

        var tenantBList = await documents.ListAsync(LiveTestConstants.TenantB, 1, 20);
        Assert.Equal(1, tenantBList.TotalCount);
        var tenantBItem = Assert.Single(tenantBList.Items);
        Assert.Equal(ids.DocumentB1, tenantBItem.DocumentId);
        Assert.DoesNotContain(tenantBList.Items, item => item.DocumentId == ids.DocumentA1);

        Assert.NotNull(await documents.GetVersionAsync(
            LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1));
        Assert.Null(await documents.GetVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1));
        Assert.Null(await documents.GetVersionForUpdateAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1));

        Assert.Single(await chunks.GetByVersionAsync(
            LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1));
        Assert.Empty(await chunks.GetByVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1));
        Assert.Equal(0, await chunks.CountByVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1));
        Assert.False(await chunks.AnyForVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1));
        Assert.Empty((await chunks.ListByVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1, 1, 20)).Items);
        Assert.Null(await chunks.GetByIdForVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1, ids.ChunkA1));

        Assert.Single(await embeddings.GetByVersionAsync(
            LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1));
        Assert.Empty(await embeddings.GetByVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1));
        Assert.Equal(0, await embeddings.CountByVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1));
        Assert.False(await embeddings.AnyForVersionAsync(
            LiveTestConstants.TenantB,
            ids.DocumentA1,
            ids.VersionA1,
            LiveTestConstants.EmbeddingModel));
        Assert.Null(await embeddings.GetMetadataByVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1));

        Assert.NotNull(await intelligence.GetByVersionAsync(
            LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1));
        Assert.Null(await intelligence.GetByVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1));

        Assert.NotNull(await processing.GetByIdAsync(LiveTestConstants.TenantA, ids.RunA1));
        Assert.Null(await processing.GetByIdAsync(LiveTestConstants.TenantB, ids.RunA1));
        Assert.Null(await processing.GetByIdForUpdateAsync(LiveTestConstants.TenantB, ids.RunA1));
        Assert.NotNull(await processing.GetStepAsync(
            LiveTestConstants.TenantA,
            ids.RunA1,
            OpenRAG.Domain.Processing.DocumentProcessingStepName.Preprocess));
        Assert.Null(await processing.GetStepAsync(
            LiveTestConstants.TenantB,
            ids.RunA1,
            OpenRAG.Domain.Processing.DocumentProcessingStepName.Preprocess));
        Assert.Empty(await processing.GetRunsByDocumentAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1));
        Assert.Empty(await processing.GetStepsByRunAsync(LiveTestConstants.TenantB, ids.RunA1));

        var authorized = await authorization.GetExistingIdsAsync(
            LiveTestConstants.TenantB,
            [ids.DocumentA1, ids.DocumentB1]);
        Assert.Equal(ids.DocumentB1, Assert.Single(authorized));

        await chunks.DeleteByVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1);
        await embeddings.DeleteByVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1);
        await intelligence.DeleteByVersionAsync(
            LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1);
        await unitOfWork.SaveChangesAsync();

        await using var verification = _fixture.CreateDbContext();
        Assert.True(await verification.Documents.AnyAsync(document =>
            document.TenantId == LiveTestConstants.TenantA && document.Id == ids.DocumentA1));
        Assert.True(await verification.DocumentChunks.AnyAsync(chunk =>
            chunk.TenantId == LiveTestConstants.TenantA && chunk.Id == ids.ChunkA1));
        Assert.True(await verification.DocumentEmbeddings.AnyAsync(embedding =>
            embedding.TenantId == LiveTestConstants.TenantA && embedding.Id == ids.EmbeddingA1));
        Assert.True(await verification.DocumentIntelligence.AnyAsync(item =>
            item.TenantId == LiveTestConstants.TenantA && item.Id == ids.IntelligenceA1));
    }
}
