using System.Linq.Expressions;
using System.Reflection;

namespace AtomMapper;

/// <summary>
/// Fluent builder for configuring how <typeparamref name="TSource"/> maps to <typeparamref name="TDestination"/>.
/// Obtained from <see cref="MappingExpressionRegistry.CreateMap{TSource,TDestination}"/>.
/// </summary>
public sealed class MappingExpression<TSource, TDestination>
{
    private readonly List<(PropertyInfo DestProp, LambdaExpression MapFrom)> _mapExpressions = [];
    private readonly HashSet<string> _ignoredMembers = [];
    private readonly HashSet<string> _mappedMembers = [];
    private bool _reverseMap;

    /// <summary>
    /// Configures how a specific destination member is mapped.
    /// Use <see cref="MemberOptions{TSource,TMember}.MapFrom"/> or <see cref="MemberOptions{TSource,TMember}.Ignore"/>.
    /// </summary>
    public MappingExpression<TSource, TDestination> ForMember<TMember>(
        Expression<Func<TDestination, TMember>> destMember,
        Action<MemberOptions<TSource, TMember>> configure)
    {
        var memberName = GetMemberName(destMember);
        var options = new MemberOptions<TSource, TMember>();
        configure(options);

        if (options.IsIgnored)
        {
            _ignoredMembers.Add(memberName);
            return this;
        }

        if (options.MapFromExpression is not null)
        {
            _mappedMembers.Add(memberName);
            var destProp = typeof(TDestination).GetProperty(memberName,
                BindingFlags.Public | BindingFlags.Instance)!;
            _mapExpressions.Add((destProp, options.MapFromExpression));
        }

        return this;
    }

    /// <summary>
    /// Generates a convention-only reverse mapping from <typeparamref name="TDestination"/>
    /// back to <typeparamref name="TSource"/>. Custom <see cref="ForMember"/> rules are not reversed.
    /// </summary>
    public MappingExpression<TSource, TDestination> ReverseMap()
    {
        _reverseMap = true;
        return this;
    }

    internal CompiledMapping<TSource, TDestination> Build()
    {
        var srcParam = Expression.Parameter(typeof(TSource), "src");
        var destParam = Expression.Parameter(typeof(TDestination), "dest");

        var assignments = BuildAssignments(srcParam, destParam);

        // Action — used for in-place / update mapping
        Action<TSource, TDestination> update = assignments.Count == 0
            ? static (_, _) => { }
        : Expression.Lambda<Action<TSource, TDestination>>(
                Expression.Block(assignments), srcParam, destParam).Compile();

        // Func — allocates TDestination and runs all assignments in one compiled call
        var destVar = Expression.Variable(typeof(TDestination), "dest");
        var createBody = new List<Expression>(assignments.Count + 2)
        {
            Expression.Assign(destVar, Expression.New(typeof(TDestination)))
        };
        var replacer = new ParameterReplacer(destParam, destVar);
        foreach (var a in assignments)
            createBody.Add(replacer.Visit(a)!);
        createBody.Add(destVar);

        var create = Expression.Lambda<Func<TSource, TDestination>>(
            Expression.Block([destVar], createBody), srcParam).Compile();

        return new CompiledMapping<TSource, TDestination>(create, update, _reverseMap);
    }

    private List<Expression> BuildAssignments(
        ParameterExpression srcParam, ParameterExpression destParam)
    {
        var srcType = typeof(TSource);
        var destType = typeof(TDestination);
        var list = new List<Expression>();

        foreach (var destProp in destType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite))
        {
            if (_ignoredMembers.Contains(destProp.Name)) continue;
            if (_mappedMembers.Contains(destProp.Name)) continue;

            var srcProp = srcType.GetProperty(destProp.Name, BindingFlags.Public | BindingFlags.Instance);
            if (srcProp is null) continue;

            // 1. Direct assignment — types are compatible
            if (destProp.PropertyType.IsAssignableFrom(srcProp.PropertyType))
            {
                list.Add(Expression.Assign(
                    Expression.Property(destParam, destProp),
                    Expression.Property(srcParam, srcProp)));
                continue;
            }

            // 2. Nested object — different class types; resolved via MappingCache at runtime
            if (srcProp.PropertyType.IsClass && destProp.PropertyType.IsClass &&
                GetEnumerableElementType(srcProp.PropertyType) is null &&
                GetEnumerableElementType(destProp.PropertyType) is null)
            {
                var nestedCacheType = typeof(MappingCache<,>).MakeGenericType(srcProp.PropertyType, destProp.PropertyType);
                var nestedCacheField = nestedCacheType.GetField(nameof(MappingCache<object, object>.Create),
                                           BindingFlags.Static | BindingFlags.NonPublic)!;

                var nestedSrc = Expression.Property(srcParam, srcProp);
                var nestedMapper = Expression.Field(null, nestedCacheField);
                var nestedCall = Expression.Invoke(nestedMapper, nestedSrc);
                // Skip if source is null OR no mapping is registered for this nested type pair.
                var nestedGuard = Expression.Condition(
                    Expression.OrElse(
                        Expression.Equal(nestedSrc, Expression.Constant(null, srcProp.PropertyType)),
                        Expression.Equal(nestedMapper, Expression.Constant(null, nestedMapper.Type))),
                    Expression.Default(destProp.PropertyType),
                    nestedCall);

                list.Add(Expression.Assign(Expression.Property(destParam, destProp), nestedGuard));
                continue;
            }

            // 3. Collection of mapped elements — e.g. IEnumerable<TItem> → IEnumerable<TItemDto>
            var srcElem = GetEnumerableElementType(srcProp.PropertyType);
            var destElem = GetEnumerableElementType(destProp.PropertyType);
            if (srcElem is null || destElem is null || srcElem == destElem) continue;

            var cacheType = typeof(MappingCache<,>).MakeGenericType(srcElem, destElem);
            var cacheField = cacheType.GetField(nameof(MappingCache<object, object>.Create),
                                 BindingFlags.Static | BindingFlags.NonPublic)!;

            var selectMethod = typeof(Enumerable)
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .First(m => m.Name == nameof(Enumerable.Select) &&
                            m.GetParameters() is [_, { ParameterType.IsGenericType: true }] ps &&
                            ps[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>))
                .MakeGenericMethod(srcElem, destElem);

            var srcAccess = Expression.Property(srcParam, srcProp);
            var mapperRead = Expression.Field(null, cacheField);
            var selectCall = Expression.Call(selectMethod, srcAccess, mapperRead);
            var converted = BuildCollectionConversion(selectCall, destProp.PropertyType, destElem);
            // Skip if source is null OR no element mapping is registered.
            var guard = Expression.Condition(
                Expression.OrElse(
                    Expression.Equal(srcAccess, Expression.Constant(null, srcProp.PropertyType)),
                    Expression.Equal(mapperRead, Expression.Constant(null, mapperRead.Type))),
                Expression.Default(destProp.PropertyType),
                converted);

            list.Add(Expression.Assign(Expression.Property(destParam, destProp), guard));
        }

        // AtomMapper ForMember expressions — inlined directly into the compiled lambda
        foreach (var (destProp, mapFrom) in _mapExpressions)
        {
            var inlined = new ParameterReplacer(mapFrom.Parameters[0], srcParam).Visit(mapFrom.Body)!;
            list.Add(Expression.Assign(Expression.Property(destParam, destProp), inlined));
        }

        return list;
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

    private static Expression BuildCollectionConversion(Expression enumerable, Type destType, Type destElem)
    {
        if (destType.IsArray)
            return Expression.Call(
                typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray))!.MakeGenericMethod(destElem),
                enumerable);

        if (destType == typeof(List<>).MakeGenericType(destElem) ||
            destType == typeof(IList<>).MakeGenericType(destElem) ||
            destType == typeof(ICollection<>).MakeGenericType(destElem))
            return Expression.Call(
                typeof(Enumerable).GetMethod(nameof(Enumerable.ToList))!.MakeGenericMethod(destElem),
                enumerable);

        return enumerable; // IEnumerable<T> or compatible — return as-is
    }

    private static string GetMemberName<T, TMember>(Expression<Func<T, TMember>> expr)
        => expr.Body is MemberExpression m
            ? m.Member.Name
            : throw new ArgumentException("Expression must be a member access.", nameof(expr));
}

file sealed class ParameterReplacer(ParameterExpression target, Expression replacement) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node)
        => node == target ? replacement : base.VisitParameter(node);
}
