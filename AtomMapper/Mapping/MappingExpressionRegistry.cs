using System.Reflection;

namespace AtomMapper;

/// <summary>
/// Stores mapping configurations during startup. Pass an instance to <see cref="IMapperProfile.Register"/>
/// and call <see cref="MapperFactory.Create"/> to compile all registered mappings.
/// </summary>
public sealed class MappingExpressionRegistry
{
    private readonly List<Action> _pending = [];

    // Keyed by (sourceType, destType) — used by Map<TDestination>(object) where source type is only known at runtime.
    private static readonly Dictionary<(Type, Type), Func<object, object>> _runtimeMaps = [];

    /// <summary>
    /// Registers a mapping from <typeparamref name="TSource"/> to <typeparamref name="TDestination"/>.
    /// Returns a fluent <see cref="MappingExpression{TSource,TDestination}"/> for further configuration.
    /// </summary>
    public MappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>()
        where TDestination : new()
    {
        var expression = new MappingExpression<TSource, TDestination>();
        _pending.Add(() => StoreMapping<TSource, TDestination>(expression.Build()));
        return expression;
    }

    internal void Commit()
    {
        foreach (var action in _pending) action();
        _pending.Clear();
    }

    private static void StoreMapping<TSource, TDestination>(CompiledMapping<TSource, TDestination> compiled)
        where TDestination : new()
    {
        MappingCache<TSource, TDestination>.Create = compiled.Create;
        MappingCache<TSource, TDestination>.Update = compiled.Update;

        var create = compiled.Create;
        _runtimeMaps[(typeof(TSource), typeof(TDestination))] = src => create((TSource)src)!;

        if (compiled.HasReverseMap)
            StoreReverseMapping<TSource, TDestination>();
    }

    private static void StoreReverseMapping<TSource, TDestination>()
        where TDestination : new()
    {
        // Convention-only reverse — custom ForMember rules are not reversed automatically.
        var compiled = new MappingExpression<TDestination, TSource>().Build();
        MappingCache<TDestination, TSource>.Create = compiled.Create;
        _runtimeMaps[(typeof(TDestination), typeof(TSource))] =
            src => compiled.Create((TDestination)src)!;
    }

    internal static Func<TSource, TDestination> Resolve<TSource, TDestination>()
    {
        if (MappingCache<TSource, TDestination>.Create is { } direct)
            return direct;

        var srcElem = GetEnumerableElementType(typeof(TSource));
        var destElem = GetEnumerableElementType(typeof(TDestination));
        if (srcElem is not null && destElem is not null)
        {
            var func = (Func<TSource, TDestination>)typeof(MappingExpressionRegistry)
                .GetMethod(nameof(BuildCollectionFunc), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(typeof(TSource), typeof(TDestination), srcElem, destElem)
                .Invoke(null, null)!;
            MappingCache<TSource, TDestination>.Create = func;
            return func;
        }

        throw new InvalidOperationException(
            $"No mapping registered: {typeof(TSource).Name} → {typeof(TDestination).Name}");
    }

    internal static Func<object, object> ResolveRuntime(Type sourceType, Type destType)
    {
        if (_runtimeMaps.TryGetValue((sourceType, destType), out var map))
            return map;

        var srcElem = GetEnumerableElementType(sourceType);
        var destElem = GetEnumerableElementType(destType);
        if (srcElem is not null && destElem is not null)
        {
            var func = (Func<object, object>)typeof(MappingExpressionRegistry)
                .GetMethod(nameof(BuildRuntimeCollectionFunc), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(sourceType, destType, srcElem, destElem)
                .Invoke(null, null)!;
            _runtimeMaps[(sourceType, destType)] = func;
            return func;
        }

        throw new InvalidOperationException(
            $"No mapping registered: {sourceType.Name} → {destType.Name}");
    }

    internal static Action<TSource, TDestination> ResolveUpdate<TSource, TDestination>()
        => MappingCache<TSource, TDestination>.Update
           ?? throw new InvalidOperationException(
               $"No update mapping registered: {typeof(TSource).Name} → {typeof(TDestination).Name}");

    // Builds a strongly-typed Func<TCol, TDestCol> for collection-to-collection mapping,
    // reusing the already-compiled per-element mapper from MappingCache.
    private static Func<TCol, TDestCol> BuildCollectionFunc<TCol, TDestCol, TElem, TDestElem>()
        where TCol : IEnumerable<TElem>
    {
        var elemMapper = MappingCache<TElem, TDestElem>.Create
            ?? throw new InvalidOperationException(
                $"No mapping registered for element type: {typeof(TElem).Name} → {typeof(TDestElem).Name}");

        var destType = typeof(TDestCol);
        if (destType == typeof(TDestElem[]))
            return col => (TDestCol)(object)col.Select(elemMapper).ToArray();

        if (destType == typeof(List<TDestElem>) ||
            destType == typeof(IList<TDestElem>) ||
            destType == typeof(ICollection<TDestElem>))
            return col => (TDestCol)(object)col.Select(elemMapper).ToList();

        // IEnumerable<TDestElem> or any compatible type — materialize to avoid deferred-execution surprises
        return col => (TDestCol)(object)col.Select(elemMapper).ToList();
    }

    // Same as above but boxing source/dest to object for the runtime-dispatch path.
    private static Func<object, object> BuildRuntimeCollectionFunc<TCol, TDestCol, TElem, TDestElem>()
        where TCol : IEnumerable<TElem>
    {
        var elemMapper = MappingCache<TElem, TDestElem>.Create
            ?? throw new InvalidOperationException(
                $"No mapping registered for element type: {typeof(TElem).Name} → {typeof(TDestElem).Name}");

        return src => (object)((TCol)src).Select(elemMapper).ToList();
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(IEnumerable<>) || def == typeof(ICollection<>) ||
                def == typeof(IList<>) || def == typeof(List<>))
                return type.GetGenericArguments()[0];
        }

        return type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                                 i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }
}

// One static slot per (TSource, TDestination) pair — O(1) lookup with no hashing.
// These are process-global: only one mapper configuration per type pair is supported.
internal static class MappingCache<TSource, TDestination>
{
    internal static Func<TSource, TDestination>? Create;
    internal static Action<TSource, TDestination>? Update;
}
