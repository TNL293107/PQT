using System.Globalization;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Turns provider rows into canonical bars, refusing the ones that cannot be.
/// </summary>
/// <remarks>
/// The single place validation happens, for every source. A provider that
/// validated its own output would be a provider whose definition of a valid
/// bar could differ from the next one's, and the entire point of the canonical
/// layer is that a bar means the same thing regardless of where it came from.
/// </remarks>
public interface IMarketDataNormalizer
{
    /// <summary>
    /// Validates and converts a provider's rows.
    /// </summary>
    /// <remarks>
    /// Nothing is dropped silently: every input row comes back either as an
    /// accepted bar or as a rejection carrying a reason.
    /// </remarks>
    /// <param name="request">The request the rows answer.</param>
    /// <param name="source">The provider that supplied them.</param>
    /// <param name="bars">The rows, as reported.</param>
    /// <param name="ingestedAtUtc">The instant the rows entered the system.</param>
    /// <returns>The accepted bars and the rejected rows.</returns>
    NormalizationResult Normalize(
        MarketDataRequest request,
        SourceCode source,
        IReadOnlyList<ProviderBar> bars,
        DateTimeOffset ingestedAtUtc);
}

/// <summary>
/// What normalising one response produced.
/// </summary>
/// <remarks>
/// Accepted bars come back ordered by period. Downstream code compares
/// consecutive bars, and an order that depended on the provider's response
/// would make that comparison provider-specific.
/// </remarks>
/// <param name="Accepted">The bars that passed, oldest first.</param>
/// <param name="Rejected">The rows that did not, with reasons.</param>
public sealed record NormalizationResult(
    IReadOnlyList<OhlcvBar> Accepted,
    IReadOnlyList<BarRejection> Rejected)
{
    /// <summary>A result from a response with no rows.</summary>
    public static NormalizationResult Empty { get; } = new([], []);

    /// <summary>Gets the opening instant of the newest accepted bar, if any.</summary>
    public DateTimeOffset? LastAcceptedOpenedAtUtc =>
        Accepted.Count == 0 ? null : Accepted[^1].OpenedAtUtc;
}

/// <summary>
/// Default <see cref="IMarketDataNormalizer"/>.
/// </summary>
/// <remarks>
/// <para>
/// The checks are ordered cheapest-first and each one names the failure it
/// catches. The domain enforces what a single bar can say about itself; this
/// adds the two things only the request knows — whether a period belongs to
/// the range that was asked for, and whether the response repeated one.
/// </para>
/// <para>
/// A row that fails is never repaired. Clamping a high up to the close, or
/// treating a negative volume as zero, would turn a visible provider fault
/// into a plausible-looking bar that every later phase would compute on.
/// </para>
/// </remarks>
internal sealed class MarketDataNormalizer : IMarketDataNormalizer
{
    /// <inheritdoc />
    public NormalizationResult Normalize(
        MarketDataRequest request,
        SourceCode source,
        IReadOnlyList<ProviderBar> bars,
        DateTimeOffset ingestedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bars);

        if (bars.Count == 0)
        {
            return NormalizationResult.Empty;
        }

        var accepted = new List<OhlcvBar>(bars.Count);
        var rejected = new List<BarRejection>();
        var seen = new HashSet<DateTimeOffset>(bars.Count);

        foreach (var bar in bars)
        {
            var openedAtUtc = bar.OpenedAtUtc.ToUniversalTime();

            if (!request.Interval.IsAligned(openedAtUtc))
            {
                rejected.Add(new BarRejection(
                    bar,
                    BarRejectionReason.MisalignedTimestamp,
                    $"{openedAtUtc:O} is not on a {request.Interval} boundary."));
                continue;
            }

            if (!request.Covers(openedAtUtc))
            {
                // Providers do over-return. Storing the extra periods would
                // look harmless and would put bars outside the range the
                // checkpoint is about to claim was covered.
                rejected.Add(new BarRejection(
                    bar,
                    BarRejectionReason.OutsideRequestedRange,
                    $"{openedAtUtc:O} is outside {request.FromUtc:O}–{request.ToUtc:O}."));
                continue;
            }

            if (!seen.Add(openedAtUtc))
            {
                rejected.Add(new BarRejection(
                    bar,
                    BarRejectionReason.DuplicateWithinBatch,
                    $"{openedAtUtc:O} appeared more than once in one response."));
                continue;
            }

            if (!TryCreatePrices(bar, out var prices, out var priceProblem))
            {
                rejected.Add(new BarRejection(
                    bar, BarRejectionReason.UnusablePrice, priceProblem));
                continue;
            }

            try
            {
                accepted.Add(OhlcvBar.Record(
                    request.InstrumentId,
                    request.Interval,
                    openedAtUtc,
                    prices.Open,
                    prices.High,
                    prices.Low,
                    prices.Close,
                    bar.Volume,
                    bar.Turnover,
                    source,
                    ingestedAtUtc));
            }
            catch (DomainValidationException exception)
            {
                // The aggregate is the authority on what a bar may be, so its
                // refusal is translated rather than duplicated here. Which of
                // the two categories it falls into is decided by the values,
                // not by the message.
                var reason = bar.Volume < 0 || bar.Turnover < 0m || (bar.Volume == 0 && bar.Turnover > 0m)
                    ? BarRejectionReason.UnusableQuantity
                    : BarRejectionReason.InconsistentPrices;

                rejected.Add(new BarRejection(bar, reason, exception.Message));
            }
        }

        accepted.Sort(static (left, right) => left.OpenedAtUtc.CompareTo(right.OpenedAtUtc));

        return new NormalizationResult(accepted, rejected);
    }

    private static bool TryCreatePrices(
        ProviderBar bar,
        out (Price Open, Price High, Price Low, Price Close) prices,
        out string problem)
    {
        prices = default;

        if (!Price.TryCreate(bar.Open, out var open))
        {
            problem = Describe("open", bar.Open);
            return false;
        }

        if (!Price.TryCreate(bar.High, out var high))
        {
            problem = Describe("high", bar.High);
            return false;
        }

        if (!Price.TryCreate(bar.Low, out var low))
        {
            problem = Describe("low", bar.Low);
            return false;
        }

        if (!Price.TryCreate(bar.Close, out var close))
        {
            problem = Describe("close", bar.Close);
            return false;
        }

        prices = (open, high, low, close);
        problem = string.Empty;
        return true;
    }

    private static string Describe(string field, decimal value) =>
        $"The {field} of {value.ToString(CultureInfo.InvariantCulture)} is not a usable price.";
}
