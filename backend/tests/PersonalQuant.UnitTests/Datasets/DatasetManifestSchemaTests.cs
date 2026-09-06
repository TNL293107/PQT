using System.Text.Json;
using PersonalQuant.Application.Datasets;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Infrastructure.Datasets;

namespace PersonalQuant.UnitTests.Datasets;

/// <summary>
/// Holds the written manifest and the published JSON Schema to each other.
/// </summary>
/// <remarks>
/// <para>
/// The schema is the contract the Python layer validates against on load, which
/// is what keeps the two languages from drifting apart — the concern ADR-004
/// raised and left open. A schema nothing checks is a document, and a document
/// is exactly what drifts.
/// </para>
/// <para>
/// This compares names rather than validating instances, which needs no schema
/// library. It catches the failure that actually happens: a field added to the
/// record and not to the schema, or renamed on one side only.
/// </para>
/// </remarks>
public sealed class DatasetManifestSchemaTests
{
    [Fact]
    public void The_written_manifest_carries_exactly_the_properties_the_schema_declares()
    {
        var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath()));
        var declared = schema.RootElement
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var written = Written().EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(declared.Order(StringComparer.Ordinal), written.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_property_the_schema_requires_is_actually_written()
    {
        // Separate from the names test because "declared" and "required" are
        // different lists, and a field the schema requires but nothing writes
        // makes every manifest invalid on load.
        var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath()));
        var required = schema.RootElement
            .GetProperty("required")
            .EnumerateArray()
            .Select(entry => entry.GetString()!)
            .ToList();

        var written = Written();

        foreach (var name in required)
        {
            Assert.True(
                written.TryGetProperty(name, out _),
                $"The schema requires '{name}' and the manifest does not write it.");
        }
    }

    [Fact]
    public void The_schema_version_the_code_writes_is_the_one_the_schema_pins()
    {
        // The schema is versioned per shape, so a bump on one side without the
        // other produces a file that validates against nothing.
        var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath()));
        var pinned = schema.RootElement
            .GetProperty("properties")
            .GetProperty("schema_version")
            .GetProperty("const")
            .GetInt32();

        Assert.Equal(DatasetManifest.CurrentSchemaVersion, pinned);
    }

    [Fact]
    public void The_policy_is_written_in_the_spelling_the_schema_accepts()
    {
        // Lowercase, the same vocabulary as the API query string and the
        // operator CLI. A manifest that said "Strict" would be one more thing
        // to translate, and would fail the schema's enum.
        Assert.Equal("strict", Written().GetProperty("announcement_policy").GetString());
    }

    /// <summary>Serialises a manifest exactly as the store writes one.</summary>
    private static JsonElement Written()
    {
        var manifest = new DatasetManifest(
            "0123456789abcdef",
            1,
            DatasetManifest.CurrentSchemaVersion,
            new string('0', 64),
            new DateTimeOffset(2016, 5, 27, 0, 0, 0, TimeSpan.Zero),
            true,
            AnnouncementPolicy.Strict,
            "VN30",
            new DateOnly(2016, 12, 31),
            [new DatasetInstrument(Guid.Empty, "FPT", "HOSE")],
            "1d",
            new DateOnly(2016, 1, 1),
            new DateOnly(2016, 12, 31),
            1,
            1,
            1,
            [new DatasetSource("CAFEF", IDatasetLicenceRegistry.UnstatedNote)],
            [new DatasetFile("bars.parquet", 5, 1_024, new string('a', 64))],
            new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero),
            null);

        var json = JsonSerializer.Serialize(manifest, ParquetDatasetStore.ManifestJson);

        return JsonDocument.Parse(json).RootElement;
    }

    /// <summary>
    /// Walks up from the test binary to the published schema.
    /// </summary>
    /// <remarks>
    /// The schema lives in <c>docs/</c> because it is a published contract
    /// rather than a build input; a copy embedded in the assembly would be the
    /// one that drifts.
    /// </remarks>
    private static string SchemaPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "docs", "schemas", "dataset-manifest-v1.schema.json");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "dataset-manifest-v1.schema.json was not found above the test binary.");
    }
}
