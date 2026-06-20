using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Infrastructure.AI;

namespace OpenRAG.UnitTests.Infrastructure.AI;

public sealed class MockEmbeddingServiceTests
{
    [Fact]
    public async Task Same_input_returns_same_vector()
    {
        var service = new MockEmbeddingService();
        var request1 = new EmbeddingRequest(Guid.NewGuid(), "hello world", "mock-embedding-8", "corr");
        var request2 = new EmbeddingRequest(Guid.NewGuid(), "hello world", "mock-embedding-8", "corr");

        var result1 = await service.GenerateEmbeddingAsync(request1);
        var result2 = await service.GenerateEmbeddingAsync(request2);

        Assert.Equal(result1.Vector.Count, result2.Vector.Count);
        for (var i = 0; i < result1.Vector.Count; i++)
        {
            Assert.Equal(result1.Vector[i], result2.Vector[i], 6);
        }
    }

    [Fact]
    public async Task Different_input_returns_different_vector()
    {
        var service = new MockEmbeddingService();
        var request1 = new EmbeddingRequest(Guid.NewGuid(), "hello world", "mock-embedding-8", "corr");
        var request2 = new EmbeddingRequest(Guid.NewGuid(), "goodbye world", "mock-embedding-8", "corr");

        var result1 = await service.GenerateEmbeddingAsync(request1);
        var result2 = await service.GenerateEmbeddingAsync(request2);

        var areDifferent = false;
        for (var i = 0; i < result1.Vector.Count; i++)
        {
            if (Math.Abs(result1.Vector[i] - result2.Vector[i]) > 0.0001f)
            {
                areDifferent = true;
                break;
            }
        }
        Assert.True(areDifferent, "Expected different inputs to produce different vectors.");
    }

    [Fact]
    public async Task Returns_8_dimensions()
    {
        var service = new MockEmbeddingService();
        var request = new EmbeddingRequest(Guid.NewGuid(), "test", "mock-embedding-8", "corr");

        var result = await service.GenerateEmbeddingAsync(request);

        Assert.Equal(8, result.Dimensions);
        Assert.Equal(8, result.Vector.Count);
    }

    [Fact]
    public async Task Returns_provider_model_version_metadata()
    {
        var service = new MockEmbeddingService();
        var request = new EmbeddingRequest(Guid.NewGuid(), "test", "mock-embedding-8", "corr");

        var result = await service.GenerateEmbeddingAsync(request);

        Assert.Equal("mock", result.Provider);
        Assert.Equal("mock-embedding-8", result.Model);
        Assert.Equal(8, result.Dimensions);
    }

    [Fact]
    public async Task Vector_is_normalized_to_unit_length()
    {
        var service = new MockEmbeddingService();
        var request = new EmbeddingRequest(Guid.NewGuid(), "test normalization", "mock-embedding-8", "corr");

        var result = await service.GenerateEmbeddingAsync(request);

        var magnitude = MathF.Sqrt(result.Vector.Sum(v => v * v));
        Assert.True(Math.Abs(magnitude - 1.0f) < 0.0001f,
            $"Expected unit vector but got magnitude {magnitude}");
    }
}
