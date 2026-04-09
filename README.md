# AtomMapper

A high-performance, lightweight object mapper for .NET — designed to feel familiar to AutoMapper users while delivering significantly better runtime performance.

[![NuGet](https://img.shields.io/nuget/v/AtomMapper)](https://www.nuget.org/packages/AtomMapper)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4)](https://dotnet.microsoft.com)

---

## Why AtomMapper?

If you have been using **AutoMapper**, you already know the pattern: define profiles, call `CreateMap`, use `ForMember`. That API is intuitive and productive.

However, AutoMapper introduced a commercial licensing model in v13, which pushed many teams to look for alternatives. AtomMapper was built to fill that gap — offering the **same familiar API** so migration is straightforward, while being fully **open-source under the MIT license**.

Beyond licensing, AtomMapper takes a different technical approach. Instead of resolving mappings through reflection at every call, AtomMapper **compiles all expression trees once at startup** into direct `Func<TSource, TDestination>` delegates. At call time, there is no reflection, no dictionary traversal, and no delegate chain — just a single compiled function invocation. This makes AtomMapper measurably faster than AutoMapper across all scenarios.

---

## Benchmark

Measured with BenchmarkDotNet on .NET 10, mapping `Entry → TransactionEntryDto` (3 custom members + 1 convention member):

| Mapper          | Single object | 100 objects  | Allocated (100) |
|-----------------|--------------|--------------|-----------------|
| **AtomMapper**  | **13 ns**    | **967 ns**   | **7,256 B**     |
| AutoMapper      | 33 ns        | 1,415 ns     | 8,592 B         |
| Mapster         | 14 ns        | 960 ns       | 7,256 B         |
| Mapperly*       | 6 ns         | 851 ns       | 7,392 B         |

> *Mapperly is a compile-time source generator — it emits plain C# methods that the JIT can inline. AtomMapper is the fastest **runtime** mapper.

---

## Features

- **Convention-based mapping** — properties with matching names and compatible types are mapped automatically
- **Custom member mapping** — override any member with a `MapFrom` expression
- **Ignore members** — exclude destination members from mapping
- **Reverse mapping** — generate a convention-based reverse map with a single call
- **Nested object mapping** — automatically maps nested objects when a mapping for the nested types is registered
- **Collection mapping** — maps `IEnumerable<T>`, `List<T>`, `ICollection<T>`, and arrays of mapped types
- **In-place / update mapping** — map onto an existing destination instance
- **Single type-parameter shorthand** — `Map<TDestination>(object)` infers the source type at runtime
- **Profile-based configuration** — group related mappings into profiles, just like AutoMapper
- **DI-friendly** — works with any DI container (Microsoft.Extensions.DI, Autofac, etc.)

---

## Installation

```bash
dotnet add package AtomMapper
```

---

## Getting Started

### 1. Define a profile

```csharp
using AtomMapper;

public class OrderMappingProfile : IMapperProfile
{
    public void Register(MappingExpressionRegistry registry)
    {
        registry.CreateMap<Order, OrderDto>()
            .ForMember(d => d.CustomerFullName,
                       o => o.MapFrom(s => $"{s.FirstName} {s.LastName}"))
            .ForMember(d => d.TotalPrice,
                       o => o.MapFrom(s => s.Items.Sum(i => i.Price)));
    }
}
```

### 2. Create the mapper

```csharp
IMapper mapper = MapperFactory.Create(
    new OrderMappingProfile(),
    new ProductMappingProfile()
);
```

> Build the mapper **once at startup** and reuse it for the lifetime of your application. All expression trees are compiled during `Create` — mapping calls themselves have near-zero overhead.

### 3. Map objects

```csharp
// Map to a new instance
var dto = mapper.Map<Order, OrderDto>(order);

// Shorthand — infers source type at runtime
var dto = mapper.Map<OrderDto>(order);

// Map a collection — works for any registered element type
IEnumerable<OrderDto> dtos = mapper.Map<IEnumerable<Order>, IEnumerable<OrderDto>>(orders);

// Ask for a concrete collection type directly
List<OrderDto> list = mapper.Map<IEnumerable<Order>, List<OrderDto>>(orders);

// Update an existing instance in-place
mapper.Map(order, existingDto);
```

---

## Feature Guide

### Convention mapping

Properties that share the same name and a compatible type are mapped automatically — no configuration needed.

```csharp
public class Product { public int Id { get; set; } public string Name { get; set; } }
public class ProductDto { public int Id { get; set; } public string Name { get; set; } }

registry.CreateMap<Product, ProductDto>(); // Id and Name mapped automatically
```

### Custom member mapping with `MapFrom`

Use `ForMember` + `MapFrom` to override how a specific destination member is populated. The expression is compiled into the mapping function — no reflection at call time.

```csharp
registry.CreateMap<Entry, TransactionEntryDto>()
    .ForMember(d => d.CurrencyCode,  o => o.MapFrom(s => s.Currency.Symbol))
    .ForMember(d => d.DebitAmount,   o => o.MapFrom(s => s.Amount > 0 ? s.Amount : 0))
    .ForMember(d => d.CreditAmount,  o => o.MapFrom(s => s.Amount < 0 ? Math.Abs(s.Amount) : 0));
```

### Ignoring members

Excluded members retain their default value in the destination object.

```csharp
registry.CreateMap<ServiceCostDto, ServiceCost>()
    .ForMember(d => d.ServiceType, o => o.Ignore());
```

### Reverse mapping

`ReverseMap` generates a convention-based reverse mapping from `TDestination` back to `TSource`. Custom `ForMember` rules are intentionally not reversed.

```csharp
registry.CreateMap<Quotation, QuotationDto>()
    .ForMember(d => d.ReserveNo, o => o.MapFrom(s => s.ConvertedToReserve!.ReserveNO))
    .ReverseMap(); // also registers QuotationDto → Quotation (convention only)

var quotation = mapper.Map<QuotationDto, Quotation>(dto);
```

### Nested object mapping

Register a mapping for the nested types and AtomMapper will use it automatically when it encounters a property whose type has a registered mapping.

```csharp
registry.CreateMap<Order, OrderDto>();
registry.CreateMap<Address, AddressDto>(); // Order.ShippingAddress → OrderDto.ShippingAddress
```

No extra configuration required — `ShippingAddress` is detected and mapped automatically, with a null guard.

### Collection mapping

Collections of mapped types are handled automatically. The destination collection type is respected.

```csharp
// Source:      IEnumerable<OrderItem>
// Destination: List<OrderItemDto>
// Element mapping must be registered: CreateMap<OrderItem, OrderItemDto>()

registry.CreateMap<Order, OrderDto>();
registry.CreateMap<OrderItem, OrderItemDto>();

// Order.Items (IEnumerable<OrderItem>) → OrderDto.Items (List<OrderItemDto>) ✓
```

Supported destination collection types: `IEnumerable<T>`, `ICollection<T>`, `IList<T>`, `List<T>`, `T[]`.

### Mapping collections directly

Register the element mapping once and pass any compatible collection to `Map`:

```csharp
registry.CreateMap<Order, OrderDto>();

// Any of these work without extra registration:
IEnumerable<OrderDto> a = mapper.Map<IEnumerable<Order>, IEnumerable<OrderDto>>(orders);
List<OrderDto>        b = mapper.Map<IEnumerable<Order>, List<OrderDto>>(orders);
OrderDto[]            c = mapper.Map<IEnumerable<Order>, OrderDto[]>(orders);
```

Supported destination collection types: `IEnumerable<T>`, `ICollection<T>`, `IList<T>`, `List<T>`, `T[]`.

### In-place mapping

Map onto an existing instance, updating only the convention-mapped properties.

```csharp
mapper.Map(command, existingEntity);
```

---

## Dependency Injection

### Microsoft.Extensions.DependencyInjection

```csharp
builder.Services.AddSingleton<IMapper>(_ => MapperFactory.Create(
    new OrderMappingProfile(),
    new ProductMappingProfile()
));
```

### Autofac

```csharp
builder.Register(_ => MapperFactory.Create(
    Assembly.GetExecutingAssembly()
        .GetTypes()
        .Where(t => !t.IsAbstract && t.IsAssignableTo(typeof(IMapperProfile)))
        .Select(t => (IMapperProfile)Activator.CreateInstance(t)!)
        .ToArray()
)).As<IMapper>().SingleInstance();
```

---

## Migrating from AutoMapper

AtomMapper is intentionally designed to minimise migration friction. The table below shows the equivalent API:

| AutoMapper | AtomMapper |
|---|---|
| `cfg.CreateMap<Src, Dest>()` | `registry.CreateMap<Src, Dest>()` |
| `.ForMember(d => d.Prop, o => o.MapFrom(s => ...))` | identical |
| `.ForMember(d => d.Prop, o => o.Ignore())` | identical |
| `.ReverseMap()` | identical |
| `mapper.Map<Dest>(source)` | identical |
| `mapper.Map<Src, Dest>(source)` | identical |
| `mapper.Map(source, dest)` | identical |
| `mapper.Map<IEnumerable<Dest>>(source)` | `mapper.Map<IEnumerable<Src>, IEnumerable<Dest>>(source)` |
| `new MapperConfiguration(cfg => ...)` | `MapperFactory.Create(profiles)` |
| `Profile` base class | `IMapperProfile` interface |

The main difference is that AutoMapper uses a `Profile` base class while AtomMapper uses an `IMapperProfile` interface, and configuration is passed directly to `MapperFactory.Create` rather than a `MapperConfiguration` wrapper.

---

## License

MIT © Ali
