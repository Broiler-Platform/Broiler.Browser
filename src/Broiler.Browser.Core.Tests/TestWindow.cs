using System.Collections.Concurrent;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.Text;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;
using Broiler.Input.Text;
using Broiler.UI;

namespace Broiler.Browser.Core.Tests;

/// <summary>A whole browser over a headless host, 800×600, run until its page is done.</summary>
internal sealed class TestWindow : IDisposable
{
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly BrowserUiHost _host;
    private readonly BImageRenderer _renderer = new();
    private readonly BrowserApp _app;
    private BRenderList? _frame;
    private bool _tickArmed;
    private long _sequence;

    public TestWindow(string url)
    {
        _host = new BrowserUiHost(
            static () => new BSize(800, 600),
            static () => 1.0,
            static () => { },
            static _ => { },
            action => { _posted.Enqueue(action); return true; });
        _app = new BrowserApp(_host, () => _renderer, url, active => _tickArmed = active);
    }

    public void Settle()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            while (_posted.TryDequeue(out Action? action))
                action();

            // A window's animation tick, once armed, fires at least once.
            if (_app.HasPendingWork || _tickArmed)
            {
                _tickArmed = false;
                _app.StepAnimation();
            }

            if (_host.IsInvalidated)
                _frame = _app.RenderFrame();

            if (string.Equals(_app.Status, "Done", StringComparison.Ordinal) && _posted.IsEmpty && !_host.IsInvalidated
                && !_tickArmed)
            {
                break;
            }

            Thread.Sleep(5);
        }

        Assert.Equal("Done", _app.Status);
        _frame = _app.RenderFrame();
    }

    /// <summary>Where the page is drawn in the window.</summary>
    public BRect PageArea => _app.PageArea;

    /// <summary>The controls the window draws over the page's own.</summary>
    public IReadOnlyList<UiElement> HostedControls => _app.HostedControls;

    /// <summary>Answers the window's file dialog in its place: the path chosen, or null for a cancelled one.</summary>
    public Func<HtmlFilePickEventArgs, string?>? ChooseFile
    {
        set => _app.ChooseFile = value;
    }

    /// <summary>The address the window shows.</summary>
    public string Address => _app.AddressText;

    /// <summary>The window's back button: false when there is nothing to go back to.</summary>
    public bool GoBack() => _app.TryGoBack();

    /// <summary>Every text run of the last frame, with where it was drawn in the window.</summary>
    public IReadOnlyList<(string Text, BPoint At)> Texts() =>
        [.. Runs().Select(static run => (run.Run.Text, run.At))];

    /// <summary>The first text run of the last frame that contains <paramref name="marker"/>, and where it was drawn.</summary>
    public (BTextRun Run, BPoint At) Run(string marker)
    {
        foreach (var run in Runs())
        {
            if (run.Run.Text.Contains(marker, StringComparison.Ordinal))
                return run;
        }

        Assert.Fail($"'{marker}' was not painted. Painted: {string.Join(" | ", Texts().Select(static t => $"{t.Text}@{t.At.X:0},{t.At.Y:0}"))}");
        return default;
    }

    /// <summary>Every text run of the last frame, as drawn, with where it was drawn in the window.</summary>
    public IReadOnlyList<(BTextRun Run, BPoint At)> Runs()
    {
        var texts = new List<(BTextRun, BPoint)>();
        var transforms = new Stack<BMatrix3x2>();
        var current = BMatrix3x2.Identity;
        foreach (var command in _frame?.Commands ?? [])
        {
            switch (command)
            {
                case BRenderCommand.PushTransform push:
                    transforms.Push(current);
                    current = push.Transform * current;
                    break;
                case BRenderCommand.PopTransform when transforms.Count > 0:
                    current = transforms.Pop();
                    break;
                case BRenderCommand.DrawText text:
                    texts.Add((text.Text, current.Transform(text.Origin)));
                    break;
            }
        }

        return texts;
    }

    /// <summary>Whether <paramref name="marker"/> was drawn inside the 600px-tall window.</summary>
    public bool IsInView(string marker) =>
        Texts().Any(t => t.Text.Contains(marker, StringComparison.Ordinal) && t.At.Y >= 0 && t.At.Y < 600);

    public string Describe() =>
        string.Join("; ", Texts().Where(static t => t.Text.Contains("marker", StringComparison.Ordinal)).Select(static t => $"{t.Text}@{t.At.Y:0}"));

    /// <summary>Moves the pointer to (<paramref name="x"/>, <paramref name="y"/>) in the window, with no button held.</summary>
    public void Move(double x, double y) =>
        _app.Dispatch(UiInputEvent.FromMouseMove(new MouseMoveEvent(
            new InputEventHeader(
                InputDeviceId.FromOpaqueValue("test-pointer"),
                new InputTimestamp(++_sequence, TimeSpan.TicksPerSecond, "test"),
                _sequence),
            InputPoint.ClientDeviceIndependentPixels(x, y),
            MouseButtons.None,
            InputEventSource.Synthetic)));

    /// <summary>
    /// Presses and releases the key the keyboard layer names <paramref name="name"/> (<c>KeyA</c>,
    /// <c>Enter</c>, <c>Tab</c>), whose Windows virtual-key code is <paramref name="virtualKey"/>,
    /// typing <paramref name="text"/> in between when the key types something.
    /// </summary>
    public void Key(string name, int virtualKey, string? text = null, bool shift = false)
    {
        KeyboardModifierState modifiers = shift ? KeyboardModifierState.Shift | KeyboardModifierState.LeftShift : KeyboardModifierState.None;
        _app.Dispatch(KeyEvent(name, virtualKey, KeyboardKeyTransition.Down, modifiers));
        if (text is not null)
            _app.Dispatch(UiInputEvent.FromKeyboardText(new KeyboardTextEvent(NextHeader("test-keyboard"), text)));
        _app.Dispatch(KeyEvent(name, virtualKey, KeyboardKeyTransition.Up, modifiers));
    }

    /// <summary>Types <paramref name="text"/>, a letter key at a time, as the keyboard layer reports each.</summary>
    public void Type(string text)
    {
        foreach (char character in text)
        {
            string name = char.IsAsciiDigit(character) ? "Digit" + character : "Key" + char.ToUpperInvariant(character);
            Key(name, char.ToUpperInvariant(character), character.ToString());
        }
    }

    /// <summary>Delivers a step of an input method's composition, as the text layer reports it.</summary>
    public void Compose(TextCompositionState state, string text) =>
        _app.Dispatch(UiInputEvent.FromTextComposition(new TextCompositionEvent(NextHeader("test-keyboard"), text, state)));

    private UiInputEvent KeyEvent(string name, int virtualKey, KeyboardKeyTransition transition, KeyboardModifierState modifiers) =>
        UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
            NextHeader("test-keyboard"),
            KeyboardKey.FromName(name),
            transition,
            modifiers,
            virtualKey,
            ScanCode: 0,
            RepeatCount: 1,
            IsExtended: false,
            WasDown: false));

    private InputEventHeader NextHeader(string device) =>
        new(InputDeviceId.FromOpaqueValue(device), new InputTimestamp(++_sequence, TimeSpan.TicksPerSecond, "test"), _sequence);

    public void Click(double x, double y)
    {
        _app.Dispatch(Button(x, y, MouseButtons.Left, MouseButtonTransition.Down));
        _app.Dispatch(Button(x, y, MouseButtons.None, MouseButtonTransition.Up));
    }

    private UiInputEvent Button(double x, double y, MouseButtons held, MouseButtonTransition transition) =>
        UiInputEvent.FromMouseButton(new MouseButtonEvent(
            new InputEventHeader(
                InputDeviceId.FromOpaqueValue("test-pointer"),
                new InputTimestamp(++_sequence, TimeSpan.TicksPerSecond, "test"),
                _sequence),
            InputPoint.ClientDeviceIndependentPixels(x, y),
            held,
            MouseButton.Left,
            transition,
            InputEventSource.Synthetic));

    public void Dispose()
    {
        _app.Dispose();
        _renderer.Dispose();
        _host.Dispose();
    }
}
