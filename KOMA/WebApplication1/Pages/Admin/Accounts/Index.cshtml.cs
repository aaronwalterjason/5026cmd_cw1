using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using KOMA.Models;
using KOMA.Services;

namespace KOMA.Pages.Admin.Accounts
{
    [Authorize(Roles = "Administrator")]
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AccountService _accounts;
        public IndexModel(UserManager<ApplicationUser> userManager, AccountService accounts)
        {
            _userManager = userManager;
            _accounts = accounts;
        }

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
                var result = await _accounts.SetRoleAsync(user, role);
                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
                    await LoadAccountsAsync();
                    return Page();
                }
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
