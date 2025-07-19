using DysonNetwork.Shared.Data;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Drive;

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
