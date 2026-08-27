using Broiler.Graphics;

namespace Broiler.Browser;

internal static class BrowserPalette
{
    public static readonly BColor Canvas = BColor.FromArgb(0xFF, 0xF8, 0xFA, 0xFC);
    public static readonly BColor Toolbar = BColor.FromArgb(0xFF, 0xF1, 0xF5, 0xF9);
    public static readonly BColor ToolbarRule = BColor.FromArgb(0xFF, 0xD8, 0xE0, 0xEA);
    public static readonly BColor Status = BColor.FromArgb(0xFF, 0xF8, 0xFA, 0xFC);
    public static readonly BColor Text = BColor.FromArgb(0xFF, 0x1F, 0x29, 0x37);
    public static readonly BColor Muted = BColor.FromArgb(0xFF, 0x5F, 0x6B, 0x7A);
    public static readonly BColor Accent = BColor.FromArgb(0xFF, 0x24, 0x64, 0xC8);
    public static readonly BColor AccentSoft = BColor.FromArgb(0xFF, 0xDF, 0xEA, 0xFF);
    public static readonly BColor Surface = BColor.White;
    public static readonly BColor Border = BColor.FromArgb(0xFF, 0xB7, 0xC2, 0xD0);
}
