using System.ComponentModel.DataAnnotations;
using KOMA.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Areas.Identity.Pages.Account.Manage;

[Authorize]
public class IndexModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : PageModel
{
    public string? Email { get; set; }
    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public class InputModel
    {
        [Required, StringLength(50), Display(Name = "First name")]
        public string FirstName { get; set; } = "";
        [Required, StringLength(49), Display(Name = "Last name")]
        public string LastName { get; set; } = "";
        [Phone, StringLength(30), Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }
    }
    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        Email = user.Email;
        Input = new() { FirstName = user.FirstName ?? "", LastName = user.LastName ?? "", PhoneNumber = user.PhoneNumber };
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        Email = user.Email;
        if (!ModelState.IsValid) return Page();
        user.FirstName = Input.FirstName.Trim();
        user.LastName = Input.LastName.Trim();
        user.FullName = $"{user.FirstName} {user.LastName}";
        var phone = string.IsNullOrWhiteSpace(Input.PhoneNumber) ? null : Input.PhoneNumber.Trim();
        if (user.PhoneNumber != phone) user.PhoneNumberConfirmed = false;
        user.PhoneNumber = phone;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return Page();
        }
        await signIn.RefreshSignInAsync(user);
        StatusMessage = "Your profile has been updated.";
        return RedirectToPage();
    }
}
