using System.Text.Json;
using DysonNetwork.Drive.Storage.Model;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Stream;
using FFMpegCore;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;
using NetVips;
using NodaTime;
using FileService = DysonNetwork.Drive.Storage.FileService;

namespace DysonNetwork.Drive.Startup;

public class BroadcastEventHandler(
    INatsConnection nats,
    ILogger<BroadcastEventHandler> logger,
    IServiceProvider serviceProvider
) : BackgroundService
{
    private const string TempFileSuffix = "dypart";

    private static readonly string[] AnimatedImageTypes =
        ["image/gif", "image/apng", "image/avif"];

    private static readonly string[] AnimatedImageExtensions =
        [".gif", ".apng", ".avif"];



    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var js = nats.CreateJetStreamContext();

        await js.EnsureStreamCreated("account_events", [AccountDeletedEvent.Type]);
        
        var accountEventConsumer = await js.CreateOrUpdateConsumerAsync("account_events",
            new ConsumerConfig("drive_account_deleted_handler"), cancellationToken: stoppingToken);
        
        await js.EnsureStreamCreated("file_events", [FileUploadedEvent.Type]);
        var fileUploadedConsumer = await js.CreateOrUpdateConsumerAsync("file_events",
            new ConsumerConfig("drive_file_uploaded_handler"), cancellationToken: stoppingToken);

        var accountDeletedTask = HandleAccountDeleted(accountEventConsumer, stoppingToken);
        var fileUploadedTask = HandleFileUploaded(fileUploadedConsumer, stoppingToken);

        await Task.WhenAll(accountDeletedTask, fileUploadedTask);
    }

    private async Task HandleFileUploaded(INatsJSConsumer consumer, CancellationToken stoppingToken)
    {
        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
        {
            var payload = JsonSerializer.Deserialize<FileUploadedEventPayload>(msg.Data, GrpcTypeHelper.SerializerOptions);
            if (payload == null)
            {
                await msg.AckAsync(cancellationToken: stoppingToken);
                continue;
            }
            
            try
            {
                await ProcessAndUploadInBackgroundAsync(
                    payload.FileId,
                    payload.RemoteId,
                    payload.StorageId,
                    payload.ContentType,
                    payload.ProcessingFilePath,
                    payload.IsTempFile
                );

                await msg.AckAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing FileUploadedEvent for file {FileId}", payload?.FileId);
                await msg.NakAsync(cancellationToken: stoppingToken);
            }
        }
    }

    private async Task HandleAccountDeleted(INatsJSConsumer consumer, CancellationToken stoppingToken)
    {
        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
        {
            try
            {
                var evt = JsonSerializer.Deserialize<AccountDeletedEvent>(msg.Data, GrpcTypeHelper.SerializerOptions);
                if (evt == null)
                {
                    await msg.AckAsync(cancellationToken: stoppingToken);
                    continue;
                }

                logger.LogInformation("Account deleted: {AccountId}", evt.AccountId);

                using var scope = serviceProvider.CreateScope();
                var fs = scope.ServiceProvider.GetRequiredService<FileService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken: stoppingToken);
                try
                {
                    var files = await db.Files
                        .Where(p => p.AccountId == evt.AccountId)
                        .ToListAsync(cancellationToken: stoppingToken);

                    await fs.DeleteFileDataBatchAsync(files);
                    await db.Files
                        .Where(p => p.AccountId == evt.AccountId)
                        .ExecuteDeleteAsync(cancellationToken: stoppingToken);

                    await transaction.CommitAsync(cancellationToken: stoppingToken);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken: stoppingToken);
                    throw;
                }

                await msg.AckAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing AccountDeleted");
                await msg.NakAsync(cancellationToken: stoppingToken);
            }
        }
    }
    
     private async Task ProcessAndUploadInBackgroundAsync(
        string fileId,
        Guid remoteId,
        string storageId,
        string contentType,
        string processingFilePath,
        bool isTempFile
    )
    {
        using var scope = serviceProvider.CreateScope();
        var fs = scope.ServiceProvider.GetRequiredService<FileService>();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDatabase>();

        var pool = await fs.GetPoolAsync(remoteId);
        if (pool is null) return;

        var uploads = new List<(string FilePath, string Suffix, string ContentType, bool SelfDestruct)>();
        var newMimeType = contentType;
        var hasCompression = false;
        var hasThumbnail = false;

        logger.LogInformation("Processing file {FileId} in background...", fileId);

        var fileToUpdate = await scopedDb.Files.AsNoTracking().FirstAsync(f => f.Id == fileId);

        if (fileToUpdate.IsEncrypted)
        {
            uploads.Add((processingFilePath, string.Empty, contentType, false));
        }
        else if (!pool.PolicyConfig.NoOptimization)
        {
            var fileExtension = Path.GetExtension(processingFilePath);
            switch (contentType.Split('/')[0])
            {
                case "image":
                    if (AnimatedImageTypes.Contains(contentType) || AnimatedImageExtensions.Contains(fileExtension))
                    {
                        logger.LogInformation("Skip optimize file {FileId} due to it is animated...", fileId);
                        uploads.Add((processingFilePath, string.Empty, contentType, false));
                        break;
                    }

                    try
                    {
                        newMimeType = "image/webp";
                        using var vipsImage = Image.NewFromFile(processingFilePath);
                        var imageToWrite = vipsImage;

                        if (vipsImage.Interpretation is Enums.Interpretation.Scrgb or Enums.Interpretation.Xyz)
                        {
                            imageToWrite = vipsImage.Colourspace(Enums.Interpretation.Srgb);
                        }

                        var webpPath = Path.Join(Path.GetTempPath(), $"{fileId}.{TempFileSuffix}.webp");
                        imageToWrite.Autorot().WriteToFile(webpPath,
                            new VOption { { "lossless", true }, { "strip", true } });
                        uploads.Add((webpPath, string.Empty, newMimeType, true));

                        if (imageToWrite.Width * imageToWrite.Height >= 1024 * 1024)
                        {
                            var scale = 1024.0 / Math.Max(imageToWrite.Width, imageToWrite.Height);
                            var compressedPath =
                                Path.Join(Path.GetTempPath(), $"{fileId}.{TempFileSuffix}.compressed.webp");
                            using var compressedImage = imageToWrite.Resize(scale);
                            compressedImage.Autorot().WriteToFile(compressedPath,
                                new VOption { { "Q", 80 }, { "strip", true } });
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
                        logger.LogError(ex, "Failed to optimize image {FileId}, uploading original", fileId);
                        uploads.Add((processingFilePath, string.Empty, contentType, false));
                        newMimeType = contentType;
                    }

                    break;

                case "video":
                    uploads.Add((processingFilePath, string.Empty, contentType, false));

                    var thumbnailPath = Path.Join(Path.GetTempPath(), $"{fileId}.{TempFileSuffix}.thumbnail.jpg");
                    try
                    {
                        await FFMpegArguments
                            .FromFileInput(processingFilePath, verifyExists: true)
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
                            logger.LogWarning("FFMpeg did not produce thumbnail for video {FileId}", fileId);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to generate thumbnail for video {FileId}", fileId);
                    }

                    break;

                default:
                    uploads.Add((processingFilePath, string.Empty, contentType, false));
                    break;
            }
        }
        else
        {
            uploads.Add((processingFilePath, string.Empty, contentType, false));
        }

        logger.LogInformation("Optimized file {FileId}, now uploading...", fileId);

        if (uploads.Count > 0)
        {
            var destPool = remoteId;
            var uploadTasks = uploads.Select(item =>
                fs.UploadFileToRemoteAsync(
                    storageId,
                    destPool,
                    item.FilePath,
                    item.Suffix,
                    item.ContentType,
                    item.SelfDestruct
                )
            ).ToList();

            await Task.WhenAll(uploadTasks);

            logger.LogInformation("Uploaded file {FileId} done!", fileId);

            var now = SystemClock.Instance.GetCurrentInstant();
            await scopedDb.Files.Where(f => f.Id == fileId).ExecuteUpdateAsync(setter => setter
                .SetProperty(f => f.UploadedAt, now)
                .SetProperty(f => f.PoolId, destPool)
                .SetProperty(f => f.MimeType, newMimeType)
                .SetProperty(f => f.HasCompression, hasCompression)
                .SetProperty(f => f.HasThumbnail, hasThumbnail)
            );

            // Only delete temp file after successful upload and db update
            if (isTempFile)
                File.Delete(processingFilePath);
        }

        await fs._PurgeCacheAsync(fileId);
    }
}
