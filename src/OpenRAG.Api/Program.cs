using Mediator;
using Microsoft.AspNetCore.Mvc;
using OpenRAG.Api.Errors;
using OpenRAG.Api.Results;
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
using OpenRAG.Application.Pipeline;
using OpenRAG.Application.Rag.AskQuestion;
using OpenRAG.Application.System.GetProvidersDiagnostics;
using OpenRAG.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddOpenRagMediatorPipelines(OpenRagPipelineHost.Api);
builder.Services.AddOpenRagAuthentication(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<OpenRagExceptionHandler>();
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
app.UseExceptionHandler();
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
    HttpContext httpContext,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new ListDocumentsQuery(
        PageNumber: pageNumber ?? 1,
        PageSize: pageSize ?? 20,
        Status: status,
        Search: search);

    var response = await sender.Send(query, cancellationToken);
    return response.ToHttpResult(httpContext, Results.Ok);
})
.WithName("ListDocuments")
.Produces<ListDocumentsResponse>()
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

// Upload document
api.MapPost("/documents/upload", async (
    IFormFile file,
    HttpContext httpContext,
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

    return response.ToHttpResult(
        httpContext,
        value => Results.Created($"/api/documents/{value.DocumentId}/status", value));
})
.WithName("UploadDocument")
.Produces<UploadDocumentResponse>(StatusCodes.Status201Created)
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
.DisableAntiforgery();

api.MapGet("/documents/{documentId:guid}/status", async (
    Guid documentId,
    HttpContext httpContext,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetDocumentStatusQuery(documentId);
    var response = await sender.Send(query, cancellationToken);
    return response.ToHttpResult(httpContext, Results.Ok);
})
.WithName("GetDocumentStatus")
.Produces<GetDocumentStatusResponse>()
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
.Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

// ── Reprocess endpoint ───────────────────────────────────────────

api.MapPost("/documents/{documentId:guid}/reprocess", async (
    Guid documentId,
    ReprocessDocumentRequest request,
    HttpContext httpContext,
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

    return response.ToHttpResult(
        httpContext,
        value => Results.Accepted($"/api/documents/{value.DocumentId}/status", value));
})
.WithName("ReprocessDocument")
.Produces<ReprocessDocumentResponse>(StatusCodes.Status202Accepted)
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
.Produces<ProblemDetails>(StatusCodes.Status409Conflict)
.Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

// ── Document detail endpoint ──────────────────────────────────────

api.MapGet("/documents/{documentId:guid}", async (
    Guid documentId,
    HttpContext httpContext,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetDocumentDetailQuery(documentId);
    var response = await sender.Send(query, cancellationToken);
    return response.ToHttpResult(httpContext, Results.Ok);
})
.WithName("GetDocumentDetail")
.Produces<GetDocumentDetailResponse>()
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
.Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

// ── Delete document endpoint ──────────────────────────────────────

api.MapDelete("/documents/{documentId:guid}", async (
    Guid documentId,
    HttpContext httpContext,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var command = new DeleteDocumentCommand(documentId);
    var response = await sender.Send(command, cancellationToken);
    return response.ToHttpResult(httpContext, _ => Results.NoContent());
})
.WithName("DeleteDocument")
.Produces(StatusCodes.Status204NoContent)
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
.Produces<ProblemDetails>(StatusCodes.Status409Conflict)
.Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

// ── Artifact preview endpoints ────────────────────────────────────

api.MapGet("/documents/{documentId:guid}/versions/{versionId:guid}/artifacts/markdown", async (
    Guid documentId,
    Guid versionId,
    HttpContext httpContext,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetMarkdownArtifactQuery(documentId, versionId);
    var response = await sender.Send(query, cancellationToken);
    return response.ToHttpResult(
        httpContext,
        value => Results.Text(value.Content, value.ContentType));
})
.WithName("GetMarkdownArtifact")
.Produces<string>(StatusCodes.Status200OK, "text/markdown")
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
.Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

api.MapGet("/documents/{documentId:guid}/versions/{versionId:guid}/artifacts/json", async (
    Guid documentId,
    Guid versionId,
    HttpContext httpContext,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetJsonArtifactQuery(documentId, versionId);
    var response = await sender.Send(query, cancellationToken);
    return response.ToHttpResult(
        httpContext,
        value => Results.Text(value.Content, value.ContentType));
})
.WithName("GetJsonArtifact")
.Produces<string>(StatusCodes.Status200OK, "application/json")
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
.Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

// ── Chunk endpoints ───────────────────────────────────────────────

api.MapGet("/documents/{documentId:guid}/versions/{versionId:guid}/chunks", async (
    Guid documentId,
    Guid versionId,
    int? pageNumber,
    int? pageSize,
    string? search,
    string? sectionTitle,
    int? pageNumberFilter,
    HttpContext httpContext,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new ListDocumentChunksQuery(
        documentId, versionId,
        pageNumber ?? 1, pageSize ?? 20,
        search, sectionTitle, pageNumberFilter);
    var response = await sender.Send(query, cancellationToken);
    return response.ToHttpResult(httpContext, Results.Ok);
})
.WithName("ListDocumentChunks")
.Produces<ListDocumentChunksResponse>()
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
.Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

api.MapGet("/documents/{documentId:guid}/versions/{versionId:guid}/chunks/{chunkId:guid}", async (
    Guid documentId,
    Guid versionId,
    Guid chunkId,
    HttpContext httpContext,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetDocumentChunkQuery(documentId, versionId, chunkId);
    var response = await sender.Send(query, cancellationToken);
    return response.ToHttpResult(httpContext, Results.Ok);
})
.WithName("GetDocumentChunk")
.Produces<GetDocumentChunkResponse>()
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
.Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

// ── Intelligence endpoint ─────────────────────────────────────────

api.MapGet("/documents/{documentId:guid}/versions/{versionId:guid}/intelligence", async (
    Guid documentId,
    Guid versionId,
    HttpContext httpContext,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new GetDocumentIntelligenceQuery(documentId, versionId);
    var response = await sender.Send(query, cancellationToken);
    return response.ToHttpResult(httpContext, Results.Ok);
})
.WithName("GetDocumentIntelligence")
.Produces<DocumentIntelligenceResponse>()
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
.Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

// ── RAG endpoints ─────────────────────────────────────────────────

api.MapPost("/rag/ask", async (
    AskQuestionRequest request,
    HttpContext httpContext,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new AskQuestionQuery(
        Question: request.Question,
        FilterDocumentIds: request.DocumentIds?.Count > 0 ? request.DocumentIds : null,
        TopK: request.TopK,
        Model: request.Model ?? "mock-chat",
        CorrelationId: Guid.NewGuid().ToString("N"));

    var response = await sender.Send(query, cancellationToken);
    return response.ToHttpResult(httpContext, Results.Ok);
})
.WithName("AskQuestion")
.Produces<AskQuestionResponse>()
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status404NotFound)
.Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

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
    int? TopK,
    string? Model
);

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
