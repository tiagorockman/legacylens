using LegacyLens.Core.Analysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyLens.Core.Tests;

public sealed class ComplexityScorerTests
{
    [Fact]
    public void Score_TrivialMethod_IsOne()
    {
        Assert.Equal(1, ScoreOf("int Answer() => 42;"));
    }

    [Fact]
    public void Score_CountsBranchesLoopsAndLogicalOperators()
    {
        // 1 base + if + && + for + case + case + catch + ?: = 8
        var method =
            """
            int Classify(int x, bool flag)
            {
                if (x > 0 && flag)
                {
                    for (var i = 0; i < x; i++) { }
                }

                switch (x)
                {
                    case 1: return 1;
                    case 2: return 2;
                }

                try { return x; }
                catch (System.Exception) { return flag ? -1 : 0; }
            }
            """;

        Assert.Equal(8, ScoreOf(method));
    }

    [Fact]
    public void Score_CountsSwitchExpressionArms()
    {
        // 1 base + 3 arms = 4
        var method =
            """
            string Name(int x) => x switch
            {
                1 => "one",
                2 => "two",
                _ => "many",
            };
            """;

        Assert.Equal(4, ScoreOf(method));
    }

    private static int ScoreOf(string methodSource)
    {
        var tree = CSharpSyntaxTree.ParseText($"class C {{ {methodSource} }}");
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        return ComplexityScorer.Score(method);
    }
}
