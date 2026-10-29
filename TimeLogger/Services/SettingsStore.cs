using System.Text.Json;
using TimeLogger.Models;

namespace TimeLogger.Services;

/// <summary>
/// Keeps the timesheet settings in App_Data/settings.json and the uploaded logo
/// in App_Data/logo.png or logo.jpg.
/// </summary>
public class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] LogoExtensions = [".png", ".jpg"];

    private readonly string _dataDir;
    private readonly string _filePath;
    private readonly object _lock = new();
    private TimesheetSettings _settings;

    public SettingsStore(IWebHostEnvironment env)
    {
        _dataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(_dataDir);
        _filePath = Path.Combine(_dataDir, "settings.json");

        _settings = File.Exists(_filePath)
            ? JsonSerializer.Deserialize<TimesheetSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new()
            : new();
    }

    public TimesheetSettings Get()
    {
        lock (_lock)
        {
            _settings.HasLogo = LogoPath() is not null;
            return _settings;
        }
    }

    public TimesheetSettings Save(TimesheetSettings settings)
    {
        foreach (var prop in typeof(TimesheetSettings).GetProperties().Where(p => p.PropertyType == typeof(string)))
        {
            prop.SetValue(settings, ((string?)prop.GetValue(settings) ?? "").Trim());
        }
        if (settings.DocumentTitle == "") settings.DocumentTitle = "Timesheet";
        if (settings.TaxLabel == "") settings.TaxLabel = "VAT";

        lock (_lock)
        {
            _settings = settings;
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_settings, JsonOptions));
        }
        return Get();
    }

    /// <summary>Updates only the values the Excel sheet has, keeping business/client details.</summary>
    public TimesheetSettings ApplyFromSheet(TimesheetSettings fromSheet)
    {
        var current = Get();
        if (fromSheet.Name != "") current.Name = fromSheet.Name;
        current.StandardHoursPerDay = fromSheet.StandardHoursPerDay;
        if (fromSheet.HourlyRate is not null) current.HourlyRate = fromSheet.HourlyRate;
        return Save(current);
    }

    // ---------- Logo ----------

    public string? LogoPath() =>
        LogoExtensions.Select(ext => Path.Combine(_dataDir, "logo" + ext)).FirstOrDefault(File.Exists);

    public void SaveLogo(byte[] bytes, string extension)
    {
        lock (_lock)
        {
            DeleteLogo();
            File.WriteAllBytes(Path.Combine(_dataDir, "logo" + extension), bytes);
        }
    }

    public void DeleteLogo()
    {
        foreach (var ext in LogoExtensions)
        {
            var path = Path.Combine(_dataDir, "logo" + ext);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
