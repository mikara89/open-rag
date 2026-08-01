namespace OpenRAG.Application.Pipeline;

internal static class OpenRagMessageMetadata
{
    internal static string Name<TMessage>() => typeof(TMessage).Name;

    internal static string Category<TMessage>()
    {
        if (typeof(IOpenRagCommand).IsAssignableFrom(typeof(TMessage)))
            return "command";

        if (typeof(IOpenRagQuery).IsAssignableFrom(typeof(TMessage)))
            return "query";

        return "message";
    }
}
