namespace OpenRAG.Infrastructure.Preprocessing;

public sealed class DoclingPreprocessorOptions
{
    public const string SectionName = "Preprocessing:Docling";

    public string Provider { get; set; } = "Mock";
    public string BaseUrl { get; set; } = "http://localhost:5001";
    public string ConvertFilePath { get; init; } = "/v1/convert/file";
    public int TimeoutSeconds { get; init; } = 300;
    public bool IncludeMarkdown { get; init; } = true;
    public bool IncludeJson { get; init; } = true;
    public bool EnableOcr { get; init; } = false;
    public List<string> ToFormats { get; init; } = ["md", "json"];
}
