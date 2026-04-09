namespace AtomMapper;

internal sealed class MapperService : IMapper
{
    public TDestination Map<TSource, TDestination>(TSource source)
        => MappingExpressionRegistry.Resolve<TSource, TDestination>()(source);

    public TDestination Map<TDestination>(object source)
        => (TDestination)MappingExpressionRegistry.ResolveRuntime(source.GetType(), typeof(TDestination))(source);

    public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
    {
        MappingExpressionRegistry.ResolveUpdate<TSource, TDestination>()(source, destination);
        return destination;
    }
}
