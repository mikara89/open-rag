namespace OpenRAG.Application.Common;

public sealed class ResourceNotFoundException : Exception
{
    public const string PublicMessage = "The requested resource was not found.";

    public ResourceNotFoundException()
        : base(PublicMessage)
    {
    }
}
