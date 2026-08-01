using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenRAG.Application;
using OpenRAG.Application.Common;
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
using OpenRAG.Application.Pipeline;
using OpenRAG.Application.Pipeline.Behaviors;
using OpenRAG.Application.Pipeline.Validation;
using OpenRAG.Application.Processing.ChunkDocument;
using OpenRAG.Application.Processing.GenerateEmbeddings;
using OpenRAG.Application.Processing.GenerateIntelligence;
using OpenRAG.Application.Processing.PreprocessDocument;
using OpenRAG.Application.Rag;
using OpenRAG.Application.Rag.AskQuestion;
using OpenRAG.Application.System.GetProvidersDiagnostics;

namespace OpenRAG.UnitTests.Application.Pipeline;

public sealed class MessageValidatorTests
{
    [Fact]
    public void Every_non_system_application_message_has_a_registered_validator()
    {
        var services = CreateServices();
        var requestTypes = typeof(OpenRAG.Application.AssemblyReference).Assembly
            .GetTypes()
            .Where(type => type.IsClass && typeof(IOpenRagMessage).IsAssignableFrom(type))
            .Where(type => type != typeof(GetProvidersDiagnosticsQuery))
            .ToArray();
        var validatedTypes = services
            .Where(descriptor => descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IMessageValidator<>))
            .Select(descriptor => descriptor.ServiceType.GetGenericArguments()[0])
            .ToHashSet();

        Assert.Equal(16, requestTypes.Length);
        Assert.All(requestTypes, type => Assert.Contains(type, validatedTypes));
        Assert.All(
            services.Where(descriptor => descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IMessageValidator<>)),
            descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));
    }

    [Fact]
    public async Task Registered_validators_cover_required_primitive_shape_rules()
    {
        var services = CreateServices();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;
        var id = Guid.NewGuid();

        await AssertInvalidAsync(scoped, new DeleteDocumentCommand(Guid.Empty), "DocumentId");
        await AssertInvalidAsync(scoped, new GetMarkdownArtifactQuery(id, Guid.Empty), "VersionId");
        await AssertInvalidAsync(scoped, new GetDocumentChunkQuery(id, id, Guid.Empty), "ChunkId");
        await AssertInvalidAsync(scoped, new ListDocumentsQuery(0, 20), "Page number");
        await AssertInvalidAsync(scoped, new ListDocumentChunksQuery(id, id, 1, 0), "Page size");
        await AssertInvalidAsync(scoped, new UploadDocumentCommand("", "text/plain", 1, Stream.Null, "corr"), "File name");
        await AssertInvalidAsync(scoped, new UploadDocumentCommand("a.txt", "text/plain", 0, Stream.Null, "corr"), "File size");
        await AssertInvalidAsync(scoped, new UploadDocumentCommand("a.txt", "text/plain", 1, null!, "corr"), "Content stream");
        await AssertInvalidAsync(scoped, new AskQuestionQuery("", null, 1, "model", "corr"), "Question");
        await AssertInvalidAsync(scoped, new AskQuestionQuery("question", null, 0, "model", "corr"), "TopK");
        await AssertInvalidAsync(scoped, new AskQuestionQuery("question", [Guid.Empty], 1, "model", "corr"), "Document IDs");
        await AssertInvalidAsync(scoped, new AskQuestionQuery("question", [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], 1, "model", "corr"), "more than 2");
        await AssertInvalidAsync(scoped, new ReprocessDocumentCommand(id, false, false, false, false, "corr"), "At least one");
        await AssertInvalidAsync(scoped, new PreprocessDocumentCommand(Guid.Empty, id, id, id, "corr"), "TenantId");
        await AssertInvalidAsync(scoped, new ChunkDocumentCommand(id, id, id, Guid.Empty, "corr"), "ProcessingRunId");
        await AssertInvalidAsync(scoped, new GenerateIntelligenceCommand(id, id, id, id, ""), "CorrelationId");
        await AssertInvalidAsync(scoped, new GenerateEmbeddingsCommand(id, Guid.Empty, id, id, "corr"), "DocumentId");
    }

    [Fact]
    public async Task Representative_valid_messages_pass_validation_and_execute_once()
    {
        var services = CreateServices();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var id = Guid.NewGuid();
        var validators = scope.ServiceProvider
            .GetServices<IMessageValidator<AskQuestionQuery>>();
        var behavior = new ValidationBehavior<AskQuestionQuery, string>(validators);
        var handlerCalls = 0;

        await behavior.Handle(
            new AskQuestionQuery("question", [id, id], 5, "model", "corr"),
            (_, _) =>
            {
                handlerCalls++;
                return ValueTask.FromResult("handled");
            },
            CancellationToken.None);

        Assert.Equal(1, handlerCalls);
    }

    [Fact]
    public async Task Upload_validator_preserves_existing_one_hundred_megabyte_limit()
    {
        var services = CreateServices();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;
        const long maximumUploadSizeBytes = 100L * 1024 * 1024;
        var validators = scoped.GetServices<IMessageValidator<UploadDocumentCommand>>();
        var behavior = new ValidationBehavior<UploadDocumentCommand, string>(validators);
        var handlerCalls = 0;

        await behavior.Handle(
            new UploadDocumentCommand(
                "document.pdf",
                "application/pdf",
                maximumUploadSizeBytes,
                Stream.Null,
                "corr"),
            (_, _) =>
            {
                handlerCalls++;
                return ValueTask.FromResult("handled");
            },
            CancellationToken.None);

        await AssertInvalidAsync(
            scoped,
            new UploadDocumentCommand(
                "document.pdf",
                "application/pdf",
                maximumUploadSizeBytes + 1,
                Stream.Null,
                "corr"),
            "maximum allowed size");

        Assert.Equal(1, handlerCalls);
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<RagOptions>>(
            Options.Create(new RagOptions { MaxDocumentFilterIds = 2 }));
        services.AddApplication();
        return services;
    }

    private static async Task AssertInvalidAsync<TMessage>(
        IServiceProvider serviceProvider,
        TMessage message,
        string expectedMessage)
        where TMessage : IOpenRagMessage
    {
        var behavior = new ValidationBehavior<TMessage, string>(
            serviceProvider.GetServices<IMessageValidator<TMessage>>());
        var handlerCalls = 0;

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => behavior.Handle(
                message,
                (_, _) =>
                {
                    handlerCalls++;
                    return ValueTask.FromResult("handled");
                },
                CancellationToken.None).AsTask());

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handlerCalls);
    }
}
