namespace LegacyLens.Core.Tracing.Models;

/// <summary>Where a referenced symbol is defined.</summary>
public enum SymbolOrigin
{
    /// <summary>Declared in a project of the analyzed solution.</summary>
    Source,

    /// <summary>Declared in a referenced assembly (framework, NuGet package).</summary>
    Metadata,
}

public enum MemberAccessKind
{
    Read,
    Write,
    ReadWrite,
}

/// <summary>How a chunk's boundaries were chosen.</summary>
public enum ChunkBoundary
{
    /// <summary>The member fit the token budget and was kept intact.</summary>
    Whole,

    /// <summary>Split between top-level statements of the body.</summary>
    Statement,

    /// <summary>A single statement exceeded the budget and was split between lines.</summary>
    Line,
}

public sealed record ParameterTrace(string Name, string Type, string Modifier, bool IsOptional);

public sealed record TypeTrace
{
    public required string FullyQualifiedName { get; init; }

    /// <summary>"class", "struct", "interface", "record class", ...</summary>
    public required string Kind { get; init; }

    /// <summary>Base types from the nearest ancestor up to <c>object</c>.</summary>
    public required IReadOnlyList<string> BaseTypes { get; init; }

    /// <summary>Every interface the type implements, directly or through its base types.</summary>
    public required IReadOnlyList<string> Interfaces { get; init; }

    /// <summary>Declared across more than one file, so no single file shows the whole type.</summary>
    public required bool IsPartial { get; init; }
}

public sealed record SignatureTrace
{
    public required string Accessibility { get; init; }

    public required IReadOnlyList<string> Modifiers { get; init; }

    public required string ReturnType { get; init; }

    public required IReadOnlyList<ParameterTrace> Parameters { get; init; }

    public required IReadOnlyList<string> TypeParameters { get; init; }
}

/// <summary>A method or constructor invoked from the traced body.</summary>
/// <param name="Container">Project name for source symbols, assembly name for metadata symbols.</param>
public sealed record CalleeTrace(string Symbol, SymbolOrigin Origin, string Container, int CallSites, bool IsObsolete);

/// <summary>A member that calls the traced method, found across every project in the solution.</summary>
/// <param name="ViaOverrideOrInterface">
/// The caller targets a base or interface member this method overrides or implements, so the call
/// reaches it only through dynamic dispatch.
/// </param>
public sealed record CallerTrace(
    string Symbol,
    string Project,
    string? File,
    int? Line,
    int CallSites,
    bool ViaOverrideOrInterface);

/// <summary>A field, property or event of a solution type touched by the traced body.</summary>
public sealed record MemberAccessTrace(string Symbol, string Kind, MemberAccessKind Access);

public sealed record DataFlowTrace(
    IReadOnlyList<string> ParametersRead,
    IReadOnlyList<string> ParametersWritten,
    IReadOnlyList<string> CapturedVariables,
    int LocalsDeclared);

/// <summary>Summary of the Roslyn control-flow graph built from the method body.</summary>
public sealed record ControlFlowTrace(
    int BasicBlocks,
    int ConditionalBranches,
    int LoopBackEdges,
    int ExceptionHandlers,
    int ExitPaths,
    int ThrowPaths);

/// <summary>
/// How completely the compiler resolved the traced body. This is what makes a trace trustworthy as
/// LLM context: when it is fully resolved, every type and member name in it is real.
/// </summary>
public sealed record GroundingTrace(int CompilerErrors, int InvalidOperations, IReadOnlyList<string> Errors)
{
    public bool IsFullyResolved => CompilerErrors == 0 && InvalidOperations == 0;
}

public sealed record TraceIncident(string RuleId, string Severity, string? Label, int? Line);

/// <param name="StartLine">1-based source line of the chunk's first line.</param>
/// <param name="EndLine">1-based source line of the chunk's last line, inclusive.</param>
public sealed record TraceChunk(
    int Index,
    int StartLine,
    int EndLine,
    int EstimatedTokens,
    ChunkBoundary Boundary,
    string Text);

/// <summary>
/// TDD Layer 3 — a compiler-verified representation of one method, built so the LLM layers never
/// have to infer a type, signature or call relationship from raw text.
/// </summary>
public sealed record MethodTrace
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Roslyn documentation comment id — stable across runs, unique across overloads.</summary>
    public required string Id { get; init; }

    /// <summary>Same format as the <c>symbols</c> table, so traces join back to the baseline index.</summary>
    public required string FullyQualifiedName { get; init; }

    public required string Project { get; init; }

    /// <summary>Path relative to the solution directory, with forward slashes.</summary>
    public required string? File { get; init; }

    public required int StartLine { get; init; }

    public required int EndLine { get; init; }

    public required int Complexity { get; init; }

    public required bool IsObsolete { get; init; }

    public required TypeTrace ContainingType { get; init; }

    public required SignatureTrace Signature { get; init; }

    public required IReadOnlyList<CalleeTrace> Callees { get; init; }

    public required IReadOnlyList<CallerTrace> Callers { get; init; }

    public required IReadOnlyList<MemberAccessTrace> State { get; init; }

    public required DataFlowTrace? DataFlow { get; init; }

    public required ControlFlowTrace? ControlFlow { get; init; }

    public required GroundingTrace Grounding { get; init; }

    public required IReadOnlyList<TraceIncident> AppCatIncidents { get; init; }

    public required IReadOnlyList<TraceChunk> Chunks { get; init; }

    public int EstimatedTokens => Chunks.Sum(c => c.EstimatedTokens);
}
