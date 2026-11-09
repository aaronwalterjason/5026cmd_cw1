using KOMA.Models;

namespace KOMA.Services;

// Promotions are shown and charged only on the table tablet (dine-in). Pre-orders show and charge the menu price.
public record OrderingMenu(List<MenuItem> Menu, Dictionary<int, int> Quantities, int Id, bool Tablet = false, Order? Preorder = null,
    bool CanRemovePreOrder = false)
{
    public MenuPricing.Price PriceOf(MenuItem item) => Tablet ? MenuPricing.For(item, ReservationRules.MalaysiaNow) : MenuPricing.Menu(item);

    // Shared by pre-orders and the table tablet, so the price shown, the total checked and the price saved always match.
    // The menu items must be loaded with their Promotion (DiningOrderService.Menu() does this).
    // promotionsAt: null for pre-orders (menu price), the order time for tablet orders.
    public static List<OrderItem> BuildLines(IEnumerable<MenuItem> menu, Dictionary<int, int> quantities, DateTime? promotionsAt) =>
        menu.Where(item => quantities.GetValueOrDefault(item.Id) > 0)
            .Select(item =>
            {
                var line = new OrderItem { MenuItemId = item.Id, ItemName = item.Name, Quantity = quantities[item.Id] };
                MenuPricing.Apply(line, item, promotionsAt);
                return line;
            }).ToList();
}
