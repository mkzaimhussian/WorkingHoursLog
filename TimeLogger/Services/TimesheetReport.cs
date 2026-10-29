using System.Globalization;
using TimeLogger.Models;

namespace TimeLogger.Services;

/// <summary>
/// Everything an export needs: the rows for a period plus the totals the spreadsheet's
/// summary block has (hours, overtime, days, average) and the amount to be paid.
/// Shared by the CSV and PDF exports so both always show the same figures.
/// </summary>
public class TimesheetReport
{
    public required TimesheetSettings Settings { get; init; }
    public required List<Row> Rows { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    public record Row(TimeEntry Entry, int OvertimeMinutes);

    public int TotalMinutes => Rows.Sum(r => r.Entry.Minutes);
    public int OvertimeMinutes => Rows.Sum(r => r.OvertimeMinutes);
    public int DaysWorked => Rows.Select(r => r.Entry.Date).Distinct().Count();
    public decimal TotalHours => Math.Round(TotalMinutes / 60m, 2);
    public decimal OvertimeHours => Math.Round(OvertimeMinutes / 60m, 2);
    public decimal AverageHoursPerDay => DaysWorked == 0 ? 0 : Math.Round(TotalMinutes / 60m / DaysWorked, 2);

    public bool HasRate => Settings.HourlyRate is > 0;
    public decimal Subtotal => HasRate ? Math.Round(TotalMinutes / 60m * Settings.HourlyRate!.Value, 2) : 0;
    public bool HasTax => HasRate && Settings.TaxPercent is > 0;
    public decimal TaxAmount => HasTax ? Math.Round(Subtotal * Settings.TaxPercent!.Value / 100m, 2) : 0;
    public decimal TotalDue => Subtotal + TaxAmount;

    /// <summary>The period shown on the document: the filter if one was set, else the first/last entry.</summary>
    public DateOnly? PeriodStart => From ?? Rows.FirstOrDefault()?.Entry.Date;
    public DateOnly? PeriodEnd => To ?? Rows.LastOrDefault()?.Entry.Date;

    /// <summary>e.g. TS-20260823-20260929 — stable for the same period, so re-exports match.</summary>
    public string Reference => PeriodStart is { } s && PeriodEnd is { } e ? $"TS-{s:yyyyMMdd}-{e:yyyyMMdd}" : "TS";

    public string Money(decimal amount)
    {
        var value = amount.ToString("#,##0.00", CultureInfo.InvariantCulture);
        return Settings.Currency == "" ? value : $"{Settings.Currency} {value}";
    }

    /// <summary>File name like "Jane-Doe-timesheet-2026-08-01_to_2026-08-31".</summary>
    public string FileBaseName()
    {
        var who = string.Join("-", (Settings.Name == "" ? "timesheet" : Settings.Name)
            .Split(Path.GetInvalidFileNameChars().Append(' ').ToArray(), StringSplitOptions.RemoveEmptyEntries));
        return PeriodStart is { } s && PeriodEnd is { } e
            ? $"{who}-timesheet-{s:yyyy-MM-dd}_to_{e:yyyy-MM-dd}"
            : $"{who}-timesheet";
    }

    public static TimesheetReport Build(TimeEntryStore store, SettingsStore settingsStore, DateOnly? from, DateOnly? to)
    {
        var settings = settingsStore.Get();
        var entries = store.GetAll(from, to);
        var standardMinutes = (int)(settings.StandardHoursPerDay * 60);

        // Overtime is per day (minutes over the standard day), shown on that day's last row.
        var lastOfDay = entries.GroupBy(e => e.Date).ToDictionary(g => g.Key, g => g.Last().Id);
        var dayMinutes = entries.GroupBy(e => e.Date).ToDictionary(g => g.Key, g => g.Sum(e => e.Minutes));

        var rows = entries
            .Select(e => new Row(e, lastOfDay[e.Date] == e.Id ? Math.Max(0, dayMinutes[e.Date] - standardMinutes) : 0))
            .ToList();

        return new TimesheetReport { Settings = settings, Rows = rows, From = from, To = to };
    }
}
