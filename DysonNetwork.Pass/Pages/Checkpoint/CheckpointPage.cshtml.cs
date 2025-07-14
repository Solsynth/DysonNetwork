using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DysonNetwork.Pass.Pages.Checkpoint;

public class CheckpointPage(IConfiguration configuration) : PageModel
{
    [BindProperty] public IConfiguration Configuration { get; set; } = configuration;

    public ActionResult OnGet()
    {
        return Page();
    }
}