namespace DysonNetwork.Sphere.Auth;

public class CloudflareVerificationResponse
{
    public bool Success { get; set; }
    public string[]? ErrorCodes { get; set; }
}

public class GoogleVerificationResponse
{
    public bool Success { get; set; }
    public float Score { get; set; }
    public string Action { get; set; }
    public DateTime ChallengeTs { get; set; }
    public string Hostname { get; set; }
    public string[]? ErrorCodes { get; set; }
}