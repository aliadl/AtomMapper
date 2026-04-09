using MapperTest.Domain;
using Riok.Mapperly.Abstractions;

[Mapper]
public partial class MapperlyEntryMapper
{
    // Public entry point — handles conditional logic that Mapperly can't express declaratively
    public TransactionEntryDto Map(Entry source)
    {
        var dto = MapCore(source);
        dto.DebitAmount = source.Amount > 0 ? source.Amount : 0;
        dto.CreditAmount = source.Amount < 0 ? Math.Abs(source.Amount) : 0;
        return dto;
    }

    public List<TransactionEntryDto> MapList(IEnumerable<Entry> source)
        => source.Select(Map).ToList();

    // Generated: copies Id and CurrencyCode (via nested path); DebitAmount/CreditAmount are ignored
    [MapProperty("Currency.Symbol", "CurrencyCode")]
    [MapperIgnoreTarget("DebitAmount")]
    [MapperIgnoreTarget("CreditAmount")]
    private partial TransactionEntryDto MapCore(Entry source);
}
