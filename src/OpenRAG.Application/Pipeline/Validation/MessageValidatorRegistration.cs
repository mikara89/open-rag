using Microsoft.Extensions.DependencyInjection;
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
using OpenRAG.Application.Processing.ChunkDocument;
using OpenRAG.Application.Processing.GenerateEmbeddings;
using OpenRAG.Application.Processing.GenerateIntelligence;
using OpenRAG.Application.Processing.PreprocessDocument;
using OpenRAG.Application.Rag.AskQuestion;

namespace OpenRAG.Application.Pipeline.Validation;

internal static class MessageValidatorRegistration
{
    internal static IServiceCollection AddOpenRagMessageValidators(
        this IServiceCollection services)
    {
        services.AddScoped<IMessageValidator<UploadDocumentCommand>, UploadDocumentCommandValidator>();
        services.AddScoped<IMessageValidator<DeleteDocumentCommand>, DeleteDocumentCommandValidator>();
        services.AddScoped<IMessageValidator<ReprocessDocumentCommand>, ReprocessDocumentCommandValidator>();
        services.AddScoped<IMessageValidator<ListDocumentsQuery>, ListDocumentsQueryValidator>();
        services.AddScoped<IMessageValidator<GetDocumentDetailQuery>, GetDocumentDetailQueryValidator>();
        services.AddScoped<IMessageValidator<GetDocumentStatusQuery>, GetDocumentStatusQueryValidator>();
        services.AddScoped<IMessageValidator<GetMarkdownArtifactQuery>, GetMarkdownArtifactQueryValidator>();
        services.AddScoped<IMessageValidator<GetJsonArtifactQuery>, GetJsonArtifactQueryValidator>();
        services.AddScoped<IMessageValidator<ListDocumentChunksQuery>, ListDocumentChunksQueryValidator>();
        services.AddScoped<IMessageValidator<GetDocumentChunkQuery>, GetDocumentChunkQueryValidator>();
        services.AddScoped<IMessageValidator<GetDocumentIntelligenceQuery>, GetDocumentIntelligenceQueryValidator>();
        services.AddScoped<IMessageValidator<AskQuestionQuery>, AskQuestionQueryValidator>();
        services.AddScoped<IMessageValidator<PreprocessDocumentCommand>, PreprocessDocumentCommandValidator>();
        services.AddScoped<IMessageValidator<ChunkDocumentCommand>, ChunkDocumentCommandValidator>();
        services.AddScoped<IMessageValidator<GenerateIntelligenceCommand>, GenerateIntelligenceCommandValidator>();
        services.AddScoped<IMessageValidator<GenerateEmbeddingsCommand>, GenerateEmbeddingsCommandValidator>();

        return services;
    }
}
