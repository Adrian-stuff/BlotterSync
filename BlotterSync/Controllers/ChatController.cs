using System.Security.Claims;
using BlotterSync.Agent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BlotterSync.Controllers;

[ApiController, Authorize, Route("api/agent/chat")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ChatController(ChatHarness chat) : ControllerBase
{
    private int OfficerId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole("Admin");

    [HttpGet]
    public Task<IReadOnlyList<ChatThreadSummary>> List(CancellationToken ct)
        => chat.RecentAsync(OfficerId, IsAdmin, ct);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await chat.GetAsync(id, OfficerId, IsAdmin, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost, EnableRateLimiting("agent"), RequestSizeLimit(32_768)]
    public async Task<IActionResult> Send(ChatRequest request, CancellationToken ct)
    {
        try
        {
            var result = await chat.SendAsync(request, OfficerId, IsAdmin, ct);
            return result is null ? NotFound(new { error = "This chat or its attached incident is unavailable." }) : Ok(result);
        }
        catch (ChatConflict) { return Conflict(new { error = "This chat changed in another request. Reopen it from Recent chats before sending again." }); }
        catch (AgentFailure ex) { return BadRequest(new { error = ex.Message }); }
    }
}
