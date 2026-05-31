using System.IO;

namespace NEPlumbingInc.Services;

public class LocalFileServiceImageStorageService(IConfiguration configuration) : IServiceImageStorageService
{
    private readonly IConfiguration _configuration = configuration;

    private string GetStoragePath()
    {
        var configPath = _configuration["ServiceImageBlobStorage:LocalStoragePath"];
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            return configPath;
        }

        var contentRootPath = Directory.GetCurrentDirectory();
        return Path.Combine(contentRootPath, "ServiceImages");
    }

    public async Task<ServiceImageUploadResult> UploadServiceImageAsync(
        int serviceId,
        byte[] imageBytes,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image bytes are required.", nameof(imageBytes));
        }

        var safeContentType = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType;
        var extension = safeContentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ".jpg"
        };

        var storagePath = GetStoragePath();
        var serviceFolder = Path.Combine(storagePath, $"services/{serviceId}");
        Directory.CreateDirectory(serviceFolder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(serviceFolder, fileName);

        await File.WriteAllBytesAsync(filePath, imageBytes, cancellationToken);

        var blobName = $"services/{serviceId}/{fileName}";
        return new ServiceImageUploadResult(blobName, safeContentType, imageBytes.LongLength);
    }

    public async Task<(Stream Content, string ContentType)> OpenServiceImageReadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new ArgumentException("Blob name is required.", nameof(blobName));
        }

        var storagePath = GetStoragePath();
        var filePath = Path.Combine(storagePath, blobName);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Service image not found in local storage.", filePath);
        }

        var contentType = DetermineContentType(filePath);
        var stream = File.OpenRead(filePath);
        return await Task.FromResult(((Stream)stream, contentType));
    }

    public async Task DeleteServiceImageAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            return;
        }

        var storagePath = GetStoragePath();
        var filePath = Path.Combine(storagePath, blobName);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        await Task.CompletedTask;
    }

    private static string DetermineContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "image/jpeg"
        };
    }
}
