using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalQuant.Application;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Cli.Diagnostics;
using PersonalQuant.Infrastructure;

var output = new Output(Console.Out, Console.Error);

if (args.Length == 0)
{
    Usage.Write(Console.Out);
    return ExitCode.Usage;
}

if (args[0] is "help" or "--help" or "-h")
{
    Usage.Write(Console.Out);
    return ExitCode.Ok;
}

if (!CommandArguments.TryParse(args, out var command, out var problem))
{
    output.Problem(problem);
    Usage.Write(Console.Out);
    return ExitCode.Usage;
}

using var cancellation = new CancellationTokenSource();

// Ctrl+C cancels the command rather than killing the process. A backfill
// interrupted mid-pass has already committed the passes before it, and the
// checkpoint sits on the last bar actually stored — so a cancelled run resumes
// rather than repeating, which is only true if the process is allowed to
// unwind.
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

// No args are handed to the configuration builder. The command line here is
// verbs and options, not configuration switches, and the command-line provider
// would try to read 'provider list' as a setting.
//
// The content root is the assembly's own directory rather than the shell's
// working directory. A CLI is run from wherever the operator happens to be
// standing, and the default would make it read whichever appsettings.json is
// beside them — inside the deployment image that is the API's.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory,
});

// Every log line goes to standard error, whatever its level. What a command
// prints is its answer, and a warning from the data source interleaved into it
// would corrupt anything reading the output through a pipe.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Built and never started. Starting it would run the hosted services the API
// host owns — migration, reference-data seeding, the scheduled ingestion pass —
// and an operator reading 'provider list' must not cause a migration.
using var host = builder.Build();
using var scope = host.Services.CreateScope();

// Resolved before the attempt, not inside the failure path. The trace is
// logged at debug and is normally disabled, and building a logger only to
// discover that is work done for nothing.
var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("pqt");

try
{
    return await Dispatcher
        .RunAsync(scope.ServiceProvider, command, output, cancellation.Token)
        .ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    output.Problem("Cancelled. Re-run to continue from the checkpoint.");
    return ExitCode.Refused;
}
catch (OptionsValidationException exception)
{
    // The whole story is in the failures, and a stack trace through the
    // container adds nothing to it. This is the most common way an operator
    // command fails — run from a shell that has none of the deployment's
    // environment — and it has to say which settings are missing on one line
    // rather than fifty.
    output.Problem("This command needs configuration the environment did not supply.");

    foreach (var failure in exception.Failures)
    {
        output.Problem($"  {failure}");
    }

    output.Problem(
        "Every setting comes from the environment, as it does for the API host. See "
            + ".env.example, or run the command inside the deployment: "
            + "docker compose exec backend dotnet cli/PersonalQuant.Cli.dll ...");

    return ExitCode.Refused;
}
catch (Exception exception)
{
    // The message, and only the message. A database this deployment cannot
    // reach is the common case and its stack is thirty frames of connection
    // pool and query pipeline that say nothing the first line does not. The
    // trace is logged at debug and stays one environment variable away.
    CliLog.CommandFailed(log, exception, command.Group, command.Verb);

    output.Problem($"{exception.GetType().Name}: {exception.Message}");
    output.Problem("Run again with Logging__LogLevel__Default=Debug for the stack.");

    return ExitCode.Refused;
}
