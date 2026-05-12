namespace AtomMapper;

/// <summary>
/// Implement to supply a destination member value from a specific source member.
/// Register via <see cref="MemberOptions{TSource,TDestination,TMember}.MapFrom{TValueResolver,TSourceMember}"/>.
/// </summary>
public interface IMemberValueResolver<TSource, TDestination, TSourceMember, TMember>
{
    TMember Resolve(TSource source, TDestination destination, TSourceMember sourceMember, TMember destMember, ResolutionContext context);
}
