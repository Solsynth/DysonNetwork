using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/presence/artworks")]
public class PresenceArtworkController(PresenceArtworkService artworkService) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(1024 * 1024)]
    [ProducesResponseType<PresenceArtworkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PresenceArtworkResponse>> UploadArtwork(
        [FromForm] IFormFile file,
        CancellationToken cancellationToken
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount)
            return Unauthorized();

        try
        {
            var result = await artworkService.SaveArtworkAsync(file, cancellationToken);
            return Ok(PresenceArtworkResponse.FromModel(result.Artwork));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{hash}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetArtwork(string hash, CancellationToken cancellationToken)
    {
        SnPresenceArtwork? artwork;
        try
        {
            artwork = await artworkService.GetArtworkAsync(hash, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        if (artwork is null)
            return NotFound();

        var stream = await artworkService.OpenArtworkAsync(artwork, cancellationToken);
        if (stream is null)
            return NotFound();

        return File(stream.Stream, artwork.MimeType, enableRangeProcessing: true);
    }

    public class PresenceArtworkResponse
    {
        public string Hash { get; set; } = null!;
        public string MimeType { get; set; } = null!;
        public long Size { get; set; }
        public string Url { get; set; } = null!;

        public static PresenceArtworkResponse FromModel(SnPresenceArtwork artwork)
        {
            return new PresenceArtworkResponse
            {
                Hash = artwork.Hash,
                MimeType = artwork.MimeType,
                Size = artwork.Size,
                Url = $"/api/presence/artworks/{artwork.Hash}"
            };
        }
    }
}
