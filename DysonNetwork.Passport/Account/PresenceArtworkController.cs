using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Account;

public class UploadArtworkRequest
{
    [Required] public IFormFile File { get; set; } = null!;
}

[ApiController]
[Route("/api/presence/artworks")]
public class PresenceArtworkController(PresenceArtworkService artworkService) : ControllerBase
{
    [HttpPost]
    [Authorize]
    [AskPermission(PermissionKeys.PresencesArtworkManage)]
    [RequestSizeLimit(1024 * 1024)]
    [ProducesResponseType<PresenceArtworkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PresenceArtworkResponse>> UploadArtwork(
        [FromForm] UploadArtworkRequest request,
        CancellationToken cancellationToken
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            var result = await artworkService.SaveArtworkAsync(request.File, cancellationToken);
            return Ok(PresenceArtworkResponse.FromModel(result.Artwork));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_ARTWORK_UPLOAD_FAILED", Message = ex.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
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
            return BadRequest(new ApiError { Code = "PASSPORT_ARTWORK_GET_FAILED", Message = ex.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }

        if (artwork is null)
            return NotFound(new ApiError { Code = "PASSPORT_ARTWORK_NOT_FOUND", Message = "Artwork not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        var stream = await artworkService.OpenArtworkAsync(artwork, cancellationToken);
        if (stream is null)
            return NotFound(new ApiError { Code = "PASSPORT_ARTWORK_STREAM_NOT_FOUND", Message = "Artwork stream not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

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
