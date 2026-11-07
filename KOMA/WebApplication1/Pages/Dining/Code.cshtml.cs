using System.Security.Claims;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;
namespace KOMA.Pages.Dining;

[Authorize(Roles = "Customer"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CodeModel(TabletAccess tablet) : PageModel
{
    public string Image { get; private set; } = "";
    public void OnGet()
    {
        var code = tablet.Issue(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var link = Url.Page("/Dining/Activate", null, null, Request.Scheme) + "#" + code;
        using var data = QRCodeGenerator.GenerateQrCode(link, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        Image = Convert.ToBase64String(qr.GetGraphic(6));
    }
}
