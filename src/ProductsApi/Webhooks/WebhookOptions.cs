namespace ProductsApi.Webhooks;

public class WebhookOptions
{
    // Shared signing secret. Comes from the environment (Webhook__Secret), never from source control.
    public string Secret { get; set; } = string.Empty;

    // How far the request timestamp may be from the server clock (in either direction)
    public int ToleranceSeconds { get; set; } = 300;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Secret);
}
