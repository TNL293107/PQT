namespace PersonalQuant.Application;

/// <summary>
/// Stable type used to reference the application assembly for assembly
/// scanning (validators, use case handlers, mapping profiles) without
/// depending on a type that may later be renamed or removed.
/// </summary>
public static class ApplicationAssemblyMarker
{
    /// <summary>Gets the application assembly.</summary>
    public static System.Reflection.Assembly Assembly { get; } =
        typeof(ApplicationAssemblyMarker).Assembly;
}
