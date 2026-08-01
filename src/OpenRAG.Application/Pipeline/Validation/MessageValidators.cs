using Microsoft.Extensions.Options;
using OpenRAG.Application.Common;
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

    internal static void NotEmpty(Guid value, string name)
    {
        if (value == Guid.Empty)
            throw new RequestValidationException($"{name} cannot be empty.");
    }

    internal static void NotBlank(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new RequestValidationException($"{name} cannot be empty.");
    }

    internal static void Pagination(int pageNumber, int pageSize)
    {
        if (pageNumber <= 0)
            throw new RequestValidationException("Page number must be greater than zero.");

        if (pageSize <= 0 || pageSize > MaximumPageSize)
        {
            throw new RequestValidationException(
                $"Page size must be between 1 and {MaximumPageSize}.");
        }
    }

    internal static void WorkerMessage(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid processingRunId,
        string correlationId)
    {
        NotEmpty(tenantId, "TenantId");
        NotEmpty(documentId, "DocumentId");
        NotEmpty(versionId, "VersionId");
        NotEmpty(processingRunId, "ProcessingRunId");
        NotBlank(correlationId, "CorrelationId");
    }

    internal static ValueTask Completed(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

internal sealed class UploadDocumentCommandValidator
    : IMessageValidator<UploadDocumentCommand>
{
    public ValueTask ValidateAsync(
        UploadDocumentCommand message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.NotBlank(message.FileName, "File name");
        PrimitiveValidation.NotBlank(message.ContentType, "Content type");
        PrimitiveValidation.NotBlank(message.CorrelationId, "CorrelationId");

        if (message.SizeBytes <= 0)
            throw new RequestValidationException("File size must be greater than zero.");

        if (message.SizeBytes > PrimitiveValidation.MaximumUploadSizeBytes)
            throw new RequestValidationException("File size exceeds the maximum allowed size.");

        if (message.Content is null)
            throw new RequestValidationException("Content stream cannot be null.");

        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class DeleteDocumentCommandValidator
    : IMessageValidator<DeleteDocumentCommand>
{
    public ValueTask ValidateAsync(
        DeleteDocumentCommand message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.NotEmpty(message.DocumentId, "DocumentId");
        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class ReprocessDocumentCommandValidator
    : IMessageValidator<ReprocessDocumentCommand>
{
    public ValueTask ValidateAsync(
        ReprocessDocumentCommand message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.NotEmpty(message.DocumentId, "DocumentId");
        PrimitiveValidation.NotBlank(message.CorrelationId, "CorrelationId");

        if (!message.ForcePreprocess
            && !message.ForceChunk
            && !message.ForceIntelligence
            && !message.ForceEmbeddings)
        {
            throw new RequestValidationException(
                "At least one reprocessing stage must be selected.");
        }

        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class ListDocumentsQueryValidator
    : IMessageValidator<ListDocumentsQuery>
{
    public ValueTask ValidateAsync(
        ListDocumentsQuery message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.Pagination(message.PageNumber, message.PageSize);
        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class GetDocumentDetailQueryValidator
    : IMessageValidator<GetDocumentDetailQuery>
{
    public ValueTask ValidateAsync(
        GetDocumentDetailQuery message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.NotEmpty(message.DocumentId, "DocumentId");
        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class GetDocumentStatusQueryValidator
    : IMessageValidator<GetDocumentStatusQuery>
{
    public ValueTask ValidateAsync(
        GetDocumentStatusQuery message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.NotEmpty(message.DocumentId, "DocumentId");
        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class GetMarkdownArtifactQueryValidator
    : IMessageValidator<GetMarkdownArtifactQuery>
{
    public ValueTask ValidateAsync(
        GetMarkdownArtifactQuery message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.NotEmpty(message.DocumentId, "DocumentId");
        PrimitiveValidation.NotEmpty(message.VersionId, "VersionId");
        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class GetJsonArtifactQueryValidator
    : IMessageValidator<GetJsonArtifactQuery>
{
    public ValueTask ValidateAsync(
        GetJsonArtifactQuery message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.NotEmpty(message.DocumentId, "DocumentId");
        PrimitiveValidation.NotEmpty(message.VersionId, "VersionId");
        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class ListDocumentChunksQueryValidator
    : IMessageValidator<ListDocumentChunksQuery>
{
    public ValueTask ValidateAsync(
        ListDocumentChunksQuery message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.NotEmpty(message.DocumentId, "DocumentId");
        PrimitiveValidation.NotEmpty(message.VersionId, "VersionId");
        PrimitiveValidation.Pagination(message.PageNumber, message.PageSize);

        if (message.PageNumberFilter <= 0)
        {
            throw new RequestValidationException(
                "Page number filter must be greater than zero.");
        }

        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class GetDocumentChunkQueryValidator
    : IMessageValidator<GetDocumentChunkQuery>
{
    public ValueTask ValidateAsync(
        GetDocumentChunkQuery message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.NotEmpty(message.DocumentId, "DocumentId");
        PrimitiveValidation.NotEmpty(message.VersionId, "VersionId");
        PrimitiveValidation.NotEmpty(message.ChunkId, "ChunkId");
        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class GetDocumentIntelligenceQueryValidator
    : IMessageValidator<GetDocumentIntelligenceQuery>
{
    public ValueTask ValidateAsync(
        GetDocumentIntelligenceQuery message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.NotEmpty(message.DocumentId, "DocumentId");
        PrimitiveValidation.NotEmpty(message.VersionId, "VersionId");
        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class AskQuestionQueryValidator
    : IMessageValidator<AskQuestionQuery>
{
    private readonly RagOptions _options;

    public AskQuestionQueryValidator(IOptions<RagOptions> options)
    {
        _options = options.Value;
    }

    public ValueTask ValidateAsync(
        AskQuestionQuery message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.NotBlank(message.Question, "Question");
        PrimitiveValidation.NotBlank(message.Model, "Model");
        PrimitiveValidation.NotBlank(message.CorrelationId, "CorrelationId");

        if (message.TopK <= 0)
            throw new RequestValidationException("TopK must be greater than zero.");

        if (message.FilterDocumentIds is not null)
        {
            if (message.FilterDocumentIds.Any(id => id == Guid.Empty))
                throw new RequestValidationException("Document IDs must be non-empty.");

            if (message.FilterDocumentIds.Distinct().Count() > _options.MaxDocumentFilterIds)
            {
                throw new RequestValidationException(
                    $"Document filter cannot contain more than {_options.MaxDocumentFilterIds} IDs.");
            }
        }

        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class PreprocessDocumentCommandValidator
    : IMessageValidator<PreprocessDocumentCommand>
{
    public ValueTask ValidateAsync(
        PreprocessDocumentCommand message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.WorkerMessage(
            message.TenantId,
            message.DocumentId,
            message.VersionId,
            message.ProcessingRunId,
            message.CorrelationId);
        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class ChunkDocumentCommandValidator
    : IMessageValidator<ChunkDocumentCommand>
{
    public ValueTask ValidateAsync(
        ChunkDocumentCommand message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.WorkerMessage(
            message.TenantId,
            message.DocumentId,
            message.VersionId,
            message.ProcessingRunId,
            message.CorrelationId);
        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class GenerateIntelligenceCommandValidator
    : IMessageValidator<GenerateIntelligenceCommand>
{
    public ValueTask ValidateAsync(
        GenerateIntelligenceCommand message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.WorkerMessage(
            message.TenantId,
            message.DocumentId,
            message.VersionId,
            message.ProcessingRunId,
            message.CorrelationId);
        return PrimitiveValidation.Completed(cancellationToken);
    }
}

internal sealed class GenerateEmbeddingsCommandValidator
    : IMessageValidator<GenerateEmbeddingsCommand>
{
    public ValueTask ValidateAsync(
        GenerateEmbeddingsCommand message,
        CancellationToken cancellationToken)
    {
        PrimitiveValidation.WorkerMessage(
            message.TenantId,
            message.DocumentId,
            message.VersionId,
            message.ProcessingRunId,
            message.CorrelationId);
        return PrimitiveValidation.Completed(cancellationToken);
    }
}
