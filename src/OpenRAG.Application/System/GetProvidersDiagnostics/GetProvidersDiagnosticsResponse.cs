namespace OpenRAG.Application.System.GetProvidersDiagnostics;

public sealed record GetProvidersDiagnosticsResponse(
    ProviderDiagnostics Preprocessing,
    ProviderDiagnostics Chunking,
    ProviderDiagnostics Embeddings,
    ProviderDiagnostics Chat,
    ProviderDiagnostics Storage
);

public sealed record ProviderDiagnostics(
    string Provider,
    bool Configured,
    string? BaseUrl = null,
    string? Model = null,
    bool? ApiKeyPresent = null,
    string? ConvertFilePath = null,
    List<string>? ToFormats = null,
    int? MaxChunkCharacters = null,
    int? OverlapCharacters = null,
    int? TimeoutSeconds = null,
    int? Dimensions = null,
    string? LocalRootPath = null,
    List<string>? ValidationErrors = null
);
