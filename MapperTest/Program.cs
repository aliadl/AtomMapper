using Autofac;
using MapperTest.Domain;
using AtomMapper;
using System.Reflection;

// ── Bootstrap ─────────────────────────────────────────────────────────────────

var builder = new ContainerBuilder();

builder.Register(_ =>
{
    var profiles = Assembly.GetExecutingAssembly()
        .GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false }
                 && t.IsAssignableTo(typeof(IMapperProfile)))
        .Select(t => (IMapperProfile)Activator.CreateInstance(t)!)
        .ToArray();

    return MapperFactory.Create(profiles);
}).As<IMapper>().SingleInstance();

var container = builder.Build();

IMapper mapper = container.Resolve<IMapper>();

// ── Sample 1: Custom ForMember with nested property + conditional logic ────────

Console.WriteLine("=== Entry → TransactionEntryDto ===");

var entry = new Entry
{
    Id = 1,
    Amount = -250.75m,
    Currency = new Currency { Symbol = "USD" }
};

var entryDto = mapper.Map<Entry, TransactionEntryDto>(entry);

Console.WriteLine($"  Id:           {entryDto.Id}");
Console.WriteLine($"  CurrencyCode: {entryDto.CurrencyCode}");   // nested: Currency.Symbol
Console.WriteLine($"  DebitAmount:  {entryDto.DebitAmount}");    // 0   (amount is negative)
Console.WriteLine($"  CreditAmount: {entryDto.CreditAmount}");   // 250.75

// ── Sample 2: ForMember with null-check + ReverseMap ──────────────────────────

Console.WriteLine("\n=== Quotation → QuotationDto ===");

var quotation = new Quotation
{
    Id = 42,
    CustomerName = "A Shipping Company",
    TotalAmount = 15_000m,
    ConvertedToReserve = new Reserve { ReserveNO = "RSV-2024-001" },
    Items = new[]
    {
        new QuotationItem { Id = 1, Description = "Freight", Amount = 10_000m  , Details = new QuotationItemDetail(){ GoodDescription = "CAR" } },
        new QuotationItem { Id = 2, Description = "Insurance", Amount = 5_000m , Details = new QuotationItemDetail() { GoodDescription = "Bicycle" } }
    }
};

var quotationDto = mapper.Map<Quotation, QuotationDto>(quotation);
var quotationDto2 = mapper.Map<QuotationDto>(quotation);

quotation.TotalAmount = 999;
quotation.Items.First().Amount = 999m;
mapper.Map(quotation, quotationDto2);

Console.WriteLine($"  Id:           {quotationDto.Id}");
Console.WriteLine($"  CustomerName: {quotationDto.CustomerName}");
Console.WriteLine($"  TotalAmount:  {quotationDto.TotalAmount}");
Console.WriteLine($"  ReserveNo:    {quotationDto.ReserveNo}");  // RSV-2024-001

Console.WriteLine("\n=== QuotationDto → Quotation (ReverseMap) ===");

var reversedQuotation = mapper.Map<QuotationDto, Quotation>(quotationDto);

Console.WriteLine($"  Id:           {reversedQuotation.Id}");
Console.WriteLine($"  CustomerName: {reversedQuotation.CustomerName}");
Console.WriteLine($"  TotalAmount:  {reversedQuotation.TotalAmount}");

// ── Sample 3: Ignore() ────────────────────────────────────────────────────────

Console.WriteLine("\n=== PurchaseServiceCostDto → PurchaseServiceCost (Ignore ServiceType) ===");

var costDto = new PurchaseServiceCostDto
{
    ServiceId = 10,
    Amount = 500m,
    ServiceType = "THIS_SHOULD_BE_IGNORED"
};

var cost = mapper.Map<PurchaseServiceCostDto, PurchaseServiceCost>(costDto);

Console.WriteLine($"  ServiceId:   {cost.ServiceId}");
Console.WriteLine($"  Amount:      {cost.Amount}");
Console.WriteLine($"  ServiceType: '{cost.ServiceType}'");  // empty — ignored

// ── Sample 4: Map onto existing instance ─────────────────────────────────────

Console.WriteLine("\n=== UpdatePurchaseCostPlanCommand → PurchaseCostPlan (Map onto existing) ===");

var command = new UpdatePurchaseCostPlanCommand
{
    PlanId = 99,
    Description = "Q1 Cost Plan"
};

var existingPlan = new PurchaseCostPlan { PlanId = 0, Description = "OLD" };
mapper.Map(command, existingPlan);

Console.WriteLine($"  PlanId:      {existingPlan.PlanId}");
Console.WriteLine($"  Description: {existingPlan.Description}");

// ── Sample 5: MapList ─────────────────────────────────────────────────────────

Console.WriteLine("\n=== IEnumerable<Entry> → IEnumerable<TransactionEntryDto> ===");

var entries = new List<Entry>
{
    new() { Id = 1, Amount =  100m, Currency = new() { Symbol = "EUR" } },
    new() { Id = 2, Amount = -200m, Currency = new() { Symbol = "GBP" } },
    new() { Id = 3, Amount =  350m, Currency = new() { Symbol = "USD" } },
};

var dtos = mapper.Map<IEnumerable<Entry>, IEnumerable<TransactionEntryDto>>(entries).ToList();

foreach (var dto in dtos)
    Console.WriteLine($"  [{dto.Id}] {dto.CurrencyCode} | Debit: {dto.DebitAmount,7} | Credit: {dto.CreditAmount,7}");
