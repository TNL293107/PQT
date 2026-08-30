using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Universes;

/// <summary>
/// The short code identifying a universe, such as <c>VN30</c>.
/// </summary>
/// <remarks>
/// <para>
/// Underscores are permitted because the useful codes for this market include
/// composites — <c>HOSE_ALL</c>, <c>VN30_TR</c> — and spelling them without a
/// separator produces codes nobody reads correctly.
/// </para>
/// <para>
/// A code, not identity. It appears in dataset manifests and research calls
/// where a UUID would be unreadable, so it is unique and normalised, but the
/// key every membership joins on is <see cref="UniverseId"/>.
/// </para>
/// </remarks>
public sealed record UniverseCode
{
    /// <summary>Shortest permitted code.</summary>
    public const int MinLength = 2;

    /// <summary>Longest permitted code.</summary>
    public const int MaxLength = 32;

    private UniverseCode(string value) => Value = value;

    /// <summary>Gets the upper-case code.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates a universe code, throwing when the input is not valid.
    /// </summary>
    /// <param name="value">The code. Case and surrounding whitespace are normalised.</param>
    /// <returns>The parsed code.</returns>
    /// <exception cref="DomainValidationException">The value is not a valid code.</exception>
    public static UniverseCode Create(string? value) =>
        TryCreate(value, out var code)
            ? code
            : throw new DomainValidationException($"'{value}' is not a valid universe code.");

    /// <summary>
    /// Attempts to create a universe code.
    /// </summary>
    /// <param name="value">The code. Case and surrounding whitespace are normalised.</param>
    /// <param name="code">The parsed code when successful.</param>
    /// <returns><see langword="true"/> when the value is a valid code.</returns>
    public static bool TryCreate(string? value, [NotNullWhen(true)] out UniverseCode? code)
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

        if (!normalised.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            return false;
        }

        code = new UniverseCode(normalised);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
