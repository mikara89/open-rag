using OpenRAG.Application.Common.Results;

namespace OpenRAG.UnitTests.Application.Results;

public sealed class ResultTests
{
    [Fact]
    public void Success_contains_value_and_no_errors()
    {
        var result = Result<string>.Success("value");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal("value", result.Value);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Success_rejects_null_value()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string>.Success(null!));
    }

    [Fact]
    public void Failure_contains_errors_and_has_no_accessible_value()
    {
        var error = ApplicationErrors.ResourceNotFound();
        var result = Result<string>.Failure(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.PrimaryError);
        Assert.Equal([error], result.Errors);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Failure_without_errors_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Result<string>.Failure([]));
    }

    [Fact]
    public void External_mutation_cannot_change_stored_errors()
    {
        var original = ApplicationErrors.ResourceNotFound();
        var replacement = ApplicationErrors.ResourceConflict("document.processing", "Processing.");
        var errors = new[] { original };
        var result = Result<string>.Failure(errors);

        errors[0] = replacement;

        Assert.Equal(original, result.PrimaryError);
        Assert.IsAssignableFrom<IReadOnlyList<ApplicationError>>(result.Errors);
        Assert.False(result.Errors is ApplicationError[]);
    }

    [Fact]
    public void Match_selects_success_branch()
    {
        var result = Result<int>.Success(42);

        var matched = result.Match(
            value => $"success:{value}",
            _ => "failure");

        Assert.Equal("success:42", matched);
    }

    [Fact]
    public void Match_selects_failure_branch()
    {
        var result = Result<int>.Failure(ApplicationErrors.ResourceNotFound());

        var matched = result.Match(
            value => $"success:{value}",
            errors => $"failure:{errors[0].Code}");

        Assert.Equal("failure:resource.not_found", matched);
    }
}
