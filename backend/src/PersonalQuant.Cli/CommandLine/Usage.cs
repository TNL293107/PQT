namespace PersonalQuant.Cli.CommandLine;

/// <summary>
/// The help text, and the one place the command surface is written down.
/// </summary>
internal static class Usage
{
    /// <summary>
    /// Writes the full help.
    /// </summary>
    /// <param name="output">Where to write it.</param>
    public static void Write(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine("pqt — operator commands for the Personal Quant Terminal.");
        output.WriteLine();
        output.WriteLine("  pqt provider list");
        output.WriteLine("  pqt provider show <CODE>");
        output.WriteLine("  pqt provider check <CODE> --instrument <TICKER> [--interval 1d] [--from yyyy-MM-dd]");
        output.WriteLine();
        output.WriteLine("  pqt ingest run      --instrument <TICKER> [--interval 1d] [--source <CODE>]");
        output.WriteLine("                      [--from yyyy-MM-dd] [--to yyyy-MM-dd]");
        output.WriteLine("  pqt ingest backfill --instrument <TICKER> --from yyyy-MM-dd [--to yyyy-MM-dd]");
        output.WriteLine("                      [--interval 1d] [--source <CODE>] [--max-passes 200]");
        output.WriteLine("  pqt ingest backfill --universe <CODE> --from yyyy-MM-dd [--as-of yyyy-MM-dd] ...");
        output.WriteLine();
        output.WriteLine("  pqt quality list    --instrument <TICKER> [--interval 1d] [--limit 50] [--status open]");
        output.WriteLine("  pqt quality resolve <ID> --explained|--dismissed --reason \"<text>\"");
        output.WriteLine();
        output.WriteLine("  pqt schema status");
        output.WriteLine("  pqt calendar status");
        output.WriteLine();
        output.WriteLine("Exit codes: 0 done, 1 refused or failed, 2 the command line was wrong.");
        output.WriteLine();
        output.WriteLine("Configuration is read exactly as the API host reads it — appsettings.json");
        output.WriteLine("then environment variables — so this operates the deployment it is");
        output.WriteLine("configured beside, and never a different database by accident.");
        output.WriteLine();
        output.WriteLine("No source is tried after another fails. Where two registered sources could");
        output.WriteLine("both serve a request, name one with --source; the ambiguity is refused");
        output.WriteLine("rather than resolved by registration order.");
    }
}
