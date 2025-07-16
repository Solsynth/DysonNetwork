using Microsoft.AspNetCore.Http;

namespace DysonNetwork.Shared.PageData;

public interface IPageDataProvider
{
    bool CanHandlePath(PathString path);
    Task<IDictionary<string, object?>> GetAppDataAsync(HttpContext context); 
}