using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Poll;

[ApiController]
[Route("/api/polls")]
public class PollController(
    AppDatabase db,
    PollService polls,
    Publisher.PublisherService pub,
    AccountClientHelper accountsHelper
) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PollWithStats>> GetPoll(Guid id)
    {
        var poll = await db.Polls
            .Include(p => p.Questions)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (poll is null) return NotFound("Poll not found");
        var pollWithAnswer = PollWithStats.FromPoll(poll);

        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Ok(pollWithAnswer);

        var accountId = Guid.Parse(currentUser.Id);
        var answer = await polls.GetPollAnswer(id, accountId);
        if (answer is not null)
            pollWithAnswer.UserAnswer = answer;
        pollWithAnswer.Stats = await polls.GetPollStats(id);

        return Ok(pollWithAnswer);
    }

    public class PollAnswerRequest
    {
        public required Dictionary<string, JsonElement> Answer { get; set; }
    }

    [HttpPost("{id:guid}/answer")]
    [Authorize]
    public async Task<ActionResult<SnPollAnswer>> AnswerPoll(Guid id, [FromBody] PollAnswerRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);
        try
        {
            return await polls.AnswerPoll(id, accountId, request.Answer);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:guid}/answer")]
    [Authorize]
    public async Task<IActionResult> DeletePollAnswer(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);
        try
        {
            await polls.UnAnswerPoll(id, accountId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:guid}/feedback")]
    public async Task<ActionResult<List<SnPollAnswer>>> GetPollFeedback(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var poll = await db.Polls
            .FirstOrDefaultAsync(p => p.Id == id);
        if (poll is null) return NotFound("Poll not found");

        if (!await pub.IsMemberWithRole(poll.PublisherId, accountId, Shared.Models.PublisherMemberRole.Viewer))
            return StatusCode(403, "You need to be a viewer to view this poll's feedback.");

        var answerQuery = db.PollAnswers
            .Where(a => a.PollId == id)
            .AsQueryable();

        var total = await answerQuery.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var answers = await answerQuery
            .OrderByDescending(a => a.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        if (!poll.IsAnonymous)
        {
            var answeredAccountsId = answers.Select(x => x.AccountId).Distinct().ToList();
            var answeredAccounts = await accountsHelper.GetAccountBatch(answeredAccountsId);

            // Populate Account field for each answer
            foreach (var answer in answers)
            {
                var protoValue = answeredAccounts.FirstOrDefault(a => a.Id == answer.AccountId.ToString());
                if (protoValue is not null)
                    answer.Account = SnAccount.FromProtoValue(protoValue);
            }
        }

        return Ok(answers);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<List<SnPoll>>> ListPolls(
        [FromQuery(Name = "pub")] string? pubName,
        [FromQuery] bool active = false,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        List<Guid> publishers;
        if (pubName is null) publishers = (await pub.GetUserPublishers(accountId)).Select(p => p.Id).ToList();
        else
        {
            publishers = await db.PublisherMembers
                .Include(p => p.Publisher)
                .Where(p => p.Publisher.Name == pubName && p.AccountId == accountId)
                .Select(p => p.PublisherId)
                .ToListAsync();
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var query = db.Polls
            .Where(e => publishers.Contains(e.PublisherId));
        if (active) query = query.Where(e => e.EndedAt > now);

        var totalCount = await query.CountAsync();
        HttpContext.Response.Headers.Append("X-Total", totalCount.ToString());

        var polls = await query
            .Skip(offset)
            .Take(take)
            .Include(p => p.Questions)
            .ToListAsync();
        return Ok(polls);
    }

    public class PollRequest
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public Instant? EndedAt { get; set; }
        public bool? IsAnonymous { get; set; }
        public List<PollRequestQuestion>? Questions { get; set; }
    }

    public class PollRequestQuestion
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public PollQuestionType Type { get; set; }
        public List<SnPollOption>? Options { get; set; }

        [MaxLength(1024)] public string Title { get; set; } = null!;
        [MaxLength(4096)] public string? Description { get; set; }
        public int Order { get; set; } = 0;
        public bool IsRequired { get; set; }

        public SnPollQuestion ToQuestion() => new()
        {
            Id = Id,
            Type = Type,
            Options = Options,
            Title = Title,
            Description = Description,
            Order = Order,
            IsRequired = IsRequired
        };
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnPoll>> CreatePoll([FromBody] PollRequest request,
        [FromQuery(Name = "pub")] string pubName)
    {
        if (request.Questions is null) return BadRequest("Questions are required.");
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await pub.GetPublisherByName(pubName);
        if (publisher is null) return BadRequest("Publisher was not found.");
        if (!await pub.IsMemberWithRole(publisher.Id, accountId, Shared.Models.PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least be an editor to create polls as this publisher.");

        var poll = new SnPoll
        {
            Title = request.Title,
            Description = request.Description,
            EndedAt = request.EndedAt,
            IsAnonymous = request.IsAnonymous ?? false,
            PublisherId = publisher.Id,
            Questions = request.Questions.Select(q => q.ToQuestion()).ToList()
        };

        try
        {
            polls.ValidatePoll(poll);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        db.Polls.Add(poll);
        await db.SaveChangesAsync();
        return Ok(poll);
    }

    [HttpPatch("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnPoll>> UpdatePoll(Guid id, [FromBody] PollRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        // Start a transaction
        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            var poll = await db.Polls
                .Include(p => p.Questions)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (poll == null) return NotFound("Poll not found");

            // Check if user is an editor of the publisher that owns the poll
            if (!await pub.IsMemberWithRole(poll.PublisherId, accountId, Shared.Models.PublisherMemberRole.Editor))
                return StatusCode(403, "You need to be at least an editor to update this poll.");

            // Update properties if they are provided in the request
            if (request.Title != null) poll.Title = request.Title;
            if (request.Description != null) poll.Description = request.Description;
            if (request.EndedAt.HasValue) poll.EndedAt = request.EndedAt;
            if (request.IsAnonymous.HasValue) poll.IsAnonymous = request.IsAnonymous.Value;

            db.Update(poll);
            await db.SaveChangesAsync();

            // Update questions if provided
            if (request.Questions != null)
            {
                await db.PollQuestions
                    .Where(p => p.PollId == poll.Id)
                    .ExecuteDeleteAsync();
                var newQuestions = request.Questions
                    .Select(q => q.ToQuestion())
                    .Select(q =>
                    {
                        q.PollId = poll.Id;
                        return q;
                    })
                    .ToList();
                db.PollQuestions.AddRange(newQuestions);
                await db.SaveChangesAsync();
                poll.Questions = newQuestions;
            }

            polls.ValidatePoll(poll);

            // Commit the transaction if all operations succeed
            await transaction.CommitAsync();

            return Ok(poll);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeletePoll(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        // Start a transaction
        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            var poll = await db.Polls
                .Include(p => p.Questions)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (poll == null) return NotFound("Poll not found");

            // Check if user is an editor of the publisher that owns the poll
            if (!await pub.IsMemberWithRole(poll.PublisherId, accountId, Shared.Models.PublisherMemberRole.Editor))
                return StatusCode(403, "You need to be at least an editor to delete this poll.");

            // Delete all answers for this poll
            var answers = await db.PollAnswers
                .Where(a => a.PollId == id)
                .ToListAsync();

            if (answers.Count != 0)
                db.PollAnswers.RemoveRange(answers);

            // Delete all questions for this poll
            if (poll.Questions.Count != 0)
                db.PollQuestions.RemoveRange(poll.Questions);

            // Finally, delete the poll itself
            db.Polls.Remove(poll);

            await db.SaveChangesAsync();

            // Commit the transaction if all operations succeed
            await transaction.CommitAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, "An error occurred while deleting the poll... " + ex.Message);
        }
    }
}