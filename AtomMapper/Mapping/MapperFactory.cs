namespace AtomMapper;

/// <summary>
/// Entry point for creating an <see cref="IMapper"/> instance.
/// </summary>
public static class MapperFactory
{
    /// <summary>
    /// Creates an <see cref="IMapper"/> from one or more <see cref="IMapperProfile"/> instances.
    /// All expression trees are compiled once during this call; subsequent mapping calls have
    /// near-zero overhead.
    /// </summary>
    /// <param name="profiles">One or more profiles that define the mappings.</param>
    /// <returns>A configured, ready-to-use <see cref="IMapper"/>.</returns>
    public static IMapper Create(params IMapperProfile[] profiles)
    {
        var registry = new MappingExpressionRegistry();

        foreach (var profile in profiles)
            profile.Register(registry);

        registry.Commit();

        return new MapperService();
    }
}
