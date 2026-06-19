using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NEPlumbingInc.Models;
using NEPlumbingInc.Services;

namespace NEPlumbingInc.Controllers;

public class ContactController(
    IMessageService messageService,
    ISpecialOfferService specialOfferService,
    ISpecialOfferSettingsService specialOfferSettingsService,
    IHttpContextAccessor httpContextAccessor,
    ISpamFilterService spamFilterService,
    ILogger<ContactController> logger) : Controller
{
    private readonly IMessageService _messageService = messageService;
    private readonly ISpecialOfferService _specialOfferService = specialOfferService;
    private readonly ISpecialOfferSettingsService _specialOfferSettingsService = specialOfferSettingsService;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly ISpamFilterService _spamFilterService = spamFilterService;
    private readonly ILogger<ContactController> _logger = logger;

    [HttpPost("/messages/submit")]
    [EnableRateLimiting("FormSubmission")]
    public async Task<IActionResult> Submit(
        [FromForm] MessageFormModel form,
        [FromForm] string? source,
        [FromForm] string? website)
    {
        // Honeypot: real users never fill this field.
        if (!string.IsNullOrWhiteSpace(website))
        {
            return Redirect("/messages?sent=1");
        }

        if (!ModelState.IsValid)
        {
            return Redirect("/messages?error=1");
        }

        var spamCheck = _spamFilterService.Check(form);
        if (spamCheck.IsSpam)
        {
            _logger.LogInformation(
                "Flagged likely spam contact form submission. Reason={Reason}",
                spamCheck.Reason ?? "unknown");

            await _messageService.CreateMessageAsync(
                form,
                isSpecialOffer: string.Equals(source, "special-offer", StringComparison.OrdinalIgnoreCase),
                isSpam: true,
                spamReason: spamCheck.Reason,
                sendEmailNotification: false);

            return Redirect("/messages?sent=1");
        }

        try
        {
            var isSpecialOffer = string.Equals(source, "special-offer", StringComparison.OrdinalIgnoreCase);

            var isDuplicate = await _messageService.IsRecentDuplicateAsync(form, isSpecialOffer, TimeSpan.FromHours(24));
            if (isDuplicate)
            {
                return Redirect("/messages?sent=1");
            }

            await _messageService.CreateMessageAsync(form, isSpecialOffer);

            if (isSpecialOffer)
            {
                var submissionIp = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

                if (!string.IsNullOrWhiteSpace(submissionIp))
                {
                    await _specialOfferService.RecordFormSubmissionAsync(submissionIp, form);
                }
            }

            return Redirect("/messages?sent=1");
        }
        catch
        {
            return Redirect("/messages?error=1");
        }
    }

    [HttpPost("/special-offer/claim")]
    [EnableRateLimiting("FormSubmission")]
    public async Task<IActionResult> ClaimSpecialOffer(
        [FromForm] MessageFormModel form,
        [FromForm] string? website)
    {
        // Honeypot: real users never fill this field.
        if (!string.IsNullOrWhiteSpace(website))
        {
            return Redirect("/special-offer?sent=1");
        }

        if (!ModelState.IsValid)
        {
            return Redirect("/special-offer?error=1");
        }

        var spamCheck = _spamFilterService.Check(form);
        if (spamCheck.IsSpam)
        {
            _logger.LogInformation(
                "Flagged likely spam special offer submission. Reason={Reason}",
                spamCheck.Reason ?? "unknown");

            await _messageService.CreateMessageAsync(
                form,
                isSpecialOffer: true,
                isSpam: true,
                spamReason: spamCheck.Reason,
                sendEmailNotification: false);

            return Redirect("/special-offer?sent=1");
        }

        try
        {
            var isDuplicate = await _messageService.IsRecentDuplicateAsync(form, isSpecialOffer: true, TimeSpan.FromHours(24));
            if (isDuplicate)
            {
                return Redirect("/special-offer?sent=1");
            }

            var settings = await _specialOfferSettingsService.GetSettingsAsync();
            if (settings.RequireAddress)
            {
                if (string.IsNullOrWhiteSpace(form.AddressLine1)
                    || string.IsNullOrWhiteSpace(form.City)
                    || string.IsNullOrWhiteSpace(form.State)
                    || string.IsNullOrWhiteSpace(form.ZipCode))
                {
                    return Redirect("/special-offer?error=1");
                }
            }

            await _messageService.CreateMessageAsync(form, isSpecialOffer: true);

            var submissionIp = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

            if (!string.IsNullOrWhiteSpace(submissionIp))
            {
                await _specialOfferService.RecordFormSubmissionAsync(submissionIp, form);
            }

            return Redirect("/special-offer?sent=1");
        }
        catch
        {
            return Redirect("/special-offer?error=1");
        }
    }
}
