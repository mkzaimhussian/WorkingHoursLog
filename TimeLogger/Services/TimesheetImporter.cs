using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TimeLogger.Models;

namespace TimeLogger.Services;

/// <summary>
/// Reads a "Working Hours Log" .xlsx timesheet:
/// header values (Name, Standard hours per day, Hourly rate) above a table with
/// Date | Day | Start | End | Break (min) | Hours worked | Overtime | Project / Task | Notes,
/// ending at the TOTAL row. Columns are found by their header text, so their order can change.
/// </summary>
public static partial class TimesheetImporter
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public record Result(TimesheetSettings? Settings, List<TimeEntryInput> Rows, List<string> Warnings);

    public static Result Read(Stream xlsx)
    {
        using var zip = new ZipArchive(xlsx, ZipArchiveMode.Read);
        var cells = ReadFirstSheet(zip);
        var warnings = new List<string>();

        // Find the header row: the one with both "Date" and "Start" in it.
        int headerRow = cells.Keys.Order().FirstOrDefault(r =>
            cells[r].Values.Any(v => Is(v, "date")) && cells[r].Values.Any(v => Is(v, "start")));
        if (headerRow == 0)
            throw new InvalidDataException("Couldn't find the table header row (a row with \"Date\" and \"Start\").");

        var header = cells[headerRow];
        string? Col(Func<string, bool> match) => header.FirstOrDefault(kv => match(kv.Value.Trim().ToLowerInvariant())).Key;
        var dateCol = Col(h => h == "date");
        var startCol = Col(h => h == "start");
        var endCol = Col(h => h == "end");
        var breakCol = Col(h => h.StartsWith("break"));
        var hoursCol = Col(h => h.StartsWith("hours"));
        var taskCol = Col(h => h.Contains("project") || h.Contains("task"));
        var notesCol = Col(h => h.StartsWith("note"));

        var settings = ReadSettings(cells, headerRow);

        var rows = new List<TimeEntryInput>();
        foreach (var r in cells.Keys.Where(r => r > headerRow).Order())
        {
            var row = cells[r];
            string Get(string? col) => col is not null && row.TryGetValue(col, out var v) ? v.Trim() : "";

            if (row.Values.Any(v => Is(v, "total"))) break;
            var dateText = Get(dateCol);
            if (dateText == "") continue;

            if (!TryParseDate(dateText, out var date))
            {
                warnings.Add($"Row {r}: couldn't read date \"{dateText}\", skipped.");
                continue;
            }

            TimeOnly? start = TryParseTime(Get(startCol));
            TimeOnly? end = TryParseTime(Get(endCol));
            int.TryParse(Get(breakCol), NumberStyles.Any, CultureInfo.InvariantCulture, out var breakMin);

            int? minutes = null;
            if (start is null || end is null)
            {
                if (double.TryParse(Get(hoursCol), NumberStyles.Any, CultureInfo.InvariantCulture, out var hours))
                    minutes = (int)Math.Round(hours * 60);
                start = end = null;
            }

            var task = Get(taskCol);
            if (task == "")
            {
                task = "(no task)";
                warnings.Add($"Row {r}: no Project / Task, saved as \"(no task)\".");
            }

            var input = new TimeEntryInput(date, start, end, breakMin, task, Get(notesCol), minutes);
            if (TimeEntryStore.MinutesFor(input) <= 0)
            {
                warnings.Add($"Row {r}: no hours worked, skipped.");
                continue;
            }
            rows.Add(input);
        }

        return new Result(settings, rows, warnings);
    }

    /// <summary>Label/value pairs above the table, e.g. "Name | Jane Doe".</summary>
    private static TimesheetSettings? ReadSettings(Dictionary<int, Dictionary<string, string>> cells, int headerRow)
    {
        var settings = new TimesheetSettings();
        var found = false;
        foreach (var r in cells.Keys.Where(r => r < headerRow))
        {
            var ordered = cells[r].OrderBy(kv => ColumnIndex(kv.Key)).Select(kv => kv.Value.Trim()).ToList();
            if (ordered.Count < 2) continue;
            var label = ordered[0].ToLowerInvariant();
            var value = ordered[1];

            if (label == "name") { settings.Name = value; found = true; }
            else if (label.StartsWith("standard hours") && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var std))
            { settings.StandardHoursPerDay = std; found = true; }
            else if (label.StartsWith("hourly rate") && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
            { settings.HourlyRate = rate; found = true; }
        }
        return found ? settings : null;
    }

    // ---------- Cell parsing ----------

    private static bool Is(string value, string word) => value.Trim().Equals(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>Excel stores dates as day numbers (1 = 1 Jan 1900); also accept typed text like 2026-08-23.</summary>
    private static bool TryParseDate(string text, out DateOnly date)
    {
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial) && serial > 0)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }
        return DateOnly.TryParse(text, CultureInfo.InvariantCulture, out date)
            || DateOnly.TryParse(text, CultureInfo.CurrentCulture, out date);
    }

    /// <summary>Excel stores times as a fraction of a day (0.75 = 18:00); rounded to the minute.</summary>
    private static TimeOnly? TryParseTime(string text)
    {
        if (text == "") return null;
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var fraction))
        {
            var minutes = (int)Math.Round((fraction % 1) * 24 * 60) % (24 * 60);
            return new TimeOnly(minutes / 60, minutes % 60);
        }
        return TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out var t) ? t : null;
    }

    // ---------- Minimal .xlsx reading (it's a zip of XML files) ----------

    /// <summary>Returns row number → column letter → cell text (shared strings resolved).</summary>
    private static Dictionary<int, Dictionary<string, string>> ReadFirstSheet(ZipArchive zip)
    {
        var shared = new List<string>();
        var sst = Load(zip, "xl/sharedStrings.xml");
        if (sst is not null)
            shared.AddRange(sst.Root!.Elements(Ns + "si").Select(si => string.Concat(si.Descendants(Ns + "t").Select(t => t.Value))));

        var sheet = Load(zip, FirstSheetPath(zip))
            ?? throw new InvalidDataException("The workbook has no worksheet.");

        var result = new Dictionary<int, Dictionary<string, string>>();
        foreach (var c in sheet.Descendants(Ns + "c"))
        {
            var match = CellRef().Match((string?)c.Attribute("r") ?? "");
            if (!match.Success) continue;

            var type = (string?)c.Attribute("t");
            string? value = type == "inlineStr"
                ? string.Concat(c.Descendants(Ns + "t").Select(t => t.Value))
                : c.Element(Ns + "v")?.Value;
            if (type == "s" && int.TryParse(value, out var idx) && idx < shared.Count) value = shared[idx];
            if (string.IsNullOrWhiteSpace(value)) continue;

            var row = int.Parse(match.Groups[2].Value);
            if (!result.TryGetValue(row, out var cols)) result[row] = cols = [];
            cols[match.Groups[1].Value] = value;
        }
        return result;
    }

    private static string FirstSheetPath(ZipArchive zip)
    {
        var workbook = Load(zip, "xl/workbook.xml");
        var rels = Load(zip, "xl/_rels/workbook.xml.rels");
        var relId = (string?)workbook?.Descendants(Ns + "sheet").FirstOrDefault()?.Attribute(RelNs + "id");
        var target = (string?)rels?.Descendants(PkgRelNs + "Relationship")
            .FirstOrDefault(r => (string?)r.Attribute("Id") == relId)?.Attribute("Target");
        if (target is null) return "xl/worksheets/sheet1.xml";
        return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
    }

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null) return null;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static int ColumnIndex(string letters) => letters.Aggregate(0, (n, ch) => n * 26 + (ch - 'A' + 1));

    [GeneratedRegex(@"^([A-Z]+)(\d+)$")]
    private static partial Regex CellRef();
}
