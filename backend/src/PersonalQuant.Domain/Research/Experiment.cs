using System.Text.RegularExpressions;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Research;

/// <summary>Identifies an experiment.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct ExperimentId(Guid Value)
{
    /// <summary>Creates a new identifier, ordered by creation time.</summary>
    /// <returns>The identifier.</returns>
    public static ExperimentId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A question under investigation, and the family of runs that tried to answer it.
/// </summary>
/// <remarks>
/// <para>
/// The unit <c>trial_count</c> is counted in. A Sharpe ratio of 1.8 from one
/// configuration and the best of three hundred are not the same evidence, and
/// the Deflated Sharpe Ratio and the Probability of Backtest Overfitting both
/// need the number tried (U9). Only a log that holds every run of an experiment
/// — the failures and the discarded variants as much as the one reported —
/// can supply it.
/// </para>
/// <para>
/// Named rather than numbered, because the operator names the question when
/// queuing work, and the same name must land in the same experiment however
/// many times it is used.
/// </para>
/// </remarks>
public sealed partial class Experiment
{
    /// <summary>The longest name accepted.</summary>
    public const int MaxNameLength = 128;

    /// <summary>The longest hypothesis accepted.</summary>
    public const int MaxHypothesisLength = 4000;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private Experiment() => Name = null!;

    private Experiment(ExperimentId id, string name, string? hypothesis, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        Hypothesis = hypothesis;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Gets the experiment's identifier.</summary>
    public ExperimentId Id { get; private set; }

    /// <summary>Gets the experiment's name, unique across experiments.</summary>
    public string Name { get; private set; }

    /// <summary>Gets what the experiment set out to test, when stated.</summary>
    public string? Hypothesis { get; private set; }

    /// <summary>Gets when the experiment was first named.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Names a new experiment.</summary>
    /// <param name="name">Lowercase letters, digits, and <c>_ - . /</c>.</param>
    /// <param name="hypothesis">What it tests, or <see langword="null"/>.</param>
    /// <param name="createdAtUtc">When it was named.</param>
    /// <returns>The experiment.</returns>
    /// <exception cref="DomainValidationException">A value is not usable.</exception>
    public static Experiment Define(string name, string? hypothesis, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength || !NamePattern().IsMatch(name))
        {
            throw new DomainValidationException(
                $"'{name}' is not an experiment name: lowercase letters, digits and _ - . /, "
                    + $"starting with a letter or digit, at most {MaxNameLength} characters.");
        }

        var stated = string.IsNullOrWhiteSpace(hypothesis) ? null : hypothesis.Trim();

        if (stated is { Length: > MaxHypothesisLength })
        {
            throw new DomainValidationException(
                $"A hypothesis may not exceed {MaxHypothesisLength} characters.");
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Timestamps must be UTC, but the offset was {createdAtUtc.Offset}.");
        }

        return new Experiment(ExperimentId.New(), name, stated, createdAtUtc);
    }

    // \z, not $: in .NET $ also matches before a final newline, so a name
    // ending in one would pass here and land in a different experiment.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9_\-./]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
