using System.ComponentModel.DataAnnotations;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Areas.Identity.Pages.Account;

// Override Identity's default register page so a public signup always has a Customer profile.
[AllowAnonymous]
public class RegisterModel(AccountService accounts, SignInManager<ApplicationUser> signIn, KOMA.Services.Email.AccountEmailService emails) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required, StringLength(50), Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;
        [Required, StringLength(49), Display(Name = "Last name")]
        public string LastName { get; set; } = string.Empty;
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required, StringLength(100, MinimumLength = 6), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
        [Required, Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = new ApplicationUser { UserName = Input.Email.Trim(), Email = Input.Email.Trim(),
            FirstName = Input.FirstName.Trim(), LastName = Input.LastName.Trim(),
            FullName = $"{Input.FirstName.Trim()} {Input.LastName.Trim()}" };
        var result = await accounts.CreateAsync(user, Input.Password, "Customer");
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }
        await signIn.SignInAsync(user, isPersistent: false);
        // "Confirm your email" - they can use the site straight away; a banner reminds them until they confirm.
        await emails.SendConfirmAsync(user, code =>
            Url.Page("/Account/ConfirmEmail", null, new { area = "Identity", userId = user.Id, code }, Request.Scheme)!);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }
}
