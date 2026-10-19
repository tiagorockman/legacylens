using LegacyLens.Core.BusinessLogic;
using LegacyLens.Core.BusinessLogic.Models;
using LegacyLens.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace LegacyLens.Core.Tests;

public sealed class RubricScoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void Constructor_RejectsScoresOutsideTheScale(int outOfRange) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RubricScore(outOfRange, 3, 3, 3));

    [Fact]
    public void MeetsThreshold_RequiresEveryDimension()
    {
        Assert.True(new RubricScore(3, 3, 3, 3).MeetsThreshold());
        Assert.True(new RubricScore(4, 4, 4, 4).MeetsThreshold());

        // A statement that reads well but is unsupported must not pass on readability alone,
        // which is why the threshold is applied per dimension rather than to an average.
        Assert.False(new RubricScore(1, 4, 4, 4).MeetsThreshold());
        Assert.False(new RubricScore(4, 4, 4, 2).MeetsThreshold());
    }

    [Fact]
    public void MeetsThreshold_HonoursACustomMinimum()
    {
        var score = new RubricScore(3, 3, 3, 3);

        Assert.True(score.MeetsThreshold(minimum: 2));
        Assert.False(score.MeetsThreshold(minimum: 4));
    }

    [Fact]
    public void IsHighConfidence_IsFalseUntilTheRuleHasBeenScored()
    {
        var unscored = Rule() with { Score = null };

        Assert.False(unscored.IsHighConfidence());
        Assert.True((Rule() with { Score = new RubricScore(3, 3, 3, 3) }).IsHighConfidence());
        Assert.False((Rule() with { Score = new RubricScore(3, 3, 3, 2) }).IsHighConfidence());
    }

    internal static BusinessRule Rule() => new()
    {
        SymbolId = "M:Billing.InvoiceService.Compute(System.Int32)",
        FullyQualifiedName = "Billing.InvoiceService.Compute(int)",
        Project = "Billing",
        File = "Billing/InvoiceService.cs",
        Kind = BusinessRuleKind.Invariant,
        Statement = "An invoice total may never fall below zero.",
        ModelSnapshot = "test-model-2026-01",
        ExtractedUtc = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc),
    };
}

public sealed class RecordedChatClientTests
{
    private static readonly ChatRequest Request = new()
    {
        SystemPrompt = "Extract business rules.",
        Messages = [new ChatMessage(ChatRole.User, "trace content")],
        Temperature = 0,
    };

    [Fact]
    public async Task CompleteAsync_ReplaysTheRecordedResponseWithItsProvenance()
    {
        var client = new RecordedChatClient([Recorded(RecordedChatClient.KeyFor(Request))]);

        var response = await client.CompleteAsync(Request);

        Assert.Equal("recorded", client.Provider);
        Assert.Equal("An invoice total may never fall below zero.", response.Text);
        Assert.Equal("test-model-2026-01", response.ModelSnapshot);
        Assert.Equal(1_200, response.Usage.InputTokens);
        Assert.Equal(1_240, response.Usage.TotalTokens);
    }

    [Fact]
    public async Task CompleteAsync_FailsRatherThanSilentlyMissingARecording()
    {
        var client = new RecordedChatClient([Recorded("some-other-key")]);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => client.CompleteAsync(Request));
    }

    [Fact]
    public void KeyFor_IsStableForTheSameRequest() =>
        Assert.Equal(RecordedChatClient.KeyFor(Request), RecordedChatClient.KeyFor(Request with { }));

    [Theory]
    [InlineData("system")]
    [InlineData("message")]
    [InlineData("temperature")]
    [InlineData("maxTokens")]
    public void KeyFor_ChangesWithAnyFieldThatCouldChangeTheResponse(string field)
    {
        var altered = field switch
        {
            "system" => Request with { SystemPrompt = "Extract integration contracts." },
            "message" => Request with { Messages = [new ChatMessage(ChatRole.User, "different trace")] },
            "temperature" => Request with { Temperature = 0.7 },
            _ => Request with { MaxOutputTokens = 256 },
        };

        Assert.NotEqual(RecordedChatClient.KeyFor(Request), RecordedChatClient.KeyFor(altered));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsARecording()
    {
        var path = Path.Combine(Path.GetTempPath(), $"recording-{Guid.NewGuid():N}.json");
        try
        {
            RecordedChatClient.Save(path, [Recorded(RecordedChatClient.KeyFor(Request))]);
            var response = await RecordedChatClient.Load(path).CompleteAsync(Request);

            Assert.Equal("An invoice total may never fall below zero.", response.Text);
            Assert.DoesNotContain("\r", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static RecordedExchange Recorded(string key) => new()
    {
        Key = key,
        Text = "An invoice total may never fall below zero.",
        ModelSnapshot = "test-model-2026-01",
        InputTokens = 1_200,
        OutputTokens = 40,
        StopReason = "end_turn",
    };
}

public sealed class BusinessRulePersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"legacylens-rules-{Guid.NewGuid():N}.db");

    [Fact]
    public void SaveBusinessRules_StoresTheStatementItsScoresAndItsProvenance()
    {
        var scored = RubricScoreTests.Rule() with { Score = new RubricScore(4, 3, 4, 3) };
        var unscored = RubricScoreTests.Rule() with
        {
            SymbolId = "M:Billing.InvoiceService.Validate",
            Kind = BusinessRuleKind.Precondition,
            Statement = "A customer must exist before an invoice is raised.",
            Score = null,
        };

        using (var db = new LegacyLensDb(_dbPath))
        {
            db.SaveBusinessRules("run-001", [scored, unscored]);
        }

        Assert.Equal(2, Scalar<long>("SELECT COUNT(*) FROM business_logic"));
        Assert.Equal("Invariant", Scalar<string>(
            "SELECT kind FROM business_logic WHERE symbol_id LIKE '%Compute%'"));
        Assert.Equal(3L, Scalar<long>(
            "SELECT score_usefulness FROM business_logic WHERE symbol_id LIKE '%Compute%'"));
        Assert.Equal("test-model-2026-01", Scalar<string>(
            "SELECT model_snapshot FROM business_logic WHERE symbol_id LIKE '%Compute%'"));

        // An unscored extraction is still recorded; its rubric columns stay null so that the absence
        // of an evaluation is visible rather than indistinguishable from a low score.
        Assert.Equal(1L, Scalar<long>(
            "SELECT COUNT(*) FROM business_logic WHERE score_usefulness IS NULL"));
    }

    private T Scalar<T>(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
