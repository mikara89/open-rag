using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Infrastructure.AI;

namespace OpenRAG.UnitTests.Infrastructure.AI;

public sealed class MockChatCompletionServiceTests
{
    [Fact]
    public async Task Returns_deterministic_answer()
    {
        var service = new MockChatCompletionService();
        var messages = new List<ChatMessageDto>
        {
            new("system", "You are helpful."),
            new("user", "What is RAG?")
        };

        var request = new ChatCompletionRequest(
            Guid.NewGuid(), messages, "mock-chat", "corr");

        var result = await service.CompleteAsync(request);

        Assert.False(string.IsNullOrWhiteSpace(result.Content));
        Assert.Contains("Mock answer", result.Content);
    }

    [Fact]
    public async Task Returns_provider_model_metadata()
    {
        var service = new MockChatCompletionService();
        var messages = new List<ChatMessageDto>
        {
            new("user", "Hello")
        };

        var request = new ChatCompletionRequest(
            Guid.NewGuid(), messages, "mock-chat", "corr");

        var result = await service.CompleteAsync(request);

        Assert.Equal("mock", result.Provider);
        Assert.Equal("mock-chat", result.Model);
    }

    [Fact]
    public async Task Does_not_throw_for_empty_messages()
    {
        var service = new MockChatCompletionService();
        var request = new ChatCompletionRequest(
            Guid.NewGuid(), Array.Empty<ChatMessageDto>(), "mock-chat", "corr");

        // Should not throw
        var result = await service.CompleteAsync(request);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Content));
    }

    [Fact]
    public async Task Provides_positive_token_counts()
    {
        var service = new MockChatCompletionService();
        var messages = new List<ChatMessageDto>
        {
            new("user", "Test question.")
        };

        var request = new ChatCompletionRequest(
            Guid.NewGuid(), messages, "mock-chat", "corr");

        var result = await service.CompleteAsync(request);

        Assert.NotNull(result.InputTokens);
        Assert.True(result.InputTokens > 0);
        Assert.NotNull(result.OutputTokens);
        Assert.True(result.OutputTokens > 0);
    }
}
