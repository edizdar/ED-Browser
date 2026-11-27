namespace EDBrowser.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--render-setup-preview", StringComparison.OrdinalIgnoreCase))
            {
                SetupForm.RenderSetupPreview(args[i + 1]);
                return;
            }
        }

        bool uninstall = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
        string? installDir = uninstall ? Path.GetDirectoryName(Environment.ProcessPath) : null;

        using var form = new SetupForm(uninstall, installDir);
        Application.Run(form);
    }
}
