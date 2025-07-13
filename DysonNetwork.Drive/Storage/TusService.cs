using System.Net;
using System.Text;
using System.Text.Json;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;

namespace DysonNetwork.Drive.Storage;

public abstract class TusService
{
    public static DefaultTusConfiguration BuildConfiguration(ITusStore store) => new()
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
                if (httpContext.Items["CurrentUser"] is not Account user)
                {
                    eventContext.FailRequest(HttpStatusCode.Unauthorized);
                    return;
                }

                if (!user.IsSuperuser)
                {
                    using var scope = httpContext.RequestServices.CreateScope();
                    var pm = scope.ServiceProvider.GetRequiredService<PermissionService.PermissionServiceClient>();
                    var allowed = await pm.HasPermissionAsync(new HasPermissionRequest
                        { Actor = $"user:{user.Id}", Area = "global", Key = "files.create" });
                    if (!allowed.HasPermission)
                        eventContext.FailRequest(HttpStatusCode.Forbidden);
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

                var fileService = services.GetRequiredService<FileService>();
                var info = await fileService.ProcessNewFileAsync(user, file.Id, fileStream, fileName, contentType);

                using var finalScope = eventContext.HttpContext.RequestServices.CreateScope();
                var jsonOptions = finalScope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value
                    .JsonSerializerOptions;
                var infoJson = JsonSerializer.Serialize(info, jsonOptions);
                eventContext.HttpContext.Response.Headers.Append("X-FileInfo", infoJson);

                // Dispose the stream after all processing is complete
                await fileStream.DisposeAsync();
            }
        }
    };
}