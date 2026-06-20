using Mediator;
using OpenRAG.Application.Common;

namespace OpenRAG.Application.Documents.UploadDocument;

public sealed record UploadDocumentCommand(
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content,
    string CorrelationId
) : IRequest<UploadDocumentResponse>;
