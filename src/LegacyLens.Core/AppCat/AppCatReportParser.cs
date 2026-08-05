using System.Text.Json;

namespace LegacyLens.Core.AppCat;

/// <summary>A single modernization incident reported by AppCAT, joined with its rule metadata.</summary>
public sealed record AppCatIncident(
    string RuleId,
    string Severity,
    string? Label,
    string? File,
    int? Line,
    string? Project,
    int Effort);

/// <summary>Parsed AppCAT report with severity roll-ups (TDD Layer 2 — AppCAT incident mapping).</summary>
public sealed record AppCatReport(string SourcePath, IReadOnlyList<AppCatIncident> Incidents)
{
    public IReadOnlyDictionary<string, int> CountsBySeverity => Incidents
        .GroupBy(i => i.Severity, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Total AppCAT story points — the tool's own migration effort estimate.</summary>
    public int TotalEffort => Incidents.Sum(i => i.Effort);
}

/// <summary>
/// Parses the JSON report produced by <c>appcat analyze --serializer json</c> (Azure Migrate
/// application and code assessment for .NET).
/// </summary>
/// <remarks>
/// The report separates rule definitions from their occurrences: <c>rules</c> is a dictionary keyed
/// by rule id carrying severity, label and effort, while the incidents themselves live in
/// <c>projects[].ruleInstances[]</c> and reference a rule only by id. Severity therefore has to be
/// joined in from the rule table — reading it off the incidents yields nothing.
/// </remarks>
public static class AppCatReportParser
{
    private sealed record RuleDefinition(string Severity, string? Label, int Effort);

    public static AppCatReport Parse(string jsonPath)
    {
        var fullPath = Path.GetFullPath(jsonPath);

        // AppCAT writes the report with a UTF-8 BOM, which JsonDocument rejects as invalid JSON.
        using var stream = File.OpenRead(fullPath);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        using var document = JsonDocument.Parse(reader.ReadToEnd());

        var root = document.RootElement;
        var rules = ReadRules(root);

        var incidents = new List<AppCatIncident>();
        if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
        {
            foreach (var project in projects.EnumerateArray())
                ReadProjectIncidents(project, rules, incidents);
        }

        return new AppCatReport(fullPath, incidents);
    }

    private static Dictionary<string, RuleDefinition> ReadRules(JsonElement root)
    {
        var rules = new Dictionary<string, RuleDefinition>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("rules", out var rulesElement) || rulesElement.ValueKind != JsonValueKind.Object)
            return rules;

        foreach (var rule in rulesElement.EnumerateObject())
        {
            rules[rule.Name] = new RuleDefinition(
                GetString(rule.Value, "severity") ?? "Unknown",
                GetString(rule.Value, "label") ?? GetString(rule.Value, "description"),
                GetInt32(rule.Value, "effort") ?? 0);
        }

        return rules;
    }

    private static void ReadProjectIncidents(
        JsonElement project,
        IReadOnlyDictionary<string, RuleDefinition> rules,
        List<AppCatIncident> incidents)
    {
        if (!project.TryGetProperty("ruleInstances", out var instances) || instances.ValueKind != JsonValueKind.Array)
            return;

        var projectName = project.TryGetProperty("properties", out var properties)
            ? GetString(properties, "appName")
            : null;

        foreach (var instance in instances.EnumerateArray())
        {
            var ruleId = GetString(instance, "ruleId");
            if (string.IsNullOrWhiteSpace(ruleId))
                continue;

            var rule = rules.GetValueOrDefault(ruleId);
            string? file = null;
            int? line = null;
            if (instance.TryGetProperty("location", out var location) && location.ValueKind == JsonValueKind.Object)
            {
                file = GetString(location, "path");
                line = GetInt32(location, "line");
            }

            incidents.Add(new AppCatIncident(
                ruleId,
                rule?.Severity ?? "Unknown",
                rule?.Label,
                file,
                line,
                projectName ?? GetString(instance, "projectPath"),
                rule?.Effort ?? 0));
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}
