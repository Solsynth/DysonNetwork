using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Drive.Index;
using DysonNetwork.Drive.Storage;
using DysonNetwork.Drive.Storage.Model;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace DysonNetwork.Drive.Startup;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddAppServices(IConfiguration configuration)
        {
            services.AddDbContext<AppDatabase>();
            services.AddHttpContextAccessor();

            services.AddHttpClient();

            // Register gRPC services
            services.AddGrpc(options =>
            {
                options.EnableDetailedErrors = true; // Will be adjusted in Program.cs
                options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16MB
                options.MaxSendMessageSize = 16 * 1024 * 1024; // 16MB
            });
            services.AddGrpcReflection();

            services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

                options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
            });

            return services;
        }

        public IServiceCollection AddAppAuthentication()
        {
            services.AddAuthorization();
            return services;
        }

        public IServiceCollection AddAppFlushHandlers()
        {
            services.AddSingleton<FlushBufferService>();

            return services;
        }

        public IServiceCollection AddAppBusinessServices(IConfiguration configuration)
        {
            services.Configure<Storage.Options.FileReanalysisOptions>(configuration.GetSection("FileReanalysis"));

            services.AddScoped<Storage.FileService>();
            services.AddScoped<Storage.FileReanalysisService>();
            services.AddScoped<Storage.PersistentTaskService>();
            services.AddScoped<FileIndexService>();
            services.AddScoped<Billing.UsageService>();
            services.AddScoped<Billing.QuotaService>();

            services.AddHostedService<Storage.FileReanalysisBackgroundService>();

            services.AddEventBus()
                .AddListener<AccountDeletedEvent>(
                    AccountDeletedEvent.Type,
                    async (evt, ctx) =>
                    {
                        var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();
                        var fs = ctx.ServiceProvider.GetRequiredService<Storage.FileService>();
                        var db = ctx.ServiceProvider.GetRequiredService<AppDatabase>();

                        logger.LogWarning("Account deleted: {AccountId}", evt.AccountId);

                        await using var transaction = await db.Database.BeginTransactionAsync(ctx.CancellationToken);
                        try
                        {
                            // var files = await db.Files
                            //     .Where(p => p.AccountId == evt.AccountId)
                            //     .ToListAsync(ctx.CancellationToken);
                            // await fs.DeleteFileDataBatchAsync(files);
                            var now = new Instant();
                            await db.Files
                                .Where(p => p.AccountId == evt.AccountId)
                                .ExecuteUpdateAsync(p => p.SetProperty(s => s.DeletedAt, s => now),
                                    ctx.CancellationToken);

                            await transaction.CommitAsync(ctx.CancellationToken);
                        }
                        catch
                        {
                            await transaction.RollbackAsync(ctx.CancellationToken);
                            throw;
                        }
                    },
                    opts =>
                    {
                        opts.UseJetStream = true;
                        opts.StreamName = "account_events";
                        opts.ConsumerName = "drive_account_deleted_handler";
                        opts.MaxRetries = 3;
                    }
                )
                .AddListener<FileUploadedEvent>(
                    FileUploadedEvent.Type,
                    async (evt, ctx) =>
                    {
                        var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();

                        logger.LogInformation("Processing file {FileId} in background...", evt.FileId);

                        await ProcessUploadedFileAsync(evt, ctx.ServiceProvider, logger, ctx.CancellationToken);
                    },
                    opts =>
                    {
                        opts.UseJetStream = true;
                        opts.StreamName = "file_events";
                        opts.ConsumerName = "drive_file_uploaded_handler";
                        opts.MaxRetries = 3;
                    }
                );

            return services;
        }

        private static async Task ProcessUploadedFileAsync(FileUploadedEvent evt, IServiceProvider serviceProvider,
            ILogger logger, CancellationToken cancellationToken)
        {
            const string TempFileSuffix = "dypart";
            var AnimatedImageTypes = new[] { "image/gif", "image/apng", "image/avif" };
            var AnimatedImageExtensions = new[] { ".gif", ".apng", ".avif" };

            var fs = serviceProvider.GetRequiredService<DysonNetwork.Drive.Storage.FileService>();
            var scopedDb = serviceProvider.GetRequiredService<AppDatabase>();
            var persistentTaskService = serviceProvider.GetRequiredService<PersistentTaskService>();
            var ringService = serviceProvider.GetRequiredService<RingService.RingServiceClient>();

            var pool = await fs.GetPoolAsync(evt.RemoteId);
            if (pool is null) return;

            var uploads = new List<(string FilePath, string Suffix, string ContentType, bool SelfDestruct)>();
            var newMimeType = evt.ContentType ?? "application/octet-stream";
            var hasCompression = false;
            var hasThumbnail = false;

            var fileToUpdate = await scopedDb.Files
                .AsNoTracking()
                .Include(f => f.Object)
                .FirstAsync(f => f.Id == evt.FileId, cancellationToken);

            // Find the upload task associated with this file
            var baseTask = await scopedDb.Tasks
                .Where(t => t.Type == TaskType.FileUpload)
                .FirstOrDefaultAsync(cancellationToken);

            var uploadTask = baseTask != null
                ? new PersistentUploadTask
                {
                    Id = baseTask.Id,
                    TaskId = baseTask.TaskId,
                    Name = baseTask.Name,
                    Description = baseTask.Description,
                    Type = baseTask.Type,
                    Status = baseTask.Status,
                    AccountId = baseTask.AccountId,
                    Progress = baseTask.Progress,
                    Parameters = baseTask.Parameters,
                    Results = baseTask.Results,
                    ErrorMessage = baseTask.ErrorMessage,
                    StartedAt = baseTask.StartedAt,
                    CompletedAt = baseTask.CompletedAt,
                    ExpiredAt = baseTask.ExpiredAt,
                    LastActivity = baseTask.LastActivity,
                    Priority = baseTask.Priority,
                    EstimatedDurationSeconds = baseTask.EstimatedDurationSeconds,
                    CreatedAt = baseTask.CreatedAt,
                    UpdatedAt = baseTask.UpdatedAt
                }
                : null;

            if (uploadTask != null &&
                (uploadTask.FileName != fileToUpdate.Name || uploadTask.FileSize != fileToUpdate.Size))
            {
                uploadTask = null;
            }

            if (!pool.PolicyConfig.NoOptimization)
            {
                var fileExtension = Path.GetExtension(evt.ProcessingFilePath);
                switch ((evt.ContentType ?? "").Split('/')[0])
                {
                    case "image":
                        if (AnimatedImageTypes.Contains(evt.ContentType) ||
                            AnimatedImageExtensions.Contains(fileExtension))
                        {
                            logger.LogInformation("Skip optimize file {FileId} due to it is animated...", evt.FileId);
                            uploads.Add((evt.ProcessingFilePath, string.Empty, evt.ContentType ?? "image/unknown",
                                false));
                            break;
                        }

                        try
                        {
                            newMimeType = "image/webp";
                            using var vipsImage = NetVips.Image.NewFromFile(evt.ProcessingFilePath);
                            var imageToWrite = vipsImage;

                            if (vipsImage.Interpretation is NetVips.Enums.Interpretation.Scrgb
                                or NetVips.Enums.Interpretation.Xyz)
                            {
                                imageToWrite = vipsImage.Colourspace(NetVips.Enums.Interpretation.Srgb);
                            }

                            var webpPath = Path.Join(Path.GetTempPath(), $"{evt.FileId}.{TempFileSuffix}.webp");
                            imageToWrite.Autorot().WriteToFile(webpPath,
                                new NetVips.VOption { { "lossless", true }, { "strip", true } });
                            uploads.Add((webpPath, string.Empty, newMimeType, true));

                            if (imageToWrite.Width * imageToWrite.Height >= 1024 * 1024)
                            {
                                var scale = 1024.0 / Math.Max(imageToWrite.Width, imageToWrite.Height);
                                var compressedPath =
                                    Path.Join(Path.GetTempPath(), $"{evt.FileId}.{TempFileSuffix}.compressed.webp");
                                using var compressedImage = imageToWrite.Resize(scale);
                                compressedImage.Autorot().WriteToFile(compressedPath,
                                    new NetVips.VOption { { "Q", 80 }, { "strip", true } });
                                uploads.Add((compressedPath, ".compressed", newMimeType, true));
                                hasCompression = true;
                            }

                            if (!ReferenceEquals(imageToWrite, vipsImage))
                            {
                                imageToWrite.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to optimize image {FileId}, uploading original", evt.FileId);
                            uploads.Add((evt.ProcessingFilePath, string.Empty, evt.ContentType ?? "image/unknown",
                                false));
                            newMimeType = evt.ContentType ?? "image/unknown";
                        }

                        break;

                    case "video":
                        uploads.Add((evt.ProcessingFilePath, string.Empty, evt.ContentType ?? "video/unknown", false));

                        var thumbnailPath = Path.Join(Path.GetTempPath(),
                            $"{evt.FileId}.{TempFileSuffix}.thumbnail.jpg");
                        try
                        {
                            await FFMpegCore.FFMpegArguments
                                .FromFileInput(evt.ProcessingFilePath, verifyExists: true)
                                .OutputToFile(thumbnailPath, overwrite: true, options => options
                                    .Seek(TimeSpan.FromSeconds(0))
                                    .WithFrameOutputCount(1)
                                    .WithCustomArgument("-q:v 2")
                                )
                                .NotifyOnOutput(line => logger.LogInformation("[FFmpeg] {Line}", line))
                                .NotifyOnError(line => logger.LogWarning("[FFmpeg] {Line}", line))
                                .ProcessAsynchronously();

                            if (File.Exists(thumbnailPath))
                            {
                                uploads.Add((thumbnailPath, ".thumbnail", "image/jpeg", true));
                                hasThumbnail = true;
                            }
                            else
                            {
                                logger.LogWarning("FFMpeg did not produce thumbnail for video {FileId}", evt.FileId);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to generate thumbnail for video {FileId}", evt.FileId);
                        }

                        break;

                    default:
                        uploads.Add((evt.ProcessingFilePath, string.Empty,
                            evt.ContentType ?? "application/octet-stream", false));
                        break;
                }
            }
            else
            {
                uploads.Add(
                    (evt.ProcessingFilePath, string.Empty, evt.ContentType ?? "application/octet-stream", false));
            }

            logger.LogInformation("Optimized file {FileId}, now uploading...", evt.FileId);

            if (uploads.Count > 0)
            {
                var destPool = evt.RemoteId;
                var uploadTasks = uploads.Select(item =>
                    (Task)fs.UploadFileToRemoteAsync(
                        evt.StorageId!,
                        destPool,
                        item.FilePath,
                        item.Suffix,
                        item.ContentType,
                        item.SelfDestruct
                    )
                ).ToList();

                await Task.WhenAll(uploadTasks);

                logger.LogInformation("Uploaded file {FileId} done!", evt.FileId);

                var now = SystemClock.Instance.GetCurrentInstant();

                var newReplica = new SnFileReplica
                {
                    Id = Guid.NewGuid(),
                    ObjectId = evt.FileId,
                    PoolId = destPool,
                    StorageId = evt.StorageId,
                    Status = SnFileReplicaStatus.Available,
                    IsPrimary = false
                };
                scopedDb.FileReplicas.Add(newReplica);

                await scopedDb.Files.Where(f => f.Id == evt.FileId).ExecuteUpdateAsync(setter => setter
                        .SetProperty(f => f.UploadedAt, now)
                    , cancellationToken);

                await scopedDb.FileObjects.Where(fo => fo.Id == evt.FileId).ExecuteUpdateAsync(setter => setter
                        .SetProperty(fo => fo.MimeType, newMimeType)
                        .SetProperty(fo => fo.HasCompression, hasCompression)
                        .SetProperty(fo => fo.HasThumbnail, hasThumbnail)
                    , cancellationToken);

                // Only delete temp file after successful upload and db update
                if (evt.IsTempFile)
                    File.Delete(evt.ProcessingFilePath);
            }

            await fs._PurgeCacheAsync(evt.FileId);

            // Complete the upload task if found
            if (uploadTask != null)
            {
                await persistentTaskService.MarkTaskCompletedAsync(uploadTask.TaskId, new Dictionary<string, object?>
                {
                    { "FileId", evt.FileId },
                    { "FileName", fileToUpdate.Name },
                    { "FileInfo", fileToUpdate },
                    { "FileSize", fileToUpdate.Size },
                    { "MimeType", newMimeType },
                    { "HasCompression", hasCompression },
                    { "HasThumbnail", hasThumbnail }
                });

                // Send push notification for large files (>5MB) that took longer to process
                if (fileToUpdate.Size > 5 * 1024 * 1024) // 5MB threshold
                {
                    try
                    {
                        var pushNotification = new PushNotification
                        {
                            Topic = "drive.tasks.upload",
                            Title = "File Processing Complete",
                            Subtitle = fileToUpdate.Name,
                            Body = $"Your file '{fileToUpdate.Name}' has finished processing and is now available.",
                            IsSavable = true
                        };

                        await ringService.SendPushNotificationToUserAsync(new SendPushNotificationToUserRequest
                        {
                            UserId = uploadTask.AccountId.ToString(),
                            Notification = pushNotification
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to send large file processing notification for task {TaskId}",
                            uploadTask.TaskId);
                    }
                }
            }
        }
    }
}