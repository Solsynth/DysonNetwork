using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Realm;
using DysonNetwork.Sphere.Chat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace DysonNetwork.Sphere.Chat;

[ApiController]
[Route("/chat")]
public class ChatController(AppDatabase db) : ControllerBase
{
}