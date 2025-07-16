using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.PageData;

namespace DysonNetwork.Pass.Pages.Data;

public class VersionPageData : IPageDataProvider
{
    public bool CanHandlePath(PathString path) => true;

    public Task<IDictionary<string, object?>> GetAppDataAsync(HttpContext context)
    {
        var versionData = new AppVersion
        {
            Version = ThisAssembly.AssemblyVersion,
            Commit = ThisAssembly.GitCommitId,
            UpdateDate = ThisAssembly.GitCommitDate
        };

        var result = typeof(AppVersion).GetProperties()
            .ToDictionary(property => property.Name, property => property.GetValue(versionData));

        return Task.FromResult<IDictionary<string, object?>>(result);
    }
}