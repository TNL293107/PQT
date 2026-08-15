using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Classification;

/// <summary>
/// Validates the display names carried by <see cref="Sector"/> and
/// <see cref="Industry"/>.
/// </summary>
/// <remarks>
/// Shared because both levels have the same rule, and two copies of a
/// validation rule are two chances for them to stop agreeing.
/// </remarks>
internal static class ClassificationName
{
    /// <summary>
    /// Trims a name and rejects it when empty or over length.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <param name="maxLength">The longest name the level permits.</param>
    /// <param name="subject">
    /// How the level is named in the failure message, such as <c>A sector</c>.
    /// </param>
    /// <returns>The trimmed name.</returns>
    /// <exception cref="DomainValidationException">The name is invalid.</exception>
    public static string Require(string? name, int maxLength, string subject)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException($"{subject} name is required.");
        }

        var trimmed = name.Trim();

        return trimmed.Length > maxLength
            ? throw new DomainValidationException(
                $"{subject} name may not exceed {maxLength} characters.")
            : trimmed;
    }
}
