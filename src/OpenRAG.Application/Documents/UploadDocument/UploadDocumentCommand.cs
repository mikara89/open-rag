using OpenRAG.Application.Common;
using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.UploadDocument;

public sealed record UploadDocumentCommand(
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content,
    string CorrelationId
) : IOpenRagCommand<UploadDocumentResponse>,
    IAuthenticatedApplicationMessage,
    ICorrelatedMessage;
