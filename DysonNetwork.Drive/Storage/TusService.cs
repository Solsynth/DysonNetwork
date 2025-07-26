using System.Net;
using System.Text;
using System.Text.Json;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Stores;

namespace DysonNetwork.Drive.Storage;

public abstract class TusService
{
    public static DefaultTusConfiguration BuildConfiguration(ITusStore store, IConfiguration configuration) => new()
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
                            $"You need to have join the Stellar Program tier {pool.PolicyConfig.RequirePrivilege} to use this pool"
                        );
                        return;
                    }

                    var privilege =
                        PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(currentUser.PerkSubscription.Identifier);
                    if (privilege < pool.PolicyConfig.RequirePrivilege)
                    {
                        eventContext.FailRequest(
                            HttpStatusCode.Forbidden,
                            $"You need to have join the Stellar Program tier {pool.PolicyConfig.RequirePrivilege} to use this pool"
                        );
                        return;
                    }
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

                var fileStream = await file.GetContentAsync(eventContext.CancellationToken);

                var filePool = httpContext.Request.Headers["X-FilePool"].FirstOrDefault();
                var encryptPassword = httpContext.Request.Headers["X-FilePass"].FirstOrDefault();

                if (string.IsNullOrEmpty(filePool))
                    filePool = configuration["Storage:PreferredRemote"];

                try
                {
                    var fileService = services.GetRequiredService<FileService>();
                    var info = await fileService.ProcessNewFileAsync(
                        user,
                        file.Id,
                        filePool!,
                        fileStream,
                        fileName,
                        contentType,
                        encryptPassword
                    );

                    using var finalScope = eventContext.HttpContext.RequestServices.CreateScope();
                    var jsonOptions = finalScope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value
                        .JsonSerializerOptions;
                    var infoJson = JsonSerializer.Serialize(info, jsonOptions);
                    eventContext.HttpContext.Response.Headers.Append("X-FileInfo", infoJson);
                }
                catch (Exception ex)
                {
                    eventContext.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await eventContext.HttpContext.Response.WriteAsync(ex.Message);
                    if (eventContext.Store is TusDiskStore disk)
                        await disk.DeleteFileAsync(file.Id, eventContext.CancellationToken);
                }
                finally
                {
                    // Dispose the stream after all processing is complete
                    await fileStream.DisposeAsync();
                }
            },
            OnBeforeCreateAsync = async eventContext =>
            {
                var filePool = eventContext.HttpContext.Request.Headers["X-FilePool"].FirstOrDefault();
                if (string.IsNullOrEmpty(filePool)) filePool = configuration["Storage:PreferredRemote"];
                if (!Guid.TryParse(filePool, out _))
                {
                    eventContext.FailRequest(HttpStatusCode.BadRequest, "Invalid file pool id");
                    return;
                }

                var metadata = eventContext.Metadata;
                var contentType = metadata.TryGetValue("content-type", out var ct) ? ct.GetString(Encoding.UTF8) : null;
                
                var scope = eventContext.HttpContext.RequestServices.CreateScope();
                
                var rejected = false;

                var fs = scope.ServiceProvider.GetRequiredService<FileService>();
                var pool = await fs.GetPoolAsync(Guid.Parse(filePool!));
                if (pool is null)
                {
                    eventContext.FailRequest(HttpStatusCode.BadRequest, "Pool not found");
                    rejected = true;
                }

                var logger = scope.ServiceProvider.GetRequiredService<ILogger<TusService>>();

                // Do the policy check
                var policy = pool!.PolicyConfig;
                if (!rejected && policy.AcceptTypes is not null)
                {
                    if (contentType is null)
                    {
                        eventContext.FailRequest(
                            HttpStatusCode.BadRequest,
                            "Content type is required by the pool's policy"
                        );
                        rejected = true;
                    }
                    else if (!policy.AcceptTypes.Contains(contentType))
                    {
                        eventContext.FailRequest(
                            HttpStatusCode.Forbidden,
                            $"Content type {contentType} is not allowed by the pool's policy"
                        );
                        rejected = true;
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

                if (rejected)
                    logger.LogInformation("File rejected #{FileId}", eventContext.FileId);
            },
            OnCreateCompleteAsync = eventContext =>
            {
                var gatewayUrl = configuration["GatewayUrl"];
                if (gatewayUrl is not null)
                    eventContext.SetUploadUrl(new Uri(gatewayUrl + "/drive/tus/" + eventContext.FileId));
                return Task.CompletedTask;
            },
        }
    };
}