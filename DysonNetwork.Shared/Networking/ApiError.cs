using System.Text.Json.Serialization;

namespace DysonNetwork.Shared.Networking;

/// <summary>
/// Standardized error payload to return to clients.
/// Inspired by RFC7807 (problem+json) with app-specific fields.
/// </summary>
public class ApiError
{
    /// <summary>
    /// Application-specific error code (e.g., "VALIDATION_ERROR", "NOT_FOUND", "SERVER_ERROR").
    /// </summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = "UNKNOWN_ERROR";

    /// <summary>
    /// Short, human-readable message for the error.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "An unexpected error occurred.";

    /// <summary>
    /// HTTP status code to be used by the server when sending this error.
    /// Optional to keep the model transport-agnostic.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Status { get; set; }

    /// <summary>
    /// More detailed description of the error.
    /// </summary>
    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; }

    /// <summary>
    /// Server trace identifier (e.g., from HttpContext.TraceIdentifier) to help debugging.
    /// </summary>
    [JsonPropertyName("traceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; set; }

    /// <summary>
    /// Field-level validation errors: key is the field name, value is an array of messages.
    /// </summary>
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string[]>? Errors { get; set; }

    /// <summary>
    /// Arbitrary additional metadata for clients.
    /// </summary>
    [JsonPropertyName("meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Meta { get; set; }

    /// <summary>
    /// Factory for a validation error payload.
    /// </summary>
    public static ApiError Validation(
        Dictionary<string, string[]> errors,
        string? message = null,
        int status = 400,
        string code = "VALIDATION_ERROR",
        string? traceId = null)
    {
        return new ApiError
        {
            Code = code,
            Message = message ?? "One or more validation errors occurred.",
            Status = status,
            Errors = errors,
            TraceId = traceId
        };
    }

    /// <summary>
    /// Factory for a not-found error payload.
    /// </summary>
    public static ApiError NotFound(
        string resource,
        string? message = null,
        int status = 404,
        string code = "NOT_FOUND",
        string? traceId = null)
    {
        return new ApiError
        {
            Code = code,
            Message = message ?? $"The requested resource '{resource}' was not found.",
            Status = status,
            Detail = resource,
            TraceId = traceId
        };
    }

    /// <summary>
    /// Factory for a generic server error payload.
    /// </summary>
    public static ApiError Server(
        string? message = null,
        int status = 500,
        string code = "SERVER_ERROR",
        string? traceId = null,
        string? detail = null)
    {
        return new ApiError
        {
            Code = code,
            Message = message ?? "An internal server error occurred.",
            Status = status,
            TraceId = traceId,
            Detail = detail
        };
    }

    /// <summary>
    /// Factory for an unauthorized/forbidden error payload.
    /// </summary>
    public static ApiError Unauthorized(
        string? message = null,
        bool forbidden = false,
        string? traceId = null)
    {
        return new ApiError
        {
            Code = forbidden ? "FORBIDDEN" : "UNAUTHORIZED",
            Message = message ?? (forbidden ? "You do not have permission to perform this action." : "Authentication is required."),
            Status = forbidden ? 403 : 401,
            TraceId = traceId
        };
    }
}
