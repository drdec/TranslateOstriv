using System.IO;
using System.Text.Json;
using System.Windows;

namespace OstrivTranslate.App;

public partial class App : Application
{
    public static Preferences Preferences { get; private set; } = new();
    public static string SettingsPath { get; private set; } = "";
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OstrivTranslate", "settings.json");
        var settingsIndex = Array.IndexOf(e.Args, "--settings");
        if (settingsIndex >= 0 && settingsIndex + 1 < e.Args.Length) SettingsPath = Path.GetFullPath(e.Args[settingsIndex + 1]);
        try { if (File.Exists(SettingsPath)) Preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Preferences = new(); }
        L.Set(Preferences.UiLanguage);
        var window = new MainWindow(); MainWindow = window; window.Show();
        var smokeIndex = Array.IndexOf(e.Args, "--smoke");
        if (smokeIndex >= 0 && smokeIndex + 1 < e.Args.Length)
            window.Loaded += async (_, _) => await window.SmokeTest(e.Args[smokeIndex + 1]);
    }
    public static void SavePreferences()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Preferences));
    }
}
public sealed class Preferences
{
    public string UiLanguage { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    public string GamePath { get; set; } = @"E:\steam\steamapps\common\Ostriv\ostriv.exe";
}
