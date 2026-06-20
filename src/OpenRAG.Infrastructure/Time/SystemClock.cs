using OpenRAG.Application.Abstractions.Time;

namespace OpenRAG.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
