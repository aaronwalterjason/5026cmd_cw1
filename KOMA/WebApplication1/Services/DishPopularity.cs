namespace KOMA.Services;

public static class DishPopularity
{
    // Reports retain the name saved when the dish was ordered, even if its menu link changed.
    public static List<(string Name, int Qty)> Rank(IEnumerable<(string? Name, int Quantity)> items) =>
        items.GroupBy(i => i.Name ?? "(removed item)")
            .Select(g => (Name: g.Key, Qty: g.Sum(i => i.Quantity)))
            .OrderByDescending(d => d.Qty).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToList();
}
