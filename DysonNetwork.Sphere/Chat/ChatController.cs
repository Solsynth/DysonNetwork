using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("/chat")]
public class ChatController : ControllerBase
{
    public class MarkMessageReadRequest
    {
        public Guid MessageId { get; set; }
        public long ChatRoomId { get; set; }
    }
}