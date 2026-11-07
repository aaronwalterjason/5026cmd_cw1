<<<<<<< HEAD
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Threading.Tasks;
=======
using System.ComponentModel.DataAnnotations;
using KOMA.Models;
using KOMA.Services.Email;
>>>>>>> origin/admin/dashboard
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
<<<<<<< HEAD
using Microsoft.AspNetCore.WebUtilities;
using KOMA.Models;

namespace KOMA.Areas.Identity.Pages.Account;

public class ResetPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ResetPasswordModel(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    [BindProperty]
    public InputModel Input { get; set; } = default!;

    /// <summary>
    ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class InputModel
    {
        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [Required]
        [EmailAddress]
        public string Email { get; set; } = default!;

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = default!;

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string? ConfirmPassword { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [Required]
        public string Code { get; set; } = default!;

    }

    public IActionResult OnGet(string? code = null)
    {
        if (code == null)
        {
            return BadRequest("A code must be supplied for password reset.");
        }
        else
        {
            Input = new InputModel
            {
                Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code))
            };
            return Page();
        }
=======

namespace KOMA.Areas.Identity.Pages.Account;

// Reset password: opened from the reset email (or the admin's "Password reset link" for staff).
// Using the link proves the person owns the inbox, so a successful reset also confirms the email.
[AllowAnonymous]
public class ResetPasswordModel(UserManager<ApplicationUser> users) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Code { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool Done { get; private set; }

    public class InputModel
    {
        [Required, EmailAddress, Display(Name = "Email address")] public string Email { get; set; } = "";
        [Required, StringLength(100, MinimumLength = 6), DataType(DataType.Password), Display(Name = "New password")] public string Password { get; set; } = "";
        [Required, Compare(nameof(Password), ErrorMessage = "The passwords don't match."), DataType(DataType.Password), Display(Name = "Confirm new password")] public string ConfirmPassword { get; set; } = "";
>>>>>>> origin/admin/dashboard
    }

    public async Task<IActionResult> OnPostAsync()
    {
<<<<<<< HEAD
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user == null)
        {
            // Don't reveal that the user does not exist
            return RedirectToPage("./ResetPasswordConfirmation");
        }

        var result = await _userManager.ResetPasswordAsync(user, Input.Code, Input.Password);
        if (result.Succeeded)
        {
            return RedirectToPage("./ResetPasswordConfirmation");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
=======
        var token = AccountEmailService.Decode(Code);
        if (token is null) ModelState.AddModelError(string.Empty, "This reset link isn't valid. Ask for a new one.");
        if (!ModelState.IsValid) return Page();

        var user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user is null) { Done = true; return Page(); }   // same result as success: don't reveal accounts
        var result = await users.ResetPasswordAsync(user, token!, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(string.Empty, e.Code == "InvalidToken" ? "This reset link has expired or was already used. Ask for a new one." : e.Description);
            return Page();
        }
        if (!user.EmailConfirmed) { user.EmailConfirmed = true; await users.UpdateAsync(user); }
        await users.ResetAccessFailedCountAsync(user);
        Done = true;
>>>>>>> origin/admin/dashboard
        return Page();
    }
}
