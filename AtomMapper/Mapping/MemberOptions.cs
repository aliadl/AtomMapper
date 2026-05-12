using System.Linq.Expressions;
using System.Reflection;

namespace AtomMapper;

/// <summary>
/// Configures how a single destination member is mapped.
/// </summary>
public sealed class MemberOptions<TSource, TDestination, TMember>
{
    // Produces the right-hand-side expression for the member assignment.
    // (srcParam, destParam) → expression of type TMember
    internal Func<ParameterExpression, ParameterExpression, Expression>? ExpressionFactory { get; private set; }
    internal bool IsIgnored { get; private set; }

    /// <summary>
    /// Maps this member using an expression evaluated against the source object.
    /// </summary>
    public MemberOptions<TSource, TDestination, TMember> MapFrom(Expression<Func<TSource, TMember>> expression)
    {
        ExpressionFactory = (srcParam, _) =>
            new ParameterReplacer(expression.Parameters[0], srcParam).Visit(expression.Body)!;
        return this;
    }

    /// <summary>
    /// Maps this member from a differently-named or differently-typed source member,
    /// using the mapping registered between <typeparamref name="TSourceMember"/> and <typeparamref name="TMember"/>.
    /// </summary>
    public MemberOptions<TSource, TDestination, TMember> MapFrom<TSourceMember>(
        Expression<Func<TSource, TSourceMember>> expression)
    {
        ExpressionFactory = BuildCrossTypeFactory(expression);
        return this;
    }

    /// <summary>
    /// Maps this member using a custom value resolver. The resolver is instantiated once at configuration time.
    /// </summary>
    public MemberOptions<TSource, TDestination, TMember> MapFrom<TValueResolver>()
        where TValueResolver : IValueResolver<TSource, TDestination, TMember>, new()
    {
        var resolver = new TValueResolver();
        Func<TSource, TDestination, TMember> del =
            (src, dest) => resolver.Resolve(src, dest, default!, ResolutionContext.Default);
        ExpressionFactory = (srcParam, destParam) =>
            Expression.Invoke(Expression.Constant(del), srcParam, destParam);
        return this;
    }

    /// <summary>
    /// Maps this member using a member value resolver supplied with a specific source member.
    /// </summary>
    public MemberOptions<TSource, TDestination, TMember> MapFrom<TValueResolver, TSourceMember>(
        Expression<Func<TSource, TSourceMember>> sourceMember)
        where TValueResolver : IMemberValueResolver<TSource, TDestination, TSourceMember, TMember>, new()
    {
        var resolver = new TValueResolver();
        var compiled = sourceMember.Compile();
        Func<TSource, TDestination, TMember> del =
            (src, dest) => resolver.Resolve(src, dest, compiled(src), default!, ResolutionContext.Default);
        ExpressionFactory = (srcParam, destParam) =>
            Expression.Invoke(Expression.Constant(del), srcParam, destParam);
        return this;
    }

    /// <summary>
    /// Maps this member using a function with access to the source, destination, current member value, and context.
    /// <typeparamref name="TResult"/> must be assignable to <typeparamref name="TMember"/> at runtime.
    /// </summary>
    public MemberOptions<TSource, TDestination, TMember> MapFrom<TResult>(
        Func<TSource, TDestination, TMember, ResolutionContext, TResult> mappingFunction)
    {
        Func<TSource, TDestination, TMember> del =
            (src, dest) => (TMember)(object)mappingFunction(src, dest, default!, ResolutionContext.Default)!;
        ExpressionFactory = (srcParam, destParam) =>
            Expression.Invoke(Expression.Constant(del), srcParam, destParam);
        return this;
    }

    /// <summary>
    /// Maps this member from a dot-separated source property path, e.g. <c>"Address.City.Name"</c>.
    /// Null intermediate values produce the default value for the destination member.
    /// </summary>
    public MemberOptions<TSource, TDestination, TMember> MapFrom(string sourceMembersPath)
    {
        var parts = sourceMembersPath.Split('.');
        ExpressionFactory = (srcParam, _) =>
            BuildPathExpression(srcParam, typeof(TSource), parts, 0);
        return this;
    }

    /// <summary>Excludes this member from mapping; it will retain its default value.</summary>
    public MemberOptions<TSource, TDestination, TMember> Ignore()
    {
        IsIgnored = true;
        return this;
    }

    private static Func<ParameterExpression, ParameterExpression, Expression> BuildCrossTypeFactory<TSourceMember>(
        Expression<Func<TSource, TSourceMember>> expression)
    {
        return (srcParam, _) =>
        {
            // Same underlying type — inline directly, no registered mapping lookup needed.
            if (typeof(TSourceMember) == typeof(TMember))
                return new ParameterReplacer(expression.Parameters[0], srcParam).Visit(expression.Body)!;

            var cacheType = typeof(MappingCache<,>).MakeGenericType(typeof(TSourceMember), typeof(TMember));
            var cacheField = cacheType.GetField("Create", BindingFlags.Static | BindingFlags.NonPublic)!;
            var mapperExpr = Expression.Field(null, cacheField);
            var replaced = (Expression)new ParameterReplacer(expression.Parameters[0], srcParam)
                .Visit(expression.Body)!;

            if (typeof(TSourceMember).IsValueType)
            {
                // Value-type source: only guard against a missing mapping registration.
                return Expression.Condition(
                    Expression.Equal(mapperExpr, Expression.Constant(null, mapperExpr.Type)),
                    Expression.Default(typeof(TMember)),
                    Expression.Invoke(mapperExpr, replaced));
            }

            // Reference-type source: cache the intermediate value, guard null + missing mapping.
            var srcVar = Expression.Variable(typeof(TSourceMember), "srcMember");
            return Expression.Block(
                [srcVar],
                Expression.Assign(srcVar, replaced),
                Expression.Condition(
                    Expression.OrElse(
                        Expression.Equal(srcVar, Expression.Constant(null, typeof(TSourceMember))),
                        Expression.Equal(mapperExpr, Expression.Constant(null, mapperExpr.Type))),
                    Expression.Default(typeof(TMember)),
                    Expression.Invoke(mapperExpr, srcVar)));
        };
    }

    // Recursively builds null-guarded property-access chains for dot-path mapping.
    private static Expression BuildPathExpression(Expression current, Type currentType, string[] parts, int index)
    {
        if (index == parts.Length)
            return current;

        var prop = currentType.GetProperty(parts[index], BindingFlags.Public | BindingFlags.Instance)
            ?? throw new ArgumentException(
                $"Property '{parts[index]}' not found on '{currentType.Name}'.");

        var propAccess = Expression.Property(current, prop);

        if (index == parts.Length - 1)
            return propAccess;

        if (!prop.PropertyType.IsValueType)
        {
            var propVar = Expression.Variable(prop.PropertyType, parts[index]);
            var inner = BuildPathExpression(propVar, prop.PropertyType, parts, index + 1);
            return Expression.Block(
                [propVar],
                Expression.Assign(propVar, propAccess),
                Expression.Condition(
                    Expression.Equal(propVar, Expression.Constant(null, prop.PropertyType)),
                    Expression.Default(typeof(TMember)),
                    inner));
        }

        return BuildPathExpression(propAccess, prop.PropertyType, parts, index + 1);
    }
}
