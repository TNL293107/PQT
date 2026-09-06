namespace PersonalQuant.Application.Datasets;

/// <summary>
/// What this deployment records about its right to use each source's data.
/// </summary>
/// <remarks>
/// <para>
/// A note per provider, carried into every dataset built from that provider's
/// rows. The terms a source was read under are a fact about the export; a note
/// looked up afterwards describes whatever the configuration happens to say
/// that day, which is not the same claim.
/// </para>
/// <para>
/// The default is the honest one. None of the Vietnamese sources this system
/// reads grants a licence — the data is retained because nothing prohibits
/// retaining it, which is an absence rather than a permission, and does not
/// extend to redistribution. A registry that defaulted to silence would let a
/// dataset leave this system with no statement attached at all.
/// </para>
/// </remarks>
public interface IDatasetLicenceRegistry
{
    /// <summary>
    /// The note recorded when a deployment has said nothing about a source.
    /// </summary>
    const string UnstatedNote =
        "No licence granted. Retained under the absence of a prohibition rather than a permission. "
        + "Not for redistribution.";

    /// <summary>
    /// The note to record against a source code.
    /// </summary>
    /// <param name="sourceCode">The provider code carried on the rows.</param>
    /// <returns>The note, never empty.</returns>
    string NoteFor(string sourceCode);
}

/// <summary>
/// Identifies the build that produced an artefact.
/// </summary>
/// <remarks>
/// A port rather than a static read, because "which build made this" is
/// answered differently by a container, a developer's machine and a test — and
/// a dataset that claimed a commit it could not know would be worse than one
/// that admits it does not.
/// </remarks>
public interface IBuildIdentity
{
    /// <summary>
    /// Gets the source revision this build came from, or
    /// <see langword="null"/> when the build did not record one.
    /// </summary>
    string? Commit { get; }
}
