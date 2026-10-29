# Contributing to Working Hours Log

Thanks for your interest in improving Working Hours Log! Bug reports, ideas and pull requests are all welcome.
By taking part you agree to follow our [Code of Conduct](CODE_OF_CONDUCT.md).

## Reporting a bug or asking for a feature

1. Search the [existing issues](https://github.com/mkzaimhussian/WorkingHoursLog/issues) first; someone may have reported it already.
2. Open a new issue and choose **Bug report** or **Feature request**.
3. For bugs, include the steps to reproduce, what you expected and what happened, and your browser/OS.

**Never include real timesheet data, client names, rates or payment details** in an issue. Replace them with made-up examples.

Security problems should **not** be reported in a public issue. See the [Security Policy](SECURITY.md).

## Setting up the project

Requirements: the [.NET 9 SDK](https://dotnet.microsoft.com/download) and, optionally, Visual Studio 2022.

```
git clone https://github.com/mkzaimhussian/WorkingHoursLog.git
cd WorkingHoursLog
dotnet run --project TimeLogger
```

Then open http://localhost:5028. In Visual Studio, open `TimeLogger.sln` and press **F5**.

Your local data is saved in `TimeLogger/App_Data/`, which Git ignores. Please keep it that way.

## Project layout

| Path | What it does |
|---|---|
| `TimeLogger/Program.cs` | Web server setup and all API endpoints |
| `TimeLogger/Models/` | Time entry, settings and request types |
| `TimeLogger/Services/TimeEntryStore.cs` | Saves entries to a JSON file |
| `TimeLogger/Services/SettingsStore.cs` | Settings and logo storage |
| `TimeLogger/Services/TimesheetImporter.cs` | Reads Excel `.xlsx` timesheets |
| `TimeLogger/Services/TimesheetReport.cs` | Totals shared by all exports |
| `TimeLogger/Services/PdfExporter.cs` | The PDF layout (QuestPDF) |
| `TimeLogger/wwwroot/` | The web page: `index.html`, `css/site.css`, `js/app.js` |

## Making a change

1. Fork the repository and create a branch, e.g. `fix/overtime-rounding` or `feature/weekly-view`.
2. Keep changes focused: one fix or feature per pull request.
3. Match the existing style: plain HTML/CSS/JavaScript on the front end (no frameworks), and small, readable C# with short comments where the *why* isn't obvious.
4. Keep the page responsive. Check it at phone width (about 375 px) and on desktop, in light and dark mode.
5. Keep it accessible: labels on inputs, buttons for actions, keyboard-usable dialogs. See [ACCESSIBILITY.md](ACCESSIBILITY.md).
6. Make sure `dotnet build` passes with no warnings, and try the feature in the browser.
7. Open a pull request and fill in the template.

## Commit messages

Use a short summary line in the imperative ("Add weekly totals", "Fix PDF column width"), and add more detail below it if needed.

## Licence

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
