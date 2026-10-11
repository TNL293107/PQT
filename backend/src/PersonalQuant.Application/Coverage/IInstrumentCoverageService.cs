using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Coverage;

/// <summary>
/// Answers what data PQT holds for an instrument, and to what extent.
/// </summary>
/// <remarks>
/// <para>
/// The coverage registry of Phase 5 (ADR-026): one application-layer query the
/// API, the CLI and the console all call, so the four coverage states mean the
/// same thing on every surface and none of them reimplements the rules.
/// </para>
/// <para>
/// Read-only introspection. It does not configure, schedule or health-check
/// providers, and it records nothing: the answer is recomputed from the facts
/// on every call.
/// </para>
/// </remarks>
public interface IInstrumentCoverageService
{
    /// <summary>
    /// Describes every dataset PQT could hold for an instrument.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The coverage, or <see langword="null"/> when the instrument is unknown.</returns>
    Task<InstrumentCoverage?> DescribeAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default);
}
