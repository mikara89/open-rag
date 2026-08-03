using Microsoft.Extensions.Options;
using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Documents.DeleteDocument;
using OpenRAG.Application.Documents.GetDocumentChunk;
using OpenRAG.Application.Documents.GetDocumentDetail;
using OpenRAG.Application.Documents.GetDocumentIntelligence;
using OpenRAG.Application.Documents.GetDocumentStatus;
using OpenRAG.Application.Documents.GetJsonArtifact;
using OpenRAG.Application.Documents.GetMarkdownArtifact;
using OpenRAG.Application.Documents.ListDocumentChunks;
using OpenRAG.Application.Documents.ListDocuments;
using OpenRAG.Application.Documents.ReprocessDocument;
using OpenRAG.Application.Documents.UploadDocument;
using OpenRAG.Application.Processing.ChunkDocument;
using OpenRAG.Application.Processing.GenerateEmbeddings;
using OpenRAG.Application.Processing.GenerateIntelligence;
using OpenRAG.Application.Processing.PreprocessDocument;
using OpenRAG.Application.Rag;
using OpenRAG.Application.Rag.AskQuestion;

namespace OpenRAG.Application.Pipeline.Validation;

internal static class PrimitiveValidation
{
    internal const int MaximumPageSize = 100;
    internal const long MaximumUploadSizeBytes = 100L * 1024 * 1024;

    internal static void NotEmpty(
        Guid value,
        ICollection<ApplicationError> errors,
        string code,
        string message,
        string target)
    {
        if (value == Guid.Empty)
            errors.Add(ApplicationErrors.InvalidRequest(code, message, target));
    }

    internal static void NotBlank(
        string? value,
        ICollection<ApplicationError> errors,
        string code,
        string message,
        string target)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(ApplicationErrors.InvalidRequest(code, message, target));
    }

    internal static void Pagination(
        int pageNumber,
        int pageSize,
        ICollection<ApplicationError> errors)
    {
        if (pageNumber <= 0)
        {
            errors.Add(ApplicationErrors.InvalidRequest(
                "request.page_number_invalid",
                "Page number must be greater than zero.",
                "pageNumber"));
        }

        if (pageSize <= 0 || pageSize > MaximumPageSize)
        {
            errors.Add(ApplicationErrors.InvalidRequest(
                "request.page_size_invalid",
                $"Page size must be between 1 and {MaximumPageSize}.",
                "pageSize"));
        }
    }

    internal static void WorkerMessage(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid processingRunId,
        string correlationId,
        ICollection<ApplicationError> errors)
    {
        NotEmpty(tenantId, errors, "worker.tenant_id_required", "TenantId cannot be empty.", "tenantId");
        NotEmpty(documentId, errors, "worker.document_id_required", "DocumentId cannot be empty.", "documentId");
        NotEmpty(versionId, errors, "worker.version_id_required", "VersionId cannot be empty.", "versionId");
        NotEmpty(
            processingRunId,
            errors,
            "worker.processing_run_id_required",
            "ProcessingRunId cannot be empty.",
            "processingRunId");
        NotBlank(
            correlationId,
            errors,
            "worker.correlation_id_required",
            "CorrelationId cannot be empty.",
            "correlationId");
    }

    internal static ValueTask<IReadOnlyList<ApplicationError>> Completed(
        List<ApplicationError> errors,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ApplicationError> result = Array.AsReadOnly(errors.ToArray());
        return ValueTask.FromResult(result);
    }
}

internal sealed class UploadDocumentCommandValidator
    : IMessageValidator<UploadDocumentCommand>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        UploadDocumentCommand message,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApplicationError>();
        PrimitiveValidation.NotBlank(
            message.FileName,
            errors,
            "request.file_name_required",
            "File name cannot be empty.",
            "fileName");
        PrimitiveValidation.NotBlank(
            message.ContentType,
            errors,
            "request.content_type_required",
            "Content type cannot be empty.",
            "contentType");
        PrimitiveValidation.NotBlank(
            message.CorrelationId,
            errors,
            "request.correlation_id_required",
            "CorrelationId cannot be empty.",
            "correlationId");

        if (message.SizeBytes <= 0)
        {
            errors.Add(ApplicationErrors.InvalidRequest(
                "request.file_size_invalid",
                "File size must be greater than zero.",
                "sizeBytes"));
        }
        else if (message.SizeBytes > PrimitiveValidation.MaximumUploadSizeBytes)
        {
            errors.Add(ApplicationErrors.InvalidRequest(
                "request.file_size_invalid",
                "File size exceeds the maximum allowed size.",
                "sizeBytes"));
        }

        if (message.Content is null)
        {
            errors.Add(ApplicationErrors.InvalidRequest(
                "request.content_required",
                "Content stream cannot be null.",
                "content"));
        }

        return PrimitiveValidation.Completed(errors, cancellationToken);
    }
}

internal sealed class DeleteDocumentCommandValidator
    : IMessageValidator<DeleteDocumentCommand>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        DeleteDocumentCommand message,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApplicationError>();
        PrimitiveValidation.NotEmpty(
            message.DocumentId,
            errors,
            "request.document_id_required",
            "DocumentId cannot be empty.",
            "documentId");
        return PrimitiveValidation.Completed(errors, cancellationToken);
    }
}

internal sealed class ReprocessDocumentCommandValidator
    : IMessageValidator<ReprocessDocumentCommand>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        ReprocessDocumentCommand message,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApplicationError>();
        PrimitiveValidation.NotEmpty(
            message.DocumentId,
            errors,
            "request.document_id_required",
            "DocumentId cannot be empty.",
            "documentId");
        PrimitiveValidation.NotBlank(
            message.CorrelationId,
            errors,
            "request.correlation_id_required",
            "CorrelationId cannot be empty.",
            "correlationId");

        if (!message.ForcePreprocess
            && !message.ForceChunk
            && !message.ForceIntelligence
            && !message.ForceEmbeddings)
        {
            errors.Add(ApplicationErrors.InvalidRequest(
                "request.reprocess_stage_required",
                "At least one reprocessing stage must be selected.",
                "stages"));
        }

        return PrimitiveValidation.Completed(errors, cancellationToken);
    }
}

internal sealed class ListDocumentsQueryValidator
    : IMessageValidator<ListDocumentsQuery>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        ListDocumentsQuery message,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApplicationError>();
        PrimitiveValidation.Pagination(message.PageNumber, message.PageSize, errors);
        return PrimitiveValidation.Completed(errors, cancellationToken);
    }
}

internal sealed class GetDocumentDetailQueryValidator
    : IMessageValidator<GetDocumentDetailQuery>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        GetDocumentDetailQuery message,
        CancellationToken cancellationToken) =>
        ValidateDocumentId(message.DocumentId, cancellationToken);

    private static ValueTask<IReadOnlyList<ApplicationError>> ValidateDocumentId(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApplicationError>();
        PrimitiveValidation.NotEmpty(
            documentId,
            errors,
            "request.document_id_required",
            "DocumentId cannot be empty.",
            "documentId");
        return PrimitiveValidation.Completed(errors, cancellationToken);
    }
}

internal sealed class GetDocumentStatusQueryValidator
    : IMessageValidator<GetDocumentStatusQuery>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        GetDocumentStatusQuery message,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApplicationError>();
        PrimitiveValidation.NotEmpty(
            message.DocumentId,
            errors,
            "request.document_id_required",
            "DocumentId cannot be empty.",
            "documentId");
        return PrimitiveValidation.Completed(errors, cancellationToken);
    }
}

internal abstract class DocumentVersionMessageValidator
{
    protected static ValueTask<IReadOnlyList<ApplicationError>> Validate(
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApplicationError>();
        PrimitiveValidation.NotEmpty(
            documentId,
            errors,
            "request.document_id_required",
            "DocumentId cannot be empty.",
            "documentId");
        PrimitiveValidation.NotEmpty(
            versionId,
            errors,
            "request.version_id_required",
            "VersionId cannot be empty.",
            "versionId");
        return PrimitiveValidation.Completed(errors, cancellationToken);
    }
}

internal sealed class GetMarkdownArtifactQueryValidator
    : DocumentVersionMessageValidator, IMessageValidator<GetMarkdownArtifactQuery>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        GetMarkdownArtifactQuery message,
        CancellationToken cancellationToken) =>
        Validate(message.DocumentId, message.VersionId, cancellationToken);
}

internal sealed class GetJsonArtifactQueryValidator
    : DocumentVersionMessageValidator, IMessageValidator<GetJsonArtifactQuery>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        GetJsonArtifactQuery message,
        CancellationToken cancellationToken) =>
        Validate(message.DocumentId, message.VersionId, cancellationToken);
}

internal sealed class ListDocumentChunksQueryValidator
    : DocumentVersionMessageValidator, IMessageValidator<ListDocumentChunksQuery>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        ListDocumentChunksQuery message,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApplicationError>();
        PrimitiveValidation.NotEmpty(
            message.DocumentId,
            errors,
            "request.document_id_required",
            "DocumentId cannot be empty.",
            "documentId");
        PrimitiveValidation.NotEmpty(
            message.VersionId,
            errors,
            "request.version_id_required",
            "VersionId cannot be empty.",
            "versionId");
        PrimitiveValidation.Pagination(message.PageNumber, message.PageSize, errors);

        if (message.PageNumberFilter <= 0)
        {
            errors.Add(ApplicationErrors.InvalidRequest(
                "request.page_number_filter_invalid",
                "Page number filter must be greater than zero.",
                "pageNumberFilter"));
        }

        return PrimitiveValidation.Completed(errors, cancellationToken);
    }
}

internal sealed class GetDocumentChunkQueryValidator
    : DocumentVersionMessageValidator, IMessageValidator<GetDocumentChunkQuery>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        GetDocumentChunkQuery message,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApplicationError>();
        PrimitiveValidation.NotEmpty(
            message.DocumentId,
            errors,
            "request.document_id_required",
            "DocumentId cannot be empty.",
            "documentId");
        PrimitiveValidation.NotEmpty(
            message.VersionId,
            errors,
            "request.version_id_required",
            "VersionId cannot be empty.",
            "versionId");
        PrimitiveValidation.NotEmpty(
            message.ChunkId,
            errors,
            "request.chunk_id_required",
            "ChunkId cannot be empty.",
            "chunkId");
        return PrimitiveValidation.Completed(errors, cancellationToken);
    }
}

internal sealed class GetDocumentIntelligenceQueryValidator
    : DocumentVersionMessageValidator, IMessageValidator<GetDocumentIntelligenceQuery>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        GetDocumentIntelligenceQuery message,
        CancellationToken cancellationToken) =>
        Validate(message.DocumentId, message.VersionId, cancellationToken);
}

internal sealed class AskQuestionQueryValidator
    : IMessageValidator<AskQuestionQuery>
{
    private readonly RagOptions _options;

    public AskQuestionQueryValidator(IOptions<RagOptions> options)
    {
        _options = options.Value;
    }

    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        AskQuestionQuery message,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApplicationError>();
        PrimitiveValidation.NotBlank(
            message.Question,
            errors,
            "request.question_required",
            "Question cannot be empty.",
            "question");
        PrimitiveValidation.NotBlank(
            message.Model,
            errors,
            "request.model_required",
            "Model cannot be empty.",
            "model");
        PrimitiveValidation.NotBlank(
            message.CorrelationId,
            errors,
            "request.correlation_id_required",
            "CorrelationId cannot be empty.",
            "correlationId");

        if (message.TopK <= 0)
        {
            errors.Add(ApplicationErrors.InvalidRequest(
                "request.top_k_invalid",
                "TopK must be greater than zero.",
                "topK"));
        }

        if (message.FilterDocumentIds is not null)
        {
            if (message.FilterDocumentIds.Any(id => id == Guid.Empty))
            {
                errors.Add(ApplicationErrors.InvalidRequest(
                    "request.document_filter_invalid",
                    "Document IDs must be non-empty.",
                    "documentIds"));
            }

            if (message.FilterDocumentIds.Distinct().Count() > _options.MaxDocumentFilterIds)
            {
                errors.Add(ApplicationErrors.InvalidRequest(
                    "request.document_filter_invalid",
                    $"Document filter cannot contain more than {_options.MaxDocumentFilterIds} IDs.",
                    "documentIds"));
            }
        }

        return PrimitiveValidation.Completed(errors, cancellationToken);
    }
}

internal abstract class WorkerMessageValidator
{
    protected static ValueTask<IReadOnlyList<ApplicationError>> Validate(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid processingRunId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApplicationError>();
        PrimitiveValidation.WorkerMessage(
            tenantId,
            documentId,
            versionId,
            processingRunId,
            correlationId,
            errors);
        return PrimitiveValidation.Completed(errors, cancellationToken);
    }
}

internal sealed class PreprocessDocumentCommandValidator
    : WorkerMessageValidator, IMessageValidator<PreprocessDocumentCommand>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        PreprocessDocumentCommand message,
        CancellationToken cancellationToken) =>
        Validate(
            message.TenantId,
            message.DocumentId,
            message.VersionId,
            message.ProcessingRunId,
            message.CorrelationId,
            cancellationToken);
}

internal sealed class ChunkDocumentCommandValidator
    : WorkerMessageValidator, IMessageValidator<ChunkDocumentCommand>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        ChunkDocumentCommand message,
        CancellationToken cancellationToken) =>
        Validate(
            message.TenantId,
            message.DocumentId,
            message.VersionId,
            message.ProcessingRunId,
            message.CorrelationId,
            cancellationToken);
}

internal sealed class GenerateIntelligenceCommandValidator
    : WorkerMessageValidator, IMessageValidator<GenerateIntelligenceCommand>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        GenerateIntelligenceCommand message,
        CancellationToken cancellationToken) =>
        Validate(
            message.TenantId,
            message.DocumentId,
            message.VersionId,
            message.ProcessingRunId,
            message.CorrelationId,
            cancellationToken);
}

internal sealed class GenerateEmbeddingsCommandValidator
    : WorkerMessageValidator, IMessageValidator<GenerateEmbeddingsCommand>
{
    public ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        GenerateEmbeddingsCommand message,
        CancellationToken cancellationToken) =>
        Validate(
            message.TenantId,
            message.DocumentId,
            message.VersionId,
            message.ProcessingRunId,
            message.CorrelationId,
            cancellationToken);
}
