using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DysonNetwork.Zone.Pages.Dynamic;

public class DynamicPage : PageModel
{
    public string Html { get; set; } = "";
}