namespace MapperTest.Domain;

// ── Shipping ──────────────────────────────────────────────────────────────────

public class Quotation
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = default!;
    public decimal TotalAmount { get; set; }
    public Reserve? ConvertedToReserve { get; set; }
    public IEnumerable<QuotationItem> Items { get; set; }
}
public class QuotationItem
{
    public int Id { get; set; }
    public string Description { get; set; } = default!;
    public decimal Amount { get; set; }
    public QuotationItemDetail Details { get; set; }
}
public class QuotationItemDetail
{
    public string GoodDescription { get; set; }
}
public class Reserve
{
    public string ReserveNO { get; set; } = default!;
}

public class QuotationDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = default!;
    public decimal TotalAmount { get; set; }
    public string? ReserveNo { get; set; }
    public IEnumerable<QuotationItemDto> Items { get; set; }

}
public class QuotationItemDto
{
    public int Id { get; set; }
    public string Description { get; set; } = default!;
    public decimal Amount { get; set; }
    public QuotationItemDetailDto Details { get; set; }

}

public class QuotationItemDetailDto
{
    public string GoodDescription { get; set; }
}
// ── Accounting ────────────────────────────────────────────────────────────────

public class Currency
{
    public string Symbol { get; set; } = default!;
}

public class Entry
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public Currency Currency { get; set; } = default!;
}

public class TransactionEntryDto
{
    public int Id { get; set; }
    public string CurrencyCode { get; set; } = default!;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
}

// ── Purchase ──────────────────────────────────────────────────────────────────

public class UpdatePurchaseCostPlanCommand
{
    public int PlanId { get; set; }
    public string Description { get; set; } = default!;
    public List<PurchaseServiceCostDto> Costs { get; set; } = [];
}

public class PurchaseCostPlan
{
    public int PlanId { get; set; }
    public string Description { get; set; } = default!;
}

public class PurchaseServiceCostDto
{
    public int ServiceId { get; set; }
    public decimal Amount { get; set; }
    public string ServiceType { get; set; } = default!;
}

public class PurchaseServiceCost
{
    public int ServiceId { get; set; }
    public decimal Amount { get; set; }
    public string ServiceType { get; set; } = default!; // ignored in mapping
}
