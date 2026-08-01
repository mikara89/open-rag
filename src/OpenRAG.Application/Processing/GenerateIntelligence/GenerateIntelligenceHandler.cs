using System.Text;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Application.Processing.GenerateIntelligence;

public sealed class GenerateIntelligenceHandler
    : IRequestHandler<GenerateIntelligenceCommand, GenerateIntelligenceResponse>
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentIntelligenceRepository _intelligenceRepository;
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IDocumentIntelligenceService _intelligenceService;
    private readonly IDocumentEventBus _eventBus;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly GenerateIntelligenceOptions _options;
    private readonly ILogger<GenerateIntelligenceHandler> _logger;

    public GenerateIntelligenceHandler(
        ICurrentTenant currentTenant,
        IDocumentRepository documentRepository,
        IDocumentIntelligenceRepository intelligenceRepository,
        IProcessingRunRepository processingRunRepository,
        IFileStorage fileStorage,
        IDocumentIntelligenceService intelligenceService,
        IDocumentEventBus eventBus,
        IClock clock,
        IUnitOfWork unitOfWork,
        IOptions<GenerateIntelligenceOptions> options,
        ILogger<GenerateIntelligenceHandler> logger)
    {
        _currentTenant = currentTenant;
        _documentRepository = documentRepository;
        _intelligenceRepository = intelligenceRepository;
        _processingRunRepository = processingRunRepository;
        _fileStorage = fileStorage;
        _intelligenceService = intelligenceService;
        _eventBus = eventBus;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<GenerateIntelligenceResponse> Handle(
        GenerateIntelligenceCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate command
        if (command.DocumentId == Guid.Empty)
            throw new AppException("DocumentId cannot be empty.");
        if (command.VersionId == Guid.Empty)
            throw new AppException("VersionId cannot be empty.");
        if (command.ProcessingRunId == Guid.Empty)
            throw new AppException("ProcessingRunId cannot be empty.");
        if (string.IsNullOrWhiteSpace(command.CorrelationId))
            throw new AppException("CorrelationId cannot be empty.");

        var tenantId = _currentTenant.TenantId;

        // 2. Load processing run for update
        var run = await _processingRunRepository.GetByIdForUpdateAsync(
            tenantId, command.ProcessingRunId, cancellationToken);

        if (run is null)
        {
            _logger.LogWarning(
                "Intelligence no-op: ProcessingRun not found. RunId={ProcessingRunId}, CorrelationId={CorrelationId}",
                command.ProcessingRunId, command.CorrelationId);
            return NoOpResult(command, "ProcessingRunNotFound");
        }

        // 3. Load document — no-op if missing or deleted
        var document = await _documentRepository.GetByIdForUpdateAsync(
            tenantId, command.DocumentId, cancellationToken);

        if (document is null)
        {
            _logger.LogWarning(
                "Intelligence no-op: Document not found. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                command.DocumentId, command.CorrelationId);
            return NoOpResult(command, "DocumentNotFound");
        }

        if (document.Status == DocumentStatus.Deleted)
        {
            _logger.LogWarning(
                "Intelligence no-op: Document is deleted. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                command.DocumentId, command.CorrelationId);
            return NoOpResult(command, "DocumentDeleted");
        }

        // 4. Check idempotency — already completed in this run?
        var existingStep = await _processingRunRepository.GetStepForUpdateAsync(
            tenantId, command.ProcessingRunId, DocumentProcessingStepName.GenerateIntelligence, cancellationToken);

        if (existingStep is not null && existingStep.Status == DocumentProcessingStepStatus.Completed)
        {
            _logger.LogInformation(
                "Intelligence already generated for this run. DocumentId={DocumentId}, VersionId={VersionId}, RunId={ProcessingRunId}",
                command.DocumentId, command.VersionId, command.ProcessingRunId);
            return new GenerateIntelligenceResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                Status: "AlreadyGenerated",
                Provider: null,
                Model: null);
        }

        // 5. Load version to get artifact keys
        var version = await _documentRepository.GetVersionForUpdateAsync(
            tenantId, command.DocumentId, command.VersionId, cancellationToken);

        if (version is null)
        {
            _logger.LogWarning(
                "Intelligence no-op: Version not found. VersionId={VersionId}, CorrelationId={CorrelationId}",
                command.VersionId, command.CorrelationId);
            return NoOpResult(command, "VersionNotFound");
        }

        // 6. Read markdown artifact (required)
        if (string.IsNullOrWhiteSpace(version.DoclingMarkdownObjectKey))
        {
            _logger.LogWarning(
                "Intelligence no-op: No markdown artifact. DocumentId={DocumentId}, VersionId={VersionId}",
                command.DocumentId, command.VersionId);
            return NoOpResult(command, "NoMarkdownArtifact");
        }

        var markdown = await ReadTextFromStorage(
            version.DoclingMarkdownObjectKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(markdown))
        {
            _logger.LogWarning(
                "Intelligence no-op: Markdown artifact is empty. DocumentId={DocumentId}, VersionId={VersionId}",
                command.DocumentId, command.VersionId);
            return NoOpResult(command, "EmptyMarkdownArtifact");
        }

        // 7. Optionally read JSON artifact
        string? jsonContent = null;
        if (!string.IsNullOrWhiteSpace(version.DoclingJsonObjectKey))
        {
            try
            {
                jsonContent = await ReadTextFromStorage(
                    version.DoclingJsonObjectKey, cancellationToken);
            }
            catch
            {
                // JSON artifact is optional for intelligence
                _logger.LogDebug("Could not read JSON artifact for intelligence. Continuing with markdown only.");
            }
        }

        // 8. Truncate markdown to max input
        var truncatedMarkdown = markdown.Length > _options.MaxInputCharacters
            ? markdown[.._options.MaxInputCharacters]
            : markdown;

        // 9. Create or reuse processing step
        var step = existingStep ?? DocumentProcessingStep.Create(
            Guid.NewGuid(),
            tenantId,
            command.DocumentId,
            command.VersionId,
            command.ProcessingRunId,
            DocumentProcessingStepName.GenerateIntelligence,
            maxAttempts: 3,
            inputHash: version.DoclingMarkdownObjectKey,
            processorName: _options.Provider,
            processorVersion: "1.0");

        if (existingStep is null)
        {
            await _processingRunRepository.AddStepAsync(step, cancellationToken);
        }

        step.Start();

        // 10. Call intelligence service
        DocumentIntelligenceResult result;
        try
        {
            var request = new DocumentIntelligenceRequest(
                TenantId: tenantId,
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                FileName: document.OriginalFileName,
                MarkdownContent: truncatedMarkdown,
                JsonContent: jsonContent,
                CorrelationId: command.CorrelationId);

            result = await _intelligenceService.GenerateAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            await using var failureTx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
            step.MarkFailed("INTELLIGENCE_FAILED", ex.Message);

            if (document.Status != DocumentStatus.Ready && document.Status != DocumentStatus.Deleted)
                document.MarkFailed();

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await failureTx.CommitAsync(cancellationToken);

            _logger.LogError(ex,
                "Intelligence generation failed: DocumentId={DocumentId}, VersionId={VersionId}, RunId={ProcessingRunId}",
                command.DocumentId, command.VersionId, command.ProcessingRunId);

            return new GenerateIntelligenceResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                Status: "Failed",
                Provider: null,
                Model: null);
        }

        // 11. Begin transaction for persistence
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // 12. Delete old intelligence for version (clean slate)
        await _intelligenceRepository.DeleteByVersionAsync(
            tenantId, command.DocumentId, command.VersionId, cancellationToken);

        // 13. Serialize keywords/entities/metadata to JSON
        var keywordsJson = JsonSerializer.Serialize(result.Keywords);
        var entitiesJson = JsonSerializer.Serialize(result.Entities);
        var metadataJson = JsonSerializer.Serialize(result.ExtractedMetadata);

        // 14. Create new intelligence record
        var intelligence = DocumentIntelligence.Create(
            id: Guid.NewGuid(),
            tenantId: tenantId,
            documentId: command.DocumentId,
            versionId: command.VersionId,
            classification: result.Classification,
            summary: TruncateSummary(result.Summary),
            keywordsJson: keywordsJson,
            entitiesJson: entitiesJson,
            extractedMetadataJson: metadataJson,
            provider: result.Provider,
            model: result.Model);

        await _intelligenceRepository.AddAsync(intelligence, cancellationToken);

        // 15. Mark step completed
        step.MarkCompleted(result.Model);

        // 16. SaveChanges
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 17. Publish intelligence generated event → triggers embeddings
        var occurredAt = _clock.UtcNow;
        var generatedEvent = new DocumentIntelligenceGeneratedEvent(
            TenantId: tenantId,
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            ProcessingRunId: command.ProcessingRunId,
            Provider: result.Provider,
            Model: result.Model,
            CorrelationId: command.CorrelationId,
            OccurredAt: occurredAt);

        await _eventBus.PublishAsync("document.intelligence.generated", generatedEvent, cancellationToken);

        // 18. Commit
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Intelligence generated: DocumentId={DocumentId}, VersionId={VersionId}, Provider={Provider}, Classification={Classification}",
            command.DocumentId, command.VersionId, result.Provider, result.Classification);

        return new GenerateIntelligenceResponse(
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            Status: "Generated",
            Provider: result.Provider,
            Model: result.Model);
    }

    private string? TruncateSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return summary;

        return summary.Length > _options.SummaryMaxCharacters
            ? summary[.._options.SummaryMaxCharacters]
            : summary;
    }

    private static GenerateIntelligenceResponse NoOpResult(
        GenerateIntelligenceCommand command, string reason)
    {
        return new GenerateIntelligenceResponse(
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            Status: reason,
            Provider: null,
            Model: null);
    }

    private async Task<string> ReadTextFromStorage(
        string objectKey, CancellationToken cancellationToken)
    {
        using var stream = await _fileStorage.OpenReadAsync(objectKey, cancellationToken);
        using var reader = new global::System.IO.StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
