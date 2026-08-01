using Mediator;
using OpenRAG.Api.Security;
using OpenRAG.Application;
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
using OpenRAG.Application.DTOs;
using OpenRAG.Application.Rag.AskQuestion;
using OpenRAG.Application.System.GetProvidersDiagnostics;
using OpenRAG.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddOpenRagAuthentication(builder.Configuration);
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api")
    .RequireAuthorization(OpenRagPolicies.AuthenticatedUser);

// ── Document endpoints ───────────────────────────────────────────

// List documents
api.MapGet("/documents", async (
    int? pageNumber,
    int? pageSize,
    string? status,
    string? search,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new ListDocumentsQuery(
        PageNumber: pageNumber ?? 1,
        PageSize: pageSize ?? 20,
        Status: status,
        Search: search);

    var response = await sender.Send(query, cancellationToken);
    return Results.Ok(response);
})
.WithName("ListDocuments");

// Upload document
api.MapPost("/documents/upload", async (
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

api.MapGet("/documents/{documentId:guid}/status", async (
    Guid documentId,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetDocumentStatusQuery(documentId);
    var response = await sender.Send(query, cancellationToken);
    return Results.Ok(response);
})
.WithName("GetDocumentStatus");

// ── Reprocess endpoint ───────────────────────────────────────────

api.MapPost("/documents/{documentId:guid}/reprocess", async (
    Guid documentId,
    ReprocessDocumentRequest request,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var correlationId = Guid.NewGuid().ToString("N");

    var command = new ReprocessDocumentCommand(
        DocumentId: documentId,
        ForcePreprocess: request.ForcePreprocess,
        ForceChunk: request.ForceChunk,
        ForceIntelligence: request.ForceIntelligence,
        ForceEmbeddings: request.ForceEmbeddings,
        CorrelationId: correlationId);

    var response = await sender.Send(command, cancellationToken);

    return Results.Accepted($"/api/documents/{response.DocumentId}/status", response);
})
.WithName("ReprocessDocument");

// ── Document detail endpoint ──────────────────────────────────────

api.MapGet("/documents/{documentId:guid}", async (
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

api.MapDelete("/documents/{documentId:guid}", async (
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

api.MapGet("/documents/{documentId:guid}/versions/{versionId:guid}/artifacts/markdown", async (
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

api.MapGet("/documents/{documentId:guid}/versions/{versionId:guid}/artifacts/json", async (
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

api.MapGet("/documents/{documentId:guid}/versions/{versionId:guid}/chunks", async (
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

api.MapGet("/documents/{documentId:guid}/versions/{versionId:guid}/chunks/{chunkId:guid}", async (
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

// ── Intelligence endpoint ─────────────────────────────────────────

api.MapGet("/documents/{documentId:guid}/versions/{versionId:guid}/intelligence", async (
    Guid documentId,
    Guid versionId,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetDocumentIntelligenceQuery(documentId, versionId);
    var response = await sender.Send(query, cancellationToken);

    if (response is null)
        return Results.NotFound(new { message = "No intelligence data for this version." });

    return Results.Ok(response);
})
.WithName("GetDocumentIntelligence");

// ── RAG endpoints ─────────────────────────────────────────────────

api.MapPost("/rag/ask", async (
    AskQuestionRequest request,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new AskQuestionQuery(
        Question: request.Question,
        FilterDocumentIds: request.DocumentIds?.Count > 0 ? request.DocumentIds : null,
        TopK: request.TopK > 0 ? request.TopK : null,
        Model: request.Model ?? "mock-chat",
        CorrelationId: Guid.NewGuid().ToString("N"));

    var response = await sender.Send(query, cancellationToken);
    return Results.Ok(response);
})
.WithName("AskQuestion");

// ── System diagnostics endpoint ──────────────────────────────────

api.MapGet("/system/providers", async (
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetProvidersDiagnosticsQuery();
    var response = await sender.Send(query, cancellationToken);
    return Results.Ok(response);
})
.WithName("GetProvidersDiagnostics")
.RequireAuthorization(OpenRagPolicies.Administrator);

app.Run();

// ── Request DTOs ──────────────────────────────────────────────────

internal sealed record AskQuestionRequest(
    string Question,
    IReadOnlyCollection<Guid>? DocumentIds,
    int TopK,
    string? Model
);

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
