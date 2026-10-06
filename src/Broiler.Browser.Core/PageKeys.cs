using Broiler.HtmlBridge;
using Broiler.Input.Keyboard;
using Broiler.UI;

namespace Broiler.Browser;

/// <summary>
/// A key the window received, as the page's <c>KeyboardEvent</c> describes it: its <c>key</c>, its
/// <c>code</c>, its legacy <c>keyCode</c>, which of a pair it is, and the modifiers held.
/// </summary>
/// <remarks>
/// <para>
/// The keyboard layer names a key by the physical key it is -- <c>KeyA</c>, <c>Digit1</c>,
/// <c>ArrowLeft</c>, <c>ShiftLeft</c>, which are <c>KeyboardEvent.code</c> values -- and gives its
/// Windows virtual-key code, which is what Chromium reports as <c>keyCode</c> on Windows. What a key
/// types arrives afterwards, as text; a keydown's <c>key</c> has to be known before that, so a
/// character key's is worked out here from the key and Shift, on a US layout, which is what a page
/// reading <c>key</c> for a shortcut usually expects.
/// </para>
/// </remarks>
internal static class PageKeys
{
    // Windows virtual-key codes the names below are a fallback for.
    private const int VkNumpad0 = 0x60;
    private const int VkNumpad9 = 0x69;
    private const int VkProcessKey = 0xE5;

    private static readonly Dictionary<int, (string Code, char Plain, char Shifted)> Punctuation = new()
    {
        [0xBA] = ("Semicolon", ';', ':'),
        [0xBB] = ("Equal", '=', '+'),
        [0xBC] = ("Comma", ',', '<'),
        [0xBD] = ("Minus", '-', '_'),
        [0xBE] = ("Period", '.', '>'),
        [0xBF] = ("Slash", '/', '?'),
        [0xC0] = ("Backquote", '`', '~'),
        [0xDB] = ("BracketLeft", '[', '{'),
        [0xDC] = ("Backslash", '\\', '|'),
        [0xDD] = ("BracketRight", ']', '}'),
        [0xDE] = ("Quote", '\'', '"'),
    };

    private const string ShiftedDigits = ")!@#$%^&*(";

    /// <summary>The page's description of <paramref name="input"/>, a key the window received.</summary>
    public static KeyboardInput From(UiInputEvent input)
    {
        var kind = input.KeyTransition == KeyboardKeyTransition.Up ? KeyboardInputKind.Up : KeyboardInputKind.Down;
        var modifiers = input.KeyModifiers;
        var shift = modifiers.HasFlag(KeyboardModifierState.Shift);
        var (key, code, location) = Describe(input.KeyName ?? string.Empty, input.NativeKeyCode, shift);

        // A key an input method takes is "Process" to the page, its keyCode 229, as Chromium reports it.
        if (input.NativeKeyCode == VkProcessKey)
            key = "Process";

        return new KeyboardInput(kind, key, code)
        {
            KeyCode = input.NativeKeyCode,
            Location = location,
            CtrlKey = modifiers.HasFlag(KeyboardModifierState.Control),
            ShiftKey = shift,
            AltKey = modifiers.HasFlag(KeyboardModifierState.Alt),
            MetaKey = modifiers.HasFlag(KeyboardModifierState.LeftWindows) || modifiers.HasFlag(KeyboardModifierState.RightWindows),
        };
    }

    private static (string Key, string Code, int Location) Describe(string name, int virtualKey, bool shift)
    {
        if (name.Length == 4 && name.StartsWith("Key", StringComparison.Ordinal) && char.IsAsciiLetterUpper(name[3]))
            return (shift ? name[3].ToString() : char.ToLowerInvariant(name[3]).ToString(), name, 0);

        if (name.Length == 6 && name.StartsWith("Digit", StringComparison.Ordinal) && char.IsAsciiDigit(name[5]))
            return ((shift ? ShiftedDigits[name[5] - '0'] : name[5]).ToString(), name, 0);

        if (virtualKey is >= VkNumpad0 and <= VkNumpad9)
        {
            var digit = (char)('0' + (virtualKey - VkNumpad0));
            return (digit.ToString(), "Numpad" + digit, 3);
        }

        if (Punctuation.TryGetValue(virtualKey, out var punctuation))
            return ((shift ? punctuation.Shifted : punctuation.Plain).ToString(), punctuation.Code, 0);

        return name switch
        {
            "Space" => (" ", "Space", 0),
            "Shift" or "ShiftLeft" => ("Shift", "ShiftLeft", 1),
            "ShiftRight" => ("Shift", "ShiftRight", 2),
            "Control" or "ControlLeft" => ("Control", "ControlLeft", 1),
            "ControlRight" => ("Control", "ControlRight", 2),
            "Alt" or "AltLeft" => ("Alt", "AltLeft", 1),
            "AltRight" => ("Alt", "AltRight", 2),
            "Meta" or "MetaLeft" or "OSLeft" => ("Meta", "MetaLeft", 1),
            "MetaRight" or "OSRight" => ("Meta", "MetaRight", 2),
            "Enter" or "Tab" or "Backspace" or "Escape" or "Delete" or "Insert" or "Home" or "End"
                or "PageUp" or "PageDown" or "ArrowLeft" or "ArrowRight" or "ArrowUp" or "ArrowDown"
                or "CapsLock" or "Pause" or "NumLock" or "ScrollLock" or "ContextMenu" or "PrintScreen" => (name, name, 0),
            _ when name.Length is 2 or 3 && name[0] == 'F' && int.TryParse(name.AsSpan(1), out _) => (name, name, 0),
            _ => ("Unidentified", name.StartsWith("VirtualKey:", StringComparison.Ordinal) ? string.Empty : name, 0),
        };
    }
}
