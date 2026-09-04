using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Abstractions;

namespace PersonalQuant.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ISchemaState"/>.
/// </summary>
/// <remarks>
/// Two queries against the migrations history table, and no writes. Reading
/// what the schema is must never be able to change it: the whole reason an
/// operator asks is that they do not yet know what state the deployment is in,
/// and a question that migrated as a side effect would be the worst possible
/// answer to it.
/// </remarks>
/// <param name="dbContext">The context whose model defines what this build expects.</param>
internal sealed class SchemaState(PersonalQuantDbContext dbContext) : ISchemaState
{
    /// <inheritdoc />
    public async Task<SchemaComparison> ReadAsync(CancellationToken cancellationToken = default)
    {
        var applied = (await dbContext.Database
                .GetAppliedMigrationsAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToList();

        var pending = (await dbContext.Database
                .GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToList();

        // Ordered by identifier, which for EF Core migrations is the timestamp
        // prefix — so the last one is the newest, and the pending list reads
        // oldest first in the order they would be applied.
        return new SchemaComparison(
            applied.Count,
            applied.OrderBy(name => name, StringComparer.Ordinal).LastOrDefault(),
            [.. pending.OrderBy(name => name, StringComparer.Ordinal)]);
    }
}
