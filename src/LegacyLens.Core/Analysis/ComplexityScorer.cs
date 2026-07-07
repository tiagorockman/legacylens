using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyLens.Core.Analysis;

/// <summary>
/// Cyclomatic complexity over C# syntax: 1 + number of independent branch points.
/// Used to flag business-critical methods for deeper trace analysis (TDD §4.1 Layer 3).
/// </summary>
public static class ComplexityScorer
{
    public static int Score(SyntaxNode node)
    {
        var score = 1;
        foreach (var descendant in node.DescendantNodes())
        {
            score += descendant switch
            {
                IfStatementSyntax => 1,
                WhileStatementSyntax => 1,
                ForStatementSyntax => 1,
                ForEachStatementSyntax => 1,
                DoStatementSyntax => 1,
                CaseSwitchLabelSyntax => 1,
                CasePatternSwitchLabelSyntax => 1,
                SwitchExpressionArmSyntax => 1,
                CatchClauseSyntax => 1,
                ConditionalExpressionSyntax => 1,
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression)
                    || b.IsKind(SyntaxKind.LogicalOrExpression)
                    || b.IsKind(SyntaxKind.CoalesceExpression) => 1,
                _ => 0,
            };
        }

        return score;
    }
}
