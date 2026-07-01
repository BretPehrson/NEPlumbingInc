using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using NEPlumbingInc.Models;
using NEPlumbingInc.Services;

namespace NEPlumbingInc.Controllers;

public class ContactController(
    IMessageService messageService,
    ISpecialOfferService specialOfferService,
    ISpecialOfferSettingsService specialOfferSettingsService,
    IHttpContextAccessor httpContextAccessor,
    ISpamFilterService spamFilterService,
    IMemoryCache memoryCache,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<ContactController> logger) : Controller
{
    private const string FormTimingPurpose = "NEPlumbingInc.ContactFormTiming.v1";
    private const int SenderBurstLimit = 4;
    private static readonly TimeSpan MinFormFillTime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan MaxFormFillTime = TimeSpan.FromHours(12);
    private static readonly TimeSpan SenderBurstWindow = TimeSpan.FromMinutes(30);

    private readonly IMessageService _messageService = messageService;
    private readonly ISpecialOfferService _specialOfferService = specialOfferService;
    private readonly ISpecialOfferSettingsService _specialOfferSettingsService = specialOfferSettingsService;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly ISpamFilterService _spamFilterService = spamFilterService;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly IDataProtector _formTimingProtector = dataProtectionProvider.CreateProtector(FormTimingPurpose);
    private readonly ILogger<ContactController> _logger = logger;

    [HttpPost("/messages/submit")]
    [EnableRateLimiting("FormSubmission")]
    public async Task<IActionResult> Submit(
        [FromForm] MessageFormModel form,
        [FromForm] string? source,
        [FromForm] string? formTimingToken,
        [FromForm] string? website,
        [FromForm] string? companyWebsite)
    {
        var isSpecialOffer = string.Equals(source, "special-offer", StringComparison.OrdinalIgnoreCase);

        // Honeypot: real users never fill these hidden fields.
        if (!string.IsNullOrWhiteSpace(website)
            || !string.IsNullOrWhiteSpace(companyWebsite))
        {
            await _messageService.CreateMessageAsync(
                form,
                isSpecialOffer,
                isSpam: true,
                spamReason: "honeypot-filled",
                sendEmailNotification: false);

            return Redirect("/messages?sent=1");
        }

        if (!ModelState.IsValid)
        {
            return Redirect("/messages?error=1");
        }

        if (IsSuspiciousSubmissionSpeed(formTimingToken, out var timingReason))
        {
            _logger.LogInformation(
                "Flagged likely spam contact form submission from timing check. Reason={Reason}",
                timingReason);

            var isDuplicate = await _messageService.IsRecentDuplicateAsync(form, isSpecialOffer, TimeSpan.FromHours(24));
            if (!isDuplicate)
            {
                await _messageService.CreateMessageAsync(
                    form,
                    isSpecialOffer,
                    isSpam: true,
                    spamReason: timingReason,
                    sendEmailNotification: false);
            }

            return Redirect("/messages?sent=1");
        }

        if (IsRepeatSenderBurst(form, isSpecialOffer, out var burstReason))
        {
            _logger.LogInformation(
                "Suppressed repeated sender burst for contact form. Reason={Reason}",
                burstReason);

            var isDuplicate = await _messageService.IsRecentDuplicateAsync(form, isSpecialOffer, TimeSpan.FromHours(24));
            if (!isDuplicate)
            {
                await _messageService.CreateMessageAsync(
                    form,
                    isSpecialOffer,
                    isSpam: true,
                    spamReason: burstReason,
                    sendEmailNotification: false);
            }

            return Redirect("/messages?sent=1");
        }

        var spamCheck = _spamFilterService.Check(form);
        if (spamCheck.IsSpam)
        {
            _logger.LogInformation(
                "Flagged likely spam contact form submission. Reason={Reason}",
                spamCheck.Reason ?? "unknown");

            var isDuplicate = await _messageService.IsRecentDuplicateAsync(form, isSpecialOffer, TimeSpan.FromHours(24));
            if (isDuplicate)
            {
                return Redirect("/messages?sent=1");
            }

            await _messageService.CreateMessageAsync(
                form,
                isSpecialOffer,
                isSpam: true,
                spamReason: spamCheck.Reason,
                sendEmailNotification: false);

            return Redirect("/messages?sent=1");
        }

        try
        {
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
        [FromForm] string? formTimingToken,
        [FromForm] string? website,
        [FromForm] string? companyWebsite)
    {
        const bool isSpecialOffer = true;

        // Honeypot: real users never fill these hidden fields.
        if (!string.IsNullOrWhiteSpace(website)
            || !string.IsNullOrWhiteSpace(companyWebsite))
        {
            await _messageService.CreateMessageAsync(
                form,
                isSpecialOffer,
                isSpam: true,
                spamReason: "honeypot-filled",
                sendEmailNotification: false);

            return Redirect("/special-offer?sent=1");
        }

        if (!ModelState.IsValid)
        {
            return Redirect("/special-offer?error=1");
        }

        if (IsSuspiciousSubmissionSpeed(formTimingToken, out var timingReason))
        {
            _logger.LogInformation(
                "Flagged likely spam special offer submission from timing check. Reason={Reason}",
                timingReason);

            var isDuplicate = await _messageService.IsRecentDuplicateAsync(form, isSpecialOffer, TimeSpan.FromHours(24));
            if (!isDuplicate)
            {
                await _messageService.CreateMessageAsync(
                    form,
                    isSpecialOffer,
                    isSpam: true,
                    spamReason: timingReason,
                    sendEmailNotification: false);
            }

            return Redirect("/special-offer?sent=1");
        }

        if (IsRepeatSenderBurst(form, isSpecialOffer, out var burstReason))
        {
            _logger.LogInformation(
                "Suppressed repeated sender burst for special offer form. Reason={Reason}",
                burstReason);

            var isDuplicate = await _messageService.IsRecentDuplicateAsync(form, isSpecialOffer, TimeSpan.FromHours(24));
            if (!isDuplicate)
            {
                await _messageService.CreateMessageAsync(
                    form,
                    isSpecialOffer,
                    isSpam: true,
                    spamReason: burstReason,
                    sendEmailNotification: false);
            }

            return Redirect("/special-offer?sent=1");
        }

        var spamCheck = _spamFilterService.Check(form);
        if (spamCheck.IsSpam)
        {
            _logger.LogInformation(
                "Flagged likely spam special offer submission. Reason={Reason}",
                spamCheck.Reason ?? "unknown");

            var isDuplicate = await _messageService.IsRecentDuplicateAsync(form, isSpecialOffer, TimeSpan.FromHours(24));
            if (isDuplicate)
            {
                return Redirect("/special-offer?sent=1");
            }

            await _messageService.CreateMessageAsync(
                form,
                isSpecialOffer,
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

    private bool IsRepeatSenderBurst(MessageFormModel form, bool isSpecialOffer, out string reason)
    {
        reason = "repeat-sender-burst";

        var normalizedEmail = NormalizeForSenderThrottle(form.Email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return false;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var cacheKey = $"spam:sender-burst:{isSpecialOffer}:{normalizedEmail}";

        var counter = _memoryCache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2);
            return new SubmissionCounter
            {
                WindowStartUtc = nowUtc,
                Count = 0
            };
        });

        if (counter is null)
        {
            return false;
        }

        lock (counter)
        {
            if (nowUtc - counter.WindowStartUtc > SenderBurstWindow)
            {
                counter.WindowStartUtc = nowUtc;
                counter.Count = 0;
            }

            counter.Count++;

            if (counter.Count >= SenderBurstLimit)
            {
                reason = "repeat-sender-burst";
                return true;
            }

            return false;
        }
    }

    private bool IsSuspiciousSubmissionSpeed(string? formTimingToken, out string reason)
    {
        if (string.IsNullOrWhiteSpace(formTimingToken))
        {
            reason = "missing-timing-token";
            return true;
        }

        long issuedAtUnixMilliseconds;
        try
        {
            var unprotectedValue = _formTimingProtector.Unprotect(formTimingToken);
            if (!long.TryParse(unprotectedValue, out issuedAtUnixMilliseconds))
            {
                reason = "invalid-timing-token";
                return true;
            }
        }
        catch
        {
            reason = "invalid-timing-token";
            return true;
        }

        var issuedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(issuedAtUnixMilliseconds);
        var elapsed = DateTimeOffset.UtcNow - issuedAtUtc;

        if (elapsed < MinFormFillTime)
        {
            reason = "submitted-too-quickly";
            return true;
        }

        if (elapsed > MaxFormFillTime)
        {
            reason = "stale-timing-token";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static string NormalizeForSenderThrottle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant();
    }

    private sealed class SubmissionCounter
    {
        public DateTimeOffset WindowStartUtc { get; set; }
        public int Count { get; set; }
    }
}
