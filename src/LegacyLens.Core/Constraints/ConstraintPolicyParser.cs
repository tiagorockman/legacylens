using System.Text.Json;
using LegacyLens.Core.Constraints.Models;

namespace LegacyLens.Core.Constraints;

/// <summary>
/// Reads an organization's constraint policy. Unknown rule kinds are preserved rather than rejected,
/// so a policy written for a later version of the framework still loads — and every rule it contains
/// is reported as requiring human review instead of quietly disappearing.
/// </summary>
public static class ConstraintPolicyParser
{
    public static ConstraintPolicy Parse(string policyPath)
    {
        var fullPath = Path.GetFullPath(policyPath);
        using var stream = File.OpenRead(fullPath);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        using var document = JsonDocument.Parse(
            reader.ReadToEnd(),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var root = document.RootElement;
        var rules = new List<ConstraintRule>();
        if (root.TryGetProperty("rules", out var ruleArray) && ruleArray.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var element in ruleArray.EnumerateArray())
                rules.Add(ReadRule(element, ++index));
        }

        return new ConstraintPolicy
        {
            SchemaVersion = GetInt32(root, "schemaVersion") ?? ConstraintPolicy.CurrentSchemaVersion,
            Organization = GetString(root, "organization"),
            Description = GetString(root, "description"),
            Rules = rules,
            SourceName = Path.GetFileName(fullPath),
        };
    }

    private static ConstraintRule ReadRule(JsonElement element, int index)
    {
        var declaredKind = GetString(element, "kind");
        return new ConstraintRule
        {
            Id = GetString(element, "id") ?? $"RULE-{index:D3}",
            Title = GetString(element, "title") ?? declaredKind ?? "(untitled rule)",
            Kind = ParseKind(declaredKind),
            DeclaredKind = declaredKind,
            Rationale = GetString(element, "rationale"),
            AppliesTo = GetStringArray(element, "appliesTo"),
            Packages = GetStringArray(element, "packages"),
            PackageVersions = GetStringMap(element, "packageVersions"),
            Frameworks = GetStringArray(element, "frameworks"),
            Requirement = GetString(element, "requirement"),
        };
    }

    private static ConstraintRuleKind ParseKind(string? kind) => kind?.ToLowerInvariant() switch
    {
        "prohibitedpackages" => ConstraintRuleKind.ProhibitedPackages,
        "approvedpackages" => ConstraintRuleKind.ApprovedPackages,
        "minimumpackageversions" => ConstraintRuleKind.MinimumPackageVersions,
        "approvedtargetframeworks" => ConstraintRuleKind.ApprovedTargetFrameworks,
        "manualreview" => ConstraintRuleKind.ManualReview,
        _ => ConstraintRuleKind.Unsupported,
    };

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> GetStringMap(JsonElement element, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            return map;

        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
                map[property.Name] = property.Value.GetString()!;
        }

        return map;
    }
}
