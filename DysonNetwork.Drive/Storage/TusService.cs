using System.Net;
using System.Text;
using System.Text.Json;
using DysonNetwork.Drive.Billing;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NodaTime;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;

namespace DysonNetwork.Drive.Storage;

public abstract class TusService
{
    public static DefaultTusConfiguration BuildConfiguration(
        ITusStore store,
        IConfiguration configuration
    ) => new()
    {
        Store = store,
        Events = new Events
        {
            OnAuthorizeAsync = async eventContext =>
            {
                if (eventContext.Intent == IntentType.DeleteFile)
                {
                    eventContext.FailRequest(
                        HttpStatusCode.BadRequest,
                        "Deleting files from this endpoint was disabled, please refer to the Dyson Network File API."
                    );
                    return;
                }

                var httpContext = eventContext.HttpContext;
                if (httpContext.Items["CurrentUser"] is not Account currentUser)
                {
                    eventContext.FailRequest(HttpStatusCode.Unauthorized);
                    return;
                }

                if (eventContext.Intent != IntentType.CreateFile) return;

                using var scope = httpContext.RequestServices.CreateScope();

                if (!currentUser.IsSuperuser)
                {
                    var pm = scope.ServiceProvider.GetRequiredService<PermissionService.PermissionServiceClient>();
                    var allowed = await pm.HasPermissionAsync(new HasPermissionRequest
                        { Actor = $"user:{currentUser.Id}", Area = "global", Key = "files.create" });
                    if (!allowed.HasPermission)
                        eventContext.FailRequest(HttpStatusCode.Forbidden);
                }

                var filePool = httpContext.Request.Headers["X-FilePool"].FirstOrDefault();
                if (string.IsNullOrEmpty(filePool)) filePool = configuration["Storage:PreferredRemote"];
                if (!Guid.TryParse(filePool, out _))
                {
                    eventContext.FailRequest(HttpStatusCode.BadRequest, "Invalid file pool id");
                    return;
                }

                var fs = scope.ServiceProvider.GetRequiredService<FileService>();
                var pool = await fs.GetPoolAsync(Guid.Parse(filePool!));
                if (pool is null)
                {
                    eventContext.FailRequest(HttpStatusCode.BadRequest, "Pool not found");
                    return;
                }

                if (pool.PolicyConfig.RequirePrivilege > 0)
                {
                    if (currentUser.PerkSubscription is null)
                    {
                        eventContext.FailRequest(
                            HttpStatusCode.Forbidden,
                            $"You need to have join the Stellar Program to use this pool"
                        );
                        return;
                    }

                    var privilege =
                        PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(currentUser.PerkSubscription.Identifier);
                    if (privilege < pool.PolicyConfig.RequirePrivilege)
                    {
                        eventContext.FailRequest(
                            HttpStatusCode.Forbidden,
                            $"You need Stellar Program tier {pool.PolicyConfig.RequirePrivilege} to use this pool, you are tier {privilege}"
                        );
                    }
                }
                
                var bundleId = eventContext.HttpContext.Request.Headers["X-FileBundle"].FirstOrDefault();
                if (!string.IsNullOrEmpty(bundleId) && !Guid.TryParse(bundleId, out _))
                {
                    eventContext.FailRequest(HttpStatusCode.BadRequest, "Invalid file bundle id");
                }
            },
            OnFileCompleteAsync = async eventContext =>
            {
                using var scope = eventContext.HttpContext.RequestServices.CreateScope();
                var services = scope.ServiceProvider;

                var httpContext = eventContext.HttpContext;
                if (httpContext.Items["CurrentUser"] is not Account user) return;

                var file = await eventContext.GetFileAsync();
                var metadata = await file.GetMetadataAsync(eventContext.CancellationToken);
                var fileName = metadata.TryGetValue("filename", out var fn)
                    ? fn.GetString(Encoding.UTF8)
                    : "uploaded_file";
                var contentType = metadata.TryGetValue("content-type", out var ct) ? ct.GetString(Encoding.UTF8) : null;

                var filePath = Path.Combine(configuration.GetValue<string>("Tus:StorePath")!, file.Id);

                var filePool = httpContext.Request.Headers["X-FilePool"].FirstOrDefault();
                var bundleId = eventContext.HttpContext.Request.Headers["X-FileBundle"].FirstOrDefault();
                var encryptPassword = httpContext.Request.Headers["X-FilePass"].FirstOrDefault();

                if (string.IsNullOrEmpty(filePool))
                    filePool = configuration["Storage:PreferredRemote"];

                Instant? expiredAt = null;
                var expiredString = httpContext.Request.Headers["X-FileExpire"].FirstOrDefault();
                if (!string.IsNullOrEmpty(expiredString) && int.TryParse(expiredString, out var expired))
                    expiredAt = Instant.FromUnixTimeSeconds(expired);
                
                try
                {
                    var fileService = services.GetRequiredService<FileService>();
                    var info = await fileService.ProcessNewFileAsync(
                        user,
                        file.Id,
                        filePool!,
                        bundleId,
                        filePath,
                        fileName,
                        contentType,
                        encryptPassword,
                        expiredAt
                    );

                    using var finalScope = eventContext.HttpContext.RequestServices.CreateScope();
                    var jsonOptions = finalScope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value
                        .JsonSerializerOptions;
                    var infoJson = JsonSerializer.Serialize(info, jsonOptions);
                    eventContext.HttpContext.Response.Headers.Append("X-FileInfo", infoJson);
                }
                catch (Exception ex)
                {
                    var logger = services.GetRequiredService<ILogger<TusService>>();
                    eventContext.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await eventContext.HttpContext.Response.WriteAsync(ex.Message);
                    logger.LogError(ex, "Error handling file upload...");
                }
            },
            OnBeforeCreateAsync = async eventContext =>
            {
                var httpContext = eventContext.HttpContext;
                if (httpContext.Items["CurrentUser"] is not Account currentUser)
                {
                    eventContext.FailRequest(HttpStatusCode.Unauthorized);
                    return;
                }
                var accountId = Guid.Parse(currentUser.Id);

                var poolId = eventContext.HttpContext.Request.Headers["X-FilePool"].FirstOrDefault();
                if (string.IsNullOrEmpty(poolId)) poolId = configuration["Storage:PreferredRemote"];
                if (!Guid.TryParse(poolId, out _))
                {
                    eventContext.FailRequest(HttpStatusCode.BadRequest, "Invalid file pool id");
                    return;
                }

                var bundleId = eventContext.HttpContext.Request.Headers["X-FileBundle"].FirstOrDefault();
                if (!string.IsNullOrEmpty(bundleId) && !Guid.TryParse(bundleId, out _))
                {
                    eventContext.FailRequest(HttpStatusCode.BadRequest, "Invalid file bundle id");
                    return;
                }

                var metadata = eventContext.Metadata;
                var contentType = metadata.TryGetValue("content-type", out var ct) ? ct.GetString(Encoding.UTF8) : null;

                var scope = eventContext.HttpContext.RequestServices.CreateScope();

                var rejected = false;

                var fs = scope.ServiceProvider.GetRequiredService<FileService>();
                var pool = await fs.GetPoolAsync(Guid.Parse(poolId!));
                if (pool is null)
                {
                    eventContext.FailRequest(HttpStatusCode.BadRequest, "Pool not found");
                    rejected = true;
                }

                var logger = scope.ServiceProvider.GetRequiredService<ILogger<TusService>>();

                // Do the policy check
                var policy = pool!.PolicyConfig;
                if (!rejected && !pool.PolicyConfig.AllowEncryption)
                {
                    var encryptPassword = eventContext.HttpContext.Request.Headers["X-FilePass"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(encryptPassword))
                    {
                        eventContext.FailRequest(
                            HttpStatusCode.Forbidden,
                            "File encryption is not allowed in this pool"
                        );
                        rejected = true;
                    }
                }

                if (!rejected && policy.AcceptTypes is not null)
                {
                    if (string.IsNullOrEmpty(contentType))
                    {
                        eventContext.FailRequest(
                            HttpStatusCode.BadRequest,
                            "Content type is required by the pool's policy"
                        );
                        rejected = true;
                    }
                    else
                    {
                        var foundMatch = false;
                        foreach (var acceptType in policy.AcceptTypes)
                        {
                            if (acceptType.EndsWith("/*", StringComparison.OrdinalIgnoreCase))
                            {
                                var type = acceptType[..^2];
                                if (!contentType.StartsWith($"{type}/", StringComparison.OrdinalIgnoreCase)) continue;
                                foundMatch = true;
                                break;
                            }
                            else if (acceptType.Equals(contentType, StringComparison.OrdinalIgnoreCase))
                            {
                                foundMatch = true;
                                break;
                            }
                        }

                        if (!foundMatch)
                        {
                            eventContext.FailRequest(
                                HttpStatusCode.Forbidden,
                                $"Content type {contentType} is not allowed by the pool's policy"
                            );
                            rejected = true;
                        }
                    }
                }

                if (!rejected && policy.MaxFileSize is not null)
                {
                    if (eventContext.UploadLength > policy.MaxFileSize)
                    {
                        eventContext.FailRequest(
                            HttpStatusCode.Forbidden,
                            $"File size {eventContext.UploadLength} is larger than the pool's maximum file size {policy.MaxFileSize}"
                        );
                        rejected = true;
                    }
                }

                if (!rejected)
                {
                    var quotaService = scope.ServiceProvider.GetRequiredService<QuotaService>();
                    var (ok, billableUnit, quota) = await quotaService.IsFileAcceptable(
                        accountId,
                        pool.BillingConfig.CostMultiplier ?? 1.0,
                        eventContext.UploadLength
                    );
                    if (!ok)
                    {
                        eventContext.FailRequest(
                            HttpStatusCode.Forbidden,
                            $"File size {billableUnit} MiB is exceeded the user's quota {quota} MiB"
                        );
                        rejected = true;
                    }
                }

                if (rejected)
                    logger.LogInformation("File rejected #{FileId}", eventContext.FileId);
            },
            OnCreateCompleteAsync = eventContext =>
            {
                var directUpload = eventContext.HttpContext.Request.Headers["X-DirectUpload"].FirstOrDefault();
                if (!string.IsNullOrEmpty(directUpload)) return Task.CompletedTask;

                var gatewayUrl = configuration["GatewayUrl"];
                if (gatewayUrl is not null)
                    eventContext.SetUploadUrl(new Uri(gatewayUrl + "/drive/tus/" + eventContext.FileId));
                return Task.CompletedTask;
            },
        }
    };
}