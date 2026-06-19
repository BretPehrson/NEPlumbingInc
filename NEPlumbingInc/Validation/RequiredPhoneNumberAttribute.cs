namespace NEPlumbingInc.Validation;

public sealed class RequiredPhoneNumberAttribute : ValidationAttribute
{
    public int MinDigits { get; init; } = 10;
    public int MaxDigits { get; init; } = 15;

    public RequiredPhoneNumberAttribute()
    {
        ErrorMessage = "Please enter a valid phone number";
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Phone is optional in the public forms; validate only when provided.
        if (value is not string phone || string.IsNullOrWhiteSpace(phone))
        {
            return ValidationResult.Success;
        }

        var digitCount = 0;
        var digits = new System.Text.StringBuilder();

        foreach (var ch in phone)
        {
            if (char.IsDigit(ch))
            {
                digitCount++;
                digits.Append(ch);
                continue;
            }

            if (ch is ' ' or '(' or ')' or '-' or '.' or '+')
            {
                continue;
            }

            return new ValidationResult(ErrorMessage);
        }

        if (digitCount < MinDigits || digitCount > MaxDigits)
        {
            return new ValidationResult(ErrorMessage);
        }

        // Reject obviously fake numbers: all digits the same (e.g. 0000000000)
        if (IsAllSameDigits(digits.ToString()))
        {
            return new ValidationResult(ErrorMessage);
        }

        // Reject simple ascending or descending sequences like 1234567890 or 9876543210
        if (IsSequential(digits.ToString()))
        {
            return new ValidationResult(ErrorMessage);
        }

        return ValidationResult.Success;
    }

    private static bool IsAllSameDigits(string digits)
    {
        if (string.IsNullOrEmpty(digits)) return false;
        var first = digits[0];
        for (int i = 1; i < digits.Length; i++)
        {
            if (digits[i] != first) return false;
        }
        return true;
    }

    private static bool IsSequential(string digits)
    {
        if (digits.Length < 6) return false; // short sequences are common in local numbers

        // Check ascending
        var asc = true;
        for (int i = 1; i < digits.Length; i++)
        {
            if ((digits[i] - digits[i - 1]) != 1)
            {
                asc = false;
                break;
            }
        }
        if (asc) return true;

        // Check descending
        var desc = true;
        for (int i = 1; i < digits.Length; i++)
        {
            if ((digits[i - 1] - digits[i]) != 1)
            {
                desc = false;
                break;
            }
        }
        return desc;
    }
}
