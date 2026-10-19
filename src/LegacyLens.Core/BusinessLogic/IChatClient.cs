namespace LegacyLens.Core.BusinessLogic;

public enum ChatRole
{
    User,
    Assistant,
}

public sealed record ChatMessage(ChatRole Role, string Content);

public sealed record ChatRequest
{
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>Instructions that frame the exchange, kept separate from the conversation itself.</summary>
    public string? SystemPrompt { get; init; }

    public int MaxOutputTokens { get; init; } = 4096;

    /// <summary>
    /// Null leaves the choice to the provider. Extraction runs set this to zero: a reproducible run
    /// requires the same input to yield the same output as far as the provider allows.
    /// </summary>
    public double? Temperature { get; init; }
}

/// <param name="InputTokens">Tokens consumed by the prompt.</param>
/// <param name="OutputTokens">Tokens produced by the model.</param>
public sealed record ChatUsage(int InputTokens, int OutputTokens)
{
    public int TotalTokens => InputTokens + OutputTokens;
}

public sealed record ChatResponse
{
    public required string Text { get; init; }

    /// <summary>
    /// The exact model version that produced the text, recorded alongside every extraction so a
    /// result can be attributed to a specific model rather than to a moving alias.
    /// </summary>
    public required string ModelSnapshot { get; init; }

    public required ChatUsage Usage { get; init; }

    /// <summary>Why generation stopped — a truncated response must not be read as a complete answer.</summary>
    public string? StopReason { get; init; }
}

/// <summary>
/// Provider-neutral abstraction over a chat-completion model (TDD §3.1 and §5).
/// </summary>
/// <remarks>
/// The abstraction exists so that the modernization logic does not depend on a particular vendor.
/// Regulated organizations that cannot route proprietary source code to a commercial service need to
/// substitute a locally hosted model without the surrounding pipeline changing, and the project needs
/// to replay recorded exchanges to keep runs reproducible. Both are implementations of this interface.
/// </remarks>
public interface IChatClient
{
    /// <summary>Identifies the implementation in audit records, for example "anthropic" or "recorded".</summary>
    string Provider { get; }

    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
