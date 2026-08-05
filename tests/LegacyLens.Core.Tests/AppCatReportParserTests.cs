using System.Text;
using LegacyLens.Core.AppCat;

namespace LegacyLens.Core.Tests;

/// <summary>
/// Fixtures mirror the real shape emitted by <c>appcat analyze --serializer json</c> (verified
/// against dotnet-appcat 1.0.1127): rule metadata in a keyed <c>rules</c> object, occurrences in
/// <c>projects[].ruleInstances[]</c> referencing rules by id only.
/// </summary>
public sealed class AppCatReportParserTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"appcat-{Guid.NewGuid():N}.json");

    private const string RealShapedReport =
        """
        {
          "settings": { "targetId": "AppService.Windows" },
          "stats": {
            "summary": { "projects": 2, "issues": 3, "incidents": 4, "effort": 10 },
            "charts": { "severity": { "Mandatory": 1, "Optional": 1, "Potential": 2 } }
          },
          "projects": [
            {
              "path": "Presentation\\Nop.Web",
              "properties": { "appName": "Nop.Web" },
              "ruleInstances": [
                {
                  "incidentId": "710a000b",
                  "ruleId": "Runtime.0003",
                  "projectPath": "Presentation\\Nop.Web\\Nop.Web.csproj",
                  "state": "Active",
                  "location": {
                    "kind": "File",
                    "path": "Presentation\\Nop.Web\\Nop.Web.csproj",
                    "snippet": "Target framework: .NETFramework,Version=v4.5.1"
                  }
                },
                {
                  "incidentId": "b71db148",
                  "ruleId": "Local.0003",
                  "projectPath": "Presentation\\Nop.Web\\Nop.Web.csproj",
                  "state": "Active",
                  "location": {
                    "kind": "File",
                    "path": "Presentation\\Nop.Web\\Controllers\\CommonController.cs",
                    "snippet": "System.IO.Directory",
                    "line": 546,
                    "column": 37
                  }
                }
              ]
            },
            {
              "path": "Libraries\\Nop.Core",
              "properties": { "appName": "Nop.Core" },
              "ruleInstances": [
                { "incidentId": "c1", "ruleId": "Security.0003", "state": "Active" },
                { "incidentId": "c2", "ruleId": "Local.0003", "state": "Active" }
              ]
            }
          ],
          "rules": {
            "Runtime.0003": {
              "id": "Runtime.0003",
              "description": "Applications running on frameworks older than .NET 8 are unsupported.",
              "label": "Old .NET Framework dependency detected",
              "severity": "Mandatory",
              "effort": 3
            },
            "Local.0003": {
              "id": "Local.0003",
              "description": "Your application has local or network IO operations.",
              "label": "Local or network IO operations detected",
              "severity": "Potential",
              "effort": 3
            },
            "Security.0003": {
              "id": "Security.0003",
              "description": "Hardcoded sensitive data detected.",
              "label": "Hardcoded sensitive data detected",
              "severity": "Optional",
              "effort": 1
            }
          }
        }
        """;

    [Fact]
    public void Parse_JoinsSeverityFromTheRuleTableOntoEachIncident()
    {
        File.WriteAllText(_tempFile, RealShapedReport);

        var report = AppCatReportParser.Parse(_tempFile);

        // Four ruleInstances, not the three rule definitions.
        Assert.Equal(4, report.Incidents.Count);
        Assert.Equal(1, report.CountsBySeverity["Mandatory"]);
        Assert.Equal(1, report.CountsBySeverity["Optional"]);
        Assert.Equal(2, report.CountsBySeverity["Potential"]);

        // 3 + 3 + 1 + 3
        Assert.Equal(10, report.TotalEffort);
    }

    [Fact]
    public void Parse_CapturesLocationAndProjectMetadata()
    {
        File.WriteAllText(_tempFile, RealShapedReport);

        var report = AppCatReportParser.Parse(_tempFile);

        var io = Assert.Single(report.Incidents, i => i.RuleId == "Local.0003" && i.Line is not null);
        Assert.Equal(546, io.Line);
        Assert.Equal("Presentation\\Nop.Web\\Controllers\\CommonController.cs", io.File);
        Assert.Equal("Nop.Web", io.Project);
        Assert.Equal("Local or network IO operations detected", io.Label);

        var security = Assert.Single(report.Incidents, i => i.RuleId == "Security.0003");
        Assert.Equal("Nop.Core", security.Project);
        Assert.Null(security.File);
    }

    [Fact]
    public void Parse_HandlesUtf8Bom()
    {
        // AppCAT writes the report with a BOM, which JsonDocument rejects if passed through raw.
        File.WriteAllText(_tempFile, RealShapedReport, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var report = AppCatReportParser.Parse(_tempFile);

        Assert.Equal(4, report.Incidents.Count);
    }

    [Fact]
    public void Parse_UnknownRuleIdStillYieldsAnIncident()
    {
        File.WriteAllText(_tempFile,
            """
            {
              "projects": [
                { "properties": { "appName": "App" },
                  "ruleInstances": [ { "ruleId": "Not.In.Rules", "state": "Active" } ] }
              ],
              "rules": {}
            }
            """);

        var report = AppCatReportParser.Parse(_tempFile);

        var incident = Assert.Single(report.Incidents);
        Assert.Equal("Not.In.Rules", incident.RuleId);
        Assert.Equal("Unknown", incident.Severity);
        Assert.Equal(0, incident.Effort);
    }

    [Fact]
    public void Parse_EmptyDocument_YieldsNoIncidents()
    {
        File.WriteAllText(_tempFile, "{}");

        var report = AppCatReportParser.Parse(_tempFile);

        Assert.Empty(report.Incidents);
        Assert.Empty(report.CountsBySeverity);
        Assert.Equal(0, report.TotalEffort);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }
}
