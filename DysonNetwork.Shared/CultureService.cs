using System.Globalization;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared;

public static class CultureService
{
    public static void SetCultureInfo(string? languageCode)
    {
        var info = new CultureInfo(languageCode ?? "en-us", false);
        CultureInfo.CurrentCulture = info;
        CultureInfo.CurrentUICulture = info;
    }
    
    public static void SetCultureInfo(Account account)
    {
        SetCultureInfo(account.Language);
    }
}