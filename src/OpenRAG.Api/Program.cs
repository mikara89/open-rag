using Mediator;
using OpenRAG.Application;
using OpenRAG.Application.Documents.DeleteDocument;
using OpenRAG.Application.Documents.GetDocumentChunk;
using OpenRAG.Application.Documents.GetDocumentDetail;
using OpenRAG.Application.Documents.GetDocumentStatus;
using OpenRAG.Application.Documents.GetJsonArtifact;
using OpenRAG.Application.Documents.GetMarkdownArtifact;
using OpenRAG.Application.Documents.ListDocumentChunks;
using OpenRAG.Application.Documents.ListDocuments;
using OpenRAG.Application.Documents.ReprocessDocument;
using OpenRAG.Application.Documents.UploadDocument;
using OpenRAG.Application.DTOs;
using OpenRAG.Application.Rag.AskQuestion;
using OpenRAG.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// ── Document endpoints ───────────────────────────────────────────

// List documents
app.MapGet("/api/documents", async (
    int? pageNumber,
    int? pageSize,
    string? status,
    string? search,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new ListDocumentsQuery(
        TenantId: Guid.Empty, // filled by handler via ICurrentTenant
        PageNumber: pageNumber ?? 1,
        PageSize: pageSize ?? 20,
        Status: status,
        Search: search);

    var response = await sender.Send(query, cancellationToken);
    return Results.Ok(response);
})
.WithName("ListDocuments");

// Upload document
app.MapPost("/api/documents/upload", async (
    IFormFile file,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var correlationId = Guid.NewGuid().ToString("N");

    await using var stream = file.OpenReadStream();

    var command = new UploadDocumentCommand(
        FileName: file.FileName,
        ContentType: file.ContentType,
        SizeBytes: file.Length,
        Content: stream,
        CorrelationId: correlationId);

    var response = await sender.Send(command, cancellationToken);

    return Results.Created($"/api/documents/{response.DocumentId}/status", response);
})
.WithName("UploadDocument")
.DisableAntiforgery();

app.MapGet("/api/documents/{documentId:guid}/status", async (
    Guid documentId,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetDocumentStatusQuery(documentId, Guid.Empty);
    var response = await sender.Send(query, cancellationToken);
    return Results.Ok(response);
})
.WithName("GetDocumentStatus");

// ── Reprocess endpoint ───────────────────────────────────────────

app.MapPost("/api/documents/{documentId:guid}/reprocess", async (
    Guid documentId,
    ReprocessDocumentRequest request,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var correlationId = Guid.NewGuid().ToString("N");

    var command = new ReprocessDocumentCommand(
        TenantId: Guid.Empty, // filled by handler via ICurrentTenant
        DocumentId: documentId,
        ForcePreprocess: request.ForcePreprocess,
        ForceChunk: request.ForceChunk,
        ForceEmbeddings: request.ForceEmbeddings,
        CorrelationId: correlationId);

    var response = await sender.Send(command, cancellationToken);

    return Results.Accepted($"/api/documents/{response.DocumentId}/status", response);
})
.WithName("ReprocessDocument");

// ── Document detail endpoint ──────────────────────────────────────

app.MapGet("/api/documents/{documentId:guid}", async (
    Guid documentId,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetDocumentDetailQuery(documentId);
    var response = await sender.Send(query, cancellationToken);
    return Results.Ok(response);
})
.WithName("GetDocumentDetail");

// ── Delete document endpoint ──────────────────────────────────────

app.MapDelete("/api/documents/{documentId:guid}", async (
    Guid documentId,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var command = new DeleteDocumentCommand(documentId);
    var response = await sender.Send(command, cancellationToken);
    return Results.NoContent();
})
.WithName("DeleteDocument");

// ── Artifact preview endpoints ────────────────────────────────────

app.MapGet("/api/documents/{documentId:guid}/versions/{versionId:guid}/artifacts/markdown", async (
    Guid documentId,
    Guid versionId,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetMarkdownArtifactQuery(documentId, versionId);
    var response = await sender.Send(query, cancellationToken);
    return Results.Text(response.Content, response.ContentType);
})
.WithName("GetMarkdownArtifact");

app.MapGet("/api/documents/{documentId:guid}/versions/{versionId:guid}/artifacts/json", async (
    Guid documentId,
    Guid versionId,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetJsonArtifactQuery(documentId, versionId);
    var response = await sender.Send(query, cancellationToken);
    return Results.Text(response.Content, response.ContentType);
})
.WithName("GetJsonArtifact");

// ── Chunk endpoints ───────────────────────────────────────────────

app.MapGet("/api/documents/{documentId:guid}/versions/{versionId:guid}/chunks", async (
    Guid documentId,
    Guid versionId,
    int? pageNumber,
    int? pageSize,
    string? search,
    string? sectionTitle,
    int? pageNumberFilter,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new ListDocumentChunksQuery(
        documentId, versionId,
        pageNumber ?? 1, pageSize ?? 20,
        search, sectionTitle, pageNumberFilter);
    var response = await sender.Send(query, cancellationToken);
    return Results.Ok(response);
})
.WithName("ListDocumentChunks");

app.MapGet("/api/documents/{documentId:guid}/versions/{versionId:guid}/chunks/{chunkId:guid}", async (
    Guid documentId,
    Guid versionId,
    Guid chunkId,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetDocumentChunkQuery(documentId, versionId, chunkId);
    var response = await sender.Send(query, cancellationToken);
    return Results.Ok(response);
})
.WithName("GetDocumentChunk");

// ── RAG endpoints ─────────────────────────────────────────────────

app.MapPost("/api/rag/ask", async (
    AskQuestionRequest request,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new AskQuestionQuery(
        Question: request.Question,
        TenantId: request.TenantId,
        FilterDocumentIds: request.DocumentIds?.Count > 0 ? request.DocumentIds : null,
        TopK: request.TopK > 0 ? request.TopK : 5,
        Model: request.Model ?? "mock-chat",
        CorrelationId: Guid.NewGuid().ToString("N"));

    var response = await sender.Send(query, cancellationToken);
    return Results.Ok(response);
})
.WithName("AskQuestion");

app.Run();

// ── Request DTOs ──────────────────────────────────────────────────

internal sealed record AskQuestionRequest(
    string Question,
    Guid TenantId,
    IReadOnlyCollection<Guid>? DocumentIds,
    int TopK,
    string? Model
);

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
