using System;

namespace Probe.Wasm.Baseline;

/// <summary>
/// Deliberately minimal. A payload probe measures what the composition costs,
/// so anything beyond keeping the entry point alive would be noise in the
/// number.
/// </summary>
public static class Program
{
    public static void Main() => Console.WriteLine(Classify("var x = 1; // comment"));

    private static int Classify(string source)
    {
        // A stand-in for the portable classifier: no dependency beyond the
        // base class library, which is the whole point of the control.
        int spans = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                spans++;
                break;
            }
        }

        return spans;
    }
}
