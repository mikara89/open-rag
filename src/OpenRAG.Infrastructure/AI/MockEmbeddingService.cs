using System.Security.Cryptography;
using System.Text;
using OpenRAG.Application.Abstractions.AI;

namespace OpenRAG.Infrastructure.AI;

/// <summary>
/// Mock embedding service that deterministically generates 8-dimensional vectors.
/// Uses SHA-256 hashing of input text to produce consistent vectors.
/// TODO: Replace with OpenAI-compatible embedding provider.
/// </summary>
public sealed class MockEmbeddingService : IEmbeddingService
{
    private const int Dimensions = 8;
    private const string Provider = "mock";
    private const string Model = "mock-embedding-8";
    private const string EmbeddingVersion = "v1";

    public Task<EmbeddingResult> GenerateEmbeddingAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        var vector = GenerateDeterministicVector(request.Input);

        var result = new EmbeddingResult(
            Vector: vector,
            Provider: Provider,
            Model: Model,
            Dimensions: Dimensions,
            EmbeddingVersion: EmbeddingVersion);

        return Task.FromResult(result);
    }

    private static float[] GenerateDeterministicVector(string input)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var vector = new float[Dimensions];

        for (var i = 0; i < Dimensions; i++)
        {
            // Use first 8 groups of 4 bytes each to generate floats in [-1, 1]
            var offset = i * 4;
            var value = (hashBytes[offset] << 24)
                        | (hashBytes[offset + 1] << 16)
                        | (hashBytes[offset + 2] << 8)
                        | hashBytes[offset + 3];

            // Normalize to [-1, 1]
            vector[i] = (value / (float)int.MaxValue) * 0.5f;
        }

        // Simple normalization to unit length
        var magnitude = MathF.Sqrt(vector.Sum(v => v * v));
        if (magnitude > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= magnitude;
            }
        }

        return vector;
    }
}
