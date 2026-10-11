using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PersonalQuant.Infrastructure.Diagnostics;

namespace PersonalQuant.Infrastructure.Persistence;

/// <summary>
/// Gives the <c>quant_research</c> role a login when one is configured, and
/// takes it away when none is.
/// </summary>
/// <remarks>
/// <para>
/// The migration that creates the role creates it without a login: grants
/// belong in a migration, a password does not. This runs after the schema is
/// current, on every start-up, so the role's login always matches the
/// configuration — including on a database volume created long before the
/// role existed, which an init script would never reach.
/// </para>
/// <para>
/// <c>ALTER ROLE ... PASSWORD</c> takes no bind parameter. The literal is
/// therefore quoted by the server itself (<c>quote_literal</c>) in a first,
/// parameterised round trip, rather than by string handling here.
/// </para>
/// </remarks>
internal static class ResearchRoleProvisioner
{
    /// <summary>The role the quant layer connects as.</summary>
    public const string RoleName = "quant_research";

    /// <summary>
    /// Brings the role's login into line with the configured password.
    /// </summary>
    /// <param name="dbContext">A context on the database being provisioned.</param>
    /// <param name="password">The password, or empty to remove the login.</param>
    /// <param name="logger">Where the outcome is recorded.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the role is provisioned.</returns>
    public static async Task ProvisionAsync(
        PersonalQuantDbContext dbContext,
        string password,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(password);

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;

        if (opened)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using (var exists = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @role)", connection))
            {
                exists.Parameters.AddWithValue("role", RoleName);

                if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
                {
                    InfrastructureLog.ResearchRoleMissing(logger, RoleName);
                    return;
                }
            }

            if (password.Length == 0)
            {
                await ExecuteAsync(connection, $"ALTER ROLE {RoleName} NOLOGIN PASSWORD NULL", cancellationToken)
                    .ConfigureAwait(false);

                InfrastructureLog.ResearchRoleWithoutLogin(logger, RoleName);
                return;
            }

            string literal;

            await using (var quote = new NpgsqlCommand("SELECT quote_literal(@password)", connection))
            {
                quote.Parameters.AddWithValue("password", password);
                literal = (string)(await quote.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            }

            await ExecuteAsync(connection, $"ALTER ROLE {RoleName} LOGIN PASSWORD {literal}", cancellationToken)
                .ConfigureAwait(false);

            InfrastructureLog.ResearchRoleProvisioned(logger, RoleName);
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        // The only variable part is a literal the server quoted itself.
#pragma warning disable CA2100
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
