namespace DysonNetwork.Sphere.ActivityPub;

public interface ISignatureService
{
    Task<(bool isValid, string? actorUri)> VerifyIncomingRequestAsync(HttpContext context);
    Task SignOutgoingRequestAsync(HttpRequestMessage request, Guid publisherId);
    Task SignOutgoingRequestAsync(HttpRequestMessage request, string actorUri);
}