using OpenRAG.Infrastructure.Vector;

namespace OpenRAG.UnitTests.Infrastructure.Vector;

public sealed class CosineSimilarityTests
{
    [Fact]
    public void Identical_vectors_return_one()
    {
        var vec = new float[] { 0.5f, 0.5f, 0.5f, 0.5f };
        var result = EfVectorSearchService.CosineSimilarity(vec, vec);

        Assert.Equal(1.0, result, 6);
    }

    [Fact]
    public void Orthogonal_vectors_return_zero()
    {
        var a = new float[] { 1f, 0f, 0f, 0f };
        var b = new float[] { 0f, 1f, 0f, 0f };

        var result = EfVectorSearchService.CosineSimilarity(a, b);

        Assert.Equal(0.0, result, 6);
    }

    [Fact]
    public void Opposite_vectors_return_negative_one()
    {
        var a = new float[] { 1f, 0f, 0f, 0f };
        var b = new float[] { -1f, 0f, 0f, 0f };

        var result = EfVectorSearchService.CosineSimilarity(a, b);

        Assert.Equal(-1.0, result, 6);
    }

    [Fact]
    public void Similar_vectors_return_high_score()
    {
        var a = new float[] { 0.7f, 0.7f, 0.1f, 0.1f };
        var b = new float[] { 0.6f, 0.8f, 0.1f, 0.1f };

        var result = EfVectorSearchService.CosineSimilarity(a, b);

        Assert.True(result > 0.9, $"Expected > 0.9 but got {result}");
    }

    [Fact]
    public void Dissimilar_vectors_return_low_score()
    {
        var a = new float[] { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
        var b = new float[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f };

        var result = EfVectorSearchService.CosineSimilarity(a, b);

        Assert.Equal(0.0, result, 6);
    }

    [Fact]
    public void Zero_vector_returns_zero()
    {
        var a = new float[] { 0f, 0f, 0f, 0f };
        var b = new float[] { 1f, 0f, 0f, 0f };

        var result = EfVectorSearchService.CosineSimilarity(a, b);

        Assert.Equal(0.0, result, 6);
    }

    [Fact]
    public void Throws_on_dimension_mismatch()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { 1f, 0f, 0f };

        Assert.Throws<ArgumentException>(() =>
            EfVectorSearchService.CosineSimilarity(a, b));
    }
}
