using System.Text;
using LegacyLens.Core.Tracing.Models;

namespace LegacyLens.Core.Tracing;

/// <summary>
/// Renders a trace as the Markdown context block handed to the LLM layers (TDD §6.1). Every type
/// and member name in it comes from the compiler, and the grounding line tells the model explicitly
/// when that guarantee does not hold for part of the body.
/// </summary>
public static class TracePromptRenderer
{
    public static string Render(MethodTrace trace)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Trace: `{trace.FullyQualifiedName}`");
        sb.AppendLine();
        sb.AppendLine($"- **Project:** {trace.Project}");
        if (trace.File is not null)
            sb.AppendLine($"- **File:** `{trace.File}` (lines {trace.StartLine}–{trace.EndLine})");
        sb.AppendLine($"- **Cyclomatic complexity:** {trace.Complexity}");
        if (trace.IsObsolete)
            sb.AppendLine("- **Marked `[Obsolete]`**");
        sb.AppendLine($"- **Grounding:** {GroundingSummary(trace.Grounding)}");
        foreach (var error in trace.Grounding.Errors)
            sb.AppendLine($"  - {error}");
        sb.AppendLine();

        AppendContainingType(sb, trace.ContainingType);
        AppendSignature(sb, trace.Signature);
        AppendControlFlow(sb, trace.ControlFlow);
        AppendCallees(sb, trace.Callees);
        AppendCallers(sb, trace.Callers);
        AppendState(sb, trace.State);
        AppendDataFlow(sb, trace.DataFlow);
        AppendIncidents(sb, trace.AppCatIncidents);
        AppendSource(sb, trace.Chunks);

        return sb.ToString();
    }

    private static string GroundingSummary(GroundingTrace grounding) =>
        grounding.IsFullyResolved
            ? "fully resolved by the compiler — every type and member named below exists."
            : $"**partially resolved** — {grounding.CompilerErrors} compiler error(s), " +
              $"{grounding.InvalidOperations} unbound operation(s). Names the compiler could not " +
              "resolve must not be assumed to exist.";

    private static void AppendContainingType(StringBuilder sb, TypeTrace type)
    {
        sb.AppendLine("## Containing type");
        sb.AppendLine();
        sb.AppendLine($"`{type.FullyQualifiedName}` ({type.Kind})");
        sb.AppendLine();
        if (type.BaseTypes.Count > 0)
            sb.AppendLine($"- **Inherits:** {string.Join(" → ", type.BaseTypes.Select(Code))}");
        if (type.Interfaces.Count > 0)
            sb.AppendLine($"- **Implements:** {string.Join(", ", type.Interfaces.Select(Code))}");
        if (type.IsPartial)
            sb.AppendLine("- Declared `partial` across multiple files.");
        sb.AppendLine();
    }

    private static void AppendSignature(StringBuilder sb, SignatureTrace signature)
    {
        sb.AppendLine("## Signature");
        sb.AppendLine();
        var modifiers = new[] { signature.Accessibility }.Concat(signature.Modifiers).Where(m => m.Length > 0);
        sb.AppendLine($"- **Modifiers:** {string.Join(" ", modifiers)}");
        sb.AppendLine($"- **Returns:** {Code(signature.ReturnType)}");
        if (signature.TypeParameters.Count > 0)
            sb.AppendLine($"- **Type parameters:** {string.Join(", ", signature.TypeParameters.Select(Code))}");

        if (signature.Parameters.Count == 0)
        {
            sb.AppendLine("- **Parameters:** none");
        }
        else
        {
            sb.AppendLine("- **Parameters:**");
            foreach (var parameter in signature.Parameters)
            {
                var modifier = parameter.Modifier.Length > 0 ? $"{parameter.Modifier} " : string.Empty;
                var optional = parameter.IsOptional ? " (optional)" : string.Empty;
                sb.AppendLine($"  - `{modifier}{parameter.Name}`: {Code(parameter.Type)}{optional}");
            }
        }

        sb.AppendLine();
    }

    private static void AppendControlFlow(StringBuilder sb, ControlFlowTrace? flow)
    {
        if (flow is null)
            return;

        sb.AppendLine("## Control flow");
        sb.AppendLine();
        sb.AppendLine("| Basic blocks | Conditional branches | Loops | Exception handlers | Exit paths | Throw paths |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|");
        sb.AppendLine(
            $"| {flow.BasicBlocks} | {flow.ConditionalBranches} | {flow.LoopBackEdges} | " +
            $"{flow.ExceptionHandlers} | {flow.ExitPaths} | {flow.ThrowPaths} |");
        sb.AppendLine();
    }

    private static void AppendCallees(StringBuilder sb, IReadOnlyList<CalleeTrace> callees)
    {
        sb.AppendLine($"## Calls ({callees.Count} distinct)");
        sb.AppendLine();
        if (callees.Count == 0)
        {
            sb.AppendLine("_None._");
            sb.AppendLine();
            return;
        }

        AppendCalleeGroup(sb, "Solution code", callees.Where(c => c.Origin == SymbolOrigin.Source));
        AppendCalleeGroup(sb, "Framework and packages", callees.Where(c => c.Origin == SymbolOrigin.Metadata));
    }

    private static void AppendCalleeGroup(StringBuilder sb, string heading, IEnumerable<CalleeTrace> callees)
    {
        var list = callees.ToList();
        if (list.Count == 0)
            return;

        sb.AppendLine($"**{heading}:**");
        sb.AppendLine();
        foreach (var callee in list)
        {
            var obsolete = callee.IsObsolete ? " — **obsolete**" : string.Empty;
            sb.AppendLine($"- {Code(callee.Symbol)} — {callee.Container} ×{callee.CallSites}{obsolete}");
        }

        sb.AppendLine();
    }

    private static void AppendCallers(StringBuilder sb, IReadOnlyList<CallerTrace> callers)
    {
        sb.AppendLine($"## Called by ({callers.Count})");
        sb.AppendLine();
        if (callers.Count == 0)
        {
            sb.AppendLine(
                "_No callers in the solution. Likely an entry point or invoked by the framework " +
                "(MVC action, dependency injection, reflection, serialization)._");
            sb.AppendLine();
            return;
        }

        foreach (var caller in callers)
        {
            var location = caller.File is null ? string.Empty : $", `{caller.File}` line {caller.Line}";
            var dispatch = caller.ViaOverrideOrInterface ? " — via override or interface" : string.Empty;
            sb.AppendLine($"- {Code(caller.Symbol)} — {caller.Project}{location} ×{caller.CallSites}{dispatch}");
        }

        sb.AppendLine();
    }

    private static void AppendState(StringBuilder sb, IReadOnlyList<MemberAccessTrace> state)
    {
        if (state.Count == 0)
            return;

        sb.AppendLine("## State");
        sb.AppendLine();
        AppendAccessGroup(sb, "Reads", state, MemberAccessKind.Read);
        AppendAccessGroup(sb, "Writes", state, MemberAccessKind.Write);
        AppendAccessGroup(sb, "Reads and writes", state, MemberAccessKind.ReadWrite);
        sb.AppendLine();
    }

    private static void AppendAccessGroup(
        StringBuilder sb,
        string label,
        IReadOnlyList<MemberAccessTrace> state,
        MemberAccessKind kind)
    {
        var members = state.Where(s => s.Access == kind).Select(s => $"{Code(s.Symbol)} ({s.Kind.ToLowerInvariant()})").ToList();
        if (members.Count > 0)
            sb.AppendLine($"- **{label}:** {string.Join(", ", members)}");
    }

    private static void AppendDataFlow(StringBuilder sb, DataFlowTrace? flow)
    {
        if (flow is null)
            return;

        sb.AppendLine("## Data flow");
        sb.AppendLine();
        sb.AppendLine($"- **Parameters read:** {NamesOrNone(flow.ParametersRead)}");
        sb.AppendLine($"- **Parameters reassigned:** {NamesOrNone(flow.ParametersWritten)}");
        sb.AppendLine($"- **Captured by lambdas or local functions:** {NamesOrNone(flow.CapturedVariables)}");
        sb.AppendLine($"- **Locals declared:** {flow.LocalsDeclared}");
        sb.AppendLine();
    }

    private static void AppendIncidents(StringBuilder sb, IReadOnlyList<TraceIncident> incidents)
    {
        if (incidents.Count == 0)
            return;

        sb.AppendLine($"## AppCAT incidents in this method ({incidents.Count})");
        sb.AppendLine();
        foreach (var incident in incidents)
        {
            var line = incident.Line is int l ? $" line {l}" : string.Empty;
            sb.AppendLine($"- `{incident.RuleId}` ({incident.Severity}){line} — {incident.Label ?? "no description"}");
        }

        sb.AppendLine();
    }

    private static void AppendSource(StringBuilder sb, IReadOnlyList<TraceChunk> chunks)
    {
        sb.AppendLine("## Source");
        sb.AppendLine();
        foreach (var chunk in chunks)
        {
            if (chunks.Count > 1)
            {
                sb.AppendLine(
                    $"### Chunk {chunk.Index + 1} of {chunks.Count} — lines {chunk.StartLine}–{chunk.EndLine} " +
                    $"(~{chunk.EstimatedTokens:N0} tokens, split at {chunk.Boundary.ToString().ToLowerInvariant()} boundary)");
                sb.AppendLine();
            }

            sb.AppendLine("```csharp");
            sb.AppendLine(chunk.Text);
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }

    private static string NamesOrNone(IReadOnlyList<string> names) =>
        names.Count == 0 ? "none" : string.Join(", ", names.Select(Code));

    private static string Code(string value) => $"`{value}`";
}
