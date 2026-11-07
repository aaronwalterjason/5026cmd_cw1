using KOMA.Models;

namespace KOMA.Services;

public record OrderingMenu(List<MenuItem> Menu, Dictionary<int, int> Quantities, int Id, bool Tablet = false, Order? Preorder = null)
{
    public static List<OrderItem> BuildLines(IEnumerable<MenuItem> menu, Dictionary<int, int> quantities) =>
        menu.Where(item => quantities.GetValueOrDefault(item.Id) > 0)
            .Select(item => new OrderItem { MenuItemId = item.Id, ItemName = item.Name,
                Quantity = quantities[item.Id], UnitPrice = item.Price }).ToList();
}
