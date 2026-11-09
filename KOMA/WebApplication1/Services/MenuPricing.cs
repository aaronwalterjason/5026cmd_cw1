using KOMA.Models;

namespace KOMA.Services;

// One place for "what does this dish cost?" - so the price shown, the total checked and the price saved always match.
// Promotions apply ONLY to dine-in orders placed on the table tablet; pre-orders always use the menu price.
// A dish has at most one promotion (MenuItem.PromotionId); it applies when Promotion.AppliesAt(time) is true.
public static class MenuPricing
{
    public record Price(decimal Unit, decimal List, Promotion? Promo)
    {
        public bool Discounted => Promo is not null && Unit < List;
        public int Percent => Promo is null ? 0 : (int)Math.Round(Promo.DiscountPercent);
    }

    // m.Promotion must be loaded (Include(m => m.Promotion)).
    public static Price For(MenuItem m, DateTime when)
    {
        if (m.Promotion is { } p && p.AppliesAt(when))
            return new Price(Math.Round(m.Price * (1 - p.DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero), m.Price, p);
        return new Price(m.Price, m.Price, null);
    }

    // The normal menu price (pre-orders).
    public static Price Menu(MenuItem m) => new(m.Price, m.Price, null);

    // Copy the price onto an order line (the line keeps its own price if the menu or promotion changes later).
    // when = null: no promotion (pre-order); otherwise the promotion running at that time (tablet order).
    public static void Apply(OrderItem line, MenuItem m, DateTime? when)
    {
        var price = when is { } t ? For(m, t) : Menu(m);
        line.UnitPrice = price.Unit;
        line.ListPrice = price.Discounted ? price.List : null;
        line.PromotionTitle = price.Discounted ? price.Promo!.Title : null;
    }
}
