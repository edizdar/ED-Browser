using System.Diagnostics;
using Microsoft.Win32;

namespace EDBrowser.Services;

public static class DefaultBrowserService
{
    public static (bool Ok, string Message) RegisterAndOpenSettings()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            const string appName = "EDBrowser";
            const string progId = "EDBrowser.HTML";
            const string desc = "Erol Dizdar tarafından geliştirilen hızlı ve ultra düşük RAM web tarayıcısı";
            var openCmd = $"\"{exePath}\" \"%1\"";

            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}"))
                k.SetValue("", "ED Browser HTML Document");
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\Application"))
            {
                k.SetValue("ApplicationName", "ED Browser");
                k.SetValue("ApplicationDescription", desc);
            }
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\open\command"))
                k.SetValue("", openCmd);

            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{appName}"))
            {
                k.SetValue("", "ED Browser URL");
                k.SetValue("URL Protocol", "");
            }
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{appName}\shell\open\command"))
                k.SetValue("", openCmd);

            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\{appName}\Capabilities"))
            {
                k.SetValue("ApplicationName", "ED Browser");
                k.SetValue("ApplicationDescription", desc);
            }
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\{appName}\Capabilities\URLAssociations"))
            {
                k.SetValue("http", appName);
                k.SetValue("https", appName);
            }
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\{appName}\Capabilities\FileAssociations"))
            {
                k.SetValue(".html", progId);
                k.SetValue(".htm", progId);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                k.SetValue(appName, $@"Software\{appName}\Capabilities");

            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
            return (true, "ED Browser varsayılan tarayıcı olarak kaydedildi. Açılan Windows Ayarlar penceresinden ED Browser'ı seçebilirsiniz.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
