using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Account;

[ApiController]
[Route("/accounts")]
public class AccountController(AppDatabase db)
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
        [Required] [MaxLength(1024)] public string Email { get; set; } = string.Empty;
        [Required] [MinLength(4)] [MaxLength(128)] public string Password { get; set; } = string.Empty;
    }
    
    [HttpPost]
    [ProducesResponseType<Account>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Account>> CreateAccount([FromBody] AccountCreateRequest request)
    {
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
            }
        };
        
        await db.Accounts.AddAsync(account);
        await db.SaveChangesAsync();
        return account;
    }
}