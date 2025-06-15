using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Auth.OpenId;

/// <summary>
/// This controller is designed to handle the OAuth callback.
/// </summary>
[ApiController]
[Route("/auth/callback")]
public class AuthCallbackController : ControllerBase
{
}