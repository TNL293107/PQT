using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalQuant.Infrastructure.Diagnostics;

namespace PersonalQuant.Infrastructure.Fundamentals;

/// <summary>
/// Logs once, as the API starts, that Vietcap's statements are registered and
/// for how many tickers (ADR-038; sprint 11.6 step 7).
/// </summary>
/// <remarks>
/// Registered only beside the provider. It states the count and never the URL
/// or the tickers, which belong to the deployment's environment. The CLI builds
/// its host without starting it, so it never logs this line.
/// </remarks>
/// <param name="tickerCount">How many tickers the provider reads.</param>
/// <param name="logger">The logger.</param>
internal sealed class VietcapStatementsStartupLog(int tickerCount, ILogger<VietcapStatementsStartupLog> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        InfrastructureLog.VietcapStatementsRegistered(logger, tickerCount);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
