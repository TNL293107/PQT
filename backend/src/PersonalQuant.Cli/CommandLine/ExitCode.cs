namespace PersonalQuant.Cli.CommandLine;

/// <summary>
/// What the process returns to the shell.
/// </summary>
/// <remarks>
/// Three values, and the distinction between the last two is the one that
/// matters to anything scripting this. A malformed command is the operator's
/// mistake and re-running it will fail identically; a refused run is the
/// system's answer about the data and may well succeed tomorrow. Collapsing
/// both into 1 would make a retry loop retry the typo.
/// </remarks>
internal static class ExitCode
{
    /// <summary>The command ran and the answer was affirmative.</summary>
    public const int Ok = 0;

    /// <summary>
    /// The command ran and the system refused, failed, or found nothing.
    /// </summary>
    public const int Refused = 1;

    /// <summary>The command line itself was wrong, and nothing was attempted.</summary>
    public const int Usage = 2;
}
