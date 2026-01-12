namespace DysonNetwork.Drive.Storage;

public class FileReanalysisBackgroundService(FileReanalysisService reanalysisService, ILogger<FileReanalysisBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("File reanalysis background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await reanalysisService.ProcessNextFileAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during file reanalysis");
            }

            // Wait 10 seconds before processing next file
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }

        logger.LogInformation("File reanalysis background service stopped");
    }
}
