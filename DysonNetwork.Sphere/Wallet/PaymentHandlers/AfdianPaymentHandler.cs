using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Sphere.Wallet.PaymentHandlers;

public class AfdianPaymentHandler(
    IHttpClientFactory httpClientFactory,
    ILogger<AfdianPaymentHandler> logger,
    IConfiguration configuration
)
{
    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    private readonly ILogger<AfdianPaymentHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly IConfiguration _configuration =
        configuration ?? throw new ArgumentNullException(nameof(configuration));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private string CalculateSign(string token, string userId, string paramsJson, long ts)
    {
        var kvString = $"{token}params{paramsJson}ts{ts}user_id{userId}";
        using (var md5 = MD5.Create())
        {
            var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(kvString));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        }
    }

    public async Task<OrderResponse?> ListOrderAsync(int page = 1)
    {
        try
        {
            var userId = "abc"; // Replace with your actual USER_ID
            var token = _configuration["Payment:Auth:Afdian"] ?? "<token here>";
            var paramsJson = JsonSerializer.Serialize(new { page }, JsonOptions);
            var ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1))
                .TotalSeconds; // Current timestamp in seconds

            var sign = CalculateSign(token, userId, paramsJson, ts);

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://afdian.com/api/open/query-order")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    user_id = userId,
                    @params = paramsJson,
                    ts,
                    sign
                }, JsonOptions), Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    $"Response Error: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                return null;
            }

            var result = await JsonSerializer.DeserializeAsync<OrderResponse>(
                await response.Content.ReadAsStreamAsync(), JsonOptions);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching orders");
            throw;
        }
    }

    /// <summary>
    /// Get a specific order by its ID (out_trade_no)
    /// </summary>
    /// <param name="orderId">The order ID to query</param>
    /// <returns>The order item if found, otherwise null</returns>
    public async Task<OrderItem?> GetOrderAsync(string orderId)
    {
        if (string.IsNullOrEmpty(orderId))
        {
            _logger.LogWarning("Order ID cannot be null or empty");
            return null;
        }

        try
        {
            var userId = "abc"; // Replace with your actual USER_ID
            var token = _configuration["Payment:Auth:Afdian"] ?? "<token here>";
            var paramsJson = JsonSerializer.Serialize(new { out_trade_no = orderId }, JsonOptions);
            var ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1))
                .TotalSeconds; // Current timestamp in seconds

            var sign = CalculateSign(token, userId, paramsJson, ts);

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://afdian.com/api/open/query-order")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    user_id = userId,
                    @params = paramsJson,
                    ts,
                    sign
                }, JsonOptions), Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    $"Response Error: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                return null;
            }

            var result = await JsonSerializer.DeserializeAsync<OrderResponse>(
                await response.Content.ReadAsStreamAsync(), JsonOptions);

            // Check if we have a valid response and orders in the list
            if (result?.Data?.Orders == null || result.Data.Orders.Count == 0)
            {
                _logger.LogWarning($"No order found with ID: {orderId}");
                return null;
            }

            // Since we're querying by a specific order ID, we should only get one result
            return result.Data.Orders.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching order with ID: {orderId}");
            throw;
        }
    }

    /// <summary>
    /// Get multiple orders by their IDs (out_trade_no)
    /// </summary>
    /// <param name="orderIds">A collection of order IDs to query</param>
    /// <returns>A list of found order items</returns>
    public async Task<List<OrderItem>> GetOrders(IEnumerable<string> orderIds)
    {
        if (orderIds == null || !orderIds.Any())
        {
            _logger.LogWarning("Order IDs cannot be null or empty");
            return new List<OrderItem>();
        }

        try
        {
            // Join the order IDs with commas as specified in the API documentation
            var orderIdsParam = string.Join(",", orderIds);

            var userId = "abc"; // Replace with your actual USER_ID
            var token = _configuration["Payment:Auth:Afdian"] ?? "<token here>";
            var paramsJson = JsonSerializer.Serialize(new { out_trade_no = orderIdsParam }, JsonOptions);
            var ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1))
                .TotalSeconds; // Current timestamp in seconds

            var sign = CalculateSign(token, userId, paramsJson, ts);

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://afdian.com/api/open/query-order")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    user_id = userId,
                    @params = paramsJson,
                    ts,
                    sign
                }, JsonOptions), Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    $"Response Error: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                return new List<OrderItem>();
            }

            var result = await JsonSerializer.DeserializeAsync<OrderResponse>(
                await response.Content.ReadAsStreamAsync(), JsonOptions);

            // Check if we have a valid response and orders in the list
            if (result?.Data?.Orders == null || result.Data.Orders.Count == 0)
            {
                _logger.LogWarning($"No orders found with IDs: {orderIdsParam}");
                return new List<OrderItem>();
            }

            return result.Data.Orders;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching orders");
            throw;
        }
    }

    /// <summary>
    /// Handle an incoming webhook from Afdian's payment platform
    /// </summary>
    /// <param name="request">The HTTP request containing webhook data</param>
    /// <param name="processOrderAction">An action to process the received order</param>
    /// <returns>A WebhookResponse object to be returned to Afdian</returns>
    public async Task<WebhookResponse> HandleWebhook(
        HttpRequest request,
        Func<WebhookOrderData, Task>? processOrderAction
    )
    {
        _logger.LogInformation("Received webhook request from afdian...");

        try
        {
            // Read the request body
            string requestBody;
            using (var reader = new StreamReader(request.Body, Encoding.UTF8))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogError("Webhook request body is empty");
                return new WebhookResponse { ErrorCode = 400, ErrorMessage = "Empty request body" };
            }

            _logger.LogInformation($"Received webhook: {requestBody}");

            // Parse the webhook data
            var webhook = JsonSerializer.Deserialize<WebhookRequest>(requestBody, JsonOptions);

            if (webhook == null)
            {
                _logger.LogError("Failed to parse webhook data");
                return new WebhookResponse { ErrorCode = 400, ErrorMessage = "Invalid webhook data" };
            }

            // Validate the webhook type
            if (webhook.Data.Type != "order")
            {
                _logger.LogWarning($"Unsupported webhook type: {webhook.Data.Type}");
                return WebhookResponse.Success;
            }

            // Process the order
            try
            {
                // Check for duplicate order processing by storing processed order IDs
                // (You would implement a more permanent storage mechanism for production)
                if (processOrderAction != null)
                    await processOrderAction(webhook.Data);
                else
                    _logger.LogInformation(
                        $"Order received but no processing action provided: {webhook.Data.Order.TradeNumber}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing order {webhook.Data.Order.TradeNumber}");
                // Still returning success to Afdian to prevent repeated callbacks
                // Your system should handle the error internally
            }

            // Return success response to Afdian
            return WebhookResponse.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling webhook");
            return WebhookResponse.Success;
        }
    }

    public string? GetSubscriptionPlanId(string subscriptionKey)
    {
        var planId = _configuration[$"Payment:Subscriptions:Afdian:{subscriptionKey}"];

        if (string.IsNullOrEmpty(planId))
        {
            _logger.LogWarning($"Unknown subscription key: {subscriptionKey}");
            return null;
        }

        return planId;
    }
}

public class OrderResponse
{
    [JsonPropertyName("ec")] public int ErrorCode { get; set; }

    [JsonPropertyName("em")] public string ErrorMessage { get; set; } = null!;

    [JsonPropertyName("data")] public OrderData Data { get; set; } = null!;
}

public class OrderData
{
    [JsonPropertyName("list")] public List<OrderItem> Orders { get; set; } = null!;

    [JsonPropertyName("total_count")] public int TotalCount { get; set; }

    [JsonPropertyName("total_page")] public int TotalPages { get; set; }

    [JsonPropertyName("request")] public RequestDetails Request { get; set; } = null!;
}

public class OrderItem : ISubscriptionOrder
{
    [JsonPropertyName("out_trade_no")] public string TradeNumber { get; set; } = null!;

    [JsonPropertyName("user_id")] public string UserId { get; set; } = null!;

    [JsonPropertyName("plan_id")] public string PlanId { get; set; } = null!;

    [JsonPropertyName("month")] public int Months { get; set; }

    [JsonPropertyName("total_amount")] public string TotalAmount { get; set; } = null!;

    [JsonPropertyName("show_amount")] public string ShowAmount { get; set; } = null!;

    [JsonPropertyName("status")] public int Status { get; set; }

    [JsonPropertyName("remark")] public string Remark { get; set; } = null!;

    [JsonPropertyName("redeem_id")] public string RedeemId { get; set; } = null!;

    [JsonPropertyName("product_type")] public int ProductType { get; set; }

    [JsonPropertyName("discount")] public string Discount { get; set; } = null!;

    [JsonPropertyName("sku_detail")] public List<object> SkuDetail { get; set; } = null!;

    [JsonPropertyName("create_time")] public long CreateTime { get; set; }

    [JsonPropertyName("user_name")] public string UserName { get; set; } = null!;

    [JsonPropertyName("plan_title")] public string PlanTitle { get; set; } = null!;

    [JsonPropertyName("user_private_id")] public string UserPrivateId { get; set; } = null!;

    [JsonPropertyName("address_person")] public string AddressPerson { get; set; } = null!;

    [JsonPropertyName("address_phone")] public string AddressPhone { get; set; } = null!;

    [JsonPropertyName("address_address")] public string AddressAddress { get; set; } = null!;

    public Instant BegunAt => Instant.FromUnixTimeSeconds(CreateTime);

    public Duration Duration => Duration.FromDays(Months * 30);

    public string Provider => "afdian";

    public string Id => TradeNumber;

    public string SubscriptionId => PlanId;

    public string AccountId => UserId;
}

public class RequestDetails
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = null!;

    [JsonPropertyName("params")] public string Params { get; set; } = null!;

    [JsonPropertyName("ts")] public long Timestamp { get; set; }

    [JsonPropertyName("sign")] public string Sign { get; set; } = null!;
}

/// <summary>
/// Request structure for Afdian webhook
/// </summary>
public class WebhookRequest
{
    [JsonPropertyName("ec")] public int ErrorCode { get; set; }

    [JsonPropertyName("em")] public string ErrorMessage { get; set; } = null!;

    [JsonPropertyName("data")] public WebhookOrderData Data { get; set; } = null!;
}

/// <summary>
/// Order data contained in the webhook
/// </summary>
public class WebhookOrderData
{
    [JsonPropertyName("type")] public string Type { get; set; } = null!;

    [JsonPropertyName("order")] public WebhookOrderDetails Order { get; set; } = null!;
}

/// <summary>
/// Order details in the webhook
/// </summary>
public class WebhookOrderDetails : OrderItem
{
    [JsonPropertyName("custom_order_id")] public string CustomOrderId { get; set; } = null!;
}

/// <summary>
/// Response structure to acknowledge webhook receipt
/// </summary>
public class WebhookResponse
{
    [JsonPropertyName("ec")] public int ErrorCode { get; set; } = 200;

    [JsonPropertyName("em")] public string ErrorMessage { get; set; } = "";

    public static WebhookResponse Success => new()
    {
        ErrorCode = 200,
        ErrorMessage = string.Empty
    };
}

/// <summary>
/// SKU detail item
/// </summary>
public class SkuDetailItem
{
    [JsonPropertyName("sku_id")] public string SkuId { get; set; } = null!;

    [JsonPropertyName("count")] public int Count { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; } = null!;

    [JsonPropertyName("album_id")] public string AlbumId { get; set; } = null!;

    [JsonPropertyName("pic")] public string Picture { get; set; } = null!;
}