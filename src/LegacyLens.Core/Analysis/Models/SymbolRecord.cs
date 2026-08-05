namespace LegacyLens.Core.Analysis.Models;

public enum SymbolKind
{
    Type,
    Method,
}

/// <summary>A source-declared symbol indexed for trace generation (TDD §4.3 `symbols` table).</summary>
public sealed record SymbolRecord
{
    public required string ProjectName { get; init; }

    /// <summary>Fully-qualified display name, e.g. "Contoso.Billing.InvoiceService.Compute(int)".</summary>
    public required string FullyQualifiedName { get; init; }

    public required SymbolKind Kind { get; init; }

    public required string? FilePath { get; init; }

    /// <summary>Cyclomatic complexity; 0 for types and bodiless methods.</summary>
    public required int Complexity { get; init; }

    /// <summary>True when the symbol is marked [Obsolete].</summary>
    public required bool IsDeprecated { get; init; }
}

/// <summary>A compiler error id with how often it occurred and one representative message.</summary>
public sealed record DiagnosticSummary(string Id, int Count, string Example);

/// <summary>Symbol index and build health for one project.</summary>
public sealed record ProjectSymbols(
    string ProjectName,
    int CompilationErrorCount,
    IReadOnlyList<SymbolRecord> Symbols)
{
    /// <summary>Most frequent compiler errors, so a failing baseline says what actually broke.</summary>
    public IReadOnlyList<DiagnosticSummary> TopErrors { get; init; } = [];
}
