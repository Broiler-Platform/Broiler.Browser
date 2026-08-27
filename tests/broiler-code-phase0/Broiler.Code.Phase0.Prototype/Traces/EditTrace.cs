using System.Text.Json.Serialization;

namespace Broiler.Code.Phase0.Prototype.Traces;

/// <summary>
/// A named, committed edit/analysis trace. Budgets are stated against these, so
/// a Phase 1 or Phase 3 regression is a number moving on a fixed input rather
/// than an argument about whether the new measurement was fair.
/// </summary>
public sealed class EditTrace
{
    public required string Id { get; init; }

    public required string Description { get; init; }

    /// <summary>Which generated fixture the trace runs against.</summary>
    public required string Fixture { get; init; }

    public required int FixtureLines { get; init; }

    /// <summary>What this trace is the evidence for.</summary>
    public required string Budget { get; init; }

    public required IReadOnlyList<TraceStep> Steps { get; init; }
}

public sealed class TraceStep
{
    public required TraceStepKind Kind { get; init; }

    /// <summary>Zero-based line the step acts on. Ignored by undo and redo, and
    /// ignored entirely when <see cref="AnchorText"/> is set.</summary>
    public int Line { get; init; }

    /// <summary>
    /// Locates the line by content instead of by number. A trace that says
    /// "type inside an inert statement" stays true when the fixture generator
    /// changes; a hard-coded line number silently starts meaning something
    /// else, and the trace goes on passing while measuring the wrong thing.
    /// </summary>
    public string? AnchorText { get; init; }

    /// <summary>Which occurrence of <see cref="AnchorText"/> to use, one-based.</summary>
    public int AnchorOccurrence { get; init; } = 1;

    /// <summary>Zero-based column within the line.</summary>
    public int Column { get; init; }

    /// <summary>Characters removed, for delete and replace.</summary>
    public int DeleteLength { get; init; }

    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// How many times to repeat the step. Repeated insertions advance by the
    /// length of the inserted text, the way held-key typing does.
    /// </summary>
    public int Repeat { get; init; } = 1;

    /// <summary>
    /// When true the step is applied without letting the previous background
    /// run publish, which is how the stale-rejection trace produces a
    /// superseded result.
    /// </summary>
    public bool WithoutDraining { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<TraceStepKind>))]
public enum TraceStepKind
{
    Insert,
    Delete,
    Replace,
    Undo,
    Redo,
}
