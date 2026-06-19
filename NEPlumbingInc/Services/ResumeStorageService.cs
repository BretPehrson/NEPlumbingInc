using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace NEPlumbingInc.Services;

public record ResumeUploadResult(
    string BlobName,
    string OriginalFileName,
    string ContentType,
    long SizeBytes);

public interface IResumeStorageService
{
    Task<ResumeUploadResult> UploadResumeAsync(int messageId, IFormFile resumeFile, CancellationToken cancellationToken = default, bool quarantine = false);
    Task<(Stream Content, string ContentType)> OpenResumeReadAsync(string blobName, CancellationToken cancellationToken = default);
    Task<bool> DeleteResumeAsync(string blobName, CancellationToken cancellationToken = default);
}

public class ResumeStorageService(IConfiguration configuration) : IResumeStorageService
{
    private readonly IConfiguration _configuration = configuration;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private (BlobContainerClient Container, string ContainerName) CreateContainerClient()
    {
        var connectionString = FirstNonEmpty(
            _configuration["ResumeBlobStorage:ConnectionString"],
            _configuration["AzureBlobStorage:ConnectionString"],
            _configuration.GetConnectionString("ResumeBlobStorage"),
            _configuration.GetConnectionString("AzureBlobStorage"));

        var containerName = FirstNonEmpty(
            _configuration["ResumeBlobStorage:ResumeContainer"],
            _configuration["AzureBlobStorage:ResumeContainer"]) ?? "resumes";

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Blob storage connection string is not configured. " +
                "Set it via Azure App Service Configuration using the key 'ResumeBlobStorage__ConnectionString', " +
                "or set 'ResumeBlobStorage:ConnectionString' in appsettings.json / user-secrets for local development. " +
                "(Legacy fallback: 'AzureBlobStorage:ConnectionString'.)");
        }

        return (new BlobContainerClient(connectionString, containerName), containerName);
    }

    public async Task<ResumeUploadResult> UploadResumeAsync(int messageId, IFormFile resumeFile, CancellationToken cancellationToken = default, bool quarantine = false)
    {
        if (resumeFile is null) throw new ArgumentNullException(nameof(resumeFile));

        var originalFileName = Path.GetFileName(resumeFile.FileName);
        var extension = Path.GetExtension(originalFileName);
        var contentType = string.IsNullOrWhiteSpace(resumeFile.ContentType)
            ? "application/octet-stream"
            : resumeFile.ContentType;

        var blobName = $"job-applications/{messageId}/{Guid.NewGuid():N}{extension}";
        if (quarantine)
        {
            blobName = "quarantine/" + blobName;
        }

        var (container, _) = CreateContainerClient();
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blobClient = container.GetBlobClient(blobName);

        await using var stream = resumeFile.OpenReadStream();

        // Validate magic-bytes / file signature for common resume types before upload
        if (!HasValidResumeSignature(stream, extension))
        {
            throw new InvalidOperationException("Uploaded resume file signature does not match the file extension.");
        }

        await blobClient.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            cancellationToken);

        return new ResumeUploadResult(
            BlobName: blobName,
            OriginalFileName: originalFileName,
            ContentType: contentType,
            SizeBytes: resumeFile.Length);
    }

    private static bool HasValidResumeSignature(Stream stream, string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;

        // Read up to 8 bytes from the start
        var header = new byte[8];
        try
        {
            if (!stream.CanSeek)
            {
                // Can't validate safely if the stream isn't seekable
                return false;
            }

            stream.Seek(0, SeekOrigin.Begin);
            var read = stream.Read(header, 0, header.Length);
            stream.Seek(0, SeekOrigin.Begin);

            var ext = extension.Trim().ToLowerInvariant();

            if (ext == ".pdf")
            {
                // %PDF-
                return read >= 4 && header[0] == (byte)'%' && header[1] == (byte)'P' && header[2] == (byte)'D' && header[3] == (byte)'F';
            }

            if (ext == ".docx")
            {
                // ZIP file signature PK\x03\x04
                return read >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
            }

            if (ext == ".doc")
            {
                // OLE Compound File header D0 CF 11 E0 A1 B1 1A E1
                return read >= 8 && header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0
                       && header[4] == 0xA1 && header[5] == 0xB1 && header[6] == 0x1A && header[7] == 0xE1;
            }

            return false;
        }
        catch
        {
            try { stream.Seek(0, SeekOrigin.Begin); } catch { }
            return false;
        }
    }

    public async Task<(Stream Content, string ContentType)> OpenResumeReadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
            throw new ArgumentException("Blob name is required", nameof(blobName));

        var (container, _) = CreateContainerClient();
        var blobClient = container.GetBlobClient(blobName);

        try
        {
            var result = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            var contentType = result.Value.Details.ContentType ?? "application/octet-stream";
            return (result.Value.Content, contentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException("Resume file not found in blob storage.", blobName, ex);
        }
    }

    public async Task<bool> DeleteResumeAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName)) return false;

        var (container, _) = CreateContainerClient();
        var blobClient = container.GetBlobClient(blobName);

        try
        {
            var resp = await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
            return resp.Value;
        }
        catch (RequestFailedException)
        {
            // Log if needed by caller; return false to indicate not deleted
            return false;
        }
    }
}
