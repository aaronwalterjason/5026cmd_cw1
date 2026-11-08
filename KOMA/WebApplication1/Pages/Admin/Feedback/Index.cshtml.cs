using KOMA.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
namespace KOMA.Pages.Admin.Feedback;
[Authorize(Roles = "Administrator")]
public class IndexModel(ApplicationDbContext db) : PageModel
{
    public List<KOMA.Models.Feedback> Feedbacks { get; set; } = [];
    public int CurrentPage { get; set; }
    public bool HasMore { get; set; }
    public async Task OnGetAsync(int p = 1)
    {
        CurrentPage = Math.Clamp(p, 1, 100000);
        Feedbacks = await db.Feedbacks.AsNoTracking().Include(f => f.TableServiceSession).ThenInclude(v => v!.RestaurantTable)
            .OrderByDescending(f => f.CreatedAtUtc).ThenByDescending(f => f.Id).Skip((CurrentPage - 1) * 24).Take(25).ToListAsync();
        HasMore = Feedbacks.Count > 24;
        if (HasMore) Feedbacks.RemoveAt(24);
    }
}
