using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Universes;

/// <summary>
/// Answers who belonged to a universe on a date.
/// </summary>
public interface IUniverseCatalog
{
    /// <summary>
    /// Reads a universe's constituents as of a date.
    /// </summary>
    /// <param name="code">The universe to read.</param>
    /// <param name="asOf">The date to read as of.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The constituents, or a statement that the membership on that date is not
    /// known. Never an empty list standing in for the second.
    /// </returns>
    Task<UniverseConstituents> ConstituentsAsOfAsync(
        UniverseCode code,
        DateOnly asOf,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads constituent sets, refusing to let an unsourced date look like an empty
/// market.
/// </summary>
/// <remarks>
/// <para>
/// The whole of this class is one decision made in one place: a read is
/// answerable only where the universe claims to know its own membership. The
/// rows are consulted second, and only once the claim has said the date is
/// covered.
/// </para>
/// <para>
/// Doing it the other way round — read the rows, return what is there — is the
/// survivorship trap in its quietest form. It produces no error, no empty
/// result the caller notices, and a backtest that ran over the years that
/// happened to be sourced while reporting coverage of the whole range.
/// </para>
/// </remarks>
/// <param name="universes">The universe store.</param>
public sealed class UniverseCatalog(IUniverseRepository universes) : IUniverseCatalog
{
    /// <inheritdoc />
    public async Task<UniverseConstituents> ConstituentsAsOfAsync(
        UniverseCode code,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        var universe = await universes.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false);

        if (universe is null)
        {
            return UniverseConstituents.Unknown(code, asOf, UniverseUnknownReason.NoSuchUniverse);
        }

        if (universe.Coverage is null)
        {
            return UniverseConstituents.Unknown(code, asOf, UniverseUnknownReason.NoCoverageDeclared);
        }

        if (!universe.Knows(asOf))
        {
            return UniverseConstituents.Unknown(code, asOf, UniverseUnknownReason.OutsideCoverage);
        }

        var members = await universes
            .ListMembersAsOfAsync(universe.Id, asOf, cancellationToken)
            .ConfigureAwait(false);

        // Empty here is a fact and is returned as one: a covered date on which
        // the universe genuinely held nothing. That is why the claim is
        // consulted first — after it, an empty list means what it says.
        return UniverseConstituents.Known(code, asOf, members);
    }
}
