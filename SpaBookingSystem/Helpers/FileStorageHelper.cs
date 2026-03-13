using Microsoft.AspNetCore.Hosting;

namespace SpaBookingSystem.Api.Helpers;

public static class FileStorageHelper
{
    // Uploaded service images are stored under wwwroot so they can be served directly as static files.
    public static async Task<string> SaveServiceImageAsync(IFormFile file, IWebHostEnvironment env)
    {
        var extension = Path.GetExtension(file.FileName);
        var fileName = $"service_{Guid.NewGuid():N}{extension}";
        var relativeFolder = Path.Combine("uploads", "services");
        var physicalFolder = Path.Combine(env.WebRootPath, relativeFolder);

        Directory.CreateDirectory(physicalFolder);

        var physicalPath = Path.Combine(physicalFolder, fileName);
        using var stream = new FileStream(physicalPath, FileMode.Create);
        await file.CopyToAsync(stream);

        return "/" + Path.Combine(relativeFolder, fileName).Replace("\\", "/");
    }

    public static void DeleteFileIfExists(string? relativePath, IWebHostEnvironment env)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;

        var normalized = relativePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString());
        var physicalPath = Path.Combine(env.WebRootPath, normalized);

        if (File.Exists(physicalPath))
        {
            File.Delete(physicalPath);
        }
    }
}
