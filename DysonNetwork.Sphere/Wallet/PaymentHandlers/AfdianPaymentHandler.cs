using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
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
            var paramsJson = JsonConvert.SerializeObject(new { page });
            var ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1))
                .TotalSeconds; // Current timestamp in seconds

            var sign = CalculateSign(token, userId, paramsJson, ts);

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://afdian.com/api/open/query-order")
            {
                Content = new StringContent(JsonConvert.SerializeObject(new
                {
                    user_id = userId,
                    @params = paramsJson,
                    ts,
                    sign
                }), Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    $"Response Error: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                return null;
            }

            var result = JsonConvert.DeserializeObject<OrderResponse>(await response.Content.ReadAsStringAsync());
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
            var paramsJson = JsonConvert.SerializeObject(new { out_trade_no = orderId });
            var ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1))
                .TotalSeconds; // Current timestamp in seconds

            var sign = CalculateSign(token, userId, paramsJson, ts);

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://afdian.com/api/open/query-order")
            {
                Content = new StringContent(JsonConvert.SerializeObject(new
                {
                    user_id = userId,
                    @params = paramsJson,
                    ts,
                    sign
                }), Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    $"Response Error: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                return null;
            }

            var result = JsonConvert.DeserializeObject<OrderResponse>(await response.Content.ReadAsStringAsync());

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
            var paramsJson = JsonConvert.SerializeObject(new { out_trade_no = orderIdsParam });
            var ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1))
                .TotalSeconds; // Current timestamp in seconds

            var sign = CalculateSign(token, userId, paramsJson, ts);

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://afdian.com/api/open/query-order")
            {
                Content = new StringContent(JsonConvert.SerializeObject(new
                {
                    user_id = userId,
                    @params = paramsJson,
                    ts,
                    sign
                }), Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    $"Response Error: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                return new List<OrderItem>();
            }

            var result = JsonConvert.DeserializeObject<OrderResponse>(await response.Content.ReadAsStringAsync());

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
    public async Task<WebhookResponse> HandleWebhook(HttpRequest request, Func<WebhookOrderData, Task>? processOrderAction)
    {
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
            var webhook = JsonConvert.DeserializeObject<WebhookRequest>(requestBody);

            if (webhook == null)
            {
                _logger.LogError("Failed to parse webhook data");
                return new WebhookResponse { ErrorCode = 400, ErrorMessage = "Invalid webhook data" };
            }

            // Validate the webhook type
            if (webhook.Data.Type != "order")
            {
                _logger.LogWarning($"Unsupported webhook type: {webhook.Data.Type}");
                return new WebhookResponse { ErrorCode = 200, ErrorMessage = "Unsupported type, but acknowledged" };
            }

            // Process the order
            try
            {
                // Check for duplicate order processing by storing processed order IDs
                // (You would implement a more permanent storage mechanism for production)
                if (processOrderAction != null)
                    await processOrderAction(webhook.Data);
                else
                    _logger.LogInformation($"Order received but no processing action provided: {webhook.Data.Order.TradeNumber}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing order {webhook.Data.Order.TradeNumber}");
                // Still returning success to Afdian to prevent repeated callbacks
                // Your system should handle the error internally
            }

            // Return success response to Afdian
            return new WebhookResponse { ErrorCode = 200, ErrorMessage = "" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling webhook");
            return new WebhookResponse { ErrorCode = 500, ErrorMessage = "Internal server error" };
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
    [JsonProperty("ec")] public int ErrorCode { get; set; }

    [JsonProperty("em")] public string ErrorMessage { get; set; } = null!;

    [JsonProperty("data")] public OrderData Data { get; set; } = null!;
}

public class OrderData
{
    [JsonProperty("list")] public List<OrderItem> Orders { get; set; } = null!;

    [JsonProperty("total_count")] public int TotalCount { get; set; }

    [JsonProperty("total_page")] public int TotalPages { get; set; }

    [JsonProperty("request")] public RequestDetails Request { get; set; } = null!;
}

public class OrderItem : ISubscriptionOrder
{
    [JsonProperty("out_trade_no")] public string TradeNumber { get; set; } = null!;

    [JsonProperty("user_id")] public string UserId { get; set; } = null!;

    [JsonProperty("plan_id")] public string PlanId { get; set; } = null!;

    [JsonProperty("month")] public int Months { get; set; }

    [JsonProperty("total_amount")] public string TotalAmount { get; set; } = null!;

    [JsonProperty("show_amount")] public string ShowAmount { get; set; } = null!;

    [JsonProperty("status")] public int Status { get; set; }

    [JsonProperty("remark")] public string Remark { get; set; } = null!;

    [JsonProperty("redeem_id")] public string RedeemId { get; set; } = null!;

    [JsonProperty("product_type")] public int ProductType { get; set; }

    [JsonProperty("discount")] public string Discount { get; set; } = null!;

    [JsonProperty("sku_detail")] public List<object> SkuDetail { get; set; } = null!;

    [JsonProperty("create_time")] public long CreateTime { get; set; }

    [JsonProperty("user_name")] public string UserName { get; set; } = null!;

    [JsonProperty("plan_title")] public string PlanTitle { get; set; } = null!;

    [JsonProperty("user_private_id")] public string UserPrivateId { get; set; } = null!;

    [JsonProperty("address_person")] public string AddressPerson { get; set; } = null!;

    [JsonProperty("address_phone")] public string AddressPhone { get; set; } = null!;

    [JsonProperty("address_address")] public string AddressAddress { get; set; } = null!;

    public Instant BegunAt => Instant.FromUnixTimeSeconds(CreateTime);

    public Duration Duration => Duration.FromDays(Months * 30);

    public string Provider => "afdian";

    public string Id => TradeNumber;

    public string SubscriptionId => PlanId;

    public string AccountId => UserId;
}

public class RequestDetails
{
    [JsonProperty("user_id")] public string UserId { get; set; } = null!;

    [JsonProperty("params")] public string Params { get; set; } = null!;

    [JsonProperty("ts")] public long Timestamp { get; set; }

    [JsonProperty("sign")] public string Sign { get; set; } = null!;
}

/// <summary>
/// Request structure for Afdian webhook
/// </summary>
public class WebhookRequest
{
    [JsonProperty("ec")] public int ErrorCode { get; set; }

    [JsonProperty("em")] public string ErrorMessage { get; set; } = null!;

    [JsonProperty("data")] public WebhookOrderData Data { get; set; } = null!;
}

/// <summary>
/// Order data contained in the webhook
/// </summary>
public class WebhookOrderData
{
    [JsonProperty("type")] public string Type { get; set; } = null!;

    [JsonProperty("order")] public WebhookOrderDetails Order { get; set; } = null!;
}

/// <summary>
/// Order details in the webhook
/// </summary>
public class WebhookOrderDetails : OrderItem
{
    [JsonProperty("custom_order_id")] public string CustomOrderId { get; set; } = null!;
}

/// <summary>
/// Response structure to acknowledge webhook receipt
/// </summary>
public class WebhookResponse
{
    [JsonProperty("ec")] public int ErrorCode { get; set; } = 200;

    [JsonProperty("em")] public string ErrorMessage { get; set; } = "";
}

/// <summary>
/// SKU detail item
/// </summary>
public class SkuDetailItem
{
    [JsonProperty("sku_id")] public string SkuId { get; set; } = null!;

    [JsonProperty("count")] public int Count { get; set; }

    [JsonProperty("name")] public string Name { get; set; } = null!;

    [JsonProperty("album_id")] public string AlbumId { get; set; } = null!;

    [JsonProperty("pic")] public string Picture { get; set; } = null!;
}
