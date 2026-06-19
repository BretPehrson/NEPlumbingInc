namespace NEPlumbingInc.Services;

public interface IEmailService
{
    Task SendNewMessageNotificationAsync(MessageFormModel model, bool isSpecialOffer, bool isPotentialSpam = false, string? spamReason = null);
}

public class EmailService(
    IOptions<EmailSettings> options,
    IMessageNotificationSettingsService messageNotificationSettingsService) : IEmailService
{
    private readonly EmailSettings _settings = options.Value;
    private readonly IMessageNotificationSettingsService _messageNotificationSettingsService = messageNotificationSettingsService;

    public async Task SendNewMessageNotificationAsync(
        MessageFormModel model,
        bool isSpecialOffer,
        bool isPotentialSpam = false,
        string? spamReason = null)
    {
        if (string.IsNullOrWhiteSpace(_settings.From)
            || string.IsNullOrWhiteSpace(_settings.AppPassword))
        {
            return;
        }

        IReadOnlyList<string> recipients;
        try
        {
            recipients = await _messageNotificationSettingsService.GetRecipientEmailsAsync();
        }
        catch
        {
            recipients = Array.Empty<string>();
        }

        var fallbackRecipient = _settings.To;
        if (recipients.Count == 0 && string.IsNullOrWhiteSpace(fallbackRecipient))
        {
            return;
        }

        var smtpClient = new SmtpClient("smtp.gmail.com")
        {
            Port = 587,
            Credentials = new NetworkCredential(_settings.From, _settings.AppPassword),
            EnableSsl = true
        };

        var addressBlock = BuildAddressBlock(model);
        var sourceLabel = isSpecialOffer ? "Special Offer" : "Contact";
        var spamSubjectPrefix = isPotentialSpam ? "[POTENTIAL SPAM] " : string.Empty;
        var spamBanner = isPotentialSpam
            ? $"POTENTIAL SPAM{(string.IsNullOrWhiteSpace(spamReason) ? string.Empty : $" ({spamReason})")}\n\n"
            : string.Empty;

        var mail = new MailMessage
        {
            From = new MailAddress(_settings.From),
            Subject = $"{spamSubjectPrefix}New {sourceLabel} message from {model.Name}",
            Body =
                spamBanner +
                $"Name: {model.Name}\n" +
                $"Email: {model.Email}\n" +
                $"Phone: {model.Phone}\n" +
                addressBlock +
                "\n" +
                "Message:\n" +
                model.Message,
            IsBodyHtml = false
        };

        if (recipients.Count > 0)
        {
            foreach (var recipient in recipients)
            {
                mail.To.Add(recipient);
            }
        }
        else
        {
            mail.To.Add(fallbackRecipient);
        }
        mail.ReplyToList.Add(new MailAddress(model.Email));

        await smtpClient.SendMailAsync(mail);
    }

    private static string BuildAddressBlock(MessageFormModel model)
    {
        if (string.IsNullOrWhiteSpace(model.AddressLine1)
            && string.IsNullOrWhiteSpace(model.AddressLine2)
            && string.IsNullOrWhiteSpace(model.City)
            && string.IsNullOrWhiteSpace(model.State)
            && string.IsNullOrWhiteSpace(model.ZipCode))
        {
            return string.Empty;
        }

        return
            "\nAddress:\n" +
            $"{model.AddressLine1}\n" +
            (string.IsNullOrWhiteSpace(model.AddressLine2) ? string.Empty : $"{model.AddressLine2}\n") +
            $"{model.City}, {model.State} {model.ZipCode}\n";
    }
}