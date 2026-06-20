namespace OpenRAG.Application.Documents.UploadDocument;

public sealed record UploadDocumentResponse(
    Guid DocumentId,
    Guid VersionId,
    string Status
);
