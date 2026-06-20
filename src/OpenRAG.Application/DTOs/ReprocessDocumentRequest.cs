namespace OpenRAG.Application.DTOs;

public sealed record ReprocessDocumentRequest(
    bool ForcePreprocess = true,
    bool ForceChunk = true,
    bool ForceEmbeddings = true
);
