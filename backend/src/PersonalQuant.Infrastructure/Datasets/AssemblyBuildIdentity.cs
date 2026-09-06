using System.Reflection;
using PersonalQuant.Application.Datasets;

namespace PersonalQuant.Infrastructure.Datasets;

/// <summary>
/// Reads the build's source revision from its own assembly metadata.
/// </summary>
/// <remarks>
/// <para>
/// The SDK appends <c>+{SourceRevisionId}</c> to the informational version when
/// the build supplies one, which the container image does and a bare
/// <c>dotnet run</c> does not.
/// </para>
/// <para>
/// A build that recorded none reports <see langword="null"/> rather than a
/// placeholder. A dataset that claims a commit it cannot know is worse than one
/// that says it does not know: the first is a fact a reader will act on, the
/// second is a gap they will go and fill.
/// </para>
/// </remarks>
internal sealed class AssemblyBuildIdentity : IBuildIdentity
{
    /// <inheritdoc />
    public string? Commit { get; } = Read();

    private static string? Read()
    {
        var version = typeof(AssemblyBuildIdentity).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (version is null)
        {
            return null;
        }

        var plus = version.IndexOf('+', StringComparison.Ordinal);

        return plus >= 0 && plus < version.Length - 1 ? version[(plus + 1)..] : null;
    }
}
