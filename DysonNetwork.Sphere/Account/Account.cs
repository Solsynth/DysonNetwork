using System.ComponentModel.DataAnnotations;
using System.Text;
using NodaTime;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;

namespace DysonNetwork.Sphere.Account;

public class Account : BaseModel
{
    public long Id { get; set; }
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string Nick { get; set; } = string.Empty;
    
    public ICollection<AccountContact> Contacts { get; set; } = new List<AccountContact>();
    public ICollection<AccountAuthFactor> AuthFactors { get; set; } = new List<AccountAuthFactor>();
}

public class AccountContact : BaseModel
{
    public long Id { get; set; }
    public AccountContactType Type { get; set; }
    public Instant? VerifiedAt { get; set; }
    [MaxLength(1024)] public string Content { get; set; } = string.Empty;
    
    public Account Account { get; set; } = null!;
}

public enum AccountContactType
{
    Email, PhoneNumber, Address
}

public class AccountAuthFactor : BaseModel
{
    public long Id { get; set; }
    public AccountAuthFactorType Type { get; set; }
    public string? Secret { get; set; } = null;
    
    public Account Account { get; set; } = null!;

    public AccountAuthFactor HashSecret(int cost = 12)
    {
        if(Secret == null) return this;
        
        var passwordBytes = Encoding.UTF8.GetBytes(Secret);
        var random = new SecureRandom();
        var salt = new byte[16];
        random.NextBytes(salt);
        var hashed = BCrypt.Generate(passwordBytes, salt, cost);
        Secret = Convert.ToBase64String(hashed);

        return this;
    }
}

public enum AccountAuthFactorType
{
    Password, EmailCode, InAppCode, TimedCode
}