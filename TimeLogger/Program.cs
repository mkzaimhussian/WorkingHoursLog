using System.Globalization;
using System.Text;
using TimeLogger.Models;
using TimeLogger.Services;

// QuestPDF (PDF export) is free under its Community license for individuals and
// businesses under USD 1M annual revenue: https://www.questpdf.com/license/
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TimeEntryStore>();
builder.Services.AddSingleton<SettingsStore>();

var app = builder.Build();

// Serve the front end from wwwroot (index.html, css, js).
app.UseDefaultFiles();
app.UseStaticFiles();

// ---------- Timesheet entries ----------

var api = app.MapGroup("/api/entries");

api.MapGet("/", (DateOnly? from, DateOnly? to, TimeEntryStore store) => store.GetAll(from, to));

api.MapPost("/", (TimeEntryInput input, TimeEntryStore store) =>
{
    var errors = Validate(input);
    if (errors is not null) return Results.ValidationProblem(errors);
    var entry = store.Add(input);
    return Results.Created($"/api/entries/{entry.Id}", entry);
});

api.MapPut("/{id:guid}", (Guid id, TimeEntryInput input, TimeEntryStore store) =>
{
    var errors = Validate(input);
    if (errors is not null) return Results.ValidationProblem(errors);
    var entry = store.Update(id, input);
    return entry is null ? Results.NotFound() : Results.Ok(entry);
});

api.MapDelete("/{id:guid}", (Guid id, TimeEntryStore store) =>
    store.Delete(id) ? Results.NoContent() : Results.NotFound());

api.MapPost("/delete-many", (DeleteManyRequest request, TimeEntryStore store) =>
{
    if (request.Ids is not { Count: > 0 })
        return Results.BadRequest(new { error = "No records selected." });
    return Results.Ok(new { deleted = store.DeleteMany(request.Ids) });
});

// Deleting everything needs the typed confirmation phrase, so it can't happen by a stray request.
const string DeleteAllPhrase = "DELETE ALL";
api.MapPost("/delete-all", (DeleteAllRequest request, TimeEntryStore store) =>
{
    if (request.Confirmation?.Trim() != DeleteAllPhrase)
        return Results.BadRequest(new { error = $"Type {DeleteAllPhrase} to confirm." });
    var (deleted, backupFile) = store.DeleteAll();
    return Results.Ok(new { deleted, backupFile });
});

// Export the period as a formal PDF (?format=pdf) or a CSV with the same columns as the
// Excel timesheet plus a summary block (?format=csv, the default).
api.MapGet("/export", (DateOnly? from, DateOnly? to, string? format, TimeEntryStore store, SettingsStore settingsStore) =>
{
    var report = TimesheetReport.Build(store, settingsStore, from, to);

    if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
    {
        var logoPath = settingsStore.LogoPath();
        var pdf = PdfExporter.Generate(report, logoPath is null ? null : File.ReadAllBytes(logoPath));
        return Results.File(pdf, "application/pdf", report.FileBaseName() + ".pdf");
    }

    return Results.File(BuildCsv(report), "text/csv", report.FileBaseName() + ".csv");
});

// ---------- Import from an Excel timesheet ----------

app.MapPost("/api/import", async (IFormFile file, TimeEntryStore store, SettingsStore settingsStore) =>
{
    if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Please choose an Excel .xlsx file." });

    TimesheetImporter.Result result;
    try
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        buffer.Position = 0;
        result = TimesheetImporter.Read(buffer);
    }
    catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
    {
        return Results.BadRequest(new { error = $"Couldn't read that spreadsheet: {ex.Message}" });
    }

    var (added, skipped) = store.AddRange(result.Rows);
    if (result.Settings is not null) settingsStore.ApplyFromSheet(result.Settings);

    return Results.Ok(new { added, skipped, settingsUpdated = result.Settings is not null, warnings = result.Warnings });
}).DisableAntiforgery(); // local single-user app; the form is posted by our own page

// ---------- Settings (timesheet header) ----------

app.MapGet("/api/settings", (SettingsStore store) => store.Get());

app.MapPut("/api/settings", (TimesheetSettings settings, SettingsStore store) =>
{
    var errors = new Dictionary<string, string[]>();
    if (settings.StandardHoursPerDay is <= 0 or > 24)
        errors["standardHoursPerDay"] = ["Standard hours per day must be between 0 and 24."];
    if (settings.HourlyRate is < 0) errors["hourlyRate"] = ["Hourly rate can't be negative."];
    if (settings.TaxPercent is < 0 or > 100) errors["taxPercent"] = ["Tax must be between 0 and 100%."];
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    return Results.Ok(store.Save(settings));
});

// Logo shown at the top of the PDF. PNG or JPG, up to 2 MB.
app.MapGet("/api/settings/logo", (SettingsStore store) =>
{
    var path = store.LogoPath();
    return path is null
        ? Results.NotFound()
        : Results.File(File.ReadAllBytes(path), path.EndsWith(".png") ? "image/png" : "image/jpeg");
});

app.MapPost("/api/settings/logo", async (IFormFile file, SettingsStore store) =>
{
    if (file.Length > 2 * 1024 * 1024) return Results.BadRequest(new { error = "The logo must be 2 MB or smaller." });

    using var buffer = new MemoryStream();
    await file.CopyToAsync(buffer);
    var bytes = buffer.ToArray();

    // Check the file's actual contents, not just its name.
    string? ext = bytes switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => ".png",
        [0xFF, 0xD8, 0xFF, ..] => ".jpg",
        _ => null,
    };
    if (ext is null) return Results.BadRequest(new { error = "Please choose a PNG or JPG image." });

    store.SaveLogo(bytes, ext);
    return Results.Ok(store.Get());
}).DisableAntiforgery(); // local single-user app; the form is posted by our own page

app.MapDelete("/api/settings/logo", (SettingsStore store) =>
{
    store.DeleteLogo();
    return Results.Ok(store.Get());
});

app.Run();

// CSV: the timesheet columns, then a summary block with the totals and the amount to be paid.
static byte[] BuildCsv(TimesheetReport report)
{
    var inv = CultureInfo.InvariantCulture;
    var s = report.Settings;
    var csv = new StringBuilder();

    csv.AppendLine(Csv(s.DocumentTitle));
    if (s.Name != "") csv.AppendLine($"Name,{Csv(s.Name)}");
    if (s.Company != "") csv.AppendLine($"Company,{Csv(s.Company)}");
    if (s.ClientName != "" || s.ClientCompany != "")
        csv.AppendLine($"Bill to,{Csv(string.Join(", ", new[] { s.ClientName, s.ClientCompany }.Where(x => x != "")))}");
    csv.AppendLine($"Reference,{Csv(report.Reference)}");
    if (report.PeriodStart is { } from && report.PeriodEnd is { } to)
        csv.AppendLine($"Period,{from.ToString("yyyy-MM-dd", inv)} to {to.ToString("yyyy-MM-dd", inv)}");
    csv.AppendLine();

    csv.AppendLine("Date,Day,Start,End,Break (min),Hours worked,Overtime (hrs),Project / Task,Notes");
    foreach (var row in report.Rows)
    {
        var e = row.Entry;
        csv.AppendLine(string.Join(",",
            e.Date.ToString("yyyy-MM-dd", inv),
            e.Date.ToString("ddd", inv),
            e.Start?.ToString("HH:mm", inv) ?? "",
            e.End?.ToString("HH:mm", inv) ?? "",
            e.BreakMinutes,
            (e.Minutes / 60m).ToString("0.00", inv),
            (row.OvertimeMinutes / 60m).ToString("0.00", inv),
            Csv(e.Task),
            Csv(e.Notes)));
    }

    csv.AppendLine();
    csv.AppendLine("Summary");
    csv.AppendLine($"Days worked,{report.DaysWorked}");
    csv.AppendLine($"Total hours worked,{report.TotalHours.ToString("0.00", inv)}");
    csv.AppendLine($"Total overtime hours,{report.OvertimeHours.ToString("0.00", inv)}");
    csv.AppendLine($"Average hours per day,{report.AverageHoursPerDay.ToString("0.00", inv)}");
    if (report.HasRate)
    {
        var cur = s.Currency == "" ? "" : $" ({s.Currency})";
        csv.AppendLine($"Hourly rate{cur},{s.HourlyRate!.Value.ToString("0.00", inv)}");
        csv.AppendLine($"Subtotal{cur},{report.Subtotal.ToString("0.00", inv)}");
        if (report.HasTax)
            csv.AppendLine($"{Csv($"{s.TaxLabel} {s.TaxPercent!.Value.ToString("0.##", inv)}%{cur}")},{report.TaxAmount.ToString("0.00", inv)}");
        csv.AppendLine($"Total amount due{cur},{report.TotalDue.ToString("0.00", inv)}");
    }
    if (s.PaymentDetails != "") csv.AppendLine($"Payment details,{Csv(s.PaymentDetails)}");

    // UTF-8 with BOM so Excel shows characters like "–" correctly.
    return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
}

static Dictionary<string, string[]>? Validate(TimeEntryInput input)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(input.Task)) errors["task"] = ["Project / Task is required."];
    if (input.Start is null != input.End is null) errors["end"] = ["Enter both a start and an end time, or neither."];
    if (input.BreakMinutes < 0) errors["breakMinutes"] = ["Break can't be negative."];
    var minutes = TimeEntryStore.MinutesFor(input);
    if (minutes is <= 0 or > 24 * 60) errors["minutes"] = ["Hours worked must be between 1 minute and 24 hours."];
    return errors.Count > 0 ? errors : null;
}

static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
