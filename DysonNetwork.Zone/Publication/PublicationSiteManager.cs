using DysonNetwork.Shared.Models;
using System.IO.Compression;

namespace DysonNetwork.Zone.Publication;

public class FileEntry
{
    public bool IsDirectory { get; set; }
    public string RelativePath { get; set; } = null!;
    public long Size { get; set; }
    public DateTimeOffset Modified { get; set; }
}

public class PublicationSiteManager(
    IConfiguration configuration,
    IWebHostEnvironment hostEnvironment,
    PublicationSiteService publicationSiteService
)
{
    private readonly string _basePath = Path.Combine(
        hostEnvironment.ContentRootPath,
        configuration["Sites:BasePath"]!.TrimStart('/')
    );

    private string GetFullPath(Guid siteId, string relativePath)
    {
        // Treat paths starting with separator as relative to site root
        relativePath = relativePath.TrimStart('/', '\\');
        var fullPath = Path.Combine(_basePath, siteId.ToString(), relativePath);
        var normalizedPath = Path.GetFullPath(fullPath);
        var siteDirFull = Path.Combine(_basePath, siteId.ToString());
        var normalizedSiteDir = Path.GetFullPath(siteDirFull);
        if (!normalizedPath.StartsWith(normalizedSiteDir + Path.DirectorySeparatorChar) &&
            !normalizedPath.Equals(normalizedSiteDir))
        {
            throw new ArgumentException("Path escapes site directory");
        }
        return normalizedPath;
    }

    private async Task EnsureSiteDirectory(Guid siteId)
    {
        var site = await publicationSiteService.GetSiteById(siteId);
        if (site == null)
            throw new InvalidOperationException("Site not found");
        var dir = Path.Combine(_basePath, siteId.ToString());
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    public async Task<List<FileEntry>> ListFiles(Guid siteId, string relativePath = "")
    {
        await EnsureSiteDirectory(siteId);
        var targetDir = GetFullPath(siteId, relativePath);
        if (!Directory.Exists(targetDir))
            throw new DirectoryNotFoundException("Directory not found");

        var entries = (from file in Directory.GetFiles(targetDir)
            let fileInfo = new FileInfo(file)
            select new FileEntry
            {
                IsDirectory = false,
                RelativePath = Path.GetRelativePath(Path.Combine(_basePath, siteId.ToString()), file),
                Size = fileInfo.Length, Modified = fileInfo.LastWriteTimeUtc
            }).ToList();
        entries.AddRange(from subDir in Directory.GetDirectories(targetDir)
            let dirInfo = new DirectoryInfo(subDir)
            select new FileEntry
            {
                IsDirectory = true,
                RelativePath = Path.GetRelativePath(Path.Combine(_basePath, siteId.ToString()), subDir),
                Size = 0, // Directories don't have size
                Modified = dirInfo.LastWriteTimeUtc
            });

        return entries;
    }

    public async Task UploadFile(Guid siteId, string relativePath, IFormFile file)
    {
        await EnsureSiteDirectory(siteId);
        var fullPath = GetFullPath(siteId, relativePath);

        var dir = Path.GetDirectoryName(fullPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await using var stream = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(stream);
    }

    public async Task<string> ReadFileContent(Guid siteId, string relativePath)
    {
        await EnsureSiteDirectory(siteId);
        var fullPath = GetFullPath(siteId, relativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException();
        return await File.ReadAllTextAsync(fullPath);
    }

    public async Task<long> GetTotalSiteSize(Guid siteId)
    {
        await EnsureSiteDirectory(siteId);
        var dir = new DirectoryInfo(Path.Combine(_basePath, siteId.ToString()));
        return GetDirectorySize(dir);
    }

    private long GetDirectorySize(DirectoryInfo dir)
    {
        var files = dir.GetFiles();
        var size = files.Sum(file => file.Length);

        var subDirs = dir.GetDirectories();
        size += subDirs.Sum(GetDirectorySize);

        return size;
    }

    public string GetValidatedFullPath(Guid siteId, string relativePath)
    {
        return GetFullPath(siteId, relativePath);
    }

    public string GetSiteDirectory(Guid siteId)
    {
        return Path.Combine(_basePath, siteId.ToString());
    }

    public async Task UpdateFile(Guid siteId, string relativePath, string newContent)
    {
        await EnsureSiteDirectory(siteId);
        var fullPath = GetFullPath(siteId, relativePath);
        await File.WriteAllTextAsync(fullPath, newContent);
    }

    public async Task DeleteFile(Guid siteId, string relativePath)
    {
        await EnsureSiteDirectory(siteId);
        var fullPath = GetFullPath(siteId, relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        else if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, true);
    }

    public async Task PurgeSite(Guid siteId)
    {
        await EnsureSiteDirectory(siteId); // This ensures site exists and is self-managed
        var siteDirectory = Path.Combine(_basePath, siteId.ToString());
        if (Directory.Exists(siteDirectory))
        {
            Directory.Delete(siteDirectory, true); // true for recursive delete
            Directory.CreateDirectory(siteDirectory); // Recreate empty directory
        }
    }

    public async Task DeployZip(Guid siteId, IFormFile zipFile)
    {
        await EnsureSiteDirectory(siteId);
        var siteDirectory = Path.Combine(_basePath, siteId.ToString());

        // Create a temporary file for the uploaded zip
        var tempFilePath = Path.GetTempFileName();
        try
        {
            await using (var stream = new FileStream(tempFilePath, FileMode.Create))
            {
                await zipFile.CopyToAsync(stream);
            }

            // Extract the zip file to the site's directory, overwriting existing files
            await ZipFile.ExtractToDirectoryAsync(tempFilePath, siteDirectory, true);
        }
        finally
        {
            // Clean up the temporary file
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }
}
