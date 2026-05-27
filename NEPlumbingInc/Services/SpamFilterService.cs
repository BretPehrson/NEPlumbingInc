using System.Text.RegularExpressions;
using NEPlumbingInc.Models;

namespace NEPlumbingInc.Services;

public interface ISpamFilterService
{
    SpamCheckResult Check(MessageFormModel form);
}

public sealed record SpamCheckResult(bool IsSpam, string? Reason = null);

public partial class SpamFilterService : ISpamFilterService
{
    public SpamCheckResult Check(MessageFormModel form)
    {
        var message = form.Message?.Trim() ?? string.Empty;
        var combined = string.Join(' ',
            form.Name ?? string.Empty,
            form.Email ?? string.Empty,
            message,
            form.AddressLine1 ?? string.Empty,
            form.AddressLine2 ?? string.Empty,
            form.City ?? string.Empty,
            form.State ?? string.Empty,
            form.ZipCode ?? string.Empty);

        if (string.IsNullOrWhiteSpace(message))
        {
            return new SpamCheckResult(false);
        }

        var urlMatches = UrlRegex().Matches(message);
        if (urlMatches.Count > 0)
        {
            return new SpamCheckResult(true, "contains-link");
        }

        if (CryptoRegex().IsMatch(combined))
        {
            return new SpamCheckResult(true, "crypto-keyword");
        }

        if (SeoSpamRegex().IsMatch(combined))
        {
            return new SpamCheckResult(true, "seo-spam-keyword");
        }

        if (RepeatedSymbolRegex().IsMatch(message))
        {
            return new SpamCheckResult(true, "repeated-symbol-pattern");
        }

        return new SpamCheckResult(false);
    }

    [GeneratedRegex(@"(https?://|www\.|\b[a-z0-9][a-z0-9\-]{1,62}\.(com|net|org|io|co|biz|info|xyz|ru|top|click|online)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\b(bitcoin|btc|crypto|cryptocurrency|blockchain|wallet|coinbase|binance|ethereum|eth|usdt|nft|forex)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CryptoRegex();

    [GeneratedRegex(@"\b(guest post|backlink|domain authority|seo service|increase traffic|whatsapp|telegram|casino|betting|loan approval)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeoSpamRegex();

    [GeneratedRegex(@"([!?.\-_=*])\1{5,}", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedSymbolRegex();
}