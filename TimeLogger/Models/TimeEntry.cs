namespace TimeLogger.Models;

/// <summary>One row of the timesheet: a block of time spent on a project or task.</summary>
public class TimeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
    public int BreakMinutes { get; set; }
    public string Task { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>Minutes worked. Calculated from Start/End/Break when both times are set.</summary>
    public int Minutes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Same rule as the Excel timesheet: (End - Start) wrapping past midnight, minus the break.
    /// </summary>
    public static int CalculateMinutes(TimeOnly start, TimeOnly end, int breakMinutes)
    {
        var span = (int)Math.Round((end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes);
        if (span < 0) span += 24 * 60;
        return Math.Max(0, span - breakMinutes);
    }
}

/// <summary>What the browser (or the Excel import) sends when creating or editing an entry.</summary>
public record TimeEntryInput(
    DateOnly Date,
    TimeOnly? Start,
    TimeOnly? End,
    int BreakMinutes,
    string Task,
    string? Notes,
    int? Minutes);

/// <summary>Body for "delete selected": the ids of the ticked records.</summary>
public record DeleteManyRequest(List<Guid>? Ids);

/// <summary>Body for "delete all records": must contain the typed confirmation phrase.</summary>
public record DeleteAllRequest(string? Confirmation);

/// <summary>
/// The timesheet header (name, standard hours, rate) plus the details printed on the
/// formal PDF: who it's from, who it's billed to, currency, tax and payment details.
/// </summary>
public class TimesheetSettings
{
    // From the Excel sheet header
    public string Name { get; set; } = "";
    public decimal StandardHoursPerDay { get; set; } = 8;
    public decimal? HourlyRate { get; set; }

    // You / your business
    public string JobTitle { get; set; } = "";
    public string Company { get; set; } = "";
    public string Address { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";

    // Client (bill to)
    public string ClientName { get; set; } = "";
    public string ClientCompany { get; set; } = "";
    public string ClientAddress { get; set; } = "";
    public string ClientEmail { get; set; } = "";

    // Money
    public string Currency { get; set; } = "USD";
    public decimal? TaxPercent { get; set; }
    public string TaxLabel { get; set; } = "VAT";

    // Document
    public string DocumentTitle { get; set; } = "Timesheet";
    public string PaymentDetails { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>Set by the server: whether a logo has been uploaded (App_Data/logo.*).</summary>
    public bool HasLogo { get; set; }
}
