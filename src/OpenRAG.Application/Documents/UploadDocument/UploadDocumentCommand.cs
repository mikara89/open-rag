using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.UploadDocument;

public sealed record UploadDocumentCommand(
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content,
    string CorrelationId
) : IOpenRagCommand<Result<UploadDocumentResponse>>,
    IAuthenticatedApplicationMessage,
    IResultApplicationMessage,
    ICorrelatedMessage;
