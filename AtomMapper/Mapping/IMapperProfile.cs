namespace AtomMapper;

/// <summary>
/// Defines a set of mapping configurations.
/// Implement this interface to group related <see cref="MappingExpressionRegistry.CreateMap{TSource,TDestination}"/> calls.
/// </summary>
public interface IMapperProfile
{
    /// <summary>Registers mappings into the provided <paramref name="registry"/>.</summary>
    void Register(MappingExpressionRegistry registry);
}
