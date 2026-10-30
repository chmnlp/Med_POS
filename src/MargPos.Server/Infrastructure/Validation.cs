using System.Text.RegularExpressions;

namespace MargPos.Server.Infrastructure;

/// <summary>Collects field errors and turns them into a 400 ValidationProblem response.</summary>
public sealed partial class Errors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool Any => _errors.Count > 0;

    public Errors Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
            _errors[field] = list = [];
        list.Add(message);
        return this;
    }

    public Errors Add(bool condition, string field, string message) => condition ? Add(field, message) : this;

    public Errors Required(string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(field, $"{field} is required.");
        else MaxLength(field, value, maxLength);
        return this;
    }

    public Errors MaxLength(string field, string? value, int maxLength)
    {
        if (value is not null && value.Trim().Length > maxLength)
            Add(field, $"{field} must be at most {maxLength} characters.");
        return this;
    }

    public Errors NonNegative(string field, decimal value)
    {
        if (value < 0) Add(field, $"{field} cannot be negative.");
        return this;
    }

    public Errors Gstin(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !GstinRegex().IsMatch(value.Trim().ToUpperInvariant()))
            Add(field, "GSTIN must be 15 characters, e.g. 27ABCDE1234F1Z5.");
        return this;
    }

    public Errors Phone(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !PhoneRegex().IsMatch(value.Trim()))
            Add(field, "Phone must be 10 digits, optionally prefixed with +91 or 0.");
        return this;
    }

    public Errors Email(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !EmailRegex().IsMatch(value.Trim()))
            Add(field, "Email address is not valid.");
        return this;
    }

    public IResult ToResult() =>
        Results.ValidationProblem(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));

    /// <summary>Trims text and converts blank values to null.</summary>
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static string Truncate(string s, int maxLength) => s.Length <= maxLength ? s : s[..maxLength];

    [GeneratedRegex(@"^\d{2}[A-Z]{5}\d{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")]
    private static partial Regex GstinRegex();

    [GeneratedRegex(@"^(\+91[\s-]?|0)?[6-9]\d{9}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^\d{4,8}$")]
    public static partial Regex HsnRegex();
}
