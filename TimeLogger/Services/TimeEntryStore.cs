using System.Text.Json;
using TimeLogger.Models;

namespace TimeLogger.Services;

/// <summary>
/// Keeps time entries in a JSON file under App_Data. Simple and dependency-free;
/// swap for a database (e.g. EF Core + SQLite) when the app grows.
/// </summary>
public class TimeEntryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly List<TimeEntry> _entries;

    public TimeEntryStore(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "timelog.json");

        _entries = File.Exists(_filePath)
            ? JsonSerializer.Deserialize<List<TimeEntry>>(File.ReadAllText(_filePath), JsonOptions) ?? []
            : [];
    }

    /// <summary>Entries in timesheet order: oldest date first, then by start time.</summary>
    public List<TimeEntry> GetAll(DateOnly? from, DateOnly? to)
    {
        lock (_lock)
        {
            return _entries
                .Where(e => (from is null || e.Date >= from) && (to is null || e.Date <= to))
                .OrderBy(e => e.Date)
                .ThenBy(e => e.Start ?? TimeOnly.MaxValue)
                .ThenBy(e => e.CreatedUtc)
                .ToList();
        }
    }

    public TimeEntry Add(TimeEntryInput input)
    {
        var entry = new TimeEntry();
        Apply(entry, input);
        lock (_lock)
        {
            _entries.Add(entry);
            Save();
        }
        return entry;
    }

    /// <summary>
    /// Adds rows from an import, skipping any that already exist (same date, times and task),
    /// so importing the same spreadsheet twice doesn't create duplicates.
    /// </summary>
    public (int Added, int Skipped) AddRange(IEnumerable<TimeEntryInput> inputs)
    {
        int added = 0, skipped = 0;
        lock (_lock)
        {
            foreach (var input in inputs)
            {
                var task = input.Task.Trim();
                var exists = _entries.Any(e =>
                    e.Date == input.Date && e.Start == input.Start && e.End == input.End &&
                    string.Equals(e.Task, task, StringComparison.OrdinalIgnoreCase));
                if (exists) { skipped++; continue; }

                var entry = new TimeEntry();
                Apply(entry, input);
                _entries.Add(entry);
                added++;
            }
            if (added > 0) Save();
        }
        return (added, skipped);
    }

    public TimeEntry? Update(Guid id, TimeEntryInput input)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            if (entry is null) return null;
            Apply(entry, input);
            Save();
            return entry;
        }
    }

    public bool Delete(Guid id)
    {
        lock (_lock)
        {
            var removed = _entries.RemoveAll(e => e.Id == id) > 0;
            if (removed) Save();
            return removed;
        }
    }

    /// <summary>Removes the given entries; ids that don't exist are ignored. Returns how many were removed.</summary>
    public int DeleteMany(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        lock (_lock)
        {
            var removed = _entries.RemoveAll(e => set.Contains(e.Id));
            if (removed > 0) Save();
            return removed;
        }
    }

    /// <summary>
    /// Removes every entry. A copy of the log is kept first as App_Data/timelog.backup-{timestamp}.json,
    /// so an accidental wipe can be undone by renaming that file back to timelog.json.
    /// </summary>
    public (int Deleted, string? BackupFile) DeleteAll()
    {
        lock (_lock)
        {
            if (_entries.Count == 0) return (0, null);

            var backup = Path.Combine(Path.GetDirectoryName(_filePath)!, $"timelog.backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(backup, JsonSerializer.Serialize(_entries, JsonOptions));

            var count = _entries.Count;
            _entries.Clear();
            Save();
            return (count, Path.GetFileName(backup));
        }
    }

    /// <summary>Minutes an input works out to: from Start/End/Break if both times are given, else Minutes.</summary>
    public static int MinutesFor(TimeEntryInput input) =>
        input is { Start: { } start, End: { } end }
            ? TimeEntry.CalculateMinutes(start, end, input.BreakMinutes)
            : input.Minutes ?? 0;

    private static void Apply(TimeEntry entry, TimeEntryInput input)
    {
        entry.Date = input.Date;
        entry.Start = input.Start;
        entry.End = input.End;
        entry.BreakMinutes = Math.Max(0, input.BreakMinutes);
        entry.Task = input.Task.Trim();
        entry.Notes = (input.Notes ?? "").Trim();
        entry.Minutes = MinutesFor(input);
    }

    private void Save() => File.WriteAllText(_filePath, JsonSerializer.Serialize(_entries, JsonOptions));
}
