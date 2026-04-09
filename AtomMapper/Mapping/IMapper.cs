namespace AtomMapper;

/// <summary>
/// Provides object mapping operations. Obtain an instance via <see cref="MapperFactory.Create"/>.
/// </summary>
public interface IMapper
{
    /// <summary>Maps <paramref name="source"/> to a new <typeparamref name="TDestination"/> instance.</summary>
    TDestination Map<TSource, TDestination>(TSource source);

    /// <summary>
    /// Maps <paramref name="source"/> to a new <typeparamref name="TDestination"/> instance,
    /// inferring the source type at runtime. Prefer the two-type-parameter overload on hot paths.
    /// </summary>
    TDestination Map<TDestination>(object source);

    /// <summary>Maps <paramref name="source"/> onto an existing <paramref name="destination"/> instance in-place.</summary>
    TDestination Map<TSource, TDestination>(TSource source, TDestination destination);

}
