namespace PersonalQuant.Domain.Research;

/// <summary>
/// One recorded execution of research work, as the quant worker wrote it.
/// </summary>
/// <remarks>
/// <para>
/// Read-only in .NET. The worker records a run in the same transaction that
/// completes its job, so a run exists exactly when its job finished; nothing
/// on this side creates or changes one. The fields are what makes a result
/// reproducible — <em>same dataset version, same code commit, same
/// configuration ⇒ identical result</em> — and what lets a question about a
/// result be asked in SQL: which runs read the dataset that held a mis-imported
/// split, which used this factor version.
/// </para>
/// <para>
/// A reproduction is a run too, pointing at the run it re-executed. It is not
/// a new configuration, so it carries no trial index and is not counted.
/// </para>
/// </remarks>
public sealed class ResearchRun
{
    private readonly List<RunMetric> _metrics = [];

    private readonly List<RunArtifact> _artifacts = [];

    // EF Core materialises through this constructor; runs are never created here.
    private ResearchRun()
    {
        Status = null!;
        Strategy = null!;
        UniverseCode = null!;
        Membership = null!;
        DatasetId = null!;
        ContentHash = null!;
        FactorVersion = null!;
        Parameters = null!;
        ParametersHash = null!;
        ResearchEngine = null!;
        ResearchEngineVersion = null!;
    }

    /// <summary>Gets the run's identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the experiment the run belongs to.</summary>
    public ExperimentId ExperimentId { get; private set; }

    /// <summary>Gets the job that produced it, while that job is still kept.</summary>
    public ResearchJobId? JobId { get; private set; }

    /// <summary>Gets how the run ended: <c>succeeded</c> or <c>failed</c>.</summary>
    public string Status { get; private set; }

    /// <summary>Gets what was run — the job kind, for now.</summary>
    public string Strategy { get; private set; }

    /// <summary>Gets the universe the dataset was cut from.</summary>
    public string UniverseCode { get; private set; }

    /// <summary>Gets the date the constituent set was read at, for an as-of dataset.</summary>
    public DateOnly? UniverseAsOf { get; private set; }

    /// <summary>Gets how rows were matched to membership.</summary>
    public string Membership { get; private set; }

    /// <summary>Gets the dataset read.</summary>
    public string DatasetId { get; private set; }

    /// <summary>Gets the build of it read — resolved, never "latest".</summary>
    public int DatasetVersion { get; private set; }

    /// <summary>Gets the manifest's schema version.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>Gets the manifest's content hash, which a reproduction checks first.</summary>
    public string ContentHash { get; private set; }

    /// <summary>Gets the feature version, when features were used.</summary>
    public string? FeatureVersion { get; private set; }

    /// <summary>Gets the factor and its version, e.g. <c>momentum_12_1@1</c>.</summary>
    public string FactorVersion { get; private set; }

    /// <summary>Gets the model version, when a model was used.</summary>
    public string? ModelVersion { get; private set; }

    /// <summary>Gets the run's parameters, as a JSON object.</summary>
    public string Parameters { get; private set; }

    /// <summary>Gets the SHA-256 of the canonical parameters, so equal configurations compare equal.</summary>
    public string ParametersHash { get; private set; }

    /// <summary>Gets the code commit the worker was built from, when recorded.</summary>
    public string? CodeCommit { get; private set; }

    /// <summary>Gets the random seed, when the run used randomness.</summary>
    public long? RandomSeed { get; private set; }

    /// <summary>Gets the engine that ran it, e.g. <c>personal_quant</c>.</summary>
    public string ResearchEngine { get; private set; }

    /// <summary>Gets that engine's version.</summary>
    public string ResearchEngineVersion { get; private set; }

    /// <summary>Gets the run's position among its experiment's configurations; null for a reproduction.</summary>
    public int? TrialIndex { get; private set; }

    /// <summary>Gets how many configurations the experiment had tried when this run was recorded.</summary>
    public int? TrialCount { get; private set; }

    /// <summary>Gets the run this one re-executed, if it is a reproduction.</summary>
    public Guid? ReproducesRunId { get; private set; }

    /// <summary>Gets when the run started.</summary>
    public DateTimeOffset StartedAtUtc { get; private set; }

    /// <summary>Gets when the run finished.</summary>
    public DateTimeOffset FinishedAtUtc { get; private set; }

    /// <summary>Gets the run's metrics.</summary>
    public IReadOnlyList<RunMetric> Metrics => _metrics;

    /// <summary>Gets the files the run produced, with their digests.</summary>
    public IReadOnlyList<RunArtifact> Artifacts => _artifacts;
}

/// <summary>One named measurement of a run.</summary>
/// <remarks>
/// <c>double</c> rather than <c>decimal</c>: these are statistics computed in
/// floating point by the research layer, not prices, and a reproduction is
/// judged on their exact equality.
/// </remarks>
public sealed class RunMetric
{
    // EF Core materialises through this constructor; metrics are never created here.
    private RunMetric()
    {
        Name = null!;
        Split = null!;
    }

    /// <summary>Gets the run measured.</summary>
    public Guid RunId { get; private set; }

    /// <summary>Gets the metric's name, e.g. <c>ic_mean</c>.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the data split it was measured on, e.g. <c>all</c> or <c>test</c>.</summary>
    public string Split { get; private set; }

    /// <summary>Gets the value, or <see langword="null"/> when it could not be computed.</summary>
    public double? Value { get; private set; }
}

/// <summary>A file a run produced, and the digest it must still have.</summary>
public sealed class RunArtifact
{
    // EF Core materialises through this constructor; artifacts are never created here.
    private RunArtifact()
    {
        Kind = null!;
        Path = null!;
        Sha256 = null!;
    }

    /// <summary>Gets the run that produced it.</summary>
    public Guid RunId { get; private set; }

    /// <summary>Gets what it is, e.g. <c>report</c>.</summary>
    public string Kind { get; private set; }

    /// <summary>Gets where it is, relative to the research store.</summary>
    public string Path { get; private set; }

    /// <summary>Gets the lowercase hex SHA-256 of its bytes.</summary>
    public string Sha256 { get; private set; }
}
