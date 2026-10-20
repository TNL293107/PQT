namespace PersonalQuant.Application.Fundamentals;

/// <summary>Why a filing was not recorded.</summary>
public enum FilingRejectionReason
{
    /// <summary>Not specified.</summary>
    Unspecified = 0,

    /// <summary>The ticker resolves to no active instrument.</summary>
    UnknownInstrument = 1,

    /// <summary>The report's name says neither its kind or its scope, or says both of one.</summary>
    Unclassified = 2,

    /// <summary>The form's fiscal period contradicts the report's kind.</summary>
    PeriodConflict = 3,

    /// <summary>The filing's date is impossible or implausible for its kind and period.</summary>
    ImplausibleDate = 4,

    /// <summary>A tab, column heading or form is not one the importer can read.</summary>
    Unreadable = 5,

    /// <summary>One filing states two different values for one line and period.</summary>
    ConflictingValues = 6,

    /// <summary>A report recorded without values now has values.</summary>
    ValuesAppeared = 7,

    /// <summary>Charter capital fits no unit the form could have been typed in.</summary>
    UnknownUnit = 8,

    /// <summary>
    /// A separate report whose form is the consolidated report's: recorded,
    /// with its values withheld.
    /// </summary>
    SharedForm = 9,

    /// <summary>
    /// A report whose form repeats an earlier period's: recorded, with its
    /// values withheld.
    /// </summary>
    RepeatedForm = 10,
}

/// <summary>A filing that was not recorded, and why.</summary>
/// <param name="Reference">The source's key for the filing.</param>
/// <param name="Ticker">The ticker it was listed under.</param>
/// <param name="Reason">Why.</param>
/// <param name="Detail">What exactly, for the operator.</param>
public sealed record FilingRejection(string Reference, string Ticker, FilingRejectionReason Reason, string Detail);

/// <summary>A filing recorded later than its deadline.</summary>
/// <param name="Reference">The source's key for the filing.</param>
/// <param name="Ticker">The ticker it was listed under.</param>
/// <param name="DaysAfterPeriod">Days from the period's end to the filing.</param>
/// <param name="DeadlineDays">The deadline it missed, in days.</param>
public sealed record LateFiling(string Reference, string Ticker, int DaysAfterPeriod, int DeadlineDays);
