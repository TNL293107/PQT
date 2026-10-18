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
        output.WriteLine("  pqt instrument transfer --instrument <TICKER> --to <VENUE>");
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
        output.WriteLine("  pqt coverage <TICKER|INSTRUMENT-ID> [--exchange <VENUE>]");
        output.WriteLine("                      (what PQT holds for it: prices, actions, universes)");
        output.WriteLine();
        output.WriteLine("  pqt adjust recompute --instrument <TICKER>");
        output.WriteLine();
        output.WriteLine("  pqt dataset export --universe <CODE> --from yyyy-MM-dd [--to yyyy-MM-dd]");
        output.WriteLine("                     (point-in-time membership; --as-of reads one set instead)");
        output.WriteLine("                     [--as-of yyyy-MM-dd] [--interval 1d] [--raw]");
        output.WriteLine("                     [--known-as-of yyyy-MM-dd] [--policy strict|permissive]");
        output.WriteLine("                     [--history N]  (bars before each name's admission; default 0)");
        output.WriteLine("  pqt dataset verify <DATASET-ID> [--version N]");
        output.WriteLine();
        output.WriteLine("  pqt research submit momentum-slice --dataset <ID> [--version N]");
        output.WriteLine("                     [--quantiles 10] [--rebalance-every 21]");
        output.WriteLine("                     [--experiment NAME] [--hypothesis TEXT]");
        output.WriteLine("  pqt research list [--limit 20]");
        output.WriteLine("  pqt research show <JOB-ID>");
        output.WriteLine("  pqt research runs [--experiment NAME] [--limit 20]");
        output.WriteLine("  pqt research run <RUN-ID>");
        output.WriteLine("  pqt research reproduce <RUN-ID>");
        output.WriteLine();
        output.WriteLine("  pqt watchlist list");
        output.WriteLine("  pqt watchlist show|create|delete <NAME>");
        output.WriteLine("  pqt watchlist quotes <NAME> [--on yyyy-MM-dd]");
        output.WriteLine("  pqt watchlist rename <NAME> <NEW-NAME>");
        output.WriteLine("  pqt watchlist add|remove <NAME> <TICKER> [<TICKER>...]");
        output.WriteLine("  pqt workspace list");
        output.WriteLine("  pqt workspace show|delete <NAME>");
        output.WriteLine("  pqt workspace save <NAME> \"<console line>\" [\"<console line>\"...]");
        output.WriteLine();
        output.WriteLine("  pqt universe list");
        output.WriteLine("  pqt universe breadth <CODE> [--on yyyy-MM-dd]");
        output.WriteLine("  pqt universe breadth --venue <VENUE> [--on yyyy-MM-dd]");
        output.WriteLine("                     (latest session with member bars when --on is omitted)");
        output.WriteLine();
        output.WriteLine("  pqt fundamentals import --provider <CODE> [--ticker FPT,VCB]");
        output.WriteLine("  pqt fundamentals as-of <TICKER> --known-as-of yyyy-MM-dd [--scope consolidated|separate]");
        output.WriteLine("                     [--statement balance|income|cash-direct|cash-indirect] [--code 10]");
        output.WriteLine("  pqt fundamentals ratios <TICKER> --on yyyy-MM-dd   (EPS, P/E, P/B, ROE, yield; ADR-030)");
        output.WriteLine("                     (disclosed reports, ADR-029; IDS reads the collector's filings)");
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
