using System.Linq.Expressions;
using System.Reflection;

namespace AtomMapper;

/// <summary>
/// Fluent builder for configuring how <typeparamref name="TSource"/> maps to <typeparamref name="TDestination"/>.
/// Obtained from <see cref="MappingExpressionRegistry.CreateMap{TSource,TDestination}"/>.
/// </summary>
public sealed class MappingExpression<TSource, TDestination>
{
    private readonly List<(PropertyInfo DestProp, Func<ParameterExpression, ParameterExpression, Expression> Factory)>
        _memberMappings = [];
    private readonly HashSet<string> _ignoredMembers = [];
    private readonly HashSet<string> _mappedMembers = [];
    private bool _reverseMap;

    /// <summary>
    /// Configures how a specific destination member is mapped.
    /// Use <see cref="MemberOptions{TSource,TDestination,TMember}.MapFrom(Expression{Func{TSource,TMember}})"/>,
    /// one of its overloads, or <see cref="MemberOptions{TSource,TDestination,TMember}.Ignore"/>.
    /// </summary>
    public MappingExpression<TSource, TDestination> ForMember<TMember>(
        Expression<Func<TDestination, TMember>> destMember,
        Action<MemberOptions<TSource, TDestination, TMember>> configure)
    {
        var memberName = GetMemberName(destMember);
        var options = new MemberOptions<TSource, TDestination, TMember>();
        configure(options);

        if (options.IsIgnored)
        {
            _ignoredMembers.Add(memberName);
            return this;
        }

        if (options.ExpressionFactory is not null)
        {
            _mappedMembers.Add(memberName);
            var destProp = typeof(TDestination).GetProperty(memberName,
                BindingFlags.Public | BindingFlags.Instance)!;
            _memberMappings.Add((destProp, options.ExpressionFactory));
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

        if (typeof(TDestination).IsValueType || typeof(TDestination).GetConstructor(Type.EmptyTypes) is not null)
        {
            // Parameterless constructor (or value type, which supports Expression.New with no ctor) —
            // allocate then apply property assignments.
            var assignments = BuildAssignments(srcParam, destParam);

            Action<TSource, TDestination> update = assignments.Count == 0
                ? static (_, _) => { }
                : Expression.Lambda<Action<TSource, TDestination>>(
                    Expression.Block(assignments), srcParam, destParam).Compile();

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
        else
        {
            // No parameterless constructor — delegate to the primary (longest) public constructor.
            // In-place update is not supported for constructor-bound types.
            var ctor = FindPrimaryConstructor(typeof(TDestination));
            var ctorExpr = BuildConstructorExpression(ctor, srcParam, destParam);
            var create = Expression.Lambda<Func<TSource, TDestination>>(ctorExpr, srcParam).Compile();
            return new CompiledMapping<TSource, TDestination>(create, null, _reverseMap);
        }
    }

    private static ConstructorInfo FindPrimaryConstructor(Type type)
    {
        var ctors = type.GetConstructors();
        if (ctors.Length == 0)
            throw new InvalidOperationException(
                $"Type '{type.Name}' has no public constructor. " +
                "Destination types must have either a parameterless constructor or a public primary constructor.");
        return ctors.MaxBy(c => c.GetParameters().Length)!;
    }

    // Builds Expression.New(ctor, ...) by resolving each parameter via:
    //   1. ForMember factory (matched case-insensitively to the property name)
    //   2. Source property convention (same name, assignable type)
    //   3. Constructor parameter's declared default value
    //   4. Expression.Default (null / 0)
    private Expression BuildConstructorExpression(
        ConstructorInfo ctor,
        ParameterExpression srcParam,
        ParameterExpression destParam)
    {
        var srcType = typeof(TSource);
        var memberFactories = _memberMappings.ToDictionary(
            m => m.DestProp.Name,
            m => m.Factory,
            StringComparer.OrdinalIgnoreCase);

        var args = new List<Expression>(ctor.GetParameters().Length);
        foreach (var param in ctor.GetParameters())
        {
            var name = param.Name!;
            var ignored = _ignoredMembers.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

            if (!ignored && memberFactories.TryGetValue(name, out var factory))
            {
                args.Add(factory(srcParam, destParam));
                continue;
            }

            if (!ignored)
            {
                var srcProp = srcType.GetProperty(name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (srcProp is not null)
                {
                    var ctorSrcUnderlying  = Nullable.GetUnderlyingType(srcProp.PropertyType);
                    var ctorDestUnderlying = Nullable.GetUnderlyingType(param.ParameterType);

                    // T? → T: unwrap; null becomes default(T)
                    if (ctorSrcUnderlying is not null && ctorSrcUnderlying == param.ParameterType)
                    {
                        var getValueOrDefault = srcProp.PropertyType
                            .GetMethod(nameof(Nullable<int>.GetValueOrDefault), Type.EmptyTypes)!;
                        args.Add(Expression.Call(Expression.Property(srcParam, srcProp), getValueOrDefault));
                        continue;
                    }

                    // T → T?: wrap via the Nullable<T>(T) constructor
                    if (ctorDestUnderlying is not null && ctorDestUnderlying == srcProp.PropertyType)
                    {
                        var nullableCtor = param.ParameterType.GetConstructor([srcProp.PropertyType])!;
                        args.Add(Expression.New(nullableCtor, Expression.Property(srcParam, srcProp)));
                        continue;
                    }

                    if (param.ParameterType.IsAssignableFrom(srcProp.PropertyType))
                    {
                        args.Add(Expression.Property(srcParam, srcProp));
                        continue;
                    }
                }
            }

            args.Add(param.HasDefaultValue
                ? MakeDefaultConstant(param.DefaultValue, param.ParameterType)
                : Expression.Default(param.ParameterType));
        }

        return Expression.New(ctor, args);
    }

    // Expression.Constant requires value.GetType() == type exactly.
    // Two reflection quirks need fixing:
    //   1. Nullable<T> params: DefaultValue is returned as T, not Nullable<T>.
    //   2. Struct params with = default: DefaultValue is null, but structs can't be null constants.
    private static Expression MakeDefaultConstant(object? value, Type type)
    {
        if (value is null && type.IsValueType && Nullable.GetUnderlyingType(type) is null)
            return Expression.Default(type);

        if (value is not null
            && Nullable.GetUnderlyingType(type) is { } underlying
            && value.GetType() == underlying)
        {
            return Expression.Convert(Expression.Constant(value, underlying), type);
        }

        return Expression.Constant(value, type);
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

            // 1. Nullable compatibility checked first — CLR's IsAssignableFrom returns true for
            //    T → Nullable<T> but Expression.Assign does not honour that special case.
            var srcUnderlying  = Nullable.GetUnderlyingType(srcProp.PropertyType);
            var destUnderlying = Nullable.GetUnderlyingType(destProp.PropertyType);

            // 1a. T? → T: unwrap; null source becomes default(T)
            if (srcUnderlying is not null && srcUnderlying == destProp.PropertyType)
            {
                var getValueOrDefault = srcProp.PropertyType
                    .GetMethod(nameof(Nullable<int>.GetValueOrDefault), Type.EmptyTypes)!;
                list.Add(Expression.Assign(
                    Expression.Property(destParam, destProp),
                    Expression.Call(Expression.Property(srcParam, srcProp), getValueOrDefault)));
                continue;
            }

            // 1b. T → T?: wrap via the Nullable<T>(T) constructor
            if (destUnderlying is not null && destUnderlying == srcProp.PropertyType)
            {
                var nullableCtor = destProp.PropertyType.GetConstructor([srcProp.PropertyType])!;
                list.Add(Expression.Assign(
                    Expression.Property(destParam, destProp),
                    Expression.New(nullableCtor, Expression.Property(srcParam, srcProp))));
                continue;
            }

            // 1c. Direct assignment — types are compatible
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
            // Expression.Condition requires both branches to share the same type.
            // BuildCollectionConversion may return e.g. List<T> for an ICollection<T> destination,
            // so coerce to destProp.PropertyType when they differ.
            Expression falseExpr = converted.Type != destProp.PropertyType
                ? Expression.Convert(converted, destProp.PropertyType)
                : converted;
            // Skip if source is null OR no element mapping is registered.
            var guard = Expression.Condition(
                Expression.OrElse(
                    Expression.Equal(srcAccess, Expression.Constant(null, srcProp.PropertyType)),
                    Expression.Equal(mapperRead, Expression.Constant(null, mapperRead.Type))),
                Expression.Default(destProp.PropertyType),
                falseExpr);

            list.Add(Expression.Assign(Expression.Property(destParam, destProp), guard));
        }

        // ForMember expressions — call each factory with the current src/dest parameters
        foreach (var (destProp, factory) in _memberMappings)
        {
            var rhs = factory(srcParam, destParam);
            list.Add(Expression.Assign(Expression.Property(destParam, destProp), rhs));
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
