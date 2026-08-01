namespace OpenRAG.Application.Common;

public static class IsolationGuard
{
    public static void Equal(Guid actual, Guid expected, string field)
    {
        if (expected == Guid.Empty || actual == Guid.Empty || actual != expected)
        {
            throw new IsolationViolationException(
                $"Isolation invariant failed: {field} does not match the trusted scope.");
        }
    }

    public static void NonEmpty(Guid value, string field)
    {
        if (value == Guid.Empty)
        {
            throw new IsolationViolationException(
                $"Isolation invariant failed: {field} is empty.");
        }
    }
}
