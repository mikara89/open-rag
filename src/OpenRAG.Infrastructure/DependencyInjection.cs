using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Abstractions.Vector;
using OpenRAG.Application.Rag;
using OpenRAG.Infrastructure.AI;
using OpenRAG.Infrastructure.Messaging;
using OpenRAG.Infrastructure.Persistence;
using OpenRAG.Infrastructure.Persistence.Repositories;
using OpenRAG.Infrastructure.Preprocessing;
using OpenRAG.Infrastructure.Processing;
using OpenRAG.Infrastructure.Security;
using OpenRAG.Infrastructure.Storage;
using OpenRAG.Infrastructure.Time;
using OpenRAG.Infrastructure.VectorSearch;
using Pgvector.EntityFrameworkCore;

namespace OpenRAG.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers all Infrastructure services including EF Core with PostgreSQL,
    /// CAP with PostgreSQL storage and RabbitMQ transport.
    /// Uses connection strings "openrag-db" and "rabbitmq" from configuration
    /// (Aspire-injected or appsettings fallback).
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("openrag-db");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'openrag-db' was not found.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.UseVector()));

        // Persistence
        services.AddScoped<IDocumentRepository, EfDocumentRepository>();
        services.AddScoped<IProcessingRunRepository, EfProcessingRunRepository>();
        services.AddScoped<IDocumentChunkRepository, EfDocumentChunkRepository>();
        services.AddScoped<IDocumentEmbeddingRepository, EfDocumentEmbeddingRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Storage
        services.Configure<LocalFileStorageOptions>(
            configuration.GetSection(LocalFileStorageOptions.SectionName));
        services.AddSingleton<IValidateOptions<LocalFileStorageOptions>, LocalFileStorageOptionsValidator>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        // CAP with PostgreSQL storage + RabbitMQ transport
        // Aspire injects ConnectionStrings:rabbitmq via WithReference(rabbitmq).
        // Fall back to environment variable, then default localhost.
        var rabbitMqConnection = configuration.GetConnectionString("rabbitmq")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__rabbitmq")
            ?? "amqp://guest:guest@localhost:5672/";

        services.AddCap(options =>
        {
            options.UsePostgreSql(connectionString);
            options.UseRabbitMQ(rabbitMqOptions =>
            {
                rabbitMqOptions.ConnectionFactoryOptions = factory =>
                {
                    factory.Uri = new Uri(rabbitMqConnection);
                };
            });

            options.FailedRetryCount = 3;
            options.ConsumerThreadCount = 2;
        });

        // Messaging — CAP-backed event bus
        services.AddSingleton<IDocumentEventBus, CapDocumentEventBus>();

        // Security (development placeholders)
        services.AddScoped<ICurrentTenant, DevelopmentCurrentTenant>();
        services.AddScoped<ICurrentUser, DevelopmentCurrentUser>();

        // Preprocessing — provider selection via configuration
        services.Configure<DoclingPreprocessorOptions>(
            configuration.GetSection(DoclingPreprocessorOptions.SectionName));
        services.AddSingleton<IValidateOptions<DoclingPreprocessorOptions>, DoclingPreprocessorOptionsValidator>();

        var preprocessorProvider = configuration["Preprocessing:Docling:Provider"] ?? "Mock";

        if (string.Equals(preprocessorProvider, "DoclingServe", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<DoclingServeDocumentPreprocessor>(client =>
            {
                var timeoutSeconds = configuration.GetValue<int>("Preprocessing:Docling:TimeoutSeconds", 300);
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            });

            // Override BaseUrl from Aspire-injected environment variable if available
            services.PostConfigure<DoclingPreprocessorOptions>(options =>
            {
                var doclingUrl = Environment.GetEnvironmentVariable("DOCLING_BASE_URL");
                if (!string.IsNullOrWhiteSpace(doclingUrl))
                {
                    options.BaseUrl = doclingUrl;
                }
            });

            services.AddScoped<IDocumentPreprocessor>(sp =>
                sp.GetRequiredService<DoclingServeDocumentPreprocessor>());
        }
        else
        {
            // Default: Mock for local dev and tests
            services.AddScoped<IDocumentPreprocessor, MockDocumentPreprocessor>();
        }

        // Chunking — provider selection via configuration
        services.Configure<ChunkingOptions>(
            configuration.GetSection(ChunkingOptions.SectionName));
        services.AddSingleton<IValidateOptions<ChunkingOptions>, ChunkingOptionsValidator>();

        var chunkingProvider = configuration["Chunking:Provider"] ?? "DoclingJson";

        services.AddScoped<SimpleMarkdownChunker>();

        if (string.Equals(chunkingProvider, "DoclingJson", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IDocumentChunker, DoclingJsonAwareChunker>();
        }
        else
        {
            services.AddScoped<IDocumentChunker>(sp =>
                sp.GetRequiredService<SimpleMarkdownChunker>());
        }

        // Time
        services.AddSingleton<IClock, SystemClock>();

        // AI — Embedding provider selection via configuration
        services.Configure<OpenAiCompatibleEmbeddingOptions>(
            configuration.GetSection(OpenAiCompatibleEmbeddingOptions.SectionName));
        services.AddSingleton<IValidateOptions<OpenAiCompatibleEmbeddingOptions>, OpenAiCompatibleEmbeddingOptionsValidator>();

        // Configure Application-level embedding options from same section
        services.Configure<OpenRAG.Application.Processing.GenerateEmbeddings.GenerateEmbeddingsOptions>(
            configuration.GetSection(OpenAiCompatibleEmbeddingOptions.SectionName));

        var embeddingProvider = configuration["AI:Embeddings:Provider"] ?? "Mock";

        if (string.Equals(embeddingProvider, "OpenAICompatible", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<OpenAiCompatibleEmbeddingService>(client =>
            {
                var timeoutSeconds = configuration.GetValue<int>("AI:Embeddings:TimeoutSeconds", 120);
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            });
            services.AddScoped<IEmbeddingService>(sp =>
                sp.GetRequiredService<OpenAiCompatibleEmbeddingService>());
        }
        else
        {
            // Default: Mock for local dev and tests
            services.AddSingleton<IEmbeddingService, MockEmbeddingService>();
        }

        // Chat completion provider selection via configuration
        services.Configure<OpenAiCompatibleChatOptions>(
            configuration.GetSection(OpenAiCompatibleChatOptions.SectionName));
        services.AddSingleton<IValidateOptions<OpenAiCompatibleChatOptions>, OpenAiCompatibleChatOptionsValidator>();

        var chatProvider = configuration["AI:Chat:Provider"] ?? "Mock";

        if (string.Equals(chatProvider, "OpenAICompatible", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<OpenAiCompatibleChatCompletionService>(client =>
            {
                var timeoutSeconds = configuration.GetValue<int>("AI:Chat:TimeoutSeconds", 120);
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            });
            services.AddScoped<IChatCompletionService>(sp =>
                sp.GetRequiredService<OpenAiCompatibleChatCompletionService>());
        }
        else
        {
            // Default: Mock for local dev and tests
            services.AddSingleton<IChatCompletionService, MockChatCompletionService>();
        }

        // RAG options
        services.Configure<RagOptions>(
            configuration.GetSection(RagOptions.SectionName));

        // Vector search (pgvector-backed EF Core implementation)
        services.AddScoped<IVectorSearchService, EfVectorSearchService>();

        return services;
    }
}
