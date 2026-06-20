namespace OpenRAG.Application.Abstractions.AI;

public sealed record ChatMessageDto(
    string Role,
    string Content
);

public sealed record ChatCompletionRequest(
    Guid TenantId,
    IReadOnlyList<ChatMessageDto> Messages,
    string Model,
    string CorrelationId
);

public sealed record ChatCompletionResult(
    string Content,
    string Provider,
    string Model,
    int? InputTokens,
    int? OutputTokens
);

public interface IChatCompletionService
{
    Task<ChatCompletionResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}
