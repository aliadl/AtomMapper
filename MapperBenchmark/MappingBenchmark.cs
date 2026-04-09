using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using MapperTest.Domain;
using Mapster;
using AtomMapper;
using AM = AutoMapper;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[ShortRunJob]
[IterationCount(4)]
[WarmupCount(1)]
public class MappingBenchmark
{
    private Entry _entry = null!;
    private List<Entry> _entries = null!;

    private IMapper _atom = null!;
    private AM.IMapper _autoMapper = null!;
    private MapperlyEntryMapper _mapperly = null!;
    private TypeAdapterConfig _mapsterConfig = null!;

    [Params(1, 100)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _entry = new Entry { Id = 1, Amount = -250.75m, Currency = new Currency { Symbol = "USD" } };

        _entries = Enumerable.Range(1, Count)
            .Select(i => new Entry
            {
                Id = i,
                Amount = i % 2 == 0 ? -(i * 10m) : i * 10m,
                Currency = new Currency { Symbol = "USD" }
            })
            .ToList();

        // AtomMapper
        _atom = MapperFactory.Create(
            new TransactionEntryDtoMappingProfile(),
            new QuotationDtoMappingProfile(),
            new PurchaseCostPlanMappingProfile()
        );

        // AutoMapper
        var amConfig = new AM.MapperConfiguration(cfg =>
            cfg.CreateMap<Entry, TransactionEntryDto>()
               .ForMember(d => d.CurrencyCode, o => o.MapFrom(s => s.Currency.Symbol))
               .ForMember(d => d.DebitAmount, o => o.MapFrom(s => s.Amount > 0 ? s.Amount : 0))
               .ForMember(d => d.CreditAmount, o => o.MapFrom(s => s.Amount < 0 ? Math.Abs(s.Amount) : 0))
        , Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        _autoMapper = amConfig.CreateMapper();

        // Mapperly (compile-time generated)
        _mapperly = new MapperlyEntryMapper();

        // Mapster
        _mapsterConfig = new TypeAdapterConfig();
        _mapsterConfig.NewConfig<Entry, TransactionEntryDto>()
            .Map(d => d.CurrencyCode, s => s.Currency.Symbol)
            .Map(d => d.DebitAmount, s => s.Amount > 0 ? s.Amount : 0)
            .Map(d => d.CreditAmount, s => s.Amount < 0 ? Math.Abs(s.Amount) : 0);
    }

    // ── Single object ────────────────────────────────────────────────────────

    [Benchmark(Baseline = true)]
    public TransactionEntryDto Atom() => _atom.Map<Entry, TransactionEntryDto>(_entry);

    [Benchmark]
    public TransactionEntryDto AutoMapper() => _autoMapper.Map<TransactionEntryDto>(_entry);

    [Benchmark]
    public TransactionEntryDto Mapperly() => _mapperly.Map(_entry);

    [Benchmark]
    public TransactionEntryDto Mapster() => _entry.Adapt<TransactionEntryDto>(_mapsterConfig);

    // ── List ─────────────────────────────────────────────────────────────────

    [Benchmark]
    public List<TransactionEntryDto> Atom_List() => _atom.Map<IEnumerable<Entry>, List<TransactionEntryDto>>(_entries);

    [Benchmark]
    public List<TransactionEntryDto> AutoMapper_List() => _autoMapper.Map<List<TransactionEntryDto>>(_entries);

    [Benchmark]
    public List<TransactionEntryDto> Mapperly_List() => _mapperly.MapList(_entries);

    [Benchmark]
    public List<TransactionEntryDto> Mapster_List() => _entries.Adapt<List<TransactionEntryDto>>(_mapsterConfig);
}
