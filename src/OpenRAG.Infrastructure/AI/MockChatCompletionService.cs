using OpenRAG.Application.Abstractions.AI;

namespace OpenRAG.Infrastructure.AI;

/// <summary>
/// Mock chat completion service for MVP.
/// Returns a deterministic answer based on the provided context.
/// TODO: Replace with real OpenAI/Azure chat completion service.
/// </summary>
public sealed class MockChatCompletionService : IChatCompletionService
{
    public Task<ChatCompletionResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var lastUserMessage = request.Messages.LastOrDefault(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));

        var questionPart = lastUserMessage?.Content ?? "the question";

        // Build a mock answer that shows the RAG flow works
        var answer = $"Mock answer based on the retrieved context. " +
                     $"The system analyzed the question and found relevant document excerpts. " +
                     $"The most relevant source discusses: \"{Truncate(questionPart, 100)}\". " +
                     $"This is a mock response for MVP validation.";

        var result = new ChatCompletionResult(
            Content: answer,
            Provider: "mock",
            Model: request.Model,
            InputTokens: answer.Length / 4,
            OutputTokens: answer.Length / 4);

        return Task.FromResult(result);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        return text[..(maxLength - 3)] + "...";
    }
}
