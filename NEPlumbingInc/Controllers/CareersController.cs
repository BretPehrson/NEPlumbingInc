using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NEPlumbingInc.Models;
using NEPlumbingInc.Services;

namespace NEPlumbingInc.Controllers;

public class CareersController(
    IMessageService messageService,
    IResumeStorageService resumeStorageService,
    ISpamFilterService spamFilterService,
    ILogger<CareersController> logger) : Controller
{
    private readonly IMessageService _messageService = messageService;
    private readonly IResumeStorageService _resumeStorageService = resumeStorageService;
    private readonly ISpamFilterService _spamFilterService = spamFilterService;
    private readonly ILogger<CareersController> _logger = logger;

    [HttpPost("/careers/submit")]
    [RequestFormLimits(MultipartBodyLengthLimit = 10_485_760)]
    [RequestSizeLimit(10_485_760)]
    public async Task<IActionResult> Submit([FromForm] JobApplicationFormModel form, [FromForm] IFormFile? resume, [FromForm] string? website, [FromForm] string? companyWebsite)
    {
        var traceId = HttpContext.TraceIdentifier;

        // Honeypot: real users never fill these hidden fields.
        if (!string.IsNullOrWhiteSpace(website)
            || !string.IsNullOrWhiteSpace(companyWebsite))
        {
            var messageText = BuildApplicationMessage(form);

            var messageForm = new MessageFormModel
            {
                Name = form.Name,
                Email = form.Email,
                Phone = form.Phone,
                Message = messageText
            };

            await _messageService.CreateMessageAsync(
                messageForm,
                isSpecialOffer: false,
                isSpam: true,
                spamReason: "honeypot-filled",
                sendEmailNotification: false);

            return Redirect("/careers?sent=1");
        }

        if (!ModelState.IsValid)
        {
            return Redirect("/careers?error=1");
        }

        try
        {
            var messageText = BuildApplicationMessage(form);

            var messageForm = new MessageFormModel
            {
                Name = form.Name,
                Email = form.Email,
                Phone = form.Phone,
                Message = messageText
            };

            var created = await _messageService.CreateMessageAsync(messageForm, isSpecialOffer: false);

            if (resume is not null && resume.Length > 0)
            {
                if (!IsAllowedResumeFile(resume.FileName))
                {
                    _logger.LogWarning(
                        "Rejected resume upload due to extension. TraceId={TraceId} MessageId={MessageId} FileName={FileName}",
                        traceId,
                        created.Id,
                        resume.FileName);
                    return Redirect("/careers?error=1");
                }

                if (resume.Length > 10_485_760)
                {
                    _logger.LogWarning(
                        "Rejected resume upload due to size. TraceId={TraceId} MessageId={MessageId} FileName={FileName} SizeBytes={SizeBytes}",
                        traceId,
                        created.Id,
                        resume.FileName,
                        resume.Length);
                    return Redirect("/careers?error=1");
                }

                // Quarantine resume uploads for submissions that look like spam
                var spamCheck = _spamFilterService.Check(messageForm);
                var uploaded = await _resumeStorageService.UploadResumeAsync(created.Id, resume, HttpContext.RequestAborted, quarantine: spamCheck.IsSpam);
                await _messageService.AttachResumeAsync(created.Id, uploaded, HttpContext.RequestAborted);
            }

            return Redirect("/careers?sent=1");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error submitting careers application. TraceId={TraceId} HasResume={HasResume} ResumeFileName={ResumeFileName} ResumeSizeBytes={ResumeSizeBytes}",
                traceId,
                resume is not null && resume.Length > 0,
                resume?.FileName,
                resume?.Length);

            return Redirect($"/careers?error=1&trace={Uri.EscapeDataString(traceId)}");
        }
    }

    private static bool IsAllowedResumeFile(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext)) return false;

         return ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
             || ext.Equals(".docx", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildApplicationMessage(JobApplicationFormModel form)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Job Application");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(form.Position))
            sb.AppendLine($"Position interested in: {form.Position}");

        if (!string.IsNullOrWhiteSpace(form.YearsExperience))
            sb.AppendLine($"Years of experience: {form.YearsExperience}");

        sb.AppendLine($"Licensed/Certified: {(form.LicensedOrCertified ? "Yes" : "No")}");

        if (!string.IsNullOrWhiteSpace(form.Availability))
            sb.AppendLine($"Availability: {form.Availability}");

        if (!string.IsNullOrWhiteSpace(form.PreferredContact))
            sb.AppendLine($"Preferred contact method/time: {form.PreferredContact}");

        sb.AppendLine();
        sb.AppendLine("Additional details:");
        sb.AppendLine(string.IsNullOrWhiteSpace(form.AdditionalDetails) ? "(none provided)" : form.AdditionalDetails.Trim());

        return sb.ToString().TrimEnd();
    }
}
