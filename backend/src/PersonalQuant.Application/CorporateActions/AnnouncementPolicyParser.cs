using System.Globalization;
using PersonalQuant.Domain.CorporateActions;

namespace PersonalQuant.Application.CorporateActions;

/// <summary>
/// Parses the null-announcement policy a caller asked for.
/// </summary>
/// <remarks>
/// In the application layer for the reason
/// <see cref="MarketData.BarIntervalParser"/> is: the API and the operator CLI
/// both ask the question, and a second copy would eventually let the two
/// disagree about what <c>strict</c> means — which is exactly the kind of
/// disagreement that produces a backtest nobody can reproduce.
/// </remarks>
public static class AnnouncementPolicyParser
{
    private static readonly Dictionary<string, AnnouncementPolicy> ByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["strict"] = AnnouncementPolicy.Strict,
            ["permissive"] = AnnouncementPolicy.Permissive,
        };

    /// <summary>
    /// Parses a policy, falling back to the default when none was given.
    /// </summary>
    /// <param name="value">The caller's value, or null.</param>
    /// <param name="policy">The parsed policy when successful.</param>
    /// <returns><see langword="true"/> when the value names a policy.</returns>
    public static bool TryParse(string? value, out AnnouncementPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            policy = AnnouncementPolicyExtensions.Default;
            return true;
        }

        return ByName.TryGetValue(value.Trim(), out policy);
    }

    /// <summary>
    /// Renders a policy in the spelling this parser accepts.
    /// </summary>
    /// <param name="policy">The policy to render.</param>
    /// <returns>The name, lowercased.</returns>
    public static string Describe(AnnouncementPolicy policy) =>
        policy.ToString().ToLower(CultureInfo.InvariantCulture);

    /// <summary>Gets the names a caller may send, for an error message.</summary>
    /// <returns>The accepted names, comma separated.</returns>
    public static string DescribeAccepted() =>
        string.Join(", ", ByName.Keys.Order(StringComparer.OrdinalIgnoreCase));
}
