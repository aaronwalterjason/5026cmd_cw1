using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;

namespace RROMS.Areas.Admin.Pages.Accounts
{
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        public IndexModel(UserManager<ApplicationUser> userManager) => _userManager = userManager;

        public List<AccountRow> Accounts { get; set; } = new();
        public string[] AvailableRoles { get; } = { "Administrator", "Staff", "Customer" };

        public async Task OnGetAsync()
        {
            await LoadAccountsAsync();
        }

        public async Task<IActionResult> OnPostAsync(string userId, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is not null)
            {
                var currentRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, role);
                TempData["StatusMessage"] = $"{user.Email} is now {role}.";
            }

            return RedirectToPage();
        }

        private async Task LoadAccountsAsync()
        {
            foreach (var user in _userManager.Users.ToList())
            {
                var roles = await _userManager.GetRolesAsync(user);
                Accounts.Add(new AccountRow(user.Id, user.Email ?? user.UserName ?? "(no email)", roles.FirstOrDefault() ?? "Customer"));
            }
        }

        public record AccountRow(string UserId, string Email, string Role);
    }
}
