using AtomMapper;

namespace MapperTest.Domain;

public class TransactionEntryDtoMappingProfile : IMapperProfile
{
    public void Register(MappingExpressionRegistry registry)
    {
        registry.CreateMap<Entry, TransactionEntryDto>()
            .ForMember(e => e.CurrencyCode, c => c.MapFrom(p => p.Currency.Symbol))
            .ForMember(e => e.DebitAmount, c => c.MapFrom(p => p.Amount > 0 ? p.Amount : 0))
            .ForMember(e => e.CreditAmount, c => c.MapFrom(p => p.Amount < 0 ? Math.Abs(p.Amount) : 0));
    }
}

public class QuotationDtoMappingProfile : IMapperProfile
{
    public void Register(MappingExpressionRegistry registry)
    {
        registry.CreateMap<Quotation, QuotationDto>()
            .ForMember(q => q.ReserveNo, m => m.MapFrom(q =>
                q.ConvertedToReserve != null && !string.IsNullOrEmpty(q.ConvertedToReserve.ReserveNO)
                    ? q.ConvertedToReserve.ReserveNO
                    : null))
            .ReverseMap();

    }
}
public class QuotationItemDtoMappingProfile : IMapperProfile
{
    public void Register(MappingExpressionRegistry registry)
    {
        registry.CreateMap<QuotationItem, QuotationItemDto>()
            .ReverseMap();
        registry.CreateMap<QuotationItemDetail, QuotationItemDetailDto>()
    .ReverseMap();
    }
}

public class PurchaseCostPlanMappingProfile : IMapperProfile
{
    public void Register(MappingExpressionRegistry registry)
    {
        registry.CreateMap<UpdatePurchaseCostPlanCommand, PurchaseCostPlan>();

        registry.CreateMap<PurchaseServiceCostDto, PurchaseServiceCost>()
            .ForMember(m => m.ServiceType, c => c.Ignore());
    }
}
