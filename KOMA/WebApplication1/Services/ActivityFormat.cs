using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using KOMA.Models;

namespace KOMA.Services;

// Turns field names and values into short, readable text for the Activity log ("IsAvailable" -> "Available", 32m -> "RM 32.00").
public static class ActivityFormat
{
    // Fields that are never shown or compared.
    public static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    { "Id", "RowVersion", "ConcurrencyStamp", "UpdatedAt", "CreatedAtUtc", "CreatedByUserId", "CustomerUserId", "PromotionId" };

    public static string Label(Type entity, string field)
    {
        var display = entity.GetProperty(field)?.GetCustomAttribute<DisplayAttribute>()?.GetName();
        if (!string.IsNullOrWhiteSpace(display)) return display!;
        var name = field switch
        {
            "MenuCategoryId" => "Category", "RestaurantTableId" => "Table", "DiningAreaId" => "Dining area",
            "FloorLayoutId" => "Floor", "ImageUrl" => "Photo", "HeroImageUrl" => "Banner photo",
            "GuestCount" => "Guests", "StartTime" => "Start", "EndTime" => "End",
            _ => field.StartsWith("Is") && field.Length > 2 && char.IsUpper(field[2]) ? field[2..] : field
        };
        return Regex.Replace(name, "(?<=[a-z])(?=[A-Z0-9])", " ");
    }

    public static string Value(string field, object? value)
    {
        switch (value)
        {
            case null: return "(empty)";
            case string s when string.IsNullOrWhiteSpace(s): return "(empty)";
            case string s when field.EndsWith("ImageUrl"): return "New photo";
            case string s: return s.Length > 60 ? s[..57] + "…" : s;
            case bool b: return b ? "On" : "Off";
            case decimal d when field.Contains("Price") || field.Contains("Amount"): return "RM " + d.ToString("N2", CultureInfo.InvariantCulture);
            case decimal d: return d.ToString("0.##", CultureInfo.InvariantCulture);
            case double x: return x.ToString("0.#", CultureInfo.InvariantCulture);
            case DateTime t: return t.ToString("d MMM, h:mm tt", CultureInfo.InvariantCulture);
            case TimeOnly t: return t.ToString("h:mm tt", CultureInfo.InvariantCulture);
            case Enum e: return e.DisplayName();
            default: return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
    }
}
