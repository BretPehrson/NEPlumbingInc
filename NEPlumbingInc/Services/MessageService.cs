namespace NEPlumbingInc.Services;

public interface IMessageService
{
    Task<List<MessageViewModel>> GetAllMessagesAsync();
    Task<MessageViewModel> GetMessageByIdAsync(int id);
    Task<int> GetUnreadCountAsync();
    Task<bool> IsRecentDuplicateAsync(MessageFormModel form, bool isSpecialOffer, TimeSpan window);
    Task<MessageViewModel> CreateMessageAsync(MessageFormModel form, bool isSpecialOffer, bool isSpam = false, string? spamReason = null, bool sendEmailNotification = true);
    Task AttachResumeAsync(int messageId, ResumeUploadResult resume, CancellationToken cancellationToken = default);
    Task<bool> DeleteResumeAsync(int messageId, CancellationToken cancellationToken = default);
    Task PromoteFromSpamAsync(int id);
    Task MarkAsSpamAsync(int id, string reason = "manual-review");
    Task<int> BackfillSpamMessagesAsync();
    Task MarkAsReadAsync(int id);
    Task DeleteMessageAsync(int id);
}

public class MessageService(
    IDbContextFactory<AppDbContext> contextFactory,
    IEmailService emailService,
    ISpamFilterService spamFilterService,
    IResumeStorageService resumeStorageService,
    ILogger<MessageService> logger) : IMessageService
{
    public const string SpamPrefix = "[SPAM]";

    private readonly IDbContextFactory<AppDbContext> _contextFactory = contextFactory;
    private readonly IEmailService _emailService = emailService;
    private readonly ISpamFilterService _spamFilterService = spamFilterService;
    private readonly IResumeStorageService _resumeStorageService = resumeStorageService;
    private readonly ILogger<MessageService> _logger = logger;

    public async Task<int> GetUnreadCountAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Messages.CountAsync(m => !m.IsRead && !m.Message.StartsWith(SpamPrefix));
    }

    public async Task<List<MessageViewModel>> GetAllMessagesAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Messages
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new MessageViewModel
            {
                Id = m.Id,
                Name = m.Name,
                Email = m.Email,
                Phone = m.Phone,
                AddressLine1 = m.AddressLine1,
                AddressLine2 = m.AddressLine2,
                City = m.City,
                State = m.State,
                ZipCode = m.ZipCode,
                Message = m.Message,
                ResumeBlobName = m.ResumeBlobName,
                ResumeFileName = m.ResumeFileName,
                ResumeContentType = m.ResumeContentType,
                ResumeSizeBytes = m.ResumeSizeBytes,
                CreatedAt = m.CreatedAt,
                IsSpecialOffer = m.IsSpecialOffer,
                IsRead = m.IsRead
            })
            .ToListAsync();
    }

    public async Task<MessageViewModel> GetMessageByIdAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Messages.FindAsync(id) 
            ?? throw new KeyNotFoundException($"Message {id} not found");
    }

    public async Task<bool> IsRecentDuplicateAsync(MessageFormModel form, bool isSpecialOffer, TimeSpan window)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var cutoff = DateTime.UtcNow.Subtract(window);
        var normalizedName = NormalizeForDedup(form.Name);
        var normalizedEmail = NormalizeForDedup(form.Email);
        var normalizedMessage = NormalizeForDedup(form.Message);

        var recentCandidates = await context.Messages
            .Where(m => m.CreatedAt >= cutoff && m.IsSpecialOffer == isSpecialOffer)
            .Select(m => new
            {
                m.Name,
                m.Email,
                m.Phone,
                m.Message
            })
            .ToListAsync();

        return recentCandidates.Any(m =>
            NormalizeForDedup(m.Name) == normalizedName
            && NormalizeForDedup(m.Email) == normalizedEmail
            && NormalizeForDedup(m.Message) == normalizedMessage);
    }

    public async Task<MessageViewModel> CreateMessageAsync(
        MessageFormModel form,
        bool isSpecialOffer,
        bool isSpam = false,
        string? spamReason = null,
        bool sendEmailNotification = true)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var messageBody = form.Message;
        if (isSpam)
        {
            var reasonLabel = string.IsNullOrWhiteSpace(spamReason) ? "unknown" : spamReason.Trim();
            messageBody = $"{SpamPrefix} reason={reasonLabel}\n\n{form.Message}";
        }

        var message = new MessageViewModel
        {
            Name = form.Name,
            Email = form.Email,
            Phone = string.IsNullOrWhiteSpace(form.Phone) ? null : form.Phone.Trim(),
            AddressLine1 = form.AddressLine1,
            AddressLine2 = form.AddressLine2,
            City = form.City,
            State = form.State,
            ZipCode = form.ZipCode,
            Message = messageBody,
            CreatedAt = DateTime.UtcNow,
            IsSpecialOffer = isSpecialOffer,
            IsRead = false
        };

        context.Messages.Add(message);
        await context.SaveChangesAsync();

        if (sendEmailNotification)
        {
            // Email notifications are best-effort; never fail message creation if SMTP fails.
            try
            {
                await _emailService.SendNewMessageNotificationAsync(
                    form,
                    isSpecialOffer,
                    isPotentialSpam: isSpam,
                    spamReason: spamReason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send new message email notification. MessageId={MessageId}", message.Id);
            }
        }

        return message;
    }

    private static string NormalizeForDedup(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return string.Join(' ', input.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public async Task AttachResumeAsync(int messageId, ResumeUploadResult resume, CancellationToken cancellationToken = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var message = await context.Messages.FindAsync(messageId, cancellationToken)
            ?? throw new KeyNotFoundException($"Message {messageId} not found");

        message.ResumeBlobName = resume.BlobName;
        message.ResumeFileName = resume.OriginalFileName;
        message.ResumeContentType = resume.ContentType;
        message.ResumeSizeBytes = resume.SizeBytes;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteResumeAsync(int messageId, CancellationToken cancellationToken = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var message = await context.Messages.FindAsync(messageId, cancellationToken)
            ?? throw new KeyNotFoundException($"Message {messageId} not found");

        if (string.IsNullOrWhiteSpace(message.ResumeBlobName)) return false;

        var blobName = message.ResumeBlobName;
        var deleted = await _resumeStorageService.DeleteResumeAsync(blobName, cancellationToken);

        if (deleted)
        {
            message.ResumeBlobName = null;
            message.ResumeFileName = null;
            message.ResumeContentType = null;
            message.ResumeSizeBytes = null;
            await context.SaveChangesAsync(cancellationToken);
        }

        return deleted;
    }

    public async Task PromoteFromSpamAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var message = await context.Messages.FindAsync(id)
            ?? throw new KeyNotFoundException($"Message {id} not found");

        if (!message.Message.StartsWith(SpamPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var split = message.Message.Split("\n\n", 2, StringSplitOptions.None);
        message.Message = split.Length == 2 ? split[1] : message.Message;
        message.IsRead = false;

        await context.SaveChangesAsync();
    }

    public async Task MarkAsSpamAsync(int id, string reason = "manual-review")
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var message = await context.Messages.FindAsync(id)
            ?? throw new KeyNotFoundException($"Message {id} not found");

        if (message.Message.StartsWith(SpamPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var reasonLabel = string.IsNullOrWhiteSpace(reason) ? "manual-review" : reason.Trim();
        message.Message = $"{SpamPrefix} reason={reasonLabel}\n\n{message.Message}";
        message.IsRead = true;

        await context.SaveChangesAsync();
    }

    public async Task<int> BackfillSpamMessagesAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var candidates = await context.Messages
            .Where(m => !m.Message.StartsWith(SpamPrefix))
            .Select(m => new
            {
                Entity = m,
                Form = new MessageFormModel
                {
                    Name = m.Name,
                    Email = m.Email,
                    Phone = m.Phone,
                    AddressLine1 = m.AddressLine1,
                    AddressLine2 = m.AddressLine2,
                    City = m.City,
                    State = m.State,
                    ZipCode = m.ZipCode,
                    Message = m.Message
                }
            })
            .ToListAsync();

        var updatedCount = 0;

        foreach (var candidate in candidates)
        {
            var spamCheck = _spamFilterService.Check(candidate.Form);
            if (!spamCheck.IsSpam)
            {
                continue;
            }

            candidate.Entity.Message = $"{SpamPrefix} reason={spamCheck.Reason ?? "unknown"}\n\n{candidate.Entity.Message}";
            updatedCount++;
        }

        if (updatedCount > 0)
        {
            await context.SaveChangesAsync();
        }

        return updatedCount;
    }

    public async Task MarkAsReadAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var message = await context.Messages.FindAsync(id)
            ?? throw new KeyNotFoundException($"Message {id} not found");
            
        message.IsRead = true;
        await context.SaveChangesAsync();
    }

    public async Task DeleteMessageAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var message = await context.Messages.FindAsync(id)
            ?? throw new KeyNotFoundException($"Message {id} not found");
            
        context.Messages.Remove(message);
        await context.SaveChangesAsync();
    }
}