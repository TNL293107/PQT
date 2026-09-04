using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Cli.Commands;

namespace PersonalQuant.Cli.CommandLine;

/// <summary>
/// Routes a parsed command line to the group that answers it.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place that touches the container. The command classes take
/// application services rather than a service provider, so each one states what
/// it needs and none of them can reach further.
/// </para>
/// <para>
/// <strong>Nothing is resolved here.</strong> Every service arrives as a
/// <see cref="Lazy{T}"/>, which enforces one rule: a malformed command line
/// never reaches the deployment. Resolving eagerly means the repository — and
/// through it the database options — is constructed before the command has
/// looked at its own arguments, so <c>--form 2015-01-01</c> is answered with
/// four lines about a missing Postgres password instead of the typo. The
/// operator then goes looking for a configuration problem that does not exist.
/// </para>
/// <para>
/// It also means a command that genuinely needs no database does not require
/// one. <c>provider list</c> reads declarations that live in the composition
/// root, and it has to work on a host that cannot reach Postgres, because that
/// is when someone most wants to ask what the host thinks it has.
/// </para>
/// </remarks>
internal static class Dispatcher
{
    /// <summary>
    /// Runs one command against a scope.
    /// </summary>
    /// <param name="services">A scope of the composed application.</param>
    /// <param name="command">The parsed command line.</param>
    /// <param name="output">Where results and refusals go.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The process exit code.</returns>
    public static Task<int> RunAsync(
        IServiceProvider services,
        CommandArguments command,
        Output output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);

        switch (command.Group)
        {
            case "provider":
                return new ProviderCommands(
                        services.GetRequiredService<IMarketDataProviderRegistry>(),
                        Defer<IInstrumentResolver>(services),
                        output)
                    .RunAsync(command, cancellationToken);

            case "ingest":
                return new IngestCommands(
                        Defer<IMarketDataIngestionService>(services),
                        Defer<IInstrumentResolver>(services),
                        Defer<IUniverseCatalog>(services),
                        output)
                    .RunAsync(command, cancellationToken);

            case "schema":
            case "calendar":
                return new DeploymentCommands(
                        Defer<ISchemaState>(services),
                        Defer<ITradingCalendar>(services),
                        Defer<IClock>(services),
                        output)
                    .RunAsync(command, cancellationToken);

            case "quality":
                return new QualityCommands(
                        Defer<IDataQualityService>(services),
                        Defer<IInstrumentResolver>(services),
                        output)
                    .RunAsync(command, cancellationToken);

            default:
                output.Problem(
                    $"'{command.Group}' is not a command group. Try provider, ingest, quality, "
                        + "schema or calendar.");

                return Task.FromResult(ExitCode.Usage);
        }
    }

    /// <summary>
    /// Promises a service without constructing one.
    /// </summary>
    /// <remarks>
    /// The registry is the exception and is resolved directly: it is a singleton
    /// built from the composition root, it reaches nothing, and every
    /// <c>provider</c> verb reads it.
    /// </remarks>
    /// <typeparam name="TService">The service the command will ask for.</typeparam>
    /// <param name="services">The scope to resolve from, when it is asked.</param>
    /// <returns>The deferred service.</returns>
    private static Lazy<TService> Defer<TService>(IServiceProvider services)
        where TService : notnull =>
        new(services.GetRequiredService<TService>);
}
