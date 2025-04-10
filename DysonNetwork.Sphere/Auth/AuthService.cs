using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Casbin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Sphere.Auth;

public class SignedTokenPair
{
    public string AccessToken { get; set; } = null!;
    public string RefreshToken { get; set; } = null!;
    public Instant ExpiredAt { get; set; }
}

public class AuthService(AppDatabase db, IConfiguration config, IEnforcer enforcer)
{
    public async Task<bool> AssignRoleToUserAsync(string user, string role, string domain = "global")
    {
        var added = await enforcer.AddGroupingPolicyAsync(user, role, domain);
        if (added) await enforcer.SavePolicyAsync();
        return added;
    }

    public async Task<bool> AddPermissionToUserAsync(string user, string domain, string obj, string act)
    {
        var added = await enforcer.AddPolicyAsync(user, domain, obj, act);
        if (added) await enforcer.SavePolicyAsync();
        return added;
    }
    
    public async Task<bool> RemovePermissionFromUserAsync(string user, string domain, string obj, string act)
    {
        var removed = await enforcer.RemovePolicyAsync(user, domain, obj, act);
        if (removed) await enforcer.SavePolicyAsync();
        return removed;
    }

    public async Task<bool> CreateRoleAsync(string role, string domain, IEnumerable<(string obj, string act)> permissions)
    {
        bool anyAdded = false;
        foreach (var (obj, act) in permissions)
        {
            var added = await enforcer.AddPolicyAsync(role, domain, obj, act);
            if (added) anyAdded = true;
        }

        if (anyAdded) await enforcer.SavePolicyAsync();
        return anyAdded;
    }

    public async Task<bool> AddPermissionsToRoleAsync(string role, string domain, IEnumerable<(string obj, string act)> permissions)
    {
        bool anyAdded = false;
        foreach (var (obj, act) in permissions)
        {
            var added = await enforcer.AddPolicyAsync(role, domain, obj, act);
            if (added) anyAdded = true;
        }

        if (anyAdded) await enforcer.SavePolicyAsync();
        return anyAdded;
    }

    public async Task<bool> RemovePermissionsFromRoleAsync(string role, string domain, IEnumerable<(string obj, string act)> permissions)
    {
        bool anyRemoved = false;
        foreach (var (obj, act) in permissions)
        {
            var removed = await enforcer.RemovePolicyAsync(role, domain, obj, act);
            if (removed) anyRemoved = true;
        }

        if (anyRemoved) await enforcer.SavePolicyAsync();
        return anyRemoved;
    }
    
    public SignedTokenPair CreateToken(Session session)
    {
        var privateKeyPem = File.ReadAllText(config["Jwt:PrivateKeyPath"]!);
        var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var key = new RsaSecurityKey(rsa);

        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var claims = new List<Claim>
        {
            new("user_id", session.Account.Id.ToString()),
            new("session_id", session.Id.ToString())
        };

        var refreshTokenClaims = new JwtSecurityToken(
            issuer: "solar-network",
            audience: string.Join(',', session.Challenge.Audiences),
            claims: claims,
            expires: DateTime.Now.AddDays(30),
            signingCredentials: creds
        );
        
        session.Challenge.Scopes.ForEach(c => claims.Add(new Claim("scope", c)));
        if(session.Account.IsSuperuser) claims.Add(new Claim("is_superuser", "1"));
        var accessTokenClaims = new JwtSecurityToken(
            issuer: "solar-network",
            audience: string.Join(',', session.Challenge.Audiences),
            claims: claims,
            expires: DateTime.Now.AddMinutes(30),
            signingCredentials: creds
        );

        var handler = new JwtSecurityTokenHandler();
        var accessToken = handler.WriteToken(accessTokenClaims);
        var refreshToken = handler.WriteToken(refreshTokenClaims);

        return new SignedTokenPair
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddMinutes(30))
        };
    }
}