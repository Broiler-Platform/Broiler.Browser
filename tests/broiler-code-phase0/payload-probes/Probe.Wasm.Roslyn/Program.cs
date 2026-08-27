using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace Probe.Wasm.Roslyn;

/// <summary>
/// Uses Roslyn's parser from the entry point so the trimmer keeps the syntax
/// half of the semantic service. A reference the program never calls is
/// trimmed away entirely, and the probe would then measure nothing.
/// </summary>
public static class Program
{
    public static void Main() => Console.WriteLine(CountTokens("var x = 1; // comment"));

    private static int CountTokens(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTokens().Count();
}
