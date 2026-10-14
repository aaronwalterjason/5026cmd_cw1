namespace KOMA.Services;

// The database stores the path; each uploaded photo is a separate file.
public class MenuImageService(IWebHostEnvironment environment)
{
    public async Task<string> SaveAsync(IFormFile photo)
    {
        if (photo.Length == 0 || photo.Length > 5 * 1024 * 1024)
            throw new ArgumentException("Choose a photo smaller than 5 MB.");
        await using var input = photo.OpenReadStream();
        var header = new byte[12];
        var count = await input.ReadAtLeastAsync(header, 12, throwOnEndOfStream: false);
        var extension = count >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ? ".png"
            : count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255 ? ".jpg"
            : count >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8) ? ".webp"
            : throw new ArgumentException("Choose a JPG, PNG or WebP photo.");
        var folder = Path.Combine(environment.WebRootPath, "images", "menu");
        Directory.CreateDirectory(folder);
        var filename = Guid.NewGuid().ToString("N") + extension;
        var path = Path.Combine(folder, filename);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew);
            await output.WriteAsync(header.AsMemory(0, count));
            await input.CopyToAsync(output);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
        return "/images/menu/" + filename;
    }
}
