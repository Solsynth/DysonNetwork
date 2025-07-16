using DysonNetwork.Shared.PageData;

namespace DysonNetwork.Pass.Pages.Data;

public class CaptchaPageData(IConfiguration configuration) : IPageDataProvider
{
    public bool CanHandlePath(PathString path) => path == "/captcha";

    public Task<IDictionary<string, object?>> GetAppDataAsync(HttpContext context)
    {
        var provider = configuration.GetSection("Captcha")["Provider"]?.ToLower();
        var apiKey = configuration.GetSection("Captcha")["ApiKey"];
        
        return Task.FromResult<IDictionary<string, object?>>(new Dictionary<string, object?>
        {
            ["Provider"] = provider,
            ["ApiKey"] = apiKey
        });
    }
}