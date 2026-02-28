namespace DysonNetwork.Drive.Storage;

public class FileReanalysisBackgroundService(IServiceProvider srv, ILogger<FileReanalysisBackgroundService> logger, IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("File reanalysis background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = srv.CreateScope();
                var reanalysisService = scope.ServiceProvider.GetRequiredService<FileReanalysisService>();
                await reanalysisService.ProcessNextFileAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during file reanalysis");
            }

            // Wait configured milliseconds before processing next file
            var delayMs = config.GetValue("FileReanalysis:DelayMs", 10000);
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), stoppingToken);
        }

        logger.LogInformation("File reanalysis background service stopped");
    }
}
