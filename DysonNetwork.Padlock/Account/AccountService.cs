using DysonNetwork.Padlock.Auth.OpenId;
using DysonNetwork.Padlock.Mailer;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Padlock.Account;

public class AccountService(
    AppDatabase db,
    ICacheService cache,
    IEventBus eventBus,
    DyFileService.DyFileServiceClient files,
    DyRingService.DyRingServiceClient ring,
    DyNfcService.DyNfcServiceClient nfcService,
    EmailService mailer,
    ILocalizationService localizer,
    ILogger<AccountService> logger,
    IHttpContextAccessor httpContextAccessor,
    ActionLogService actionLogs,
    DyMagicSpellService.DyMagicSpellServiceClient magicSpells
)
{
    private const string AuthFactorCachePrefix = "authfactor:";

    public async Task<SnAccount?> LookupAccount(string probe)
    {
        var account = await db.Accounts.Where(a => EF.Functions.ILike(a.Name, probe)).FirstOrDefaultAsync();
        if (account is not null) return account;

        var contact = await db.AccountContacts
            .Where(c => c.Type == AccountContactType.Email || c.Type == AccountContactType.PhoneNumber)
            .Where(c => EF.Functions.ILike(c.Content, probe))
            .Include(c => c.Account)
            .FirstOrDefaultAsync();
        return contact?.Account;
    }

    public async Task<bool> CheckAccountNameHasTaken(string name)
    {
        return await db.Accounts.AnyAsync(a => EF.Functions.ILike(a.Name, name));
    }

    public async Task<bool> CheckEmailHasBeenUsed(string email)
    {
        return await db.AccountContacts.AnyAsync(c =>
            c.Type == AccountContactType.Email && EF.Functions.ILike(c.Content, email));
    }

    public async Task<SnAccount> CreateAccount(
        string name,
        string nick,
        string email,
        string? password,
        string language = "en-US",
        string region = "en",
        bool isEmailVerified = false,
        bool isActivated = false
    )
    {
        if (await CheckAccountNameHasTaken(name))
            throw new InvalidOperationException("Account name has already been taken.");
        if (await CheckEmailHasBeenUsed(email))
            throw new InvalidOperationException("Account email has already been used.");

        var account = new SnAccount
        {
            Name = name,
            Nick = nick,
            Language = language,
            Region = region,
            Contacts =
            [
                new SnAccountContact
                {
                    Type = AccountContactType.Email,
                    Content = email,
                    VerifiedAt = isEmailVerified ? SystemClock.Instance.GetCurrentInstant() : null,
                    IsPrimary = true
                }
            ],
            AuthFactors = password is not null
                ?
                [
                    new SnAccountAuthFactor
                    {
                        Type = AccountAuthFactorType.Password,
                        Secret = password,
                        EnabledAt = SystemClock.Instance.GetCurrentInstant()
                    }.HashSecret()
                ]
                : [],
            ActivatedAt = isActivated ? SystemClock.Instance.GetCurrentInstant() : null
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        await PublishAccountCreated(account);
        return account;
    }

    public async Task<SnAccount> CreateAccount(OidcUserInfo userInfo)
    {
        if (string.IsNullOrWhiteSpace(userInfo.Email))
            throw new ArgumentException("Email is required for account creation");

        var displayName = !string.IsNullOrWhiteSpace(userInfo.DisplayName)
            ? userInfo.DisplayName
            : $"{userInfo.FirstName} {userInfo.LastName}".Trim();
        var baseName = userInfo.Email.Split('@')[0].ToLowerInvariant();
        var name = await GenerateAvailableUsername(baseName);

        return await CreateAccount(
            name,
            string.IsNullOrWhiteSpace(displayName) ? name : displayName,
            userInfo.Email,
            null,
            isEmailVerified: userInfo.EmailVerified
        );
    }

    public async Task<bool> CheckAuthFactorExists(SnAccount account, AccountAuthFactorType type)
    {
        return await db.AccountAuthFactors
            .Where(x => x.AccountId == account.Id && x.Type == type)
            .AnyAsync();
    }

    public async Task<bool> CheckAuthFactorEnabled(SnAccount account, AccountAuthFactorType type)
    {
        return await db.AccountAuthFactors
            .Where(x => x.AccountId == account.Id && x.Type == type)
            .Where(x => x.EnabledAt != null)
            .AnyAsync();
    }

    public async Task<SnAccountAuthFactor?> CreateAuthFactor(SnAccount account, AccountAuthFactorType type, string? secret)
    {
        if (type == AccountAuthFactorType.RecoveryCode)
        {
            var recoveryCode = Guid.NewGuid().ToString("N");
            var recoveryFactor = new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.RecoveryCode,
                Trustworthy = 0,
                AccountId = account.Id,
                Secret = recoveryCode,
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
                CreatedResponse = new Dictionary<string, object>
                {
                    ["recovery_code"] = recoveryCode
                }
            };
            db.AccountAuthFactors.Add(recoveryFactor);
            await db.SaveChangesAsync();
            await CreateAccountActionLogAsync(
                account.Id,
                ActionLogType.AuthFactorCreate,
                new Dictionary<string, object> { ["factor_type"] = recoveryFactor.Type.ToString() }
            );
            return recoveryFactor;
        }

        SnAccountAuthFactor? factor = type switch
        {
            AccountAuthFactorType.Password when !string.IsNullOrWhiteSpace(secret) => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.Password,
                Trustworthy = 1,
                AccountId = account.Id,
                Secret = secret,
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
            }.HashSecret(),
            AccountAuthFactorType.EmailCode => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.EmailCode,
                Trustworthy = 2,
                AccountId = account.Id,
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
            },
            AccountAuthFactorType.InAppCode => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.InAppCode,
                Trustworthy = 2,
                AccountId = account.Id,
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
            },
            AccountAuthFactorType.TimedCode => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.TimedCode,
                Trustworthy = 3,
                AccountId = account.Id,
                Secret = secret ?? Guid.NewGuid().ToString("N"),
            },
            AccountAuthFactorType.PinCode when !string.IsNullOrWhiteSpace(secret) => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.PinCode,
                Trustworthy = 0, // PIN Code is designed for confirm operations, not for logged etc
                AccountId = account.Id,
                Secret = secret,
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
            }.HashSecret(),
            AccountAuthFactorType.NfcToken => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.NfcToken,
                Trustworthy = 1,
                AccountId = account.Id,
                Secret = null, // Verification is delegated to Passport via gRPC
                Config = !string.IsNullOrEmpty(secret) ? new Dictionary<string, object>
                {
                    ["tag_id"] = secret
                } : null,
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
            },
            AccountAuthFactorType.Passkey when !string.IsNullOrWhiteSpace(secret) => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.Passkey,
                Trustworthy = 4,
                AccountId = account.Id,
                Secret = secret,
                Config = new Dictionary<string, object>
                {
                    ["verified"] = false
                },
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
            },
            _ => null
        };

        if (factor == null) return null;
        db.AccountAuthFactors.Add(factor);
        await db.SaveChangesAsync();
        await CreateAccountActionLogAsync(
            account.Id,
            ActionLogType.AuthFactorCreate,
            new Dictionary<string, object> { ["factor_type"] = factor.Type.ToString() }
        );
        return factor;
    }

    public async Task<SnAccountAuthFactor> EnableAuthFactor(SnAccountAuthFactor factor, string? code)
    {
        if (factor.Type == AccountAuthFactorType.RecoveryCode)
        {
            var newRecoveryCode = Guid.NewGuid().ToString("N");
            factor.Secret = newRecoveryCode;
            factor.EnabledAt = SystemClock.Instance.GetCurrentInstant();
            factor.CreatedResponse = new Dictionary<string, object>
            {
                ["recovery_code"] = newRecoveryCode
            };
            db.Update(factor);
            await db.SaveChangesAsync();
            await CreateAccountActionLogAsync(
                factor.AccountId,
                ActionLogType.AuthFactorEnable,
                new Dictionary<string, object>
                {
                    ["factor_type"] = factor.Type.ToString(),
                    ["regenerated"] = true
                }
            );
            return factor;
        }

        if (factor.Type is AccountAuthFactorType.Password or AccountAuthFactorType.TimedCode)
        {
            factor.EnabledAt = SystemClock.Instance.GetCurrentInstant();
            db.Update(factor);
            await db.SaveChangesAsync();
            await CreateAccountActionLogAsync(
                factor.AccountId,
                ActionLogType.AuthFactorEnable,
                new Dictionary<string, object> { ["factor_type"] = factor.Type.ToString() }
            );
            return factor;
        }

        if (string.IsNullOrWhiteSpace(code) || !await VerifyFactorCode(factor, code))
            throw new InvalidOperationException("Invalid factor code.");

        factor.EnabledAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(factor);
        await db.SaveChangesAsync();
        await CreateAccountActionLogAsync(
            factor.AccountId,
            ActionLogType.AuthFactorEnable,
            new Dictionary<string, object> { ["factor_type"] = factor.Type.ToString() }
        );
        return factor;
    }

    public async Task<SnAccountAuthFactor> DisableAuthFactor(SnAccountAuthFactor factor)
    {
        factor.EnabledAt = null;
        db.Update(factor);
        await db.SaveChangesAsync();
        await CreateAccountActionLogAsync(
            factor.AccountId,
            ActionLogType.AuthFactorDisable,
            new Dictionary<string, object> { ["factor_type"] = factor.Type.ToString() }
        );
        return factor;
    }

    public async Task DeleteAuthFactor(SnAccountAuthFactor factor)
    {
        var meta = new Dictionary<string, object> { ["factor_type"] = factor.Type.ToString() };
        db.AccountAuthFactors.Remove(factor);
        await db.SaveChangesAsync();
        await CreateAccountActionLogAsync(factor.AccountId, ActionLogType.AuthFactorDelete, meta);
    }

    public async Task<SnAccountAuthFactor> ResetPasswordFactor(Guid accountId, string newPassword)
    {
        var factor = await db.AccountAuthFactors
            .FirstOrDefaultAsync(f => f.AccountId == accountId && f.Type == AccountAuthFactorType.Password);

        if (factor is null)
        {
            factor = new SnAccountAuthFactor
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Type = AccountAuthFactorType.Password,
                Trustworthy = 1,
                Secret = newPassword,
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
                ExpiredAt = null
            }.HashSecret();

            db.AccountAuthFactors.Add(factor);
        }
        else
        {
            factor.Secret = newPassword;
            factor.HashSecret();
            factor.EnabledAt ??= SystemClock.Instance.GetCurrentInstant();
            factor.ExpiredAt = null;
            db.AccountAuthFactors.Update(factor);
        }

        await db.SaveChangesAsync();
        await CreateAccountActionLogAsync(
            accountId,
            ActionLogType.AuthFactorResetPassword,
            new Dictionary<string, object> { ["factor_type"] = AccountAuthFactorType.Password.ToString() }
        );
        return factor;
    }


    private async Task _SetFactorCode(SnAccountAuthFactor factor, string code, TimeSpan expires)
    {
        await cache.SetAsync(
            $"{AuthFactorCachePrefix}{factor.Id}:code",
            code,
            expires
        );
    }

    private async Task<string?> _GetFactorCode(SnAccountAuthFactor factor)
    {
        return await cache.GetAsync<string?>(
            $"{AuthFactorCachePrefix}{factor.Id}:code"
        );
    }

    public async Task SendFactorCode(SnAccount account, SnAccountAuthFactor factor)
    {
        var code = Random.Shared.Next(100000, 999999).ToString();

        switch (factor.Type)
        {
            case Shared.Models.AccountAuthFactorType.InAppCode:
                if (await _GetFactorCode(factor) is not null)
                    throw new InvalidOperationException("A factor code has been sent and in active duration.");

                await ring.SendPushNotificationToUserAsync(
                    new DySendPushNotificationToUserRequest
                    {
                        UserId = account.Id.ToString(),
                        Notification = new DyPushNotification
                        {
                            Topic = "auth.verification",
                            Title = localizer.Get("authCodeTitle", account.Language),
                            Body = localizer.Get("authCodeBody", locale: account.Language, args: new { code }),
                            IsSavable = false
                        }
                    }
                );
                await _SetFactorCode(factor, code, TimeSpan.FromMinutes(5));
                break;
            case AccountAuthFactorType.EmailCode:
                if (await _GetFactorCode(factor) is not null)
                    throw new InvalidOperationException("A factor code has been sent and in active duration.");

                var contact = await db.AccountContacts
                    .Where(c => c.Type == Shared.Models.AccountContactType.Email)
                    .Where(c => c.VerifiedAt != null)
                    .Where(c => c.IsPrimary)
                    .Where(c => c.AccountId == account.Id)
                    .Include(c => c.Account)
                    .FirstOrDefaultAsync();
                if (contact is null)
                {
                    logger.LogWarning(
                        "Unable to send factor code to #{FactorId} with, due to no contact method was found...",
                        factor.Id
                    );
                    return;
                }

                await mailer
                    .SendTemplatedEmailAsync(
                        account.Nick,
                        contact.Content,
                        localizer.Get("codeEmailTitle", account.Language),
                        "FactorCode",
                        new { name = account.Name, code },
                        account.Language
                    );

                await _SetFactorCode(factor, code, TimeSpan.FromMinutes(30));
                break;
            case Shared.Models.AccountAuthFactorType.Password:
            case Shared.Models.AccountAuthFactorType.TimedCode:
            default:
                // No need to send, such as password etc...
                return;
        }
    }

    public async Task<bool> VerifyFactorCode(SnAccountAuthFactor factor, string code)
    {
        return factor.Type switch
        {
            AccountAuthFactorType.EmailCode or AccountAuthFactorType.InAppCode => await VerifyCachedFactorCode(factor, code),
            AccountAuthFactorType.Password or AccountAuthFactorType.PinCode => BCrypt.Net.BCrypt.Verify(code, factor.Secret),
            AccountAuthFactorType.TimedCode => factor.VerifyPassword(code),
            AccountAuthFactorType.NfcToken => await VerifyNfcToken(code),
            AccountAuthFactorType.Passkey => await VerifyPasskey(factor, code),
            _ => false
        };
    }

    /// <summary>
    /// Verify an NFC SUN token by calling Passport's gRPC service.
    /// The code parameter is expected to be the full hex-encoded UID data from the NFC scan URL.
    /// E.g., for "solian://phpass?uid=D7E4AF3C6F49A801D351FB82974B7729000000",
    /// the code would be "<tag hardware uid>:D7E4AF3C6F49A801D351FB82974B7729000000".
    /// </summary>
    private async Task<bool> VerifyNfcToken(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length < 32)
            return false;

        var parts = code.Split(":");
        if (parts.Length != 2)
            return false;

        var uid = parts[0];
        var hex = parts[1];

        try
        {
            var response = await nfcService.ValidateNfcTokenAsync(new DyValidateNfcTokenRequest
            {
                TagUid = uid,
                UidHex = hex,
            });

            return response.IsValid;
        }
        catch (RpcException)
        {
            return false;
        }
    }

    private Task<bool> VerifyPasskey(SnAccountAuthFactor factor, string assertionJson)
    {
        if (string.IsNullOrWhiteSpace(factor.Secret))
            return Task.FromResult(false);

        try
        {
            var credential = System.Text.Json.JsonSerializer.Deserialize<PasskeyCredential>(factor.Secret);
            if (credential == null)
                return Task.FromResult(false);

            var assertion = System.Text.Json.JsonSerializer.Deserialize<PasskeyAssertion>(assertionJson);
            if (assertion == null)
                return Task.FromResult(false);

            if (credential.CredentialId != assertion.CredentialId)
                return Task.FromResult(false);

            if ((assertion.AuthenticatorData.Flags & AuthenticatorFlags.UserPresent) == 0)
                return Task.FromResult(false);

            var signatureData = assertion.AuthenticatorData.GetSignedData(assertion.ClientDataJson);
            using var ECDsa = System.Security.Cryptography.ECDsa.Create(new System.Security.Cryptography.ECParameters
            {
                Curve = System.Security.Cryptography.ECCurve.NamedCurves.nistP256,
                Q = new System.Security.Cryptography.ECPoint
                {
                    X = credential.PublicKeyX,
                    Y = credential.PublicKeyY
                }
            });

            return Task.FromResult(ECDsa.VerifyData(signatureData, assertion.Signature, System.Security.Cryptography.HashAlgorithmName.SHA256));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private const string PasskeyChallengePrefix = "passkey:challenge:";

    public async Task<string> GeneratePasskeyChallengeAsync(SnAccount account, string deviceId)
    {
        var challengeBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(challengeBytes);
        var challenge = Convert.ToBase64String(challengeBytes);
        var key = $"{PasskeyChallengePrefix}{account.Id}:{deviceId}";
        await cache.SetAsync(key, challenge, TimeSpan.FromMinutes(5));
        return challenge;
    }

    public async Task<PasskeyCredential?> CompletePasskeyRegistrationAsync(
        SnAccount account,
        string deviceId,
        string clientDataJson,
        string authenticatorData,
        string attestationObject)
    {
        var key = $"{PasskeyChallengePrefix}{account.Id}:{deviceId}";
        var storedChallenge = await cache.GetAsync<string>(key);
        if (string.IsNullOrEmpty(storedChallenge))
            return null;

        var attestation = ParseAttestationObject(attestationObject);
        if (attestation == null)
            return null;

        if (!VerifyAttestationStatement(attestation, clientDataJson, storedChallenge))
            return null;

        await cache.RemoveAsync(key);

        var credential = new PasskeyCredential
        {
            CredentialId = attestation.CredentialId,
            PublicKeyX = attestation.PublicKeyX,
            PublicKeyY = attestation.PublicKeyY,
            Counter = attestation.Counter
        };

        return credential;
    }

    private static AttestationStatement? ParseAttestationObject(string attestationObjectBase64)
    {
        try
        {
            var attestationBytes = Convert.FromBase64String(attestationObjectBase64);
            using var ms = new MemoryStream(attestationBytes);
            using var reader = new BinaryReader(ms);

            var format = ReadString(reader);
            if (format != "fido-u2f")
                return null;

            var statement = new AttestationStatement();

            var attStmtMap = ReadMap(reader);
            if (attStmtMap.TryGetValue("sig", out var sigValue) && sigValue is byte[] signature)
                statement.Signature = signature;

            if (attStmtMap.TryGetValue("x5c", out var x5cValue) && x5cValue is List<object> x5cList && x5cList.Count > 0 && x5cList[0] is byte[] certBytes)
                statement.AttestationCertificate = certBytes;

            var authData = ReadBytes(reader);
            return ParseAuthenticatorData(authData, statement);
        }
        catch
        {
            return null;
        }
    }

    private static AttestationStatement? ParseAuthenticatorData(byte[] authData, AttestationStatement statement)
    {
        if (authData.Length < 37)
            return null;

        using var ms = new MemoryStream(authData);
        using var reader = new BinaryReader(ms);

        statement.RpIdHash = reader.ReadBytes(32);
        var flags = reader.ReadByte();
        statement.UserPresent = (flags & 0x01) != 0;
        statement.UserVerified = (flags & 0x04) != 0;
        statement.Counter = reader.ReadUInt32();

        if ((flags & 0x40) != 0)
        {
            var credIdLen = reader.ReadUInt16();
            statement.CredentialId = Convert.ToBase64String(reader.ReadBytes(credIdLen));

            var publicKeyBytes = reader.ReadBytes((int)(authData.Length - ms.Position));
            var (x, y) = ExtractPublicKeyFromCbor(publicKeyBytes);
            if (x != null && y != null)
            {
                statement.PublicKeyX = x;
                statement.PublicKeyY = y;
            }
        }

        return statement;
    }

    private static (byte[]?, byte[]?) ExtractPublicKeyFromCbor(byte[] keyBytes)
    {
        try
        {
            using var ms = new MemoryStream(keyBytes);
            using var reader = new BinaryReader(ms);

            ReadCbor(reader);
            ReadCbor(reader);
            var keyType = ReadCbor(reader);
            if (keyType is not 2)
                return (null, null);

            ReadCbor(reader);
            var x = ReadCbor(reader) as byte[];
            ReadCbor(reader);
            var y = ReadCbor(reader) as byte[];

            return (x, y);
        }
        catch
        {
            return (null, null);
        }
    }

    private static object? ReadCbor(BinaryReader reader)
    {
        var initialByte = reader.ReadByte();

        if (initialByte >= 0x60 && initialByte <= 0x77)
        {
            var len = initialByte - 0x60 + 1;
            return reader.ReadBytes(len);
        }
        if (initialByte >= 0x80 && initialByte <= 0x97)
        {
            var count = initialByte - 0x80 + 1;
            var items = new List<object>();
            for (var i = 0; i < count; i++)
                items.Add(ReadCbor(reader)!);
            return items;
        }
        if (initialByte == 0xA0)
        {
            var map = new Dictionary<object, object>();
            var count = ReadCbor(reader) is int c ? c : 0;
            for (var i = 0; i < count; i++)
            {
                var k = ReadCbor(reader);
                var v = ReadCbor(reader);
                if (k != null) map[k] = v!;
            }
            return map;
        }
        if (initialByte == 0x20 || initialByte == 0x21 || initialByte == 0x22 || initialByte == 0x23)
            return reader.ReadBytes(initialByte - 0x20 + 1);
        if (initialByte == 0x58)
            return reader.ReadBytes(reader.ReadByte());
        if (initialByte == 0x01 || initialByte == 0x02 || initialByte == 0x03)
            return reader.ReadBytes(initialByte - 0x20);

        return null;
    }

    private static bool VerifyAttestationStatement(AttestationStatement statement, string clientDataJson, string expectedChallenge)
    {
        if (!statement.UserPresent)
            return false;

        var clientData = System.Text.Json.JsonDocument.Parse(clientDataJson);
        var root = clientData.RootElement;

        if (root.TryGetProperty("type", out var typeElement) && typeElement.GetString() != "webauthn.create")
            return false;

        if (root.TryGetProperty("challenge", out var challengeElement) && challengeElement.GetString() != expectedChallenge)
            return false;

        if (statement.AttestationCertificate == null || statement.Signature == null)
            return false;

        var clientDataHash = System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(clientDataJson));
        var signedData = new List<byte>();
        signedData.AddRange(statement.RpIdHash);
        signedData.Add(0x00);
        signedData.AddRange(BitConverter.GetBytes(statement.Counter).Reverse());
        signedData.AddRange(clientDataHash);

        using var ECDsa = System.Security.Cryptography.ECDsa.Create();
        try
        {
            ECDsa.ImportSubjectPublicKeyInfo(statement.AttestationCertificate, out _);
        }
        catch
        {
            return false;
        }

        return ECDsa.VerifyData(signedData.ToArray(), statement.Signature, System.Security.Cryptography.HashAlgorithmName.SHA256);
    }

    private static string ReadString(BinaryReader reader)
    {
        var len = reader.ReadByte();
        return System.Text.Encoding.UTF8.GetString(reader.ReadBytes(len));
    }

    private static byte[] ReadBytes(BinaryReader reader)
    {
        var len = reader.ReadByte();
        return reader.ReadBytes(len);
    }

    private static Dictionary<object, object> ReadMap(BinaryReader reader)
    {
        var map = new Dictionary<object, object>();
        if (reader.ReadByte() != 0xBF)
            return map;

        while (reader.PeekChar() != 0xFF)
        {
            var key = ReadCbor(reader);
            var value = ReadCbor(reader);
            if (key != null)
                map[key] = value!;
        }
        reader.ReadByte();
        return map;
    }

    private class AttestationStatement
    {
        public byte[] RpIdHash { get; set; } = null!;
        public bool UserPresent { get; set; }
        public bool UserVerified { get; set; }
        public uint Counter { get; set; }
        public string CredentialId { get; set; } = null!;
        public byte[] PublicKeyX { get; set; } = null!;
        public byte[] PublicKeyY { get; set; } = null!;
        public byte[]? AttestationCertificate { get; set; }
        public byte[]? Signature { get; set; }
    }

    public async Task DeleteSession(SnAccount account, Guid sessionId)
    {
        var session = await db.AuthSessions.FirstOrDefaultAsync(s => s.AccountId == account.Id && s.Id == sessionId);
        if (session == null) throw new InvalidOperationException("Session not found.");
        session.ExpiredAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(session);
        await db.SaveChangesAsync();
        await cache.SetAsync(
            AuthCacheKeys.RevokedJti(sessionId.ToString()),
            true,
            TimeSpan.FromDays(AuthCacheKeys.RevokedJtiTtlDays)
        );
        await CreateAccountActionLogAsync(
            account.Id,
            ActionLogType.SessionRevoke,
            new Dictionary<string, object> { ["session_id"] = sessionId }
        );
    }

    public async Task DeleteDevice(SnAccount account, string deviceId)
    {
        var client = await db.AuthClients.FirstOrDefaultAsync(c => c.AccountId == account.Id && c.DeviceId == deviceId);
        if (client == null) throw new InvalidOperationException("Device not found.");
        await db.AuthSessions
            .Where(s => s.ClientId == client.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiredAt, SystemClock.Instance.GetCurrentInstant()));
        client.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(client);
        await db.SaveChangesAsync();
        await CreateAccountActionLogAsync(
            account.Id,
            ActionLogType.DeviceRevoke,
            new Dictionary<string, object> { ["device_id"] = deviceId }
        );
    }

    public async Task UpdateDeviceName(SnAccount account, string deviceId, string label)
    {
        var client = await db.AuthClients.FirstOrDefaultAsync(c => c.AccountId == account.Id && c.DeviceId == deviceId);
        if (client == null) throw new InvalidOperationException("Device not found.");
        client.DeviceName = label;
        db.Update(client);
        await db.SaveChangesAsync();
        await CreateAccountActionLogAsync(
            account.Id,
            ActionLogType.DeviceRename,
            new Dictionary<string, object>
            {
                ["device_id"] = deviceId,
                ["label"] = label
            }
        );
    }

    public async Task<SnAccountContact> CreateContactMethod(SnAccount account, AccountContactType type, string content)
    {
        var contact = new SnAccountContact
        {
            AccountId = account.Id,
            Type = type,
            Content = content,
            IsPrimary = false
        };
        db.AccountContacts.Add(contact);
        await db.SaveChangesAsync();
        return contact;
    }

    public async Task RequestContactVerification(SnAccount account, SnAccountContact contact)
    {
        if (contact.AccountId != account.Id)
            throw new InvalidOperationException("Contact does not belong to the account.");
        if (contact.VerifiedAt is not null)
            throw new InvalidOperationException("Contact has already been verified.");

        var request = new DyCreateMagicSpellRequest
        {
            AccountId = account.Id.ToString(),
            Type = DyMagicSpellType.DyMagicSpellContactVerification,
            ExpiresAt = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromHours(24)).ToTimestamp(),
            PreventRepeat = true
        };
        request.Meta.Add("contact_id", InfraObjectCoder.ConvertObjectToValue(contact.Id.ToString()));
        request.Meta.Add("contact_type", InfraObjectCoder.ConvertObjectToValue(contact.Type.ToString()));
        request.Meta.Add("contact_method", InfraObjectCoder.ConvertObjectToValue(contact.Content));

        var spell = await magicSpells.CreateMagicSpellAsync(request);
        await magicSpells.NotifyMagicSpellAsync(new DyNotifyMagicSpellRequest
        {
            SpellId = spell.Id,
            BypassVerify = true
        });
    }

    public async Task<bool> MarkContactMethodVerified(Guid accountId, Guid contactId, Instant verifiedAt)
    {
        var contact = await db.AccountContacts
            .FirstOrDefaultAsync(c => c.AccountId == accountId && c.Id == contactId);
        if (contact is null) return false;

        if (contact.VerifiedAt is null || contact.VerifiedAt < verifiedAt)
            contact.VerifiedAt = verifiedAt;

        db.AccountContacts.Update(contact);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<SnAccountContact> SetContactMethodPrimary(SnAccount account, SnAccountContact contact)
    {
        await db.AccountContacts
            .Where(c => c.AccountId == account.Id && c.Type == contact.Type)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsPrimary, false));
        contact.IsPrimary = true;
        db.Update(contact);
        await db.SaveChangesAsync();
        return contact;
    }

    public async Task<SnAccountContact> SetContactMethodPublic(SnAccount account, SnAccountContact contact, bool isPublic)
    {
        contact.IsPublic = isPublic;
        db.Update(contact);
        await db.SaveChangesAsync();
        return contact;
    }

    public async Task DeleteContactMethod(SnAccount account, SnAccountContact contact)
    {
        db.AccountContacts.Remove(contact);
        await db.SaveChangesAsync();
    }

    public async Task<bool> ActivateAccountAndGrantDefaultPermissions(Guid accountId, Instant activatedAt)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null) return false;

        if (account.ActivatedAt is null || account.ActivatedAt < activatedAt)
        {
            account.ActivatedAt = activatedAt;
            db.Accounts.Update(account);
        }

        var actor = accountId.ToString();
        var defaultGroup = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Key == "default");
        if (defaultGroup is not null)
        {
            var memberExists = await db.PermissionGroupMembers
                .AnyAsync(m => m.GroupId == defaultGroup.Id && m.Actor == actor);
            if (!memberExists)
            {
                db.PermissionGroupMembers.Add(new SnPermissionGroupMember
                {
                    GroupId = defaultGroup.Id,
                    Actor = actor
                });
            }
        }

        await db.SaveChangesAsync();
        return true;
    }

    private async Task PublishAccountCreated(SnAccount account)
    {
        var primaryEmail = account.Contacts
            .Where(c => c.Type == AccountContactType.Email)
            .OrderByDescending(c => c.IsPrimary)
            .FirstOrDefault();

        await eventBus.PublishAsync(AccountCreatedEvent.Type, new AccountCreatedEvent
        {
            AccountId = account.Id,
            Name = account.Name,
            Nick = account.Nick,
            Language = account.Language,
            Region = account.Region,
            PrimaryEmail = primaryEmail?.Content,
            PrimaryEmailVerifiedAt = primaryEmail?.VerifiedAt,
            ActivatedAt = account.ActivatedAt,
            IsSuperuser = account.IsSuperuser,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });
    }

    private async Task<bool> VerifyCachedFactorCode(SnAccountAuthFactor factor, string code)
    {
        var cached = await cache.GetAsync<string>($"{AuthFactorCachePrefix}{factor.Id}:code");
        if (!string.Equals(cached, code, StringComparison.Ordinal))
            return false;
        await cache.RemoveAsync($"{AuthFactorCachePrefix}{factor.Id}:code");
        return true;
    }

    private async Task<string> GenerateAvailableUsername(string baseName)
    {
        var normalized = new string(baseName.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray());
        if (string.IsNullOrWhiteSpace(normalized)) normalized = "user";

        var candidate = normalized;
        var suffix = 1;
        while (await CheckAccountNameHasTaken(candidate))
        {
            candidate = $"{normalized}{suffix++}";
        }

        return candidate;
    }
    public async Task<SnAccount> CreateBotAccount(
       SnAccount account,
       Guid automatedId,
       string? pictureId,
       string? backgroundId
   )
    {
        var dupeAutomateCount = await db.Set<SnAccount>().Where(a => a.AutomatedId == automatedId).CountAsync();
        if (dupeAutomateCount > 0)
            throw new InvalidOperationException("Automated ID has already been used.");

        var dupeNameCount = await db.Set<SnAccount>().Where(a => a.Name == account.Name).CountAsync();
        if (dupeNameCount > 0)
            throw new InvalidOperationException("Account name has already been taken.");

        account.AutomatedId = automatedId;
        account.ActivatedAt = SystemClock.Instance.GetCurrentInstant();
        account.IsSuperuser = false;

        if (!string.IsNullOrEmpty(pictureId))
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = pictureId });
            account.Profile.Picture = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        if (!string.IsNullOrEmpty(backgroundId))
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = backgroundId });
            account.Profile.Background = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        var defaultGroup = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Key == "default");
        if (defaultGroup is not null)
        {
            db.PermissionGroupMembers.Add(new SnPermissionGroupMember
            {
                Actor = account.Id.ToString(),
                Group = defaultGroup
            });
        }

        db.Set<SnAccount>().Add(account);
        await db.SaveChangesAsync();

        return account;
    }

    public async Task<SnAccount?> GetBotAccount(Guid automatedId)
    {
        return await db.Accounts.FirstOrDefaultAsync(a => a.AutomatedId == automatedId);
    }

    public async Task DeleteAccount(SnAccount account)
    {
        logger.LogWarning("Deleting account {AccountId}", account.Id);
        var now = SystemClock.Instance.GetCurrentInstant();
        await db.Set<SnAuthSession>()
            .Where(s => s.AccountId == account.Id)
            .ExecuteUpdateAsync(p => p.SetProperty(s => s.DeletedAt, now));

        db.Set<SnAccount>().Remove(account);
        await db.SaveChangesAsync();

        await eventBus.PublishAsync(AccountDeletedEvent.Type, new AccountDeletedEvent
        {
            AccountId = account.Id,
            DeletedAt = SystemClock.Instance.GetCurrentInstant()
        });
    }

    public async Task<bool> DeleteAccountById(Guid accountId)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null) return false;

        await DeleteAccount(account);
        return true;
    }

    public async Task<SnAccount> UpdateBasicInfo(SnAccount account, string? nick, string? language, string? region)
    {
        var dbAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == account.Id);
        if (dbAccount is null)
            throw new InvalidOperationException("Account not found.");

        if (nick is not null) dbAccount.Nick = nick;
        if (language is not null) dbAccount.Language = language;
        if (region is not null) dbAccount.Region = region;

        db.Update(dbAccount);
        await db.SaveChangesAsync();
        var changedFields = new List<string>();
        if (nick is not null) changedFields.Add("nick");
        if (language is not null) changedFields.Add("language");
        if (region is not null) changedFields.Add("region");
        if (changedFields.Count > 0)
        {
            await CreateAccountActionLogAsync(
                account.Id,
                ActionLogType.AccountProfileUpdate,
                new Dictionary<string, object> { ["fields"] = changedFields }
            );
        }

        return dbAccount;
    }

    private Task CreateAccountActionLogAsync(Guid accountId, string action, Dictionary<string, object> meta)
    {
        var request = httpContextAccessor.HttpContext?.Request;
        Guid? sessionId = null;
        if (httpContextAccessor.HttpContext?.Items["CurrentSession"] is SnAuthSession currentSession)
            sessionId = currentSession.Id;

        return actionLogs.CreateActionLogAsync(
            accountId,
            action,
            meta,
            request?.Headers.UserAgent.ToString(),
            request?.HttpContext.GetClientIpAddress(),
            null,
            sessionId
        );
    }

    public class PasskeyCredential
    {
        public string CredentialId { get; set; } = null!;
        public byte[] PublicKeyX { get; set; } = null!;
        public byte[] PublicKeyY { get; set; } = null!;
        public byte[]? CredentialPublicKey { get; set; }
        public ulong Counter { get; set; }
    }

    public class PasskeyAssertion
    {
        public string CredentialId { get; set; } = null!;
        public string ClientDataJson { get; set; } = null!;
        public AuthenticatorData AuthenticatorData { get; set; } = null!;
        public byte[] Signature { get; set; } = null!;
    }

    public class AuthenticatorData
    {
        public byte[] RpIdHash { get; set; } = null!;
        public AuthenticatorFlags Flags { get; set; }
        public ulong Counter { get; set; }
        public byte[]? AttestedCredentialData { get; set; }

        public byte[] GetSignedData(string clientDataJson)
        {
            var clientDataHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(clientDataJson));
            var signedData = new byte[37 + clientDataHash.Length];
            Buffer.BlockCopy(RpIdHash, 0, signedData, 0, 32);
            signedData[32] = (byte)Flags;
            var counterBytes = BitConverter.GetBytes(Counter);
            if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);
            Buffer.BlockCopy(counterBytes, 0, signedData, 33, 4);
            Buffer.BlockCopy(clientDataHash, 0, signedData, 37, clientDataHash.Length);
            return signedData;
        }
    }

    [Flags]
    public enum AuthenticatorFlags : byte
    {
        RawValue = 0,
        UserPresent = 1 << 0,
        UserVerified = 1 << 1,
        BackupEligibility = 1 << 2,
        BackupState = 1 << 3,
        DeviceBound = 1 << 6
    }
}
