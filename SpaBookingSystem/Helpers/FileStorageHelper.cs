using Microsoft.AspNetCore.Hosting;

namespace SpaBookingSystem.Api.Helpers;

public static class FileStorageHelper
{
    public static async Task<string?> SaveServiceImageAsync(IFormFile? file, IWebHostEnvironment env)
    {
        if (file == null || file.Length == 0) return null;

        var extension = Path.GetExtension(file.FileName);
        var fileName = $"service_{Guid.NewGuid():N}{extension}";
        var relativeFolder = Path.Combine("uploads", "services");
        var webRootPath = string.IsNullOrWhiteSpace(env.WebRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : env.WebRootPath;
        var physicalFolder = Path.Combine(webRootPath, relativeFolder);

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
        var webRootPath = string.IsNullOrWhiteSpace(env.WebRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : env.WebRootPath;
        var physicalPath = Path.Combine(webRootPath, normalized);

        if (File.Exists(physicalPath))
        {
            File.Delete(physicalPath);
        }
    }
}
