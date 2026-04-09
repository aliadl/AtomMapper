namespace AtomMapper;

internal sealed class CompiledMapping<TSource, TDestination>(
    Func<TSource, TDestination> create,
    Action<TSource, TDestination> update,
    bool hasReverseMap)
{
    internal Func<TSource, TDestination> Create { get; } = create;
    internal Action<TSource, TDestination> Update { get; } = update;
    internal bool HasReverseMap { get; } = hasReverseMap;
}
