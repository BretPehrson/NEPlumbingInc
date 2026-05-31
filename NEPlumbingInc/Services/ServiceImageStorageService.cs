using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace NEPlumbingInc.Services;

public record ServiceImageUploadResult(string BlobName, string ContentType, long SizeBytes);

public interface IServiceImageStorageService
{
    Task<ServiceImageUploadResult> UploadServiceImageAsync(int serviceId, byte[] imageBytes, string contentType, CancellationToken cancellationToken = default);
    Task<(Stream Content, string ContentType)> OpenServiceImageReadAsync(string blobName, CancellationToken cancellationToken = default);
    Task DeleteServiceImageAsync(string blobName, CancellationToken cancellationToken = default);
}

public class ServiceImageStorageService(IConfiguration configuration) : IServiceImageStorageService
{
    private readonly IConfiguration _configuration = configuration;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private BlobContainerClient CreateContainerClient()
    {
        var connectionString = FirstNonEmpty(
            _configuration["ServiceImageBlobStorage:ConnectionString"],
            _configuration["AzureBlobStorage:ConnectionString"],
            _configuration.GetConnectionString("ServiceImageBlobStorage"),
            _configuration.GetConnectionString("AzureBlobStorage"));

        var containerName = FirstNonEmpty(
            _configuration["ServiceImageBlobStorage:ServiceImageContainer"],
            _configuration["AzureBlobStorage:ServiceImageContainer"]) ?? "service-images";

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Blob storage connection string is not configured for service images. " +
                "Set 'ServiceImageBlobStorage__ConnectionString' in Azure or 'ServiceImageBlobStorage:ConnectionString' locally.");
        }

        return new BlobContainerClient(connectionString, containerName);
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

        var blobName = $"services/{serviceId}/{Guid.NewGuid():N}{extension}";
        var container = CreateContainerClient();
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blobClient = container.GetBlobClient(blobName);
        await using var stream = new MemoryStream(imageBytes, writable: false);
        await blobClient.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = safeContentType }
            },
            cancellationToken);

        return new ServiceImageUploadResult(blobName, safeContentType, imageBytes.LongLength);
    }

    public async Task<(Stream Content, string ContentType)> OpenServiceImageReadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new ArgumentException("Blob name is required.", nameof(blobName));
        }

        var container = CreateContainerClient();
        var blobClient = container.GetBlobClient(blobName);

        try
        {
            var result = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            var contentType = result.Value.Details.ContentType ?? "image/jpeg";
            return (result.Value.Content, contentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException("Service image not found in blob storage.", blobName, ex);
        }
    }

    public async Task DeleteServiceImageAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            return;
        }

        var container = CreateContainerClient();
        var blobClient = container.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }
}