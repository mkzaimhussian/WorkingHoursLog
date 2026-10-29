# Working Hours Log

A responsive ASP.NET Core (.NET 9) web app for logging working hours and producing formal timesheets.

> **Built with [Claude Code](https://claude.com/claude-code).** The code in this repository was written using Claude Code, Anthropic's AI coding assistant, working from requirements by Muhammad Kazim.

## Features

- Log time with date, start/end, break, project/task and notes (hours are calculated like the Excel timesheet)
- Start/stop timer that fills in the date and times
- Import an Excel "Working Hours Log" timesheet (`.xlsx`); re-importing skips duplicates
- Filter by period, sort by any column, select several records to delete
- Summary: total hours, overtime (per day over the standard hours), days worked, average per day, amount due
- Export a formal **PDF** (logo, from / bill to, entries, totals, tax, payment details, signature lines) or a **CSV** with a summary block
- Share the export via the device share menu, WhatsApp or email
- Settings for name, logo, client, hourly rate, currency, tax and payment details
- Delete all records only after typing `DELETE ALL` (a backup is saved first)

## Run it

Open `TimeLogger.sln` in Visual Studio 2022 and press **F5**, or from a terminal:

```
dotnet run --project TimeLogger
```

Then open http://localhost:5028.

## Data

Entries, settings and the logo are stored locally in `TimeLogger/App_Data/`, which is excluded from Git so personal data stays off GitHub.

## Contributing

Bug reports, ideas and pull requests are welcome. Please read the [Contributing guide](CONTRIBUTING.md) and follow the [Code of Conduct](CODE_OF_CONDUCT.md).

- Found a bug or have an idea? [Open an issue](https://github.com/mkzaimhussian/WorkingHoursLog/issues/new/choose).
- Found a security problem? Please report it privately; see the [Security Policy](SECURITY.md).
- Hit an accessibility barrier? See the [Accessibility statement](ACCESSIBILITY.md).

## License

This project is licensed under the [MIT License](LICENSE). © 2026 Muhammad Kazim.

PDF export uses [QuestPDF](https://www.questpdf.com/) under its Community licence (free for individuals and businesses under USD 1M annual revenue).
