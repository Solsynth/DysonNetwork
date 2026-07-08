using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DysonNetwork.Develop.Identity;

public class CustomAppService(
    AppDatabase db,
    DyFileService.DyFileServiceClient files
)
{
    private static bool HasBoardScope(SnCustomApp app)
    {
        return app.OauthConfig?.AllowedScopes?.Contains(PermissionKeys.AccountsProfileBoard, StringComparer.OrdinalIgnoreCase) == true;
    }

    public async Task<SnCustomApp?> CreateAppAsync(
        Guid projectId,
        CustomAppController.CustomAppRequest request
    )
    {
        var project = await db.DevProjects
            .Include(p => p.Developer)
            .FirstOrDefaultAsync(p => p.Id == projectId);
            
        if (project == null)
            return null;
            
        var app = new SnCustomApp
        {
            Slug = request.Slug!,
            Name = request.Name!,
            Description = request.Description,
            Status = request.Status ?? Shared.Models.CustomAppStatus.Developing,
            Links = request.Links,
            OauthConfig = request.OauthConfig,
            BoardWidgets = request.BoardWidgets,
            ProjectId = projectId
        };

        if (request.PictureId is not null)
        {
            var picture = await files.GetFileAsync(new DyGetFileRequest { Id = request.PictureId });
            if (picture is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");
            app.Picture = SnCloudFileReferenceObject.FromProtoValue(picture);

        }
        if (request.BackgroundId is not null)
        {
            var background = await files.GetFileAsync(
                new DyGetFileRequest { Id = request.BackgroundId }
            );
            if (background is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");
            app.Background = SnCloudFileReferenceObject.FromProtoValue(background);

        }

        db.CustomApps.Add(app);
        await db.SaveChangesAsync();

        return app;
    }

    public async Task<SnCustomApp?> GetAppAsync(Guid id, Guid? projectId = null)
    {
        var query = db.CustomApps.AsQueryable();
        
        if (projectId.HasValue)
        {
            query = query.Where(a => a.ProjectId == projectId.Value);
        }

        return await query.FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<SnCustomApp?> GetAppBySlugAsync(string slug)
    {
        return await db.CustomApps
            .Include(a => a.Project)
            .ThenInclude(p => p.Developer)
            .FirstOrDefaultAsync(a => a.Slug.ToLower() == slug.ToLowerInvariant());
    }

    public async Task<List<SnCustomAppSecret>> GetAppSecretsAsync(Guid appId)
    {
        return await db.CustomAppSecrets
            .Where(s => s.AppId == appId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<SnCustomAppSecret?> GetAppSecretAsync(Guid secretId, Guid appId)
    {
        return await db.CustomAppSecrets
            .FirstOrDefaultAsync(s => s.Id == secretId && s.AppId == appId);
    }

    public async Task<bool> ValidateApiSecretAsync(Guid appId, string secret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return false;

        var now = NodaTime.SystemClock.Instance.GetCurrentInstant();
        return await db.CustomAppSecrets.AnyAsync(
            s => s.AppId == appId
                 && !s.IsOidc
                 && s.Secret == secret
                 && (s.ExpiredAt == null || s.ExpiredAt > now),
            cancellationToken
        );
    }

    public async Task<SnCustomAppSecret> CreateAppSecretAsync(SnCustomAppSecret secret)
    {
        if (string.IsNullOrWhiteSpace(secret.Secret))
        {
            // Generate a new random secret if not provided
            secret.Secret = GenerateRandomSecret();
        }

        secret.Id = Guid.NewGuid();
        secret.CreatedAt = NodaTime.SystemClock.Instance.GetCurrentInstant();
        secret.UpdatedAt = secret.CreatedAt;

        db.CustomAppSecrets.Add(secret);
        await db.SaveChangesAsync();

        return secret;
    }

    public async Task<bool> DeleteAppSecretAsync(Guid secretId, Guid appId)
    {
        var secret = await db.CustomAppSecrets
            .FirstOrDefaultAsync(s => s.Id == secretId && s.AppId == appId);

        if (secret == null)
            return false;

        db.CustomAppSecrets.Remove(secret);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<SnCustomAppSecret> RotateAppSecretAsync(SnCustomAppSecret secretUpdate)
    {
        var existingSecret = await db.CustomAppSecrets
            .FirstOrDefaultAsync(s => s.Id == secretUpdate.Id && s.AppId == secretUpdate.AppId);

        if (existingSecret == null)
            throw new InvalidOperationException("Secret not found");

        // Update the existing secret with new values
        existingSecret.Secret = GenerateRandomSecret();
        existingSecret.Description = secretUpdate.Description ?? existingSecret.Description;
        existingSecret.ExpiredAt = secretUpdate.ExpiredAt ?? existingSecret.ExpiredAt;
        existingSecret.Type = secretUpdate.Type;
        existingSecret.UpdatedAt = NodaTime.SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();
        return existingSecret;
    }

    private static string GenerateRandomSecret(int length = 64)
    {
        const string valid = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890-._~+";
        var res = new StringBuilder();
        using (var rng = RandomNumberGenerator.Create())
        {
            var uintBuffer = new byte[sizeof(uint)];
            while (length-- > 0)
            {
                rng.GetBytes(uintBuffer);
                var num = BitConverter.ToUInt32(uintBuffer, 0);
                res.Append(valid[(int)(num % (uint)valid.Length)]);
            }
        }
        return res.ToString();
    }

    public async Task<List<SnCustomApp>> GetAppsByProjectAsync(Guid projectId)
    {
        return await db.CustomApps
            .Where(a => a.ProjectId == projectId)
            .ToListAsync();
    }

    public async Task<SnCustomApp?> UpdateAppAsync(SnCustomApp app, CustomAppController.CustomAppRequest request)
    {
        var oldStatus = app.Status;
        if (request.Slug is not null)
            app.Slug = request.Slug;
        if (request.Name is not null)
            app.Name = request.Name;
        if (request.Description is not null)
            app.Description = request.Description;
        if (request.Status is not null)
            app.Status = request.Status.Value;
        if (request.Links is not null)
            app.Links = request.Links;
        if (request.OauthConfig is not null)
            app.OauthConfig = request.OauthConfig;
        if (request.BoardWidgets is not null)
            app.BoardWidgets = request.BoardWidgets;

        if (request.PictureId is not null)
        {
            var picture = await files.GetFileAsync(new DyGetFileRequest { Id = request.PictureId });
            if (picture is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");
            app.Picture = SnCloudFileReferenceObject.FromProtoValue(picture);

        }
        if (request.BackgroundId is not null)
        {
            var background = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
            if (background is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");
            app.Background = SnCloudFileReferenceObject.FromProtoValue(background);

        }

        db.Update(app);
        await db.SaveChangesAsync();

        if (oldStatus == CustomAppStatus.Production && app.Status != CustomAppStatus.Production)
        {
            if (app.Picture is not null)
                await files.UnsetFilePublicAsync(new DyUnsetFilePublicRequest { FileId = app.Picture.Id });
            if (app.Background is not null)
                await files.UnsetFilePublicAsync(new DyUnsetFilePublicRequest { FileId = app.Background.Id });
        }

        return app;
    }

    public async Task<bool> DeleteAppAsync(Guid id)
    {
        var app = await db.CustomApps.FindAsync(id);
        if (app == null)
        {
            return false;
        }

        db.CustomApps.Remove(app);
        await db.SaveChangesAsync();

        return true;
    }

    public async Task<(SnCustomApp App, SnBoardWidgetManifest Widget)> GetBoardWidgetAsync(Guid appId, string widgetKey)
    {
        var app = await db.CustomApps.FirstOrDefaultAsync(a => a.Id == appId);
        if (app is null)
            throw new InvalidOperationException("App not found");
        if (!HasBoardScope(app))
            throw new InvalidOperationException($"Custom app must declare '{PermissionKeys.AccountsProfileBoard}' scope to provide board widgets.");
        if (app.BoardWidgets is null || app.BoardWidgets.Count == 0)
            throw new InvalidOperationException("Board widget is not configured for this app");
        var widget = app.BoardWidgets.FirstOrDefault(x => string.Equals(x.Key, widgetKey, StringComparison.OrdinalIgnoreCase));
        if (widget is null)
            throw new InvalidOperationException($"Board widget '{widgetKey}' is not configured for this app");

        return (app, widget);
    }

    public (bool Valid, string? Message, Dictionary<string, object?> NormalizedPayload, SnBoardWidgetManifest Widget)
        ValidateBoardWidgetPayload(SnCustomApp app, string widgetKey, Dictionary<string, object?>? payload)
    {
        var widget = app.BoardWidgets?.FirstOrDefault(x => string.Equals(x.Key, widgetKey, StringComparison.OrdinalIgnoreCase));
        if (widget is null)
            return (false, "Board widget is not configured for this app.", [], new SnBoardWidgetManifest());
        if (!HasBoardScope(app))
            return (false, $"Custom app must declare '{PermissionKeys.AccountsProfileBoard}' scope to provide board widgets.", [], widget);
        if (!widget.IsEnabled)
            return (false, "Board widget is disabled for this app.", [], widget);
        if (app.Status != CustomAppStatus.Production)
            return (false, "Only production custom apps can be used as board widgets.", [], widget);

        var normalizedPayload = payload ?? [];
        var payloadJson = JsonSerializer.Serialize(normalizedPayload);
        if (widget.MaxPayloadBytes.HasValue && Encoding.UTF8.GetByteCount(payloadJson) > widget.MaxPayloadBytes.Value)
            return (false, $"Board widget payload exceeds {widget.MaxPayloadBytes.Value} bytes.", normalizedPayload, widget);

        foreach (var requiredField in widget.RequiredFields)
        {
            if (!normalizedPayload.ContainsKey(requiredField))
                return (false, $"Board widget payload is missing required field '{requiredField}'.", normalizedPayload, widget);
        }

        foreach (var fieldType in widget.FieldTypes)
        {
            if (!normalizedPayload.TryGetValue(fieldType.Key, out var value))
                continue;

            if (!IsValueMatchingType(value, fieldType.Value))
                return (false, $"Board widget payload field '{fieldType.Key}' must be of type '{fieldType.Value}'.", normalizedPayload, widget);
        }

        return (true, null, normalizedPayload, widget);
    }

    private static bool IsValueMatchingType(object? value, string expectedType)
    {
        if (value is null)
            return true;

        if (value is JsonElement element)
        {
            return expectedType.ToLowerInvariant() switch
            {
                "string" => element.ValueKind == JsonValueKind.String,
                "number" => element.ValueKind == JsonValueKind.Number,
                "boolean" => element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False,
                "object" => element.ValueKind == JsonValueKind.Object,
                "array" => element.ValueKind == JsonValueKind.Array,
                "null" => element.ValueKind == JsonValueKind.Null,
                _ => true
            };
        }

        return expectedType.ToLowerInvariant() switch
        {
            "string" => value is string,
            "number" => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal,
            "boolean" => value is bool,
            "object" => value is Dictionary<string, object?>,
            "array" => value is IEnumerable<object>,
            _ => true
        };
    }
}
