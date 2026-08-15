using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Classification;

/// <summary>
/// The short code identifying a <see cref="Sector"/> or an
/// <see cref="Industry"/>, such as <c>TECH</c> or <c>TECH-SOFT</c>.
/// </summary>
/// <remarks>
/// <para>
/// One type serves both levels because the rules are the same and the two are
/// always read together. It is not identity: a taxonomy can renumber, and
/// every instrument points at a surrogate key instead.
/// </para>
/// <para>
/// A hyphen is permitted so that a narrower level can be spelled as a
/// refinement of its parent. Nothing depends on that structure — it is a
/// readability convention, and the parent link is what the model actually
/// joins on.
/// </para>
/// </remarks>
public sealed record ClassificationCode
{
    /// <summary>Shortest permitted code.</summary>
    public const int MinLength = 2;

    /// <summary>Longest permitted code.</summary>
    public const int MaxLength = 24;

    private ClassificationCode(string value) => Value = value;

    /// <summary>Gets the upper-case code.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates a classification code, throwing when the input is not valid.
    /// </summary>
    /// <param name="value">The code. Case and surrounding whitespace are normalised.</param>
    /// <returns>The parsed code.</returns>
    /// <exception cref="DomainValidationException">The value is not a valid code.</exception>
    public static ClassificationCode Create(string? value) =>
        TryCreate(value, out var code)
            ? code
            : throw new DomainValidationException($"'{value}' is not a valid classification code.");

    /// <summary>
    /// Attempts to create a classification code.
    /// </summary>
    /// <param name="value">The code. Case and surrounding whitespace are normalised.</param>
    /// <param name="code">The parsed code when successful.</param>
    /// <returns><see langword="true"/> when the value is a valid code.</returns>
    public static bool TryCreate(string? value, [NotNullWhen(true)] out ClassificationCode? code)
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

        // A leading or trailing hyphen would make two codes that read as the
        // same taxonomy node compare unequal.
        if (normalised[0] == '-' || normalised[^1] == '-')
        {
            return false;
        }

        code = new ClassificationCode(normalised);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
