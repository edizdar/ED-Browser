using System.Runtime.InteropServices;
using EDBrowser.Models;

namespace EDBrowser.Services;

public sealed record ThemePalette(
    Color BaseBg,
    Color SurfaceBg,
    Color ElevatedBg,
    Color HoverBg,
    Color Border,
    Color TextPrimary,
    Color TextSecondary,
    Color TextMuted,
    Color Accent,
    Color AccentPurple,
    Color Green,
    Color Red);

public static class ThemeService
{
    public static readonly ThemePalette Dark = new(
        BaseBg: Color.FromArgb(12, 12, 20),
        SurfaceBg: Color.FromArgb(17, 17, 32),
        ElevatedBg: Color.FromArgb(22, 22, 40),
        HoverBg: Color.FromArgb(32, 32, 58),
        Border: Color.FromArgb(42, 44, 68),
        TextPrimary: Color.FromArgb(226, 232, 240),
        TextSecondary: Color.FromArgb(148, 163, 184),
        TextMuted: Color.FromArgb(100, 116, 139),
        Accent: Color.FromArgb(56, 189, 248),
        AccentPurple: Color.FromArgb(129, 140, 248),
        Green: Color.FromArgb(52, 211, 153),
        Red: Color.FromArgb(248, 113, 113));

    public static readonly ThemePalette IncognitoDark = new(
        BaseBg: Color.FromArgb(10, 10, 18),
        SurfaceBg: Color.FromArgb(14, 12, 28),
        ElevatedBg: Color.FromArgb(24, 20, 44),
        HoverBg: Color.FromArgb(38, 32, 66),
        Border: Color.FromArgb(62, 52, 100),
        TextPrimary: Color.FromArgb(232, 234, 246),
        TextSecondary: Color.FromArgb(167, 139, 250),
        TextMuted: Color.FromArgb(116, 105, 160),
        Accent: Color.FromArgb(167, 139, 250),
        AccentPurple: Color.FromArgb(129, 140, 248),
        Green: Color.FromArgb(52, 211, 153),
        Red: Color.FromArgb(248, 113, 113));

    public static readonly ThemePalette Light = new(
        BaseBg: Color.FromArgb(240, 244, 248),
        SurfaceBg: Color.FromArgb(255, 255, 255),
        ElevatedBg: Color.FromArgb(232, 237, 244),
        HoverBg: Color.FromArgb(208, 218, 232),
        Border: Color.FromArgb(210, 218, 228),
        TextPrimary: Color.FromArgb(26, 32, 44),
        TextSecondary: Color.FromArgb(74, 85, 104),
        TextMuted: Color.FromArgb(140, 154, 172),
        Accent: Color.FromArgb(2, 132, 199),
        AccentPurple: Color.FromArgb(99, 102, 241),
        Green: Color.FromArgb(5, 150, 105),
        Red: Color.FromArgb(220, 38, 38));

    public static ThemePalette GetPalette(BrowserTheme theme, bool incognito)
    {
        if (incognito) return IncognitoDark;
        return theme == BrowserTheme.Light ? Light : Dark;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void ApplyNativeTitleBarTheme(IntPtr handle, bool dark)
    {
        try
        {
            int val = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, 20, ref val, sizeof(int));
        }
        catch
        {
            // Older Windows builds ignore attribute 20
        }
    }
}
