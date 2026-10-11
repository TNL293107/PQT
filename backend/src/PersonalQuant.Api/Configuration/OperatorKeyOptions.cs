using System.ComponentModel.DataAnnotations;

namespace PersonalQuant.Api.Configuration;

/// <summary>
/// The operator key every non-GET endpoint requires until Phase 19 brings
/// authentication (ADR-027).
/// </summary>
/// <remarks>
/// Empty means the deployment accepts no writes at all, which is the state a
/// fresh clone starts in: the operator opts in by setting a key, rather than
/// opting out of an open endpoint they may not know exists.
/// </remarks>
public sealed class OperatorKeyOptions : IValidatableObject
{
    /// <summary>The configuration section the options bind from.</summary>
    public const string SectionName = "Terminal";

    /// <summary>The shortest key accepted.</summary>
    /// <remarks>
    /// Long enough that guessing it over HTTP is not a plan, and short enough
    /// that <c>openssl rand -hex 16</c> produces one.
    /// </remarks>
    public const int MinimumKeyLength = 32;

    /// <summary>Gets or sets the key, or empty to refuse every write.</summary>
    public string OperatorKey { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether writes are enabled at all.</summary>
    public bool WritesEnabled => OperatorKey.Length > 0;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OperatorKey.Length is > 0 and < MinimumKeyLength)
        {
            yield return new ValidationResult(
                $"Terminal:OperatorKey must be empty or at least {MinimumKeyLength} characters.",
                [nameof(OperatorKey)]);
        }

        // A key that differs from what the operator pasted only by surrounding
        // whitespace would refuse every request and look like a wrong key.
        if (OperatorKey.Length > 0 && OperatorKey.Trim().Length != OperatorKey.Length)
        {
            yield return new ValidationResult(
                "Terminal:OperatorKey may not begin or end with whitespace.",
                [nameof(OperatorKey)]);
        }
    }
}
