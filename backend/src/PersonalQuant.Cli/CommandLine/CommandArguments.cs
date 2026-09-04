using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PersonalQuant.Cli.CommandLine;

/// <summary>
/// One parsed command line: a group, a verb, whatever followed them, and the
/// named options.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than delegated to a parsing library. The surface is six
/// commands and a dozen options, it has to produce refusals an operator can act
/// on, and a dependency whose own failure modes have to be learned is a poor
/// trade for that.
/// </para>
/// <para>
/// <strong>An option the command does not recognise is an error.</strong> That
/// is the whole reason <see cref="Validate"/> exists: a mistyped
/// <c>--form 2015-01-01</c> silently ignored is a backfill that runs over the
/// wrong range and reports success, and nothing downstream would ever say so.
/// </para>
/// </remarks>
internal sealed class CommandArguments
{
    private readonly Dictionary<string, string?> _options;

    private CommandArguments(
        string group,
        string verb,
        IReadOnlyList<string> operands,
        Dictionary<string, string?> options)
    {
        Group = group;
        Verb = verb;
        Operands = operands;
        _options = options;
    }

    /// <summary>Gets the command group — provider, ingest, quality.</summary>
    public string Group { get; }

    /// <summary>Gets the verb within the group.</summary>
    public string Verb { get; }

    /// <summary>Gets the values that followed the verb before any option.</summary>
    public IReadOnlyList<string> Operands { get; }

    /// <summary>
    /// Parses a command line.
    /// </summary>
    /// <remarks>
    /// A token starting with two dashes opens an option. The token after it is
    /// its value unless that token is itself an option, in which case the first
    /// is a flag. Everything past the group and the verb, before the first
    /// option, is an operand.
    /// </remarks>
    /// <param name="args">The raw arguments.</param>
    /// <param name="command">The parsed command when successful.</param>
    /// <param name="problem">What was wrong with the line, when it was.</param>
    /// <returns><see langword="true"/> when a group and a verb were given.</returns>
    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out CommandArguments? command,
        [NotNullWhen(false)] out string? problem)
    {
        ArgumentNullException.ThrowIfNull(args);

        command = null;

        if (args.Count == 0)
        {
            problem = "No command was given.";
            return false;
        }

        if (IsOption(args[0]))
        {
            problem = $"{args[0]} is an option, not a command.";
            return false;
        }

        if (args.Count == 1 || IsOption(args[1]))
        {
            problem = $"{args[0]} needs a verb. Run 'pqt help' for the list.";
            return false;
        }

        var operands = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);

        for (var index = 2; index < args.Count; index++)
        {
            var token = args[index];

            if (!IsOption(token))
            {
                if (options.Count > 0)
                {
                    problem = $"{token} follows an option and belongs to none.";
                    return false;
                }

                operands.Add(token);
                continue;
            }

            var name = token[2..];

            if (name.Length == 0)
            {
                problem = "-- names no option.";
                return false;
            }

            var hasValue = index + 1 < args.Count && !IsOption(args[index + 1]);

            if (!options.TryAdd(name, hasValue ? args[index + 1] : null))
            {
                problem = $"--{name} was given more than once.";
                return false;
            }

            if (hasValue)
            {
                index++;
            }
        }

        command = new CommandArguments(args[0], args[1], operands, options);
        problem = null;
        return true;
    }

    /// <summary>
    /// Refuses any option the command does not know about.
    /// </summary>
    /// <param name="known">Every option name this command accepts.</param>
    /// <param name="problem">The offending option, when there is one.</param>
    /// <returns><see langword="true"/> when every option is recognised.</returns>
    public bool Validate(IReadOnlyList<string> known, [NotNullWhen(false)] out string? problem)
    {
        ArgumentNullException.ThrowIfNull(known);

        foreach (var name in _options.Keys.Order(StringComparer.Ordinal))
        {
            if (known.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            var accepted = string.Join(
                ", ",
                known.Order(StringComparer.Ordinal).Select(option => "--" + option));

            problem = $"--{name} is not an option of '{Group} {Verb}'. Accepted: {accepted}.";
            return false;
        }

        problem = null;
        return true;
    }

    /// <summary>Reports whether a flag was present.</summary>
    /// <param name="name">The option name, without the leading dashes.</param>
    /// <returns><see langword="true"/> when the flag was given.</returns>
    public bool HasFlag(string name) => _options.ContainsKey(name);

    /// <summary>
    /// Reads an option's value, or <see langword="null"/> when it was not given.
    /// </summary>
    /// <param name="name">The option name, without the leading dashes.</param>
    /// <returns>The value, or null.</returns>
    public string? Value(string name) =>
        _options.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// Reads an option the command cannot run without.
    /// </summary>
    /// <param name="name">The option name, without the leading dashes.</param>
    /// <param name="value">The value when it was given with one.</param>
    /// <param name="problem">What is missing, when something is.</param>
    /// <returns><see langword="true"/> when the option carried a value.</returns>
    public bool TryRequired(
        string name,
        [NotNullWhen(true)] out string? value,
        [NotNullWhen(false)] out string? problem)
    {
        value = Value(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            problem = _options.ContainsKey(name)
                ? $"--{name} was given without a value."
                : $"'{Group} {Verb}' needs --{name}.";
            return false;
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// Reads a date option, or nothing when it was not given.
    /// </summary>
    /// <remarks>
    /// ISO 8601 only, and invariant. A locale-sensitive date on a command line
    /// is how 03/09 becomes March in one shell and September in another, and a
    /// backfill would silently cover a different half-year.
    /// </remarks>
    /// <param name="name">The option name, without the leading dashes.</param>
    /// <param name="date">The parsed date, or null when the option was absent.</param>
    /// <param name="problem">Why the value is not a date, when it is not.</param>
    /// <returns><see langword="true"/> when the option was absent or parsed.</returns>
    public bool TryDate(string name, out DateOnly? date, [NotNullWhen(false)] out string? problem)
    {
        date = null;
        problem = null;

        var value = Value(name);

        if (value is null)
        {
            if (!_options.ContainsKey(name))
            {
                return true;
            }

            problem = $"--{name} was given without a date.";
            return false;
        }

        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            problem = $"--{name} {value} is not a date. Write it as yyyy-MM-dd.";
            return false;
        }

        date = parsed;
        return true;
    }

    /// <summary>
    /// Reads a whole-number option, falling back when it was not given.
    /// </summary>
    /// <param name="name">The option name, without the leading dashes.</param>
    /// <param name="fallback">The value to use when the option is absent.</param>
    /// <param name="number">The parsed number.</param>
    /// <param name="problem">Why the value is not a positive whole number.</param>
    /// <returns><see langword="true"/> when the option was absent or parsed.</returns>
    public bool TryCount(
        string name,
        int fallback,
        out int number,
        [NotNullWhen(false)] out string? problem)
    {
        number = fallback;
        problem = null;

        var value = Value(name);

        if (value is null)
        {
            if (!_options.ContainsKey(name))
            {
                return true;
            }

            problem = $"--{name} was given without a number.";
            return false;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number)
            || number <= 0)
        {
            problem = $"--{name} {value} is not a positive whole number.";
            return false;
        }

        return true;
    }

    private static bool IsOption(string token) =>
        token.StartsWith("--", StringComparison.Ordinal);
}
