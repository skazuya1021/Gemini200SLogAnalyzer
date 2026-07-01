using Gemini200SLogAnalyzer.Models;
using Microsoft.Extensions.Configuration;

namespace Gemini200SLogAnalyzer.Services;

public sealed class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings;

    public SettingsService()
    {
        _settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        _settings = config.Get<AppSettings>() ?? new AppSettings();
    }

    public AppSettings Current => _settings;

    public string GetInitialFolder(string? savedFolder)
    {
        if (!string.IsNullOrWhiteSpace(savedFolder) && Directory.Exists(savedFolder))
        {
            return savedFolder;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public void UpdateInputFolder(string folder)
    {
        _settings.LastInputFolder = folder;
        Save();
    }

    public void UpdateOutput(string folder, string fileName)
    {
        _settings.LastOutputFolder = folder;
        _settings.LastOutputFileName = fileName;
        Save();
    }

    private void Save()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(_settings, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_settingsPath, json);
    }
}
