using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NEPlumbingInc.Services;

namespace NEPlumbingInc.Controllers;

[Authorize(Roles = "Admin")]
[ApiExplorerSettings(IgnoreApi = true)]
public class AdminResumesController(
    IMessageService messageService,
    IResumeStorageService resumeStorageService,
    ILogger<AdminResumesController> logger) : Controller
{
    private readonly IMessageService _messageService = messageService;
    private readonly IResumeStorageService _resumeStorageService = resumeStorageService;
    private readonly ILogger<AdminResumesController> _logger = logger;

    [HttpGet("/admin/messages/{id:int}/resume")]
    public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
    {
        var message = await _messageService.GetMessageByIdAsync(id);

        if (string.IsNullOrWhiteSpace(message.ResumeBlobName))
            return NotFound();

        try
        {
            var (content, contentType) = await _resumeStorageService.OpenResumeReadAsync(message.ResumeBlobName, cancellationToken);
            var downloadName = string.IsNullOrWhiteSpace(message.ResumeFileName) ? "resume" : message.ResumeFileName;
            // Force download as binary to avoid in-browser rendering/execution
            return File(content, "application/octet-stream", downloadName);
        }
        catch (FileNotFoundException)
        {
            _logger.LogWarning(
                "Resume blob not found. MessageId={MessageId} BlobName={BlobName} TraceId={TraceId}",
                id,
                message.ResumeBlobName,
                HttpContext.TraceIdentifier);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error downloading resume blob. MessageId={MessageId} BlobName={BlobName} TraceId={TraceId}",
                id,
                message.ResumeBlobName,
                HttpContext.TraceIdentifier);
            return StatusCode(500);
        }
    }
}
