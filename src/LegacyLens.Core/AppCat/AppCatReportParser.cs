using System.Text.Json;

namespace LegacyLens.Core.AppCat;

/// <summary>A single modernization incident reported by AppCAT.</summary>
public sealed record AppCatIncident(string RuleId, string Severity, string? Description, string? File);

/// <summary>Parsed AppCAT report with severity roll-ups (TDD Layer 2 — AppCAT incident mapping).</summary>
public sealed record AppCatReport(string SourcePath, IReadOnlyList<AppCatIncident> Incidents)
{
    public IReadOnlyDictionary<string, int> CountsBySeverity => Incidents
        .GroupBy(i => i.Severity, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Parses the JSON report exported by `dotnet appcat` (Azure Migrate application and code
/// assessment). The exported schema varies across tool versions, so the parser walks the
/// document and collects every object carrying a recognized AppCAT severity value instead
/// of binding to a fixed shape.
/// </summary>
public static class AppCatReportParser
{
    private static readonly HashSet<string> KnownSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mandatory", "Optional", "Potential", "Information",
    };

    private static readonly string[] RuleIdProperties = ["ruleId", "rule", "id", "issueId"];
    private static readonly string[] DescriptionProperties = ["description", "label", "message", "title"];
    private static readonly string[] FileProperties = ["file", "path", "uri", "filePath", "location"];

    public static AppCatReport Parse(string jsonPath)
    {
        var fullPath = Path.GetFullPath(jsonPath);
        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));

        var incidents = new List<AppCatIncident>();
        Walk(document.RootElement, incidents);
        return new AppCatReport(fullPath, incidents);
    }

    private static void Walk(JsonElement element, List<AppCatIncident> incidents)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryReadIncident(element, out var incident))
                    incidents.Add(incident);

                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, incidents);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Walk(item, incidents);
                break;
        }
    }

    private static bool TryReadIncident(JsonElement element, out AppCatIncident incident)
    {
        incident = default!;
        if (!TryGetString(element, ["severity"], out var severity) || !KnownSeverities.Contains(severity))
            return false;

        TryGetString(element, RuleIdProperties, out var ruleId);
        TryGetString(element, DescriptionProperties, out var description);
        TryGetString(element, FileProperties, out var file);

        incident = new AppCatIncident(
            string.IsNullOrWhiteSpace(ruleId) ? "(unknown rule)" : ruleId,
            severity,
            string.IsNullOrWhiteSpace(description) ? null : description,
            string.IsNullOrWhiteSpace(file) ? null : file);
        return true;
    }

    private static bool TryGetString(JsonElement element, string[] candidates, out string value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                continue;

            if (candidates.Any(c => string.Equals(c, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value.GetString()!;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
