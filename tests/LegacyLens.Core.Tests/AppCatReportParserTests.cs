using LegacyLens.Core.AppCat;

namespace LegacyLens.Core.Tests;

public sealed class AppCatReportParserTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"appcat-{Guid.NewGuid():N}.json");

    [Fact]
    public void Parse_CollectsIncidentsAcrossNestedStructures()
    {
        File.WriteAllText(_tempFile,
            """
            {
              "projects": [
                {
                  "name": "Legacy.Web",
                  "ruleInstances": [
                    {
                      "ruleId": "Binary.Framework.NotSupported",
                      "severity": "Mandatory",
                      "description": "Target framework not supported on .NET 8",
                      "location": "Legacy.Web.csproj"
                    },
                    {
                      "ruleId": "Api.WebForms",
                      "severity": "Mandatory",
                      "description": "Web Forms is not available in ASP.NET Core"
                    }
                  ]
                }
              ],
              "other": {
                "deep": [
                  { "rule": "Config.TlsVersion", "severity": "Potential", "message": "TLS 1.0 configured" }
                ]
              },
              "metadata": { "severity": "not-a-real-severity" }
            }
            """);

        var report = AppCatReportParser.Parse(_tempFile);

        Assert.Equal(3, report.Incidents.Count);
        Assert.Equal(2, report.CountsBySeverity["Mandatory"]);
        Assert.Equal(1, report.CountsBySeverity["Potential"]);

        var tls = Assert.Single(report.Incidents, i => i.RuleId == "Config.TlsVersion");
        Assert.Equal("TLS 1.0 configured", tls.Description);

        var framework = Assert.Single(report.Incidents, i => i.RuleId == "Binary.Framework.NotSupported");
        Assert.Equal("Legacy.Web.csproj", framework.File);
    }

    [Fact]
    public void Parse_EmptyDocument_YieldsNoIncidents()
    {
        File.WriteAllText(_tempFile, "{}");

        var report = AppCatReportParser.Parse(_tempFile);

        Assert.Empty(report.Incidents);
        Assert.Empty(report.CountsBySeverity);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }
}
