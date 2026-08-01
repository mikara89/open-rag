namespace OpenRAG.Application.DTOs;

public sealed record ReprocessDocumentRequest(
    bool ForcePreprocess = true,
    bool ForceChunk = true,
    bool ForceIntelligence = true,
    bool ForceEmbeddings = true
);
