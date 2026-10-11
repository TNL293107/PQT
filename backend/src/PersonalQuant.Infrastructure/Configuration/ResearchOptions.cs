using System.ComponentModel.DataAnnotations;

namespace PersonalQuant.Infrastructure.Configuration;

/// <summary>
/// Settings for the research boundary, bound from the <c>Research</c> section.
/// </summary>
public sealed class ResearchOptions : IValidatableObject
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Research";

    /// <summary>The shortest password the research role may be given.</summary>
    public const int MinimumPasswordLength = 12;

    /// <summary>
    /// Gets or sets the password the <c>quant_research</c> role logs in with,
    /// or an empty string to leave the role without a login.
    /// </summary>
    /// <remarks>
    /// Empty by default. A deployment that does not run the quant worker then
    /// has no research credential at all, rather than one nobody uses.
    /// </remarks>
    public string DatabasePassword { get; set; } = string.Empty;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DatabasePassword.Length is > 0 and < MinimumPasswordLength)
        {
            yield return new ValidationResult(
                $"Research:DatabasePassword must be empty or at least {MinimumPasswordLength} characters.",
                [nameof(DatabasePassword)]);
        }

        if (DatabasePassword.Contains('\0', StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                "Research:DatabasePassword may not contain a NUL character.",
                [nameof(DatabasePassword)]);
        }
    }
}
