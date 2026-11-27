using System.Runtime.InteropServices;
using EDBrowser.Services;
using EDBrowser.UI;

namespace EDBrowser;

internal static class Program
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [STAThread]
    private static void Main(string[] args)
    {
        try { _ = SetCurrentProcessExplicitAppUserModelID("com.eroldizdar.edbrowser.v4"); } catch { }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        string? diagLog = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--self-test", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                diagLog = args[i + 1];
            else if (args[i].Equals("--render-ui-preview", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                diagLog = Path.Combine(args[i + 1], "preview-error.log");
        }

        Application.ThreadException += (_, exArgs) =>
        {
            if (!string.IsNullOrEmpty(diagLog))
            {
                try { File.AppendAllText(diagLog, $"[EXCEPTION] ThreadException: {exArgs.Exception}\n"); } catch { }
                Environment.Exit(2);
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, exArgs) =>
        {
            if (!string.IsNullOrEmpty(diagLog))
            {
                try { File.AppendAllText(diagLog, $"[EXCEPTION] Unhandled: {exArgs.ExceptionObject}\n"); } catch { }
                Environment.Exit(3);
            }
        };

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
            {
                string? outLog = (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) ? args[i + 1] : null;
                int code = MainForm.RunSelfTests(outLog);
                Environment.Exit(code);
                return;
            }
            if (args[i].Equals("--render-ui-preview", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                MainForm.RenderUiPreview(args[i + 1]);
                Environment.Exit(0);
                return;
            }
        }

        var context = new BrowserContext();
        var logPath = Path.Combine(context.Storage.DataDirectory, "edbrowser.log");
        Application.ThreadException += (_, exArgs) =>
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] ThreadException: {exArgs.Exception}\n"); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, exArgs) =>
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] Unhandled: {exArgs.ExceptionObject}\n"); } catch { }
        };

        bool incognito = args.Any(a => a.Equals("--incognito", StringComparison.OrdinalIgnoreCase));
        string? initialUrl = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--benchmark-url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                initialUrl = args[i + 1];
                break;
            }
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                initialUrl = args[i];
                break;
            }
        }

        using var mainForm = new MainForm(context, incognito, initialUrl);
        Application.Run(mainForm);
    }
}
