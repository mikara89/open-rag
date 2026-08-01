using OpenRAG.Application.Common;

namespace OpenRAG.Infrastructure.Persistence.Repositories;

internal static class RepositoryIsolationGuard
{
    public static void NonEmpty(Guid value, string field)
    {
        if (value == Guid.Empty)
        {
            throw new IsolationViolationException(
                $"Repository isolation invariant failed: {field} is empty.");
        }
    }

    public static void Equal(Guid actual, Guid expected, string field)
    {
        NonEmpty(expected, $"expected {field}");
        if (actual == Guid.Empty || actual != expected)
        {
            throw new IsolationViolationException(
                $"Repository isolation invariant failed: {field} does not match the expected scope.");
        }
    }
}
