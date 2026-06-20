using OpenRAG.Application.Abstractions.AI;

namespace OpenRAG.Infrastructure.AI;

/// <summary>
/// Placeholder chat completion service. Returns a fixed response.
/// TODO: Replace with real OpenAI/Azure chat completion service.
/// </summary>
public sealed class FakeChatCompletionService : IChatCompletionService
{
    public Task<ChatCompletionResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatCompletionResult(
            Content: "Placeholder response.",
            Provider: "Fake",
            Model: request.Model,
            InputTokens: null,
            OutputTokens: null));
    }
}
