namespace ProductsApi.Webhooks;

// Shape of the JSON body:
//   { "id": "evt_123", "type": "stock.adjusted", "data": { "productId": 1, "delta": 5 } }
public record WebhookEventRequest(string? Id, string? Type, StockAdjustedData? Data);

public record StockAdjustedData(int ProductId, int Delta);
