using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DysonNetwork.Sphere.Pages;

public class CheckpointPage(IConfiguration configuration) : PageModel
{
    [BindProperty] public IConfiguration Configuration { get; set; } = configuration;

    public void OnGet()
    {
    }
}