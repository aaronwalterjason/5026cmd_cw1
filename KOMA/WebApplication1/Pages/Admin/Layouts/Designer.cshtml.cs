using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;

namespace KOMA.Pages.Admin.Layouts
{
    // The floor layout designer: the admin draws dining areas on the floor and adds tables inside them.
    // The page sends the whole layout as JSON on Save; this class checks it and creates/updates/deletes
    // the DiningArea and RestaurantTable rows to match.
    [Authorize(Roles = "Administrator")]
    public class DesignerModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public DesignerModel(ApplicationDbContext db) => _db = db;

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private const double Eps = 0.001;   // tolerance for floating-point edges

        public FloorLayout Floor { get; set; } = default!;
        public IList<FloorLayout> AllFloors { get; set; } = new List<FloorLayout>();

        // Everything the designer script needs, as JSON (see wwwroot/js/layout-designer.js).
        public string DesignerJson { get; set; } = "{}";
        public List<string> Errors { get; set; } = new();

        // The layout posted back by the designer when the admin clicks Save.
        [BindProperty] public string? Payload { get; set; }

        // ---- Shapes of the JSON exchanged with the page ----
        public class LayoutDto
        {
            public List<AreaDto> Areas { get; set; } = new();
            public List<TableDto> Tables { get; set; } = new();
        }
        public class AreaDto
        {
            public int Id { get; set; }               // > 0 = existing area, <= 0 = new (temporary id from the page)
            public string Name { get; set; } = "";
            public string Zone { get; set; } = "Indoor";
            public string? Description { get; set; }
            public bool IsActive { get; set; } = true;
            public double X { get; set; }
            public double Y { get; set; }
            public double W { get; set; }
            public double H { get; set; }
        }
        public class TableDto
        {
            public int Id { get; set; }               // > 0 = existing table, <= 0 = new
            public int AreaId { get; set; }           // the AreaDto.Id it sits in
            public string Number { get; set; } = "";
            public int Capacity { get; set; } = 4;
            public string Shape { get; set; } = nameof(TableShape.Square);
            public bool IsActive { get; set; } = true;
            public double X { get; set; }
            public double Y { get; set; }
            public double W { get; set; }
            public double H { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var floor = await LoadFloorAsync(id, tracking: false);
            if (floor is null) return NotFound();

            Floor = floor;
            await BuildDesignerJsonAsync(ToDto(floor));
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            var floor = await LoadFloorAsync(id, tracking: true);
            if (floor is null) return NotFound();
            Floor = floor;

            LayoutDto? layout = null;
            try { layout = JsonSerializer.Deserialize<LayoutDto>(Payload ?? "", Json); }
            catch (JsonException) { }
            if (layout is null)
            {
                Errors.Add("The layout could not be read. Please reload the page and try again.");
                await BuildDesignerJsonAsync(ToDto(floor));
                return Page();
            }

            Errors = await ValidateAsync(floor, layout);
            if (Errors.Count > 0)
            {
                // Show the admin's unsaved layout again so no work is lost.
                await BuildDesignerJsonAsync(layout);
                return Page();
            }

            Apply(floor, layout);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] =
                $"Layout saved: {layout.Areas.Count} dining area(s), {layout.Tables.Count} table(s), {layout.Tables.Sum(t => t.Capacity)} seats.";
            return RedirectToPage(new { id });
        }

        // ------------------------------------------------------------------

        private Task<FloorLayout?> LoadFloorAsync(int id, bool tracking)
        {
            IQueryable<FloorLayout> q = _db.FloorLayouts.Include(f => f.DiningAreas).ThenInclude(a => a.Tables);
            if (!tracking) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync(f => f.Id == id);
        }

        private static LayoutDto ToDto(FloorLayout floor) => new()
        {
            Areas = floor.DiningAreas.OrderBy(a => a.Name).Select(a => new AreaDto
            {
                Id = a.Id, Name = a.Name, Zone = a.Zone, Description = a.Description, IsActive = a.IsActive,
                X = a.LayoutX, Y = a.LayoutY, W = a.LayoutWidth, H = a.LayoutHeight
            }).ToList(),
            Tables = floor.DiningAreas.SelectMany(a => a.Tables).OrderBy(t => t.TableNumber).Select(t => new TableDto
            {
                Id = t.Id, AreaId = t.DiningAreaId, Number = t.TableNumber, Capacity = t.Capacity,
                Shape = t.Shape.ToString(), IsActive = t.IsActive,
                X = t.LayoutX, Y = t.LayoutY, W = t.LayoutWidth, H = t.LayoutHeight
            }).ToList()
        };

        private async Task BuildDesignerJsonAsync(LayoutDto layout)
        {
            AllFloors = await _db.FloorLayouts.AsNoTracking().OrderBy(f => f.Level).ThenBy(f => f.Name).ToListAsync();

            // Table numbers used on OTHER floors, so the page can suggest numbers that aren't taken.
            var otherNumbers = await _db.RestaurantTables
                .Where(t => t.DiningArea!.FloorLayoutId != Floor.Id)
                .Select(t => t.TableNumber).ToListAsync();

            DesignerJson = JsonSerializer.Serialize(new
            {
                floor = new { id = Floor.Id, name = Floor.Name, width = Floor.Width, height = Floor.Height },
                zones = DiningArea.ZoneOptions,
                shapes = Enum.GetNames<TableShape>(),
                otherTableNumbers = otherNumbers,
                areas = layout.Areas,
                tables = layout.Tables
            }, Json);
        }

        private async Task<List<string>> ValidateAsync(FloorLayout floor, LayoutDto layout)
        {
            var errors = new List<string>();
            var areaIds = new HashSet<int>();

            foreach (var a in layout.Areas)
            {
                var label = string.IsNullOrWhiteSpace(a.Name) ? "A dining area" : $"'{a.Name.Trim()}'";
                if (!areaIds.Add(a.Id)) errors.Add("The layout contains the same dining area twice. Please reload the page.");
                if (a.Id > 0 && floor.DiningAreas.All(x => x.Id != a.Id)) errors.Add($"{label} does not belong to this floor.");
                if (string.IsNullOrWhiteSpace(a.Name)) errors.Add("Every dining area needs a name.");
                else if (a.Name.Trim().Length > 80) errors.Add($"{label}: the name must be 80 characters or fewer.");
                if (!DiningArea.ZoneOptions.Contains(a.Zone)) errors.Add($"{label}: choose a zone from the list.");
                if ((a.Description?.Length ?? 0) > 250) errors.Add($"{label}: the description must be 250 characters or fewer.");
                if (a.W < 1 - Eps || a.H < 1 - Eps) errors.Add($"{label} must be at least 1 m × 1 m.");
                if (!Inside(a.X, a.Y, a.W, a.H, 0, 0, floor.Width, floor.Height)) errors.Add($"{label} goes outside the floor.");
            }
            var names = layout.Areas.Where(a => !string.IsNullOrWhiteSpace(a.Name))
                .GroupBy(a => a.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var n in names) errors.Add($"Two dining areas are both called '{n}'. Give each area its own name.");

            for (int i = 0; i < layout.Areas.Count; i++)
                for (int j = i + 1; j < layout.Areas.Count; j++)
                    if (Overlaps(layout.Areas[i], layout.Areas[j]))
                        errors.Add($"'{layout.Areas[i].Name}' and '{layout.Areas[j].Name}' overlap.");

            var otherFloorNumbers = (await _db.RestaurantTables
                    .Where(t => t.DiningArea!.FloorLayoutId != floor.Id)
                    .Select(t => t.TableNumber).ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingTableIds = floor.DiningAreas.SelectMany(a => a.Tables).Select(t => t.Id).ToHashSet();
            var seenNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in layout.Tables)
            {
                var label = string.IsNullOrWhiteSpace(t.Number) ? "A table" : $"Table {t.Number.Trim()}";
                var area = layout.Areas.FirstOrDefault(a => a.Id == t.AreaId);
                if (t.Id > 0 && !existingTableIds.Contains(t.Id)) errors.Add($"{label} does not belong to this floor.");
                if (string.IsNullOrWhiteSpace(t.Number)) errors.Add("Every table needs a table number.");
                else
                {
                    var number = t.Number.Trim();
                    if (number.Length > 20) errors.Add($"{label}: the number must be 20 characters or fewer.");
                    if (!seenNumbers.Add(number)) errors.Add($"Table number {number} is used more than once on this floor.");
                    if (otherFloorNumbers.Contains(number)) errors.Add($"Table number {number} is already used on another floor.");
                }
                if (t.Capacity < 1 || t.Capacity > 20) errors.Add($"{label}: seats must be between 1 and 20.");
                if (!Enum.TryParse<TableShape>(t.Shape, out _)) errors.Add($"{label}: choose a shape from the list.");
                if (t.W < 0.5 - Eps || t.H < 0.5 - Eps) errors.Add($"{label} must be at least 0.5 m × 0.5 m.");
                if (area is null) errors.Add($"{label} must be inside a dining area.");
                else
                {
                    var f = Footprint(t);
                    if (!Inside(f.X, f.Y, f.W, f.H, area.X, area.Y, area.W, area.H))
                        errors.Add($"{label} and its chairs must sit fully inside '{area.Name}'.");
                }
            }
            for (int i = 0; i < layout.Tables.Count; i++)
                for (int j = i + 1; j < layout.Tables.Count; j++)
                {
                    var a = Footprint(layout.Tables[i]); var b = Footprint(layout.Tables[j]);
                    if (Overlaps(a.X, a.Y, a.W, a.H, b.X, b.Y, b.W, b.H))
                        errors.Add($"Tables {layout.Tables[i].Number} and {layout.Tables[j].Number} are too close - their chairs overlap.");
                }

            return errors.Distinct().ToList();
        }

        private void Apply(FloorLayout floor, LayoutDto layout)
        {
            var existingAreas = floor.DiningAreas.ToDictionary(a => a.Id);
            var existingTables = floor.DiningAreas.SelectMany(a => a.Tables).ToDictionary(t => t.Id);
            var areaByDtoId = new Dictionary<int, DiningArea>();

            // 1. Dining areas: update existing ones, add new ones.
            foreach (var dto in layout.Areas)
            {
                if (dto.Id <= 0 || !existingAreas.TryGetValue(dto.Id, out var area))
                {
                    area = new DiningArea();
                    floor.DiningAreas.Add(area);
                }
                area.Name = dto.Name.Trim();
                area.Zone = dto.Zone;
                area.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
                area.IsActive = dto.IsActive;
                area.LayoutX = dto.X; area.LayoutY = dto.Y; area.LayoutWidth = dto.W; area.LayoutHeight = dto.H;
                areaByDtoId[dto.Id] = area;
            }

            // 2. Tables: update existing ones (including moving them to another area), add new ones.
            foreach (var dto in layout.Tables)
            {
                var area = areaByDtoId[dto.AreaId];
                if (dto.Id <= 0 || !existingTables.TryGetValue(dto.Id, out var table))
                {
                    table = new RestaurantTable();
                    area.Tables.Add(table);
                }
                else if (table.DiningArea != area)
                {
                    table.DiningArea?.Tables.Remove(table);
                    area.Tables.Add(table);
                    table.DiningArea = area;
                }
                table.TableNumber = dto.Number.Trim();
                table.Capacity = dto.Capacity;
                table.Shape = Enum.Parse<TableShape>(dto.Shape);
                table.IsActive = dto.IsActive;
                table.LayoutX = dto.X; table.LayoutY = dto.Y; table.LayoutWidth = dto.W; table.LayoutHeight = dto.H;
            }

            // 3. Delete tables and areas the admin removed from the layout.
            var keptTables = layout.Tables.Where(t => t.Id > 0).Select(t => t.Id).ToHashSet();
            foreach (var t in existingTables.Values.Where(t => !keptTables.Contains(t.Id)))
                _db.RestaurantTables.Remove(t);

            var keptAreas = layout.Areas.Where(a => a.Id > 0).Select(a => a.Id).ToHashSet();
            foreach (var a in existingAreas.Values.Where(a => !keptAreas.Contains(a.Id)))
                _db.DiningAreas.Remove(a);
        }

        // Space each shape needs around it for chairs, in metres (left, top, right, bottom).
        // Keep in sync with padding() in wwwroot/js/layout-chairs.js.
        private static (double L, double T, double R, double B) ChairPadding(string shape) => shape switch
        {
            nameof(TableShape.Booth) => (0, 0.5, 0, 0.5),     // benches on the two long sides
            nameof(TableShape.Counter) => (0, 0, 0, 0.5),     // stools along the front
            _ => (0.5, 0.5, 0.5, 0.5)                          // chairs can go on any side
        };

        // The table plus the space its chairs take up.
        private static (double X, double Y, double W, double H) Footprint(TableDto t)
        {
            var p = ChairPadding(t.Shape);
            return (t.X - p.L, t.Y - p.T, t.W + p.L + p.R, t.H + p.T + p.B);
        }

        private static bool Inside(double x, double y, double w, double h, double ox, double oy, double ow, double oh) =>
            x >= ox - Eps && y >= oy - Eps && x + w <= ox + ow + Eps && y + h <= oy + oh + Eps;

        private static bool Overlaps(AreaDto a, AreaDto b) => Overlaps(a.X, a.Y, a.W, a.H, b.X, b.Y, b.W, b.H);

        private static bool Overlaps(double ax, double ay, double aw, double ah, double bx, double by, double bw, double bh) =>
            ax < bx + bw - Eps && bx < ax + aw - Eps && ay < by + bh - Eps && by < ay + ah - Eps;
    }
}
