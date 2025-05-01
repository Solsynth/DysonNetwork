using Microsoft.AspNetCore.Mvc;
using DysonNetwork.Sphere.Account;
using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

using DysonNetwork.Sphere.Storage;

namespace DysonNetwork.Sphere.Chat;

[ApiController]
[Route("/chatrooms")]
public class ChatRoomController(AppDatabase db, FileService fs) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ChatRoom>> CreateChatRoom(ChatRoomRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        if (request.Name is null) return BadRequest("You cannot create a chat room without a name.");
        
        CloudFile? picture = null;
        if (request.PictureId is not null)
        {
            picture = await db.Files.FindAsync(request.PictureId);
            if (picture is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
        }

        CloudFile? background = null;
        if (request.BackgroundId is not null)
        {
            background = await db.Files.FindAsync(request.BackgroundId);
            if (background is null) return BadRequest("Invalid background id, unable to find the file on cloud.");
        }

        var chatRoom = new ChatRoom
        {
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Picture = picture,
            Background = background,
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        db.ChatRooms.Add(chatRoom);
        await db.SaveChangesAsync();

        if (chatRoom.Picture is not null)
            await fs.MarkUsageAsync(chatRoom.Picture, 1);
        if (chatRoom.Background is not null)
            await fs.MarkUsageAsync(chatRoom.Background, 1);

        return Ok(chatRoom);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ChatRoom>> GetChatRoom(long id)
    {
        var chatRoom = await db.ChatRooms.FindAsync(id);
        if (chatRoom is null) return NotFound();
        return Ok(chatRoom);
    }

    [HttpGet]
    public async Task<ActionResult<List<ChatRoom>>> ListChatRooms()
    {
        var chatRooms = await db.ChatRooms.ToListAsync();
        return chatRooms;
    }

    public class ChatRoomRequest
    {
        [Required]
        [MaxLength(1024)]
        public string? Name { get; set; }

        [MaxLength(4096)] public string? Description { get; set; }
        public string? PictureId { get; set; }
        public string? BackgroundId { get; set; }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ChatRoom>> UpdateChatRoom(long id, [FromBody] ChatRoomRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms.FindAsync(id);
        if (chatRoom is null) return NotFound();

        CloudFile? picture = null;
        if (request.PictureId is not null)
        {
            picture = await db.Files.FindAsync(request.PictureId);
            if (picture is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
            chatRoom.Picture = picture;
        }
        else if(request.PictureId == "")
        {
            chatRoom.Picture = null;
        }
        CloudFile? background = null;
        if (request.BackgroundId is not null)
        {
            background = await db.Files.FindAsync(request.BackgroundId);
            if (background is null) return BadRequest("Invalid background id, unable to find the file on cloud.");
        }
        else if(request.BackgroundId == "")
        {
            chatRoom.Background = null;
        }
        if (request.Name is not null)
            chatRoom.Name = request.Name;

        db.ChatRooms.Update(chatRoom);
        await db.SaveChangesAsync();
        return Ok(chatRoom);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteChatRoom(long id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms.FindAsync(id);
        if (chatRoom is null) return NotFound();

        db.ChatRooms.Remove(chatRoom);
        await db.SaveChangesAsync();
        return NoContent();
    }
}