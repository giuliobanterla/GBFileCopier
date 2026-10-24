using System.Text.Json.Serialization;
using GBFileCopier.Core.Copying;
using GBFileCopier.Core.Hashing;

namespace GBFileCopier.App.Settings;

/// <summary>What the app remembers between runs.</summary>
public sealed record AppSettings
{
    /// <summary>Interface language ("en", "it"); empty to follow the system.</summary>
    public string Language { get; init; } = string.Empty;

    public string LastDestination { get; init; } = string.Empty;

    public bool Verify { get; init; }

    public HashAlgorithmKind HashAlgorithm { get; init; } = HashAlgorithmKind.XxHash3;

    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.Ask;

    public double WindowWidth { get; init; }

    public double WindowHeight { get; init; }
}

/// <summary>Source-generated serialization: no reflection, works with Native AOT.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
