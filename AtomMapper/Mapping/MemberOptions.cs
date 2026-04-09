using System.Linq.Expressions;

namespace AtomMapper;

/// <summary>
/// Configures how a single destination member is mapped.
/// </summary>
public sealed class MemberOptions<TSource, TMember>
{
    internal Expression<Func<TSource, TMember>>? MapFromExpression { get; private set; }
    internal bool IsIgnored { get; private set; }

    /// <summary>Maps this member using a custom expression evaluated against the source object.</summary>
    public MemberOptions<TSource, TMember> MapFrom(Expression<Func<TSource, TMember>> expression)
    {
        MapFromExpression = expression;
        return this;
    }

    /// <summary>Excludes this member from mapping; it will retain its default value.</summary>
    public MemberOptions<TSource, TMember> Ignore()
    {
        IsIgnored = true;
        return this;
    }
}
