using System.Globalization;

namespace PersonalQuant.Cli.CommandLine;

/// <summary>
/// Writes what a command found, in a shape a terminal and a pipe both read.
/// </summary>
/// <remarks>
/// <para>
/// Takes writers rather than touching <see cref="Console"/>, so the rendering
/// is testable without a console and refusals go to standard error where a
/// script expects them.
/// </para>
/// <para>
/// Nothing here decides anything. A value that is not known renders as
/// <c>unknown</c> and never as a blank or a dash, because a column of blanks
/// and a column of empty sets look identical and mean opposite things — the
/// same rule the capability declaration itself is written under.
/// </para>
/// </remarks>
/// <param name="output">Where results go.</param>
/// <param name="error">Where refusals go.</param>
internal sealed class Output(TextWriter output, TextWriter error)
{
    /// <summary>How an absent value is rendered, everywhere.</summary>
    public const string Unknown = "unknown";

    /// <summary>Writes a blank line.</summary>
    public void Blank() => output.WriteLine();

    /// <summary>Writes one line of result.</summary>
    /// <param name="text">The line.</param>
    public void Line(string text) => output.WriteLine(text);

    /// <summary>Writes a refusal to standard error.</summary>
    /// <param name="text">What went wrong.</param>
    public void Problem(string text) => error.WriteLine(text);

    /// <summary>
    /// Writes a labelled value, aligned against its siblings.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <param name="value">The value, already rendered.</param>
    /// <param name="width">The column width the caller aligned on.</param>
    public void Field(string label, string value, int width) =>
        output.WriteLine($"{label.PadRight(width)}  {value}");

    /// <summary>
    /// Writes a table, sized to its widest cell.
    /// </summary>
    /// <remarks>
    /// Padded rather than delimited. An operator reads this on a terminal, and
    /// a machine that wants columns has the ingestion API — inventing a second
    /// serialisation format here would be a contract nobody agreed to.
    /// </remarks>
    /// <param name="headers">The column headings.</param>
    /// <param name="rows">The rows, each the same length as the headings.</param>
    public void Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var widths = headers.Select(header => header.Length).ToArray();

        foreach (var row in rows)
        {
            for (var column = 0; column < widths.Length && column < row.Count; column++)
            {
                widths[column] = Math.Max(widths[column], row[column].Length);
            }
        }

        output.WriteLine(Compose(headers, widths));

        foreach (var row in rows)
        {
            output.WriteLine(Compose(row, widths));
        }
    }

    /// <summary>Renders a count with its noun, singular or plural.</summary>
    /// <param name="count">How many.</param>
    /// <param name="noun">The singular noun.</param>
    /// <returns>The rendered phrase.</returns>
    public static string Plural(int count, string noun) =>
        count == 1
            ? $"{count} {noun}"
            : string.Create(CultureInfo.InvariantCulture, $"{count} {noun}s");

    private static string Compose(IReadOnlyList<string> cells, int[] widths)
    {
        var line = string.Join(
            "  ",
            cells.Select((cell, column) =>
                column < widths.Length ? cell.PadRight(widths[column]) : cell));

        return line.TrimEnd();
    }
}
