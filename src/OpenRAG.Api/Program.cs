using Mediator;
using OpenRAG.Application;
using OpenRAG.Application.Documents.GetDocumentStatus;
using OpenRAG.Application.Documents.UploadDocument;
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
