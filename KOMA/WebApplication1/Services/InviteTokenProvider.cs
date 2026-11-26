using KOMA.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace KOMA.Services;

// One-time code in the staff/admin invitation email (Admin > Accounts > Invite staff / Invite admin).
// Its own provider so the invite can last 24 hours without changing how long password-reset and
// confirm-email links last. The code includes the account's security stamp, so it stops working as soon as
// the person sets a password, or when the admin resends the invite (the stamp is renewed).
public class InviteTokenProvider(IDataProtectionProvider protection, IOptions<InviteTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(protection, options, logger)
{
    public const string ProviderName = "StaffInvite";
    public const string Purpose = "AcceptInvite";
    public static readonly TimeSpan Lifespan = TimeSpan.FromHours(24);
}

public class InviteTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InviteTokenProviderOptions()
    {
        Name = InviteTokenProvider.ProviderName;
        TokenLifespan = InviteTokenProvider.Lifespan;
    }
}
