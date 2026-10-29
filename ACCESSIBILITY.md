# Accessibility

We want Working Hours Log to be usable by everyone, including people who use a keyboard only, screen readers, zoom or high-contrast settings.

## Our commitment

- New features should work with a keyboard as well as a mouse or touch.
- Every input has a visible label, and actions use real buttons and links.
- The layout adapts from phone width (about 375 px) to desktop, and supports browser zoom.
- The page follows the system's light or dark mode.
- We aim for [WCAG 2.2](https://www.w3.org/TR/WCAG22/) level AA, and treat accessibility problems as bugs.

## What's in place today

- Native HTML form controls (date, time, number, checkbox, select) with labels.
- Dialogs use the native `<dialog>` element, so focus stays inside them and **Esc** closes them.
- Sortable table headers are buttons and announce their state with `aria-sort`.
- Row checkboxes have descriptive labels (task and date), and "Select all" shows a mixed state when only some rows are selected.
- Buttons that aren't available, for example **Edit** with more than one row selected, are disabled and explain why in a tooltip.
- Inputs use a 16 px font so phones don't zoom unexpectedly, and focus outlines are visible.
- Deleting all records requires typing a confirmation phrase, which helps prevent accidental data loss.

## Supported environments

Recent versions of Microsoft Edge, Google Chrome, Mozilla Firefox and Safari, on Windows, macOS, Android and iOS. Tested with keyboard navigation and at desktop and phone widths.

## Known limitations

- **Not formally audited.** The app hasn't been through a full WCAG audit or tested with every screen reader.
- **Exported PDFs are not tagged.** Screen readers may read the PDF's table and summary out of order. Use the CSV export for an accessible, structured copy of the same data.
- **Some confirmations use the browser's built-in `confirm()` box,** which looks different in each browser and can't be styled.
- **Status messages** (such as "Imported 10 entries") appear in a banner but aren't yet announced automatically by screen readers.
- **Colour is not the only signal** for overtime or selection, but some muted text may be below AA contrast in dark mode.

## Reporting a barrier

If something stops you from using the app, please [open an issue](https://github.com/mkzaimhussian/WorkingHoursLog/issues/new/choose) and choose **Accessibility barrier**. Tell us:

- What you were trying to do
- What happened instead
- Your browser, device and any assistive technology (for example NVDA, JAWS, VoiceOver, TalkBack or zoom level)

We'll respond as soon as we can, and accessibility fixes are prioritised.
