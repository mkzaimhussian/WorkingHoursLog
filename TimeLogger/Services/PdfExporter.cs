using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace TimeLogger.Services;

/// <summary>
/// Formal A4 timesheet PDF: logo and title, From / Bill to, the entries table,
/// a summary with total hours and the amount to be paid, payment details and signature lines.
/// </summary>
public static class PdfExporter
{
    private const string Accent = "#1E3A8A";
    private const string AccentLight = "#EEF2FB";
    private const string Muted = "#6B7280";
    private const string Rule = "#D9DEE7";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static byte[] Generate(TimesheetReport report, byte[]? logo)
    {
        var s = report.Settings;
        var showOvertime = report.OvertimeMinutes > 0;

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(36);
            page.MarginVertical(32);
            page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor("#1F2937"));

            page.Header().Element(c => Header(c, report, logo));

            page.Content().PaddingTop(14).Column(col =>
            {
                col.Spacing(14);
                col.Item().Element(c => Parties(c, report));
                col.Item().Element(c => EntriesTable(c, report, showOvertime));
                col.Item().ShowEntire().Element(c => SummaryAndPayment(c, report));
            });

            page.Footer().PaddingTop(8).BorderTop(0.5f).BorderColor(Rule).PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                    t.Span($"{s.DocumentTitle} {report.Reference}");
                    if (s.Name != "") t.Span($" · {s.Name}");
                    t.Span($" · Generated {DateTime.Now.ToString("dd MMM yyyy", Inv)}");
                });
                row.AutoItem().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        })).GeneratePdf();
    }

    private static void Header(IContainer container, TimesheetReport report, byte[]? logo)
    {
        var s = report.Settings;
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                if (logo is not null)
                {
                    row.ConstantItem(150).Height(60).AlignLeft().AlignMiddle().Image(logo).FitArea();
                }
                else
                {
                    row.RelativeItem().AlignMiddle().Column(c =>
                    {
                        c.Item().Text(s.Company != "" ? s.Company : s.Name).FontSize(16).Bold().FontColor(Accent);
                        if (s.Company != "" && s.Name != "") c.Item().Text(s.Name).FontColor(Muted);
                    });
                }

                row.RelativeItem().AlignRight().AlignMiddle().Column(c =>
                {
                    c.Item().AlignRight().Text(s.DocumentTitle.ToUpperInvariant()).FontSize(20).Bold().FontColor(Accent).LetterSpacing(0.05f);
                    c.Item().AlignRight().Text(t => { t.Span("Reference  ").FontColor(Muted); t.Span(report.Reference).SemiBold(); });
                    c.Item().AlignRight().Text(t => { t.Span("Issued  ").FontColor(Muted); t.Span(DateTime.Today.ToString("dd MMM yyyy", Inv)); });
                    if (report.PeriodStart is { } from && report.PeriodEnd is { } to)
                    {
                        c.Item().AlignRight().Text(t =>
                        {
                            t.Span("Period  ").FontColor(Muted);
                            t.Span($"{from.ToString("dd MMM yyyy", Inv)} – {to.ToString("dd MMM yyyy", Inv)}");
                        });
                    }
                });
            });
            col.Item().PaddingTop(10).LineHorizontal(2).LineColor(Accent);
        });
    }

    private static void Parties(IContainer container, TimesheetReport report)
    {
        var s = report.Settings;
        container.Row(row =>
        {
            row.Spacing(24);
            row.RelativeItem().Element(c => Party(c, "FROM",
                s.Name, Join(s.JobTitle, s.Company), s.Address, s.Email, s.Phone));
            row.RelativeItem().Element(c => Party(c, "BILL TO",
                s.ClientName, s.ClientCompany, s.ClientAddress, s.ClientEmail));
        });
    }

    private static void Party(IContainer container, string label, string title, params string[] lines)
    {
        container.Background(AccentLight).Padding(10).Column(c =>
        {
            c.Item().Text(label).FontSize(7.5f).SemiBold().FontColor(Accent).LetterSpacing(0.1f);
            if (title != "") c.Item().PaddingTop(3).Text(title).Bold().FontSize(10.5f);
            foreach (var line in lines.Where(l => l != ""))
                c.Item().Text(line).FontColor("#374151");
            if (title == "" && lines.All(l => l == ""))
                c.Item().PaddingTop(3).Text("—").FontColor(Muted);
        });
    }

    private static void EntriesTable(IContainer container, TimesheetReport report, bool showOvertime)
    {
        if (report.Rows.Count == 0)
        {
            container.Padding(20).AlignCenter().Text("No time logged for this period.").FontColor(Muted);
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(68);  // Date
                c.ConstantColumn(30);  // Day
                c.ConstantColumn(38);  // Start
                c.ConstantColumn(38);  // End
                c.ConstantColumn(34);  // Break
                c.ConstantColumn(38);  // Hours
                if (showOvertime) c.ConstantColumn(42);
                c.RelativeColumn(3);   // Project / Task
                c.RelativeColumn(3);   // Notes
            });

            table.Header(h =>
            {
                void Head(string text, bool right = false)
                {
                    var cell = h.Cell().Background(Accent).PaddingVertical(5).PaddingHorizontal(4);
                    (right ? cell.AlignRight() : cell).Text(text).FontSize(8).SemiBold().FontColor(Colors.White);
                }
                Head("Date"); Head("Day"); Head("Start"); Head("End"); Head("Break", true); Head("Hours", true);
                if (showOvertime) Head("Overtime", true);
                Head("Project / Task"); Head("Notes");
            });

            var i = 0;
            foreach (var row in report.Rows)
            {
                var e = row.Entry;
                var bg = i++ % 2 == 1 ? "#F7F9FC" : "#FFFFFF";
                IContainer Cell() => table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(4).PaddingHorizontal(4);

                Cell().Text(e.Date.ToString("dd MMM yyyy", Inv));
                Cell().Text(e.Date.ToString("ddd", Inv));
                Cell().Text(e.Start?.ToString("HH:mm", Inv) ?? "—");
                Cell().Text(e.End?.ToString("HH:mm", Inv) ?? "—");
                Cell().AlignRight().Text(e.BreakMinutes.ToString(Inv));
                Cell().AlignRight().Text((e.Minutes / 60m).ToString("0.00", Inv)).SemiBold();
                if (showOvertime)
                    Cell().AlignRight().Text(row.OvertimeMinutes > 0 ? (row.OvertimeMinutes / 60m).ToString("0.00", Inv) : "");
                Cell().Text(e.Task).SemiBold();
                Cell().Text(e.Notes).FontSize(8.5f).FontColor("#4B5563");
            }

            // Totals row under the Hours (and Overtime) column.
            table.Cell().ColumnSpan(5).BorderTop(1).BorderColor(Accent).PaddingVertical(5).PaddingHorizontal(4)
                .AlignRight().Text("Total hours").Bold();
            table.Cell().BorderTop(1).BorderColor(Accent).PaddingVertical(5).PaddingHorizontal(4)
                .AlignRight().Text(report.TotalHours.ToString("0.00", Inv)).Bold();
            if (showOvertime)
                table.Cell().BorderTop(1).BorderColor(Accent).PaddingVertical(5).PaddingHorizontal(4)
                    .AlignRight().Text(report.OvertimeHours.ToString("0.00", Inv)).Bold();
            table.Cell().ColumnSpan(2).BorderTop(1).BorderColor(Accent);
        });
    }

    private static void SummaryAndPayment(IContainer container, TimesheetReport report)
    {
        var s = report.Settings;
        container.Row(row =>
        {
            row.Spacing(24);

            row.RelativeItem().Column(c =>
            {
                c.Spacing(10);
                if (s.PaymentDetails != "")
                {
                    c.Item().Column(p =>
                    {
                        p.Item().Text("PAYMENT DETAILS").FontSize(7.5f).SemiBold().FontColor(Accent).LetterSpacing(0.1f);
                        p.Item().PaddingTop(3).Text(s.PaymentDetails);
                    });
                }
                if (s.Notes != "")
                {
                    c.Item().Column(p =>
                    {
                        p.Item().Text("NOTES").FontSize(7.5f).SemiBold().FontColor(Accent).LetterSpacing(0.1f);
                        p.Item().PaddingTop(3).Text(s.Notes).FontColor("#374151");
                    });
                }

                // Signature lines sit beside the summary so they stay on the same page.
                c.Item().PaddingTop(4).Element(e => Signature(e, "Submitted by", s.Name));
                c.Item().Element(e => Signature(e, "Approved by", s.ClientName));
            });

            row.ConstantItem(230).Border(0.75f).BorderColor(Rule).Column(c =>
            {
                void Line(string label, string value, bool strong = false)
                {
                    c.Item().PaddingHorizontal(10).PaddingVertical(4).Row(r =>
                    {
                        r.RelativeItem().Text(label).FontColor(Muted);
                        var v = r.AutoItem().Text(value);
                        if (strong) v.Bold();
                    });
                }

                c.Item().Background(AccentLight).PaddingHorizontal(10).PaddingVertical(6)
                    .Text("SUMMARY").FontSize(7.5f).SemiBold().FontColor(Accent).LetterSpacing(0.1f);
                Line("Days worked", report.DaysWorked.ToString(Inv));
                Line("Total hours worked", report.TotalHours.ToString("0.00", Inv), strong: true);
                Line("Overtime hours", report.OvertimeHours.ToString("0.00", Inv));
                Line("Average hours per day", report.AverageHoursPerDay.ToString("0.00", Inv));

                if (report.HasRate)
                {
                    c.Item().PaddingHorizontal(10).PaddingVertical(2).LineHorizontal(0.5f).LineColor(Rule);
                    Line("Hourly rate", report.Money(s.HourlyRate!.Value));
                    Line($"Subtotal ({report.TotalHours.ToString("0.00", Inv)} h × {s.HourlyRate!.Value.ToString("0.##", Inv)})", report.Money(report.Subtotal));
                    if (report.HasTax)
                        Line($"{s.TaxLabel} ({s.TaxPercent!.Value.ToString("0.##", Inv)}%)", report.Money(report.TaxAmount));

                    c.Item().Background(Accent).PaddingHorizontal(10).PaddingVertical(8).Row(r =>
                    {
                        r.RelativeItem().Text("TOTAL AMOUNT DUE").FontColor(Colors.White).Bold().FontSize(9);
                        r.AutoItem().Text(report.Money(report.TotalDue)).FontColor(Colors.White).Bold().FontSize(11.5f);
                    });
                }
            });
        });
    }

    private static void Signature(IContainer container, string label, string name)
    {
        container.Column(c =>
        {
            c.Item().Height(26);
            c.Item().LineHorizontal(0.75f).LineColor("#9CA3AF");
            c.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem().Text(t =>
                {
                    t.Span($"{label}: ").FontColor(Muted);
                    t.Span(name);
                });
                r.AutoItem().Text("Date: ____________").FontColor(Muted);
            });
        });
    }

    private static string Join(params string[] parts) => string.Join(", ", parts.Where(p => p != ""));
}
