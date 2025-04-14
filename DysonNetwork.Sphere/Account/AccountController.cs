using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Account;

[ApiController]
[Route("/accounts")]
public class AccountController(AppDatabase db) : ControllerBase
{
    [HttpGet("{name}")]
    [ProducesResponseType<Account>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Account?>> GetByName(string name)
    {
        var account = await db.Accounts.Where(a => a.Name == name).FirstOrDefaultAsync();
        return account is null ? new NotFoundResult() : account;
    }

    public class AccountCreateRequest
    {
        [Required] [MaxLength(256)] public string Name { get; set; } = string.Empty;
        [Required] [MaxLength(256)] public string Nick { get; set; } = string.Empty;

        [EmailAddress]
        [Required]
        [MaxLength(1024)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(4)]
        [MaxLength(128)]
        public string Password { get; set; } = string.Empty;
    }

    [HttpPost]
    [ProducesResponseType<Account>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Account>> CreateAccount([FromBody] AccountCreateRequest request)
    {
        var dupeNameCount = await db.Accounts.Where(a => a.Name == request.Name).CountAsync();
        if (dupeNameCount > 0)
            return BadRequest("The name is already taken.");

        var account = new Account
        {
            Name = request.Name,
            Nick = request.Nick,
            Contacts = new List<AccountContact>
            {
                new()
                {
                    Type = AccountContactType.Email,
                    Content = request.Email
                }
            },
            AuthFactors = new List<AccountAuthFactor>
            {
                new AccountAuthFactor
                {
                    Type = AccountAuthFactorType.Password,
                    Secret = request.Password
                }.HashSecret()
            },
            Profile = new Profile()
        };

        await db.Accounts.AddAsync(account);
        await db.SaveChangesAsync();
        return account;
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<Account>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Account>> GetMe()
    {
        var userIdClaim = User.FindFirst("user_id")?.Value;
        long? userId = long.TryParse(userIdClaim, out var id) ? id : null;
        if (userId is null) return new BadRequestObjectResult("Invalid or missing user_id claim.");

        var account = await db.Accounts
            .Include(e => e.Profile)
            .Where(e => e.Id == userId)
            .FirstOrDefaultAsync();

        return Ok(account);
    }

    public class BasicInfoRequest
    {
        [MaxLength(256)] public string? Nick { get; set; }
        [MaxLength(32)] public string? Language { get; set; }
    }

    [Authorize]
    [HttpPatch("me")]
    public async Task<ActionResult<Account>> UpdateBasicInfo([FromBody] BasicInfoRequest request)
    {
        var userIdClaim = User.FindFirst("user_id")?.Value;
        long? userId = long.TryParse(userIdClaim, out var id) ? id : null;
        if (userId is null) return new BadRequestObjectResult("Invalid or missing user_id claim.");

        var account = await db.Accounts.FindAsync(userId);
        if (account is null) return BadRequest("Unable to get your account.");

        if (request.Nick is not null) account.Nick = request.Nick;
        if (request.Language is not null) account.Language = request.Language;

        await db.SaveChangesAsync();
        return account;
    }

    public class ProfileRequest
    {
        [MaxLength(256)] public string? FirstName { get; set; }
        [MaxLength(256)] public string? MiddleName { get; set; }
        [MaxLength(256)] public string? LastName { get; set; }
        [MaxLength(4096)] public string? Bio { get; set; }

        public string? PictureId { get; set; }
        public string? BackgroundId { get; set; }
    }

    [Authorize]
    [HttpPatch("me/profile")]
    public async Task<ActionResult<Profile>> UpdateProfile([FromBody] ProfileRequest request)
    {
        var userIdClaim = User.FindFirst("user_id")?.Value;
        long? userId = long.TryParse(userIdClaim, out var id) ? id : null;
        if (userId is null) return new BadRequestObjectResult("Invalid or missing user_id claim.");

        var profile = await db.AccountProfiles
            .Where(p => p.Account.Id == userId)
            .Include(profile => profile.Background)
            .Include(profile => profile.Picture)
            .FirstOrDefaultAsync();
        if (profile is null) return BadRequest("Unable to get your account.");

        if (request.FirstName is not null) profile.FirstName = request.FirstName;
        if (request.MiddleName is not null) profile.MiddleName = request.MiddleName;
        if (request.LastName is not null) profile.LastName = request.LastName;
        if (request.Bio is not null) profile.Bio = request.Bio;

        if (request.PictureId is not null)
        {
            var picture = await db.Files.Where(f => f.Id == request.PictureId).FirstOrDefaultAsync();
            if (picture is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
            if (profile.Picture is not null)
            {
                profile.Picture.UsedCount--;
                db.Update(profile.Picture);
            }
            
            picture.UsedCount++;
            profile.Picture = picture;
        }

        if (request.BackgroundId is not null)
        {
            var background = await db.Files.Where(f => f.Id == request.BackgroundId).FirstOrDefaultAsync();
            if (background is null) return BadRequest("Invalid background id, unable to find the file on cloud.");
            if (profile.Background is not null)
            {
                profile.Background.UsedCount--;
                db.Update(profile.Background);
            }

            background.UsedCount++;
            profile.Background = background;
        }

        await db.SaveChangesAsync();
        return profile;
    }
}