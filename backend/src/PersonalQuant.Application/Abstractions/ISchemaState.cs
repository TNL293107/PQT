namespace PersonalQuant.Application.Abstractions;

/// <summary>
/// Reports whether the database this process is talking to holds the schema
/// this build expects.
/// </summary>
/// <remarks>
/// <para>
/// A port rather than a direct call on the context, because the operator
/// surface that asks the question must not depend on the persistence
/// technology to ask it. What migrations are is an infrastructure detail; that
/// the schema can be behind the build is not.
/// </para>
/// <para>
/// This exists because the two drifted silently once. A deployed image ran two
/// weeks behind the source and its database ran nine migrations behind the
/// image, and nothing in the system said so — the API answered every request,
/// the health check was green, and the divergence was found by looking rather
/// than by being told. Everything a running deployment believes about itself
/// should be answerable.
/// </para>
/// </remarks>
public interface ISchemaState
{
    /// <summary>
    /// Reads what the database has applied and what this build still expects.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The comparison.</returns>
    Task<SchemaComparison> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// What the database holds, against what the running build was compiled with.
/// </summary>
/// <remarks>
/// Both halves are needed and neither is sufficient. A pending list says the
/// database is behind the build; it says nothing about whether the build is
/// behind the source, which is the other half of the drift and is only
/// answerable by comparing <see cref="LastApplied"/> and the build's own
/// version against the repository.
/// </remarks>
/// <param name="AppliedCount">How many migrations the database has applied.</param>
/// <param name="LastApplied">
/// The newest migration the database has applied, or <see langword="null"/>
/// when it has applied none — an empty database and an up-to-date one are not
/// the same state.
/// </param>
/// <param name="Pending">
/// The migrations this build carries that the database has not applied, oldest
/// first.
/// </param>
public sealed record SchemaComparison(
    int AppliedCount,
    string? LastApplied,
    IReadOnlyList<string> Pending)
{
    /// <summary>
    /// Gets a value indicating whether the database holds the schema this build
    /// expects.
    /// </summary>
    public bool IsUpToDate => Pending.Count == 0;

    /// <summary>
    /// Gets a value indicating whether the database has never been migrated.
    /// </summary>
    /// <remarks>
    /// Distinguished from behind-by-some, because the remedy differs: an empty
    /// database is a deployment that has not been initialised, and one missing
    /// three migrations is a deployment that stopped being maintained.
    /// </remarks>
    public bool IsEmpty => AppliedCount == 0;
}
