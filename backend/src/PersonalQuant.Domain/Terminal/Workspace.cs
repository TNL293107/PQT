using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Terminal;

/// <summary>Identifies a workspace.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct WorkspaceId(Guid Value)
{
    /// <summary>Creates a new identifier, ordered by creation time.</summary>
    /// <returns>The identifier.</returns>
    public static WorkspaceId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A named set of console lines the operator replays to rebuild a view.
/// </summary>
/// <remarks>
/// <para>
/// A workspace stores what was asked, never what was answered: replaying
/// <c>FPT GP</c> tomorrow draws tomorrow's series. A saved answer would be a
/// snapshot of data that has since been revised, presented as current.
/// </para>
/// <para>
/// The lines are kept as typed and are not parsed here. What a line means is
/// the console's business, and a workspace saved before a function existed
/// should replay into a refusal that says so, not fail to load.
/// </para>
/// </remarks>
public sealed class Workspace
{
    /// <summary>The most lines one workspace may hold.</summary>
    public const int MaxEntries = 50;

    /// <summary>The longest line accepted.</summary>
    public const int MaxEntryLength = 256;

    private string[] _entries = [];

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private Workspace() => Name = null!;

    private Workspace(WorkspaceId id, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    /// <summary>Gets the workspace's identifier.</summary>
    public WorkspaceId Id { get; private set; }

    /// <summary>Gets the workspace's name, unique across workspaces.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the console lines, in replay order.</summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Gets when the workspace was first saved.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Gets when it was last saved.</summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Creates a workspace holding the given lines.</summary>
    /// <param name="name">Its name, under the same rule as a watchlist's.</param>
    /// <param name="entries">The console lines.</param>
    /// <param name="createdAtUtc">When, in UTC.</param>
    /// <returns>The workspace.</returns>
    /// <exception cref="DomainValidationException">A rule is broken.</exception>
    public static Workspace Create(string name, IReadOnlyList<string> entries, DateTimeOffset createdAtUtc)
    {
        if (!Watchlist.IsValidName(name))
        {
            throw new DomainValidationException(
                $"'{name}' is not a workspace name: lowercase letters, digits, _ and -, "
                    + $"starting with a letter or digit, at most {Watchlist.MaxNameLength} characters.");
        }

        var workspace = new Workspace(WorkspaceId.New(), name, createdAtUtc);
        workspace.Replace(entries, createdAtUtc);

        return workspace;
    }

    /// <summary>Replaces the workspace's lines.</summary>
    /// <param name="entries">The console lines.</param>
    /// <param name="savedAtUtc">When, in UTC.</param>
    /// <exception cref="DomainValidationException">A rule is broken.</exception>
    public void Replace(IReadOnlyList<string> entries, DateTimeOffset savedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (savedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Timestamps must be UTC, but the offset was {savedAtUtc.Offset}.");
        }

        // Blank lines carry nothing to replay; trimmed so a saved line reads
        // back exactly as the console will run it.
        var lines = entries
            .Select(entry => entry?.Trim() ?? string.Empty)
            .Where(entry => entry.Length > 0)
            .ToArray();

        if (lines.Length == 0)
        {
            throw new DomainValidationException("A workspace needs at least one console line.");
        }

        if (lines.Length > MaxEntries)
        {
            throw new DomainValidationException(
                $"A workspace holds at most {MaxEntries} console lines, not {lines.Length}.");
        }

        if (lines.FirstOrDefault(line => line.Length > MaxEntryLength || line.Any(char.IsControl)) is { } bad)
        {
            throw new DomainValidationException(
                $"'{Shorten(bad)}' is not a console line: at most {MaxEntryLength} characters "
                    + "and no control characters.");
        }

        _entries = lines;
        UpdatedAtUtc = savedAtUtc;
    }

    // Echoed in an error, so control characters are not passed through to a
    // terminal that would act on them.
    private static string Shorten(string line)
    {
        var visible = new string([.. line.Select(character => char.IsControl(character) ? '?' : character)]);

        return visible.Length <= 40 ? visible : string.Concat(visible.AsSpan(0, 40), "…");
    }
}
