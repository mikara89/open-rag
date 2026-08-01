namespace OpenRAG.Application.Common;

public sealed class IsolationViolationException : Exception
{
    public const string PublicMessage = "An internal isolation invariant was violated.";

    public IsolationViolationException(string message)
        : base(message)
    {
    }

    public IsolationViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
