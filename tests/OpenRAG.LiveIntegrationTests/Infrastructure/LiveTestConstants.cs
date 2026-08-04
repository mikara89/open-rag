namespace OpenRAG.LiveIntegrationTests.Infrastructure;

internal static class LiveTestConstants
{
    public const string PostgreSqlImage = "pgvector/pgvector:0.8.2-pg17-bookworm";
    public const string PostgreSqlVersion = "17";
    public const string PgvectorVersion = "0.8.2";
    public const string TenantAMarker = "TENANT_A_ONLY_CONTENT";
    public const string TenantBMarker = "TENANT_B_ONLY_CONTENT";
    public const string EmbeddingProvider = "live-deterministic";
    public const string EmbeddingModel = "live-3d";
    public const string EmbeddingVersion = "v1";
    public const int EmbeddingDimensions = 3;

    public static readonly Guid TenantA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid TenantB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid UserB = new("22222222-2222-2222-2222-222222222222");
}

internal sealed record LiveTestIds(
    Guid DocumentA1,
    Guid DocumentA2,
    Guid DocumentB1,
    Guid VersionA1,
    Guid VersionA2,
    Guid VersionB1,
    Guid ChunkA1,
    Guid ChunkA2,
    Guid ChunkB1,
    Guid EmbeddingA1,
    Guid EmbeddingA2,
    Guid EmbeddingB1,
    Guid IntelligenceA1,
    Guid IntelligenceB1,
    Guid RunA1,
    Guid RunB1,
    Guid StepA1,
    Guid StepB1)
{
    public static LiveTestIds ForScenario(int scenario)
    {
        if (scenario is < 1 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(scenario));

        return new LiveTestIds(
            Id(0x10, scenario, 1),
            Id(0x10, scenario, 2),
            Id(0x20, scenario, 1),
            Id(0x30, scenario, 1),
            Id(0x30, scenario, 2),
            Id(0x40, scenario, 1),
            Id(0x50, scenario, 1),
            Id(0x50, scenario, 2),
            Id(0x60, scenario, 1),
            Id(0x70, scenario, 1),
            Id(0x70, scenario, 2),
            Id(0x80, scenario, 1),
            Id(0x90, scenario, 1),
            Id(0x91, scenario, 1),
            Id(0xa0, scenario, 1),
            Id(0xb0, scenario, 1),
            Id(0xc0, scenario, 1),
            Id(0xd0, scenario, 1));
    }

    private static Guid Id(byte category, int scenario, int item)
    {
        Span<byte> bytes = stackalloc byte[16];
        bytes[0] = category;
        BitConverter.TryWriteBytes(bytes[8..12], scenario);
        BitConverter.TryWriteBytes(bytes[12..16], item);
        return new Guid(bytes);
    }
}
