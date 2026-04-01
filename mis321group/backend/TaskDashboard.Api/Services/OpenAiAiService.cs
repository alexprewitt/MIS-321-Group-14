using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskDashboard.Api.Services;

/// <summary>
/// Calls OpenAI Chat Completions API with structured prompts and JSON-only replies.
/// Configure: OpenAI:ApiKey (use User Secrets or environment variable in development).
/// </summary>
public sealed class OpenAiAiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAiAiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAiAiService(HttpClient httpClient, IConfiguration configuration, ILogger<OpenAiAiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AiCategorizeResult> CategorizeAsync(
        string taskDescription,
        IReadOnlyList<string>? existingProjectNames,
        CancellationToken cancellationToken = default)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiCategorizeResult(false, "OpenAI API key is not configured. Set OpenAI:ApiKey in appsettings, User Secrets, or environment.", null, null, null);
        }

        var systemPrompt = """
            You help organize tasks for a personal task dashboard.
            The user will send JSON with a task description and optional existing project names.
            Respond with ONLY a single JSON object (no markdown, no code fences) using exactly these keys:
            - suggestedCategory: string (short label, e.g. Education, Work, Personal)
            - suggestedProjectName: string (concise project name; prefer matching an existing name if it fits)
            - reason: string (one short sentence)
            """;

        var userPayload = JsonSerializer.Serialize(new
        {
            taskDescription,
            existingProjectNames = existingProjectNames ?? Array.Empty<string>()
        }, JsonOptions);

        var content = await CompleteChatJsonAsync(apiKey, systemPrompt, userPayload, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return new AiCategorizeResult(false, "OpenAI did not return usable content.", null, null, null);
        }

        try
        {
            var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            return new AiCategorizeResult(
                true,
                null,
                root.GetProperty("suggestedCategory").GetString(),
                root.GetProperty("suggestedProjectName").GetString(),
                root.GetProperty("reason").GetString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse categorize JSON: {Content}", content);
            return new AiCategorizeResult(false, "Could not parse AI response as expected JSON.", null, null, null);
        }
    }

    public async Task<AiBreakdownResult> BreakdownAsync(
        string taskTitle,
        string? taskDescription,
        CancellationToken cancellationToken = default)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiBreakdownResult(false, "OpenAI API key is not configured. Set OpenAI:ApiKey in appsettings, User Secrets, or environment.", Array.Empty<string>());
        }

        var systemPrompt = """
            You break large tasks into small, actionable subtasks for a student/professional dashboard.
            The user sends JSON with taskTitle and optional taskDescription.
            Respond with ONLY a single JSON object (no markdown, no code fences) using exactly these keys:
            - subtasks: array of strings (5–12 items, each one clear and doable in one sitting; start with verbs)
            """;

        var userPayload = JsonSerializer.Serialize(new { taskTitle, taskDescription }, JsonOptions);

        var content = await CompleteChatJsonAsync(apiKey, systemPrompt, userPayload, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return new AiBreakdownResult(false, "OpenAI did not return usable content.", Array.Empty<string>());
        }

        try
        {
            var doc = JsonDocument.Parse(content);
            var arr = doc.RootElement.GetProperty("subtasks");
            var list = new List<string>();
            foreach (var item in arr.EnumerateArray())
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    list.Add(s.Trim());
                }
            }

            return new AiBreakdownResult(true, null, list);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse breakdown JSON: {Content}", content);
            return new AiBreakdownResult(false, "Could not parse AI response as expected JSON.", Array.Empty<string>());
        }
    }

    public async Task<AiNextStepResult> RecommendNextAsync(
        IReadOnlyList<AiTaskSummary> tasks,
        CancellationToken cancellationToken = default)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiNextStepResult(false, "OpenAI API key is not configured. Set OpenAI:ApiKey in appsettings, User Secrets, or environment.", null, null, null);
        }

        if (tasks.Count == 0)
        {
            return new AiNextStepResult(false, "No tasks provided.", null, null, null);
        }

        var systemPrompt = """
            You recommend ONE next task to work on from a list for a unified task dashboard.
            Prefer: not Done; higher priority (High before Medium before Low); sooner due dates; blocked or stale work when relevant.
            The user sends JSON: { "tasks": [ { "id", "title", "priority", "status", "dueDate" } ] }.
            Respond with ONLY a single JSON object (no markdown, no code fences) using exactly these keys:
            - recommendedTaskId: number or null (use the id from the list if it matches your pick; else null)
            - recommendedTitle: string (must match one task title exactly if possible)
            - reason: string (one or two short sentences)
            """;

        var userPayload = JsonSerializer.Serialize(new { tasks }, JsonOptions);

        var content = await CompleteChatJsonAsync(apiKey, systemPrompt, userPayload, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return new AiNextStepResult(false, "OpenAI did not return usable content.", null, null, null);
        }

        try
        {
            var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            int? id = null;
            if (root.TryGetProperty("recommendedTaskId", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
            {
                id = idEl.GetInt32();
            }

            return new AiNextStepResult(
                true,
                null,
                id,
                root.GetProperty("recommendedTitle").GetString(),
                root.GetProperty("reason").GetString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse next-step JSON: {Content}", content);
            return new AiNextStepResult(false, "Could not parse AI response as expected JSON.", null, null, null);
        }
    }

    private string? GetApiKey() => _configuration["OpenAI:ApiKey"];

    private string? GetModel() => _configuration["OpenAI:Model"] ?? "gpt-4o-mini";

    /// <summary>
    /// Sends chat completion request with JSON response format; returns parsed message content (JSON string).
    /// </summary>
    private async Task<string?> CompleteChatJsonAsync(
        string apiKey,
        string systemPrompt,
        string userContent,
        CancellationToken cancellationToken)
    {
        var model = GetModel();
        var requestBody = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            },
            response_format = new { type = "json_object" },
            temperature = 0.3
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI HTTP request failed.");
            return null;
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI API error {Status}: {Body}", (int)response.StatusCode, responseText);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            var choices = root.GetProperty("choices");
            var first = choices[0];
            var message = first.GetProperty("message");
            var content = message.GetProperty("content").GetString();
            return NormalizeModelJson(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse OpenAI response: {Body}", responseText);
            return null;
        }
    }

    /// <summary>Strip optional ```json ... ``` wrapper if the model adds it despite response_format.</summary>
    private static string? NormalizeModelJson(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 0)
            {
                trimmed = trimmed[..end].Trim();
            }
        }

        return trimmed;
    }
}
