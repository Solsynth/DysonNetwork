using TencentCloud.Common;
using TencentCloud.Tmt.V20180321;
using TencentCloud.Tmt.V20180321.Models;

namespace DysonNetwork.Sphere.Translation;

public class TencentTranslation(IConfiguration configuration) : ITranslationProvider
{
    private readonly string _region = configuration["Translation:Region"]!;
    private readonly Credential _apiCredential = new Credential
    {
        SecretId = configuration["Translation:SecretId"]!,
        SecretKey = configuration["Translation:SecretKey"]!
    };

    public async Task<string> Translate(string text, string targetLanguage)
    {
        var client = new TmtClient(_apiCredential, _region);
        var request = new TextTranslateRequest();
        request.SourceText = text;
        request.Source = "auto";
        request.Target = targetLanguage;
        var response = await client.TextTranslate(request);
        return response.TargetText;
    }
}