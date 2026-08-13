namespace PersonalQuant.Domain;

/// <summary>
/// Stable type used to reference the domain assembly (for example by
/// EF Core model configuration discovery or test assembly scanning) without
/// depending on a type that may later be renamed or removed.
/// </summary>
public static class DomainAssemblyMarker
{
    /// <summary>Gets the domain assembly.</summary>
    public static System.Reflection.Assembly Assembly { get; } =
        typeof(DomainAssemblyMarker).Assembly;
}
