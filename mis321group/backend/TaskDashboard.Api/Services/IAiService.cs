namespace TaskDashboard.Api.Services;

/// <summary>
/// AI helpers backed by OpenAI (categorize tasks, break down work, suggest next step).
/// </summary>
public interface IAiService
{
    /// <summary>Suggest a category / project name from a task description.</summary>
    Task<AiCategorizeResult> CategorizeAsync(string taskDescription, IReadOnlyList<string>? existingProjectNames, CancellationToken cancellationToken = default);

    /// <summary>Split one large task into smaller subtasks.</summary>
    Task<AiBreakdownResult> BreakdownAsync(string taskTitle, string? taskDescription, CancellationToken cancellationToken = default);

    /// <summary>Pick the best next task from a list the user provides.</summary>
    Task<AiNextStepResult> RecommendNextAsync(IReadOnlyList<AiTaskSummary> tasks, CancellationToken cancellationToken = default);
}

/// <summary>Input shape for "next task" — mirrors dashboard fields without requiring DB ids.</summary>
public sealed record AiTaskSummary(int? Id, string Title, string? Priority, string? Status, DateTime? DueDate);

public sealed record AiCategorizeResult(bool Success, string? Error, string? SuggestedCategory, string? SuggestedProjectName, string? Reason);

public sealed record AiBreakdownResult(bool Success, string? Error, IReadOnlyList<string> Subtasks);

public sealed record AiNextStepResult(bool Success, string? Error, int? RecommendedTaskId, string? RecommendedTitle, string? Reason);
