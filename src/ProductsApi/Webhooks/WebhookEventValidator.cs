using System.Text.RegularExpressions;

namespace ProductsApi.Webhooks;

public static class WebhookEventValidator
{
    public const string SupportedType = "stock.adjusted";
    public const int MaxDelta = 100_000;

    // \z (not $) so a trailing newline can never sneak into an id that ends up in logs
    private static readonly Regex IdPattern = new(@"^[A-Za-z0-9_.:\-]{1,100}\z", RegexOptions.Compiled);

    public static List<string> Validate(WebhookEventRequest? request)
    {
        var errors = new List<string>();

        if (request is null)
        {
            errors.Add("body must be a JSON object");
            return errors;
        }

        if (string.IsNullOrEmpty(request.Id) || !IdPattern.IsMatch(request.Id))
        {
            errors.Add("id is required: 1 to 100 characters (letters, digits, '_', '.', ':' or '-')");
        }

        if (request.Type != SupportedType)
        {
            errors.Add($"type must be '{SupportedType}'");
        }

        if (request.Data is null)
        {
            errors.Add("data is required");
        }
        else
        {
            if (request.Data.ProductId <= 0)
            {
                errors.Add("data.productId must be a positive integer");
            }

            if (request.Data.Delta == 0 || request.Data.Delta < -MaxDelta || request.Data.Delta > MaxDelta)
            {
                errors.Add($"data.delta must be a non-zero integer between -{MaxDelta} and {MaxDelta}");
            }
        }

        return errors;
    }
}
