using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LegacyLens.Core.BusinessLogic;

/// <summary>One request and the response it produced, stored so the exchange can be replayed.</summary>
public sealed record RecordedExchange
{
    /// <summary>Hash of the request, computed by <see cref="RecordedChatClient.KeyFor"/>.</summary>
    public required string Key { get; init; }

    public required string Text { get; init; }

    public required string ModelSnapshot { get; init; }

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }

    public string? StopReason { get; init; }

    /// <summary>The prompt that produced this response, kept so a recording can be reviewed by a person.</summary>
    public string? RequestPreview { get; init; }
}

/// <summary>
/// Replays previously recorded model responses instead of calling a provider.
/// </summary>
/// <remarks>
/// The TDD requires every analysis run to be reproducible and pinned to a model snapshot. A live
/// model cannot give that guarantee: the same prompt may yield different text, and a provider may
/// retire the version that produced a published result. Replaying a recording makes the evaluation
/// harness deterministic, lets the test suite exercise the extraction pipeline without network access
/// or API cost, and allows a third party to re-run an analysis against the exact responses behind it.
/// </remarks>
public sealed class RecordedChatClient : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IReadOnlyDictionary<string, RecordedExchange> _exchanges;

    public RecordedChatClient(IEnumerable<RecordedExchange> exchanges) =>
        _exchanges = exchanges.ToDictionary(exchange => exchange.Key, StringComparer.Ordinal);

    public string Provider => "recorded";

    public static RecordedChatClient Load(string path)
    {
        var exchanges = JsonSerializer.Deserialize<List<RecordedExchange>>(
            File.ReadAllText(Path.GetFullPath(path)), JsonOptions) ?? [];
        return new RecordedChatClient(exchanges);
    }

    public static void Save(string path, IEnumerable<RecordedExchange> exchanges) =>
        File.WriteAllText(path, JsonSerializer.Serialize(exchanges.ToList(), JsonOptions));

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = KeyFor(request);
        if (!_exchanges.TryGetValue(key, out var exchange))
        {
            // Falling back to a live call here would silently break the reproducibility this client
            // exists to provide, so a gap in the recording is an error rather than a cache miss.
            throw new KeyNotFoundException(
                $"No recorded response for request {key}. Record the exchange before replaying it.");
        }

        return Task.FromResult(new ChatResponse
        {
            Text = exchange.Text,
            ModelSnapshot = exchange.ModelSnapshot,
            Usage = new ChatUsage(exchange.InputTokens, exchange.OutputTokens),
            StopReason = exchange.StopReason,
        });
    }

    /// <summary>
    /// Deterministic identity of a request. Every field that can change the response participates, so
    /// an altered prompt cannot silently reuse a recording made for a different one.
    /// </summary>
    public static string KeyFor(ChatRequest request)
    {
        var builder = new StringBuilder();
        builder.Append(request.SystemPrompt ?? string.Empty).Append('\u001f');
        builder.Append(request.MaxOutputTokens).Append('\u001f');
        builder.Append(request.Temperature?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "default")
            .Append('\u001f');
        foreach (var message in request.Messages)
            builder.Append(message.Role).Append('\u001e').Append(message.Content).Append('\u001f');

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
