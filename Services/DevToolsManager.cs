using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace EDBrowser.Services;

/// <summary>
/// Manages WebView2 DevTools window discovery, Chrome-like right-side docking,
/// owner-syncing, and clean toggling via F12 / Ctrl+Shift+I.
/// </summary>
public static class DevToolsManager
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint WM_CLOSE = 0x0010;
    private const int GWLP_HWNDPARENT = -8;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    public static bool IsOpen(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd) && IsWindowVisible(hwnd);

    public static IntPtr FindDevToolsWindow(HashSet<int>? excludedHwnds = null)
    {
        var candidatePids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("msedgewebview2"))
        {
            try { candidatePids.Add(p.Id); } catch { }
            finally { p.Dispose(); }
        }
        candidatePids.Add(Environment.ProcessId);

        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _l) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;
            if (excludedHwnds is not null && excludedHwnds.Contains(hwnd.ToInt32()))
                return true;

            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            if (!candidatePids.Contains((int)pid))
                return true;

            var sb = new StringBuilder(256);
            _ = GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.Contains("DevTools", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Geliştirici", StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static HashSet<int> SnapshotVisibleDevToolsHwnds()
    {
        var set = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("msedgewebview2"))
        {
            p.Dispose();
        }
        EnumWindows((hwnd, _l) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var sb = new StringBuilder(256);
            _ = GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.Contains("DevTools", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Geliştirici", StringComparison.OrdinalIgnoreCase))
            {
                set.Add(hwnd.ToInt32());
            }
            return true;
        }, IntPtr.Zero);
        return set;
    }

    public static void AttachAsDockedOwner(IntPtr devToolsHwnd, IntPtr mainFormHwnd, Rectangle screenDockRect)
    {
        if (!IsOpen(devToolsHwnd)) return;
        try
        {
            if (IntPtr.Size == 8)
                SetWindowLongPtr64(devToolsHwnd, GWLP_HWNDPARENT, mainFormHwnd);
            else
                SetWindowLong32(devToolsHwnd, GWLP_HWNDPARENT, mainFormHwnd.ToInt32());
        }
        catch { }

        UpdateDockBounds(devToolsHwnd, screenDockRect);
    }

    public static void UpdateDockBounds(IntPtr devToolsHwnd, Rectangle screenDockRect)
    {
        if (!IsOpen(devToolsHwnd) || screenDockRect.Width < 100 || screenDockRect.Height < 100)
            return;

        SetWindowPos(
            devToolsHwnd,
            HWND_TOP,
            screenDockRect.X,
            screenDockRect.Y,
            screenDockRect.Width,
            screenDockRect.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public static Rectangle? GetBounds(IntPtr hwnd)
    {
        if (!IsOpen(hwnd)) return null;
        if (GetWindowRect(hwnd, out var r))
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return null;
    }

    public static void Close(IntPtr devToolsHwnd)
    {
        if (devToolsHwnd != IntPtr.Zero && IsWindow(devToolsHwnd))
            PostMessage(devToolsHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }
}
