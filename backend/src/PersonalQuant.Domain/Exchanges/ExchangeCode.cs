using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Exchanges;

/// <summary>
/// The short code identifying a trading venue, such as <c>HOSE</c>.
/// </summary>
/// <remarks>
/// This is the operating code the market uses day to day, not an ISO 10383
/// MIC. MIC coverage for Vietnamese venues is inconsistent across providers,
/// so it is stored as an optional attribute of <see cref="Exchange"/> rather
/// than used as identity.
/// </remarks>
public sealed record ExchangeCode
{
    /// <summary>Shortest permitted code.</summary>
    public const int MinLength = 2;

    /// <summary>Longest permitted code.</summary>
    public const int MaxLength = 12;

    private ExchangeCode(string value) => Value = value;

    /// <summary>Gets the upper-case code.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates an exchange code, throwing when the input is not valid.
    /// </summary>
    /// <param name="value">The code. Case and surrounding whitespace are normalised.</param>
    /// <returns>The parsed code.</returns>
    /// <exception cref="DomainValidationException">The value is not a valid code.</exception>
    public static ExchangeCode Create(string? value) =>
        TryCreate(value, out var code)
            ? code
            : throw new DomainValidationException($"'{value}' is not a valid exchange code.");

    /// <summary>
    /// Attempts to create an exchange code.
    /// </summary>
    /// <param name="value">The code. Case and surrounding whitespace are normalised.</param>
    /// <param name="code">The parsed code when successful.</param>
    /// <returns><see langword="true"/> when the value is a valid code.</returns>
    public static bool TryCreate(string? value, [NotNullWhen(true)] out ExchangeCode? code)
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

        if (!normalised.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        code = new ExchangeCode(normalised);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
