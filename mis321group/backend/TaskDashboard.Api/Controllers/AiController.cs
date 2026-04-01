using Microsoft.AspNetCore.Mvc;
using TaskDashboard.Api.Services;

namespace TaskDashboard.Api.Controllers;

/// <summary>
/// AI endpoints powered by OpenAI (see OpenAiAiService). Requires OpenAI:ApiKey configuration.
/// </summary>
[ApiController]
[Route("api/ai")]
public class AiController(IAiService aiService) : ControllerBase
{
    /// <summary>
    /// Suggest a category and project name from a task description.
    /// </summary>
    [HttpPost("categorize")]
    public async Task<IActionResult> Categorize([FromBody] CategorizeRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        if (string.IsNullOrWhiteSpace(request.TaskDescription))
        {
            return BadRequest(new { error = "taskDescription is required." });
        }

        var result = await aiService.CategorizeAsync(
            request.TaskDescription.Trim(),
            request.ExistingProjectNames,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return StatusCode(502, new { error = result.Error ?? "AI request failed." });
        }

        return Ok(new
        {
            suggestedCategory = result.SuggestedCategory,
            suggestedProjectName = result.SuggestedProjectName,
            reason = result.Reason
        });
    }

    /// <summary>
    /// Break a large task into smaller subtasks.
    /// </summary>
    [HttpPost("breakdown")]
    public async Task<IActionResult> Breakdown([FromBody] BreakdownRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        if (string.IsNullOrWhiteSpace(request.TaskTitle))
        {
            return BadRequest(new { error = "taskTitle is required." });
        }

        var result = await aiService.BreakdownAsync(
            request.TaskTitle.Trim(),
            request.TaskDescription?.Trim(),
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return StatusCode(502, new { error = result.Error ?? "AI request failed." });
        }

        return Ok(new { subtasks = result.Subtasks });
    }

    /// <summary>
    /// Recommend the next task to work on from a list.
    /// </summary>
    [HttpPost("next")]
    public async Task<IActionResult> Next([FromBody] NextRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        if (request.Tasks is null || request.Tasks.Count == 0)
        {
            return BadRequest(new { error = "tasks array is required and must not be empty." });
        }

        var summaries = request.Tasks
            .Where(t => !string.IsNullOrWhiteSpace(t.Title))
            .Select(t => new AiTaskSummary(t.Id, t.Title.Trim(), t.Priority, t.Status, t.DueDate))
            .ToList();

        if (summaries.Count == 0)
        {
            return BadRequest(new { error = "Each task must include a non-empty title." });
        }

        var result = await aiService.RecommendNextAsync(summaries, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return StatusCode(502, new { error = result.Error ?? "AI request failed." });
        }

        return Ok(new
        {
            recommendedTaskId = result.RecommendedTaskId,
            recommendedTitle = result.RecommendedTitle,
            reason = result.Reason
        });
    }

    public sealed class CategorizeRequest
    {
        public string TaskDescription { get; set; } = string.Empty;

        /// <summary>Optional names of projects the user already has (helps match naming).</summary>
        public List<string>? ExistingProjectNames { get; set; }
    }

    public sealed class BreakdownRequest
    {
        public string TaskTitle { get; set; } = string.Empty;
        public string? TaskDescription { get; set; }
    }

    public sealed class NextRequest
    {
        public List<NextTaskItem>? Tasks { get; set; }
    }

    public sealed class NextTaskItem
    {
        public int? Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Priority { get; set; }
        public string? Status { get; set; }
        public DateTime? DueDate { get; set; }
    }
}
