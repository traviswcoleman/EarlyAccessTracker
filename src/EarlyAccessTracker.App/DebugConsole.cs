using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EarlyAccessTracker.App;

/// <summary>
/// The published app is a WinExe and never has a console. But Visual Studio launches Web SDK projects
/// inside its own console host (VsDebugConsole.exe), which leaves an empty console window next to the
/// tray app. Hide that window — but only when every other process sharing the console is VS's host,
/// so we never hide a terminal the user launched us from (e.g. `dotnet EarlyAccessTracker.dll`).
/// </summary>
internal static class DebugConsole
{
    private const int SwHide = 0;

    public static void HideIfOwnedByVisualStudio()
    {
        try
        {
            var window = GetConsoleWindow();
            if (window == IntPtr.Zero) return;

            var pids = new uint[16];
            var count = GetConsoleProcessList(pids, (uint)pids.Length);
            if (count == 0 || count > pids.Length) return;

            var self = (uint)Environment.ProcessId;
            var others = pids.Take((int)count).Where(p => p != self).ToList();
            if (others.Count == 0 || others.All(IsVsConsoleHost))
                ShowWindow(window, SwHide);
        }
        catch
        {
            // Purely cosmetic; never block startup.
        }
    }

    private static bool IsVsConsoleHost(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Equals("VsDebugConsole", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
