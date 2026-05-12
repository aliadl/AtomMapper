namespace AtomMapper;

/// <summary>
/// Contextual information passed to value resolvers during mapping.
/// </summary>
public sealed class ResolutionContext
{
    public static readonly ResolutionContext Default = new();
    private ResolutionContext() { }
}
