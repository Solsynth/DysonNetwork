using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DysonNetwork.Sphere.Pages.Auth
{
    public class ChallengeModel() : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public IActionResult OnGet()
        {
            return RedirectToPage("SelectFactor", new { id = Id });
        }
    }
}