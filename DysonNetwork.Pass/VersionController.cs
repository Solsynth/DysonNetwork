using Microsoft.AspNetCore.Mvc;
using DysonNetwork.Shared.Data;

namespace DysonNetwork.Pass;

[ApiController]
[Route("/api/version")]
public class VersionController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new AppVersion
        {
            Version = ThisAssembly.AssemblyVersion,
            Commit = ThisAssembly.GitCommitId,
            UpdateDate = ThisAssembly.GitCommitDate
        });
    }
}
