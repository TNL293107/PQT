using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.MarketData;

/// <summary>
/// Identifies where a piece of market data came from.
/// </summary>
/// <remarks>
/// <para>
/// Stored on every bar and every raw batch, so that a series can always be
/// traced back to what produced it. That is not bookkeeping: when two
/// providers disagree about a close, the only way to decide which one to
/// believe is to know which rows came from which, and a series that has lost
/// its source cannot be re-normalised or corrected.
/// </para>
/// <para>
/// A code, not a foreign key to a provider table. Providers are registered in
/// code and configuration rather than as data, and a row referencing a
/// provider that has since been removed must stay readable.
/// </para>
/// </remarks>
public sealed record SourceCode
{
    /// <summary>Shortest permitted code.</summary>
    public const int MinLength = 2;

    /// <summary>Longest permitted code.</summary>
    public const int MaxLength = 32;

    private SourceCode(string value) => Value = value;

    /// <summary>Gets the upper-case code.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates a source code, throwing when the input is not valid.
    /// </summary>
    /// <param name="value">The code. Case and surrounding whitespace are normalised.</param>
    /// <returns>The parsed code.</returns>
    /// <exception cref="DomainValidationException">The value is not a valid code.</exception>
    public static SourceCode Create(string? value) =>
        TryCreate(value, out var code)
            ? code
            : throw new DomainValidationException($"'{value}' is not a valid market data source code.");

    /// <summary>
    /// Attempts to create a source code.
    /// </summary>
    /// <param name="value">The code. Case and surrounding whitespace are normalised.</param>
    /// <param name="code">The parsed code when successful.</param>
    /// <returns><see langword="true"/> when the value is a valid code.</returns>
    public static bool TryCreate(string? value, [NotNullWhen(true)] out SourceCode? code)
    {
        code = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalised = value.Trim().ToUpperInvariant();

        if (normalised.Length is < MinLength or > MaxLength)
        {
            return false;
        }

        if (!normalised.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            return false;
        }

        if (normalised[0] == '-' || normalised[^1] == '-')
        {
            return false;
        }

        code = new SourceCode(normalised);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
