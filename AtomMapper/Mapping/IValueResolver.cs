namespace AtomMapper;

/// <summary>
/// Implement to supply a destination member value from the full source and destination objects.
/// Register via <see cref="MemberOptions{TSource,TDestination,TMember}.MapFrom{TValueResolver}()"/>.
/// </summary>
public interface IValueResolver<TSource, TDestination, TMember>
{
    TMember Resolve(TSource source, TDestination destination, TMember destMember, ResolutionContext context);
}
