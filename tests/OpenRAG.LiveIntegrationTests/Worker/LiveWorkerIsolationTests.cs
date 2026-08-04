using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Domain.Processing;
using OpenRAG.Infrastructure.Persistence.Repositories;
using OpenRAG.LiveIntegrationTests.Infrastructure;
using OpenRAG.Worker.Consumers;

namespace OpenRAG.LiveIntegrationTests.Worker;

[Collection(OpenRagLiveInfrastructureTestGroup.Name)]
[Trait("Category", "LiveIntegration")]
[Trait("Security", "CrossTenant")]
public sealed class LiveWorkerIsolationTests
{
    private readonly OpenRagLiveInfrastructureFixture _fixture;

    public LiveWorkerIsolationTests(OpenRagLiveInfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Actual_consumers_treat_foreign_tenant_messages_as_no_ops()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(600);
        var seeded = await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        var manifestBefore = await _fixture.GetStorageManifestAsync();

        using var scope = _fixture.CreateWorkerScope();
        Assert.IsType<EfDocumentRepository>(
            scope.ServiceProvider.GetRequiredService<IDocumentRepository>());
        Assert.Null(scope.ServiceProvider.GetService<ICurrentTenant>());
        Assert.Null(scope.ServiceProvider.GetService<ICurrentUser>());

        await scope.ServiceProvider.GetRequiredService<DocumentPreprocessRequestedConsumer>()
            .HandleAsync(PreprocessEvent(
                LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1, ids.RunA1,
                seeded.Version.OriginalObjectKey), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<DocumentChunkingRequestedConsumer>()
            .HandleAsync(ChunkEvent(
                LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1, ids.RunA1,
                seeded.Version.DoclingMarkdownObjectKey!), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<DocumentIntelligenceRequestedConsumer>()
            .HandleAsync(IntelligenceEvent(
                LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1, ids.RunA1),
                CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<DocumentEmbeddingsRequestedConsumer>()
            .HandleAsync(EmbeddingsEvent(
                LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionA1, ids.RunA1),
                CancellationToken.None);

        Assert.Empty(_fixture.ProviderProbe.PreprocessingRequests);
        Assert.Empty(_fixture.ProviderProbe.IntelligenceRequests);
        Assert.Empty(_fixture.ProviderProbe.EmbeddingRequests);
        Assert.Empty(_fixture.EventBus.Events);
        Assert.Equal(manifestBefore, await _fixture.GetStorageManifestAsync());

        await using var context = _fixture.CreateDbContext();
        Assert.Equal(1, await context.Documents.CountAsync());
        Assert.Equal(1, await context.DocumentChunks.CountAsync());
        Assert.Equal(1, await context.DocumentEmbeddings.CountAsync());
        Assert.Equal(1, await context.DocumentIntelligence.CountAsync());
        Assert.Equal(1, await context.DocumentProcessingSteps.CountAsync());
        Assert.All(await context.Documents.ToListAsync(), document =>
            Assert.Equal(LiveTestConstants.TenantA, document.TenantId));
    }

    [Fact]
    public async Task Actual_consumers_propagate_explicit_tenant_through_real_positive_pipeline()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(601);
        var seeded = await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        var runId = Guid.NewGuid();
        await _fixture.CreateRunningProcessingRunAsync(
            LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId, "worker-positive");

        using var scope = _fixture.CreateWorkerScope();
        await scope.ServiceProvider.GetRequiredService<DocumentPreprocessRequestedConsumer>()
            .HandleAsync(PreprocessEvent(
                LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId,
                seeded.Version.OriginalObjectKey), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<DocumentChunkingRequestedConsumer>()
            .HandleAsync(ChunkEvent(
                LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId,
                seeded.Version.DoclingMarkdownObjectKey!), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<DocumentIntelligenceRequestedConsumer>()
            .HandleAsync(IntelligenceEvent(
                LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId),
                CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<DocumentEmbeddingsRequestedConsumer>()
            .HandleAsync(EmbeddingsEvent(
                LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId),
                CancellationToken.None);

        Assert.All(_fixture.ProviderProbe.PreprocessingRequests, request =>
            Assert.Equal(LiveTestConstants.TenantA, request.TenantId));
        Assert.All(_fixture.ProviderProbe.IntelligenceRequests, request =>
            Assert.Equal(LiveTestConstants.TenantA, request.TenantId));
        Assert.All(_fixture.ProviderProbe.EmbeddingRequests, request =>
            Assert.Equal(LiveTestConstants.TenantA, request.TenantId));
        Assert.Equal(
            [
                "document.preprocessed",
                "document.chunked",
                "document.intelligence.generated",
                "document.embeddings.generated"
            ],
            _fixture.EventBus.Events.Select(item => item.Topic));

        await using var context = _fixture.CreateDbContext();
        var steps = await context.DocumentProcessingSteps
            .Where(step => step.ProcessingRunId == runId)
            .ToListAsync();
        Assert.Equal(4, steps.Count);
        Assert.All(steps, step =>
        {
            Assert.Equal(LiveTestConstants.TenantA, step.TenantId);
            Assert.Equal(DocumentProcessingStepStatus.Completed, step.Status);
        });
        Assert.All(await context.DocumentChunks.ToListAsync(), chunk =>
            Assert.Equal(LiveTestConstants.TenantA, chunk.TenantId));
        Assert.All(await context.DocumentEmbeddings.Select(embedding => embedding.TenantId).ToListAsync(),
            tenantId => Assert.Equal(LiveTestConstants.TenantA, tenantId));
    }

    [Fact]
    public async Task Storage_failure_is_contained_and_persisted_without_event_publish()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(602);
        var seeded = await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        var runId = Guid.NewGuid();
        await _fixture.CreateRunningProcessingRunAsync(
            LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId, "storage-failure");
        using (var deleteScope = _fixture.CreateWorkerScope())
        {
            await deleteScope.ServiceProvider.GetRequiredService<IFileStorage>()
                .DeleteAsync(seeded.Version.OriginalObjectKey);
        }

        using var scope = _fixture.CreateWorkerScope();
        await scope.ServiceProvider.GetRequiredService<DocumentPreprocessRequestedConsumer>()
            .HandleAsync(PreprocessEvent(
                LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId,
                seeded.Version.OriginalObjectKey), CancellationToken.None);

        Assert.Single(_fixture.ProviderProbe.PreprocessingRequests);
        Assert.Empty(_fixture.EventBus.Events);
        await using var context = _fixture.CreateDbContext();
        var step = await context.DocumentProcessingSteps.SingleAsync(item =>
            item.ProcessingRunId == runId);
        Assert.Equal(DocumentProcessingStepStatus.Failed, step.Status);
        Assert.Equal("PREPROCESS_FAILED", step.LastErrorCode);
    }

    [Fact]
    public async Task Provider_failure_preserves_existing_embeddings_and_records_failed_step()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(603);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        var runId = Guid.NewGuid();
        await _fixture.CreateRunningProcessingRunAsync(
            LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId, "provider-failure");
        _fixture.ProviderProbe.EmbeddingFailure = new InvalidOperationException("controlled provider failure");

        using var scope = _fixture.CreateWorkerScope();
        await scope.ServiceProvider.GetRequiredService<DocumentEmbeddingsRequestedConsumer>()
            .HandleAsync(EmbeddingsEvent(
                LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId),
                CancellationToken.None);

        Assert.Single(_fixture.ProviderProbe.EmbeddingRequests);
        Assert.Empty(_fixture.EventBus.Events);
        await using var context = _fixture.CreateDbContext();
        Assert.True(await context.DocumentEmbeddings.AnyAsync(item => item.Id == ids.EmbeddingA1));
        var step = await context.DocumentProcessingSteps.SingleAsync(item =>
            item.ProcessingRunId == runId);
        Assert.Equal(DocumentProcessingStepStatus.Failed, step.Status);
        Assert.Equal("EMBEDDING_FAILED", step.LastErrorCode);
    }

    [Fact]
    public async Task Event_publish_failure_rolls_back_database_mutations()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(604);
        var seeded = await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        var runId = Guid.NewGuid();
        await _fixture.CreateRunningProcessingRunAsync(
            LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId, "event-failure");
        _fixture.EventBus.Failure = new InvalidOperationException("controlled CAP publish failure");

        using var scope = _fixture.CreateWorkerScope();
        var consumer = scope.ServiceProvider.GetRequiredService<DocumentChunkingRequestedConsumer>();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.HandleAsync(
            ChunkEvent(
                LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId,
                seeded.Version.DoclingMarkdownObjectKey!), CancellationToken.None));
        Assert.Equal("controlled CAP publish failure", exception.Message);

        await using var context = _fixture.CreateDbContext();
        Assert.True(await context.DocumentChunks.AnyAsync(item => item.Id == ids.ChunkA1));
        Assert.True(await context.DocumentEmbeddings.AnyAsync(item => item.Id == ids.EmbeddingA1));
        Assert.False(await context.DocumentProcessingSteps.AnyAsync(item =>
            item.ProcessingRunId == runId));
    }

    [Fact]
    public async Task Cancellation_stops_consumer_before_provider_or_mutation()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(605);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        var runId = Guid.NewGuid();
        await _fixture.CreateRunningProcessingRunAsync(
            LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId, "cancellation");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using var scope = _fixture.CreateWorkerScope();
        var consumer = scope.ServiceProvider.GetRequiredService<DocumentEmbeddingsRequestedConsumer>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consumer.HandleAsync(
            EmbeddingsEvent(LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, runId),
            cancellation.Token));

        Assert.Empty(_fixture.ProviderProbe.EmbeddingRequests);
        Assert.Empty(_fixture.EventBus.Events);
        await using var context = _fixture.CreateDbContext();
        Assert.False(await context.DocumentProcessingSteps.AnyAsync(item =>
            item.ProcessingRunId == runId));
        Assert.True(await context.DocumentEmbeddings.AnyAsync(item => item.Id == ids.EmbeddingA1));
    }

    [Fact]
    public async Task Database_unavailability_is_surfaced_by_real_worker_repository_path()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(606);
        await using var provider = _fixture.CreateWorkerProvider(
            "Host=127.0.0.1;Port=1;Database=unavailable;Username=live;Password=live;Timeout=1;Command Timeout=1");
        using var scope = provider.CreateScope();
        var consumer = scope.ServiceProvider.GetRequiredService<DocumentPreprocessRequestedConsumer>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.HandleAsync(
            PreprocessEvent(
                LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1, ids.RunA1,
                $"tenants/{LiveTestConstants.TenantA:D}/documents/{ids.DocumentA1:D}/versions/{ids.VersionA1:D}/source.txt"),
            CancellationToken.None));
        Assert.IsType<NpgsqlException>(exception.InnerException);
        Assert.Empty(_fixture.ProviderProbe.PreprocessingRequests);
    }

    private static DocumentPreprocessRequestedEvent PreprocessEvent(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid runId,
        string sourceKey) =>
        new(
            tenantId,
            documentId,
            versionId,
            runId,
            sourceKey,
            "source.txt",
            "text/plain",
            "live-worker",
            DateTimeOffset.UtcNow);

    private static DocumentChunkingRequestedEvent ChunkEvent(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid runId,
        string markdownKey) =>
        new(
            tenantId,
            documentId,
            versionId,
            runId,
            markdownKey,
            "live-worker",
            DateTimeOffset.UtcNow);

    private static DocumentIntelligenceRequestedEvent IntelligenceEvent(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid runId) =>
        new(tenantId, documentId, versionId, runId, "live-worker", DateTimeOffset.UtcNow);

    private static DocumentEmbeddingsRequestedEvent EmbeddingsEvent(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid runId) =>
        new(tenantId, documentId, versionId, runId, "live-worker", DateTimeOffset.UtcNow);
}
