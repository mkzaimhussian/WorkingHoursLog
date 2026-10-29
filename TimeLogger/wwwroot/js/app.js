// Working Hours Log front end: talks to the /api endpoints in Program.cs.

const $ = (id) => document.getElementById(id);
const TIMER_KEY = "timelogger.timerStart";

let entries = [];
let settings = { name: "", standardHoursPerDay: 8, hourlyRate: null };
let editingId = null;
const selected = new Set(); // ids of ticked rows
let timerInterval = null;

// ---------- Helpers ----------

const pad = (n) => String(n).padStart(2, "0");

function toIsoDate(d) {
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function toHm(d) {
    return `${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function presetRange(p) {
    const today = new Date();
    if (p === "month") return { from: toIsoDate(new Date(today.getFullYear(), today.getMonth(), 1)), to: "" };
    if (p === "lastMonth") {
        return {
            from: toIsoDate(new Date(today.getFullYear(), today.getMonth() - 1, 1)),
            to: toIsoDate(new Date(today.getFullYear(), today.getMonth(), 0)),
        };
    }
    return { from: "", to: "" };
}

// Accepts "1:30", "1.5", "1.5h", "90m", "2h 15m".
function parseDuration(text) {
    const s = text.trim().toLowerCase();
    if (!s) return null;
    let m = s.match(/^(\d+):(\d{1,2})$/);
    if (m) return Number(m[1]) * 60 + Number(m[2]);
    m = s.match(/^(?:(\d+(?:\.\d+)?)\s*h)?\s*(?:(\d+)\s*m)?$/);
    if (m && (m[1] || m[2])) return Math.round(Number(m[1] || 0) * 60 + Number(m[2] || 0));
    m = s.match(/^\d+(?:\.\d+)?$/);
    if (m) return Math.round(Number(s) * 60);
    return null;
}

// Same rule as the spreadsheet: end - start (wrapping past midnight) minus the break.
function minutesBetween(start, end, breakMin) {
    const [sh, sm] = start.split(":").map(Number);
    const [eh, em] = end.split(":").map(Number);
    let span = eh * 60 + em - (sh * 60 + sm);
    if (span < 0) span += 24 * 60;
    return Math.max(0, span - (breakMin || 0));
}

const hours = (min) => (min / 60).toFixed(2);

function formatDate(iso) {
    const [y, mo, d] = iso.split("-").map(Number);
    return new Date(y, mo - 1, d).toLocaleDateString(undefined, { day: "2-digit", month: "short", year: "numeric" });
}

function dayName(iso) {
    const [y, mo, d] = iso.split("-").map(Number);
    return new Date(y, mo - 1, d).toLocaleDateString(undefined, { weekday: "short" });
}

const hm = (t) => (t ? t.slice(0, 5) : "");

function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
}

function formatMoney(n) {
    return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/** Amount with the currency from settings, e.g. "AED 475.00". */
function money(n) {
    return settings.currency ? `${settings.currency} ${formatMoney(n)}` : formatMoney(n);
}

const round2 = (n) => Math.round(n * 100) / 100;

/** Subtotal, tax and total due for some minutes, rounded like the PDF/CSV export. Null when no rate is set. */
function amounts(totalMinutes) {
    if (!(settings.hourlyRate > 0)) return null;
    const subtotal = round2((totalMinutes / 60) * settings.hourlyRate);
    const tax = settings.taxPercent > 0 ? round2((subtotal * settings.taxPercent) / 100) : 0;
    return { subtotal, tax, due: round2(subtotal + tax) };
}

function showBanner(text, isError = false) {
    const b = $("banner");
    b.textContent = text;
    b.classList.toggle("is-error", isError);
    b.hidden = false;
    clearTimeout(showBanner.timer);
    showBanner.timer = setTimeout(() => (b.hidden = true), isError ? 12000 : 8000);
}

// ---------- API ----------

async function request(url, options = {}) {
    const res = await fetch(url, options);
    if (!res.ok) {
        let message = `Request failed (${res.status})`;
        try {
            const body = await res.json();
            if (body.errors) message = Object.values(body.errors).flat().join(" ");
            else if (body.error) message = body.error;
        } catch { /* no JSON body */ }
        throw new Error(message);
    }
    return res.status === 204 ? null : res.json();
}

const json = (method, body) => ({ method, headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });

function rangeQuery() {
    const params = new URLSearchParams();
    if ($("fromDate").value) params.set("from", $("fromDate").value);
    if ($("toDate").value) params.set("to", $("toDate").value);
    const qs = params.toString();
    return qs ? `?${qs}` : "";
}

async function loadEntries() {
    const qs = rangeQuery();
    entries = await request(`/api/entries/${qs}`);
    $("exportLink").href = `/api/entries/export${qs}`;
    $("exportPdfLink").href = `/api/entries/export${qs ? `${qs}&` : "?"}format=pdf`;
    // Only keep ticks on rows that are still shown, so nothing hidden by the filter gets deleted.
    const visible = new Set(entries.map((e) => e.id));
    for (const id of [...selected]) if (!visible.has(id)) selected.delete(id);
    render();
}

async function loadSettings() {
    settings = await request("/api/settings");
    $("headerName").textContent = settings.name;
}

// ---------- Rendering ----------

/** Totals for the entries on screen (minutes), the same figures as the spreadsheet's summary. */
function summarize() {
    const standard = (settings.standardHoursPerDay || 8) * 60;
    const dayTotals = {};
    for (const e of entries) dayTotals[e.date] = (dayTotals[e.date] || 0) + e.minutes;
    const total = entries.reduce((sum, e) => sum + e.minutes, 0);
    return {
        total,
        days: Object.keys(dayTotals).length,
        overtime: Object.values(dayTotals).reduce((sum, m) => sum + Math.max(0, m - standard), 0),
        first: entries[0]?.date,
        last: entries[entries.length - 1]?.date,
    };
}

// ---------- Sorting (on screen; exports stay in date order like a timesheet) ----------

const SORT_KEY = "timelogger.sort";
const WEEKDAY = (iso) => (new Date(`${iso}T00:00`).getDay() + 6) % 7; // Mon = 0 … Sun = 6

// Value to sort by for each column. Ties keep date order.
const SORTERS = {
    date: (e) => `${e.date} ${e.start || "99"}`,
    day: (e) => WEEKDAY(e.date),
    start: (e) => e.start || "99",
    end: (e) => e.end || "99",
    break: (e) => e.breakMinutes || 0,
    hours: (e) => e.minutes,
    task: (e) => e.task,
    notes: (e) => e.notes || "",
};

let sort = (() => {
    try {
        const saved = JSON.parse(localStorage.getItem(SORT_KEY));
        if (saved && SORTERS[saved.key] && (saved.dir === "asc" || saved.dir === "desc")) return saved;
    } catch { /* ignore */ }
    return { key: "date", dir: "asc" };
})();

function setSort(key, dir) {
    sort = { key, dir };
    try { localStorage.setItem(SORT_KEY, JSON.stringify(sort)); } catch { /* storage unavailable */ }
    page = 1;
    render();
}

// ---------- Pagination (client-side: the whole period is already loaded) ----------

const PAGE_SIZE_KEY = "timelogger.pageSize";
const PAGE_SIZES = [10, 25, 50, 100, 0]; // 0 = all rows

let page = 1;
let pageSize = (() => {
    try {
        const saved = Number(localStorage.getItem(PAGE_SIZE_KEY));
        if (PAGE_SIZES.includes(saved) && localStorage.getItem(PAGE_SIZE_KEY) !== null) return saved;
    } catch { /* ignore */ }
    return 10;
})();
let pageIds = []; // ids of the rows on the current page

function pageCount(total) {
    return pageSize === 0 ? 1 : Math.max(1, Math.ceil(total / pageSize));
}

/** The rows for the current page; also clamps the page number if rows were removed. */
function pageRows(rows) {
    page = Math.min(Math.max(1, page), pageCount(rows.length));
    return pageSize === 0 ? rows : rows.slice((page - 1) * pageSize, page * pageSize);
}

// Page buttons: first, last, current ±1, with "…" for gaps, e.g. ‹ 1 … 4 5 6 … 12 ›
function pageButtons(count) {
    const wanted = new Set([1, count, page - 1, page, page + 1].filter((p) => p >= 1 && p <= count));
    const list = [...wanted].sort((a, b) => a - b);
    const parts = [];
    list.forEach((p, i) => {
        if (i > 0 && p - list[i - 1] > 1) parts.push(`<span class="gap" aria-hidden="true">…</span>`);
        parts.push(`<button type="button" data-page="${p}" ${p === page ? 'aria-current="page"' : ""} aria-label="Page ${p}">${p}</button>`);
    });
    return parts.join("");
}

function renderPager(total) {
    const count = pageCount(total);
    $("pager").hidden = total === 0;
    $("pageSize").value = String(pageSize);
    const first = pageSize === 0 ? 1 : (page - 1) * pageSize + 1;
    const last = pageSize === 0 ? total : Math.min(page * pageSize, total);
    $("pagerInfo").textContent = `Showing ${first}–${last} of ${total} ${total === 1 ? "record" : "records"}`;
    $("pagerPages").innerHTML = count <= 1 ? "" :
        `<button type="button" data-page="${page - 1}" ${page === 1 ? "disabled" : ""} aria-label="Previous page">‹</button>` +
        pageButtons(count) +
        `<button type="button" data-page="${page + 1}" ${page === count ? "disabled" : ""} aria-label="Next page">›</button>`;
}

$("pagerPages").addEventListener("click", (ev) => {
    const btn = ev.target.closest("button[data-page]");
    if (!btn || btn.disabled) return;
    page = Number(btn.dataset.page);
    render();
    document.querySelector(".selection-bar").scrollIntoView({ block: "nearest", behavior: "smooth" });
});

$("pageSize").addEventListener("change", () => {
    pageSize = Number($("pageSize").value);
    try { localStorage.setItem(PAGE_SIZE_KEY, String(pageSize)); } catch { /* storage unavailable */ }
    page = 1;
    render();
});

function compare(a, b) {
    if (typeof a === "string") return a.localeCompare(b, undefined, { sensitivity: "base", numeric: true });
    return a - b;
}

function sortedEntries() {
    const value = SORTERS[sort.key];
    const sign = sort.dir === "asc" ? 1 : -1;
    return entries
        .map((e, i) => ({ e, i }))
        .sort((x, y) => sign * compare(value(x.e), value(y.e)) || x.i - y.i)
        .map((x) => x.e);
}

function updateSortUi() {
    document.querySelectorAll(".sheet th[data-sort]").forEach((th) => {
        const active = th.dataset.sort === sort.key;
        th.setAttribute("aria-sort", active ? (sort.dir === "asc" ? "ascending" : "descending") : "none");
        th.classList.toggle("sorted", active);
    });
    $("sortSelect").value = sort.key;
    $("sortDir").textContent = sort.dir === "asc" ? "↑" : "↓";
    $("sortDir").title = sort.dir === "asc" ? "Ascending (click to reverse)" : "Descending (click to reverse)";
}

// Click a column header: sort by it; click again to reverse.
document.querySelector(".sheet thead").addEventListener("click", (ev) => {
    const th = ev.target.closest("th[data-sort]");
    if (!th) return;
    const key = th.dataset.sort;
    setSort(key, sort.key === key && sort.dir === "asc" ? "desc" : "asc");
});

$("sortSelect").addEventListener("change", () => setSort($("sortSelect").value, sort.dir));
$("sortDir").addEventListener("click", () => setSort(sort.key, sort.dir === "asc" ? "desc" : "asc"));

// ---------- Long notes: show the first 100 characters, click "…" to expand ----------

const NOTES_PREVIEW = 100;
const expandedNotes = new Set(); // ids whose notes are shown in full

function notesHtml(e) {
    const chars = [...(e.notes || "")]; // by character, so emoji aren't cut in half
    if (chars.length <= NOTES_PREVIEW) return escapeHtml(e.notes || "");
    const open = expandedNotes.has(e.id);
    const text = open ? e.notes : chars.slice(0, NOTES_PREVIEW).join("").trimEnd();
    return `${escapeHtml(text)}${open ? " " : ""}<button type="button" class="notes-toggle" data-notes="${e.id}" ` +
        `aria-expanded="${open}" title="${open ? "Show less" : "Show the full note"}">${open ? "less" : "…"}</button>`;
}

function render() {
    const standard = (settings.standardHoursPerDay || 8) * 60;

    // Overtime is worked out per day and shown on that day's last row.
    const dayTotals = {};
    const lastOfDay = {};
    for (const e of entries) {
        dayTotals[e.date] = (dayTotals[e.date] || 0) + e.minutes;
        lastOfDay[e.date] = e.id;
    }

    // Overtime placement above uses date order; the rows themselves follow the chosen sort.
    updateSortUi();
    const rows = pageRows(sortedEntries());
    pageIds = rows.map((e) => e.id);
    renderPager(entries.length);
    $("entries").innerHTML = rows.map((e) => {
        const overtime = lastOfDay[e.date] === e.id ? Math.max(0, dayTotals[e.date] - standard) : 0;
        const times = e.start ? `${hm(e.start)}<span class="mobile-only">–${hm(e.end)}${e.breakMinutes ? ` · ${e.breakMinutes} min break` : ""}</span>` : "";
        const rowClass = [selected.has(e.id) && "is-selected", editingId === e.id && "is-editing"].filter(Boolean).join(" ");
        return `
        <tr class="${rowClass}">
            <td class="c-check"><input type="checkbox" data-select="${e.id}" ${selected.has(e.id) ? "checked" : ""} aria-label="Select ${escapeHtml(e.task)} on ${formatDate(e.date)}" /></td>
            <td class="c-date nowrap">${formatDate(e.date)}<span class="mobile-only"> · ${dayName(e.date)}</span></td>
            <td class="c-day">${dayName(e.date)}</td>
            <td class="c-start nowrap">${times}</td>
            <td class="c-end">${hm(e.end)}</td>
            <td class="c-break num">${e.breakMinutes || 0}</td>
            <td class="c-hours num">${hours(e.minutes)}</td>
            <td class="c-overtime num ${overtime ? "overtime" : ""}">${overtime ? `+${hours(overtime)}<span class="mobile-only"> overtime</span>` : ""}</td>
            <td class="task">${escapeHtml(e.task)}</td>
            <td class="notes">${notesHtml(e)}</td>
            <td class="c-actions">
                <div class="row-actions">
                    <button type="button" data-edit="${e.id}">Edit</button>
                    <button type="button" class="delete" data-delete="${e.id}">Delete</button>
                </div>
            </td>
        </tr>`;
    }).join("");
    $("emptyState").hidden = entries.length > 0;
    updateSelectionUi();

    // Summary, matching the spreadsheet's summary block.
    const { total, days, overtime } = summarize();
    const stats = [
        ["Total hours worked", hours(total), true],
        ["Total overtime hours", hours(overtime)],
        ["Days worked", days],
        ["Average hours per day", days ? hours(total / days) : "0.00"],
    ];
    const a = amounts(total);
    if (a) {
        const taxNote = a.tax ? `, incl. ${settings.taxLabel || "VAT"} ${settings.taxPercent}%` : "";
        stats.push([`Amount due (× ${settings.hourlyRate}/h${taxNote})`, money(a.due)]);
    }
    $("stats").innerHTML = stats.map(([label, value, highlight]) =>
        `<div class="stat ${highlight ? "highlight" : ""}"><div class="stat-value">${value}</div><div class="stat-label">${label}</div></div>`).join("");

    const tasks = [...new Set(entries.map((e) => e.task))];
    $("taskList").innerHTML = tasks.map((t) => `<option value="${escapeHtml(t)}">`).join("");
}

// ---------- Form ----------

function syncDuration() {
    const start = $("start").value;
    const end = $("end").value;
    const fromTimes = Boolean(start && end);
    $("duration").readOnly = fromTimes;
    if (fromTimes) $("duration").value = hours(minutesBetween(start, end, Number($("breakMin").value)));
}

["start", "end", "breakMin"].forEach((id) => $(id).addEventListener("input", syncDuration));

function resetForm() {
    editingId = null;
    $("entryForm").reset();
    $("date").value = toIsoDate(new Date());
    $("breakMin").value = 0;
    $("formTitle").textContent = "Log time";
    $("saveBtn").textContent = "Add entry";
    $("cancelEdit").hidden = true;
    $("formError").hidden = true;
    syncDuration();
}

// Only one record can be edited at a time: switching to another one asks first.
function startEdit(id) {
    const e = entries.find((x) => x.id === id);
    if (!e) return;
    if (editingId && editingId !== id &&
        !confirm("You're already editing a record. Discard those changes and edit this one instead?")) return;
    editingId = id;
    $("date").value = e.date;
    $("start").value = hm(e.start);
    $("end").value = hm(e.end);
    $("breakMin").value = e.breakMinutes || 0;
    $("duration").value = hours(e.minutes);
    $("task").value = e.task;
    $("notes").value = e.notes || "";
    $("formTitle").textContent = "Edit entry";
    $("saveBtn").textContent = "Save changes";
    $("cancelEdit").hidden = false;
    $("formError").hidden = true;
    syncDuration();
    render();
    $("entryForm").scrollIntoView({ behavior: "smooth", block: "center" });
    $("task").focus();
}

function showError(msg) {
    $("formError").textContent = msg;
    $("formError").hidden = false;
}

$("entryForm").addEventListener("submit", async (ev) => {
    ev.preventDefault();
    const start = $("start").value;
    const end = $("end").value;
    const task = $("task").value.trim();
    const breakMinutes = Number($("breakMin").value) || 0;

    if (!$("date").value) return showError("Pick a date.");
    if (Boolean(start) !== Boolean(end)) return showError("Enter both a start and an end time, or leave both empty.");
    const minutes = start ? minutesBetween(start, end, breakMinutes) : parseDuration($("duration").value);
    if (!minutes) return showError("Enter start and end times, or hours worked like 1:30 or 1.5.");
    if (!task) return showError("Enter a project / task.");

    const body = {
        date: $("date").value,
        start: start ? `${start}:00` : null,
        end: end ? `${end}:00` : null,
        breakMinutes,
        task,
        notes: $("notes").value.trim(),
        minutes: start ? null : minutes,
    };

    try {
        if (editingId) {
            await request(`/api/entries/${editingId}`, json("PUT", body));
            selected.delete(editingId);
        } else {
            await request("/api/entries/", json("POST", body));
        }
        resetForm();
        await loadEntries();
    } catch (err) {
        showError(err.message);
    }
});

$("cancelEdit").addEventListener("click", () => {
    resetForm();
    render();
});

$("entries").addEventListener("click", async (ev) => {
    const notesId = ev.target.dataset.notes;
    if (notesId) {
        // Redraw just this cell and keep keyboard focus on the toggle.
        if (expandedNotes.has(notesId)) expandedNotes.delete(notesId);
        else expandedNotes.add(notesId);
        const cell = ev.target.closest("td");
        cell.innerHTML = notesHtml(entries.find((x) => x.id === notesId));
        cell.querySelector(".notes-toggle").focus();
        return;
    }
    const editId = ev.target.dataset.edit;
    const deleteId = ev.target.dataset.delete;
    if (editId) startEdit(editId);
    if (deleteId && confirm("Delete this entry?")) {
        await request(`/api/entries/${deleteId}`, { method: "DELETE" });
        if (editingId === deleteId) resetForm();
        await loadEntries();
    }
});

// ---------- Selecting several records ----------

// "Select all" works on the current page; ticks on other pages are kept and counted,
// so it's always clear what "Delete selected" will remove.
function updateSelectionUi() {
    const n = selected.size;
    const onPage = pageIds.filter((id) => selected.has(id)).length;
    const elsewhere = n - onPage;
    $("selectionBar").hidden = entries.length === 0;
    $("selectionCount").textContent = !n ? "" :
        elsewhere ? `${n} selected (${elsewhere} on other pages)` : `${n} selected`;
    $("selectAll").checked = pageIds.length > 0 && onPage === pageIds.length;
    $("selectAll").indeterminate = onPage > 0 && onPage < pageIds.length;

    // Offer to extend a full-page selection to the whole period, or to clear a big one.
    const everything = $("selectEverything");
    if (n === entries.length && entries.length > pageIds.length) {
        everything.textContent = "Clear selection";
        everything.dataset.action = "clear";
        everything.hidden = false;
    } else if (pageIds.length && onPage === pageIds.length && entries.length > pageIds.length) {
        everything.textContent = `Select all ${entries.length} records`;
        everything.dataset.action = "all";
        everything.hidden = false;
    } else {
        everything.hidden = true;
    }
    $("editSelected").disabled = n !== 1;
    $("editSelected").title = n === 1 ? "Edit the selected record" : "Select exactly one record to edit";
    $("deleteSelected").disabled = n === 0;
    $("deleteSelected").textContent = n ? `Delete selected (${n})` : "Delete selected";
}

$("entries").addEventListener("change", (ev) => {
    const id = ev.target.dataset.select;
    if (!id) return;
    if (ev.target.checked) selected.add(id);
    else selected.delete(id);
    ev.target.closest("tr").classList.toggle("is-selected", ev.target.checked);
    updateSelectionUi();
});

$("selectAll").addEventListener("change", () => {
    if ($("selectAll").checked) pageIds.forEach((id) => selected.add(id));
    else pageIds.forEach((id) => selected.delete(id));
    render();
});

$("selectEverything").addEventListener("click", () => {
    if ($("selectEverything").dataset.action === "all") entries.forEach((e) => selected.add(e.id));
    else selected.clear();
    render();
});

$("editSelected").addEventListener("click", () => {
    if (selected.size === 1) startEdit([...selected][0]);
});

$("deleteSelected").addEventListener("click", async () => {
    const ids = [...selected];
    if (!ids.length) return;
    const label = ids.length === 1 ? "this record" : `these ${ids.length} records`;
    if (!confirm(`Delete ${label}? This can't be undone.`)) return;
    try {
        const r = await request("/api/entries/delete-many", json("POST", { ids }));
        if (ids.includes(editingId)) resetForm();
        selected.clear();
        await loadEntries();
        showBanner(`Deleted ${r.deleted} ${r.deleted === 1 ? "record" : "records"}.`);
    } catch (err) {
        showBanner(err.message, true);
    }
});

// ---------- Period ----------

function setPreset(p) {
    const r = presetRange(p);
    $("fromDate").value = r.from;
    $("toDate").value = r.to;
    document.querySelectorAll("#periodTabs button").forEach((b) => b.classList.toggle("active", b.dataset.period === p));
    page = 1;
    loadEntries();
}

$("periodTabs").addEventListener("click", (ev) => {
    if (ev.target.dataset.period) setPreset(ev.target.dataset.period);
});

["fromDate", "toDate"].forEach((id) => $(id).addEventListener("change", () => {
    document.querySelectorAll("#periodTabs button").forEach((b) => b.classList.remove("active"));
    page = 1;
    loadEntries();
}));

// ---------- Import from Excel ----------

$("importFile").addEventListener("change", async () => {
    const file = $("importFile").files[0];
    $("importFile").value = "";
    if (!file) return;

    const form = new FormData();
    form.append("file", file);
    try {
        const r = await request("/api/import", { method: "POST", body: form });
        let msg = `Imported ${r.added} ${r.added === 1 ? "entry" : "entries"} from ${file.name}.`;
        if (r.skipped) msg += ` Skipped ${r.skipped} already in the log.`;
        if (r.settingsUpdated) msg += " Name, standard hours and hourly rate updated from the sheet.";
        if (r.warnings.length) msg += `\n${r.warnings.join("\n")}`;
        showBanner(msg);
        await loadSettings();
        setPreset("all");
    } catch (err) {
        showBanner(err.message, true);
    }
});

// ---------- Settings ----------

// Text fields in the settings dialog: input id → settings property.
const SETTINGS_TEXT_FIELDS = {
    setName: "name", setJobTitle: "jobTitle", setCompany: "company", setEmail: "email", setPhone: "phone",
    setAddress: "address", setClientName: "clientName", setClientCompany: "clientCompany",
    setClientEmail: "clientEmail", setClientAddress: "clientAddress", setCurrency: "currency",
    setTaxLabel: "taxLabel", setTitle: "documentTitle", setPayment: "paymentDetails", setNotes: "notes",
};

function showLogo() {
    const box = $("logoPreview");
    box.innerHTML = settings.hasLogo
        ? `<img src="/api/settings/logo?v=${Date.now()}" alt="Your logo" />`
        : "<span>No logo</span>";
    $("logoRemove").hidden = !settings.hasLogo;
}

function settingsError(msg) {
    $("settingsError").textContent = msg;
    $("settingsError").hidden = !msg;
}

$("settingsBtn").addEventListener("click", () => {
    for (const [id, key] of Object.entries(SETTINGS_TEXT_FIELDS)) $(id).value = settings[key] || "";
    $("setStd").value = settings.standardHoursPerDay ?? 8;
    $("setRate").value = settings.hourlyRate ?? "";
    $("setTax").value = settings.taxPercent ?? "";
    $("previewPdf").href = $("exportPdfLink").href;
    showLogo();
    settingsError("");
    $("settingsDialog").showModal();
});

$("settingsCancel").addEventListener("click", () => $("settingsDialog").close());

$("settingsForm").addEventListener("submit", async (ev) => {
    ev.preventDefault();
    const body = { standardHoursPerDay: Number($("setStd").value) || 8 };
    for (const [id, key] of Object.entries(SETTINGS_TEXT_FIELDS)) body[key] = $(id).value.trim();
    body.hourlyRate = $("setRate").value === "" ? null : Number($("setRate").value);
    body.taxPercent = $("setTax").value === "" ? null : Number($("setTax").value);
    try {
        await request("/api/settings", json("PUT", body));
        $("settingsDialog").close();
        await loadSettings();
        render();
        showBanner("Settings saved. They'll appear on your next PDF and CSV export.");
    } catch (err) {
        settingsError(err.message);
    }
});

$("logoFile").addEventListener("change", async () => {
    const file = $("logoFile").files[0];
    $("logoFile").value = "";
    if (!file) return;
    if (file.size > 2 * 1024 * 1024) return settingsError("The logo must be 2 MB or smaller.");
    const form = new FormData();
    form.append("file", file);
    try {
        settings = await request("/api/settings/logo", { method: "POST", body: form });
        settingsError("");
        showLogo();
    } catch (err) {
        settingsError(err.message);
    }
});

$("logoRemove").addEventListener("click", async () => {
    if (!confirm("Remove the logo?")) return;
    settings = await request("/api/settings/logo", { method: "DELETE" });
    showLogo();
});

// ---------- Share via WhatsApp / email ----------

const SHARE_KEY = "timelogger.share";

let shareFormat = "pdf";
const FORMAT_TYPES = { pdf: "application/pdf", csv: "text/csv" };

function shareMessage() {
    const s = summarize();
    const title = settings.documentTitle || "Timesheet";
    const lines = [`${title}${settings.name ? ` – ${settings.name}` : ""}`];
    if (s.first) lines.push(`Period: ${formatDate($("fromDate").value || s.first)} – ${formatDate($("toDate").value || s.last)}`);
    lines.push(`Total hours worked: ${hours(s.total)} (${s.days} ${s.days === 1 ? "day" : "days"})`);
    lines.push(`Overtime: ${hours(s.overtime)} hrs`);
    const a = amounts(s.total);
    if (a) {
        lines.push(`Hourly rate: ${money(settings.hourlyRate)}`);
        if (a.tax) lines.push(`${settings.taxLabel || "VAT"} (${settings.taxPercent}%): ${money(a.tax)}`);
        lines.push(`Total amount due: ${money(a.due)}`);
    }
    return lines.join("\n");
}

function shareFileName() {
    const s = summarize();
    const who = (settings.name || "timesheet").trim().replace(/[^\w-]+/g, "-");
    const from = $("fromDate").value || s.first || "";
    const to = $("toDate").value || s.last || "";
    return `${who}-timesheet${from ? `-${from}_to_${to}` : ""}.${shareFormat}`;
}

/** Fetches the export for the period on screen, the same file Export PDF / Export CSV gives. */
async function exportFile() {
    const res = await fetch((shareFormat === "pdf" ? $("exportPdfLink") : $("exportLink")).href);
    if (!res.ok) throw new Error(`Couldn't create the export (${res.status}).`);
    return new File([await res.blob()], shareFileName(), { type: FORMAT_TYPES[shareFormat] });
}

function updateSharePreview() {
    $("sharePreview").textContent = `${shareMessage()}\n\nFile: ${shareFileName()}`;
    document.querySelectorAll("#shareFormat button").forEach((b) => b.classList.toggle("active", b.dataset.format === shareFormat));
    $("shareNative").hidden = !canShareFiles();
}

$("shareFormat").addEventListener("click", (ev) => {
    if (!ev.target.dataset.format) return;
    shareFormat = ev.target.dataset.format;
    updateSharePreview();
});

function downloadFile(file) {
    const url = URL.createObjectURL(file);
    const a = Object.assign(document.createElement("a"), { href: url, download: file.name });
    document.body.append(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}

// The device share menu can attach the file directly (phones, and Chrome/Edge on Windows).
function canShareFiles() {
    try {
        const probe = new File(["x"], `probe.${shareFormat}`, { type: FORMAT_TYPES[shareFormat] });
        return Boolean(navigator.canShare && navigator.canShare({ files: [probe] }));
    } catch {
        return false;
    }
}

function loadShareContacts() {
    try { return JSON.parse(localStorage.getItem(SHARE_KEY)) || {}; } catch { return {}; }
}

function saveShareContacts() {
    try {
        localStorage.setItem(SHARE_KEY, JSON.stringify({ wa: $("shareWaNumber").value.trim(), email: $("shareEmailTo").value.trim() }));
    } catch { /* storage unavailable: just don't remember */ }
}

function showShareError(msg) {
    $("shareError").textContent = msg;
    $("shareError").hidden = !msg;
}

$("shareBtn").addEventListener("click", () => {
    if (!entries.length) return showBanner("There's nothing to share for this period.", true);
    const saved = loadShareContacts();
    $("shareWaNumber").value = saved.wa || "";
    $("shareEmailTo").value = saved.email || "";
    updateSharePreview();
    $("shareNote").textContent = canShareFiles()
        ? "WhatsApp and email buttons below send the summary and download the file; attach it before you send."
        : "The file downloads first. WhatsApp and email links can't attach files themselves, so attach the downloaded file before you send.";
    showShareError("");
    $("shareDialog").showModal();
});

$("shareClose").addEventListener("click", () => $("shareDialog").close());

$("shareNativeBtn").addEventListener("click", async () => {
    showShareError("");
    try {
        const file = await exportFile();
        await navigator.share({ files: [file], title: file.name, text: shareMessage() });
        $("shareDialog").close();
    } catch (err) {
        if (err.name !== "AbortError") showShareError(err.message); // AbortError = user closed the share menu
    }
});

$("shareWhatsApp").addEventListener("click", async () => {
    showShareError("");
    const number = $("shareWaNumber").value.replace(/[^\d]/g, "");
    if ($("shareWaNumber").value.trim() && number.length < 8) {
        return showShareError("Enter the WhatsApp number with country code, digits only (e.g. 971501234567), or leave it empty to pick a chat.");
    }
    try {
        downloadFile(await exportFile());
        saveShareContacts();
        const text = `${shareMessage()}\n\n(${shareFormat.toUpperCase()} file attached)`;
        window.open(`https://wa.me/${number}?text=${encodeURIComponent(text)}`, "_blank", "noopener");
    } catch (err) {
        showShareError(err.message);
    }
});

$("shareEmail").addEventListener("click", async () => {
    showShareError("");
    const to = $("shareEmailTo").value.trim();
    if (to && !$("shareEmailTo").checkValidity()) return showShareError("That email address doesn't look right.");
    try {
        const file = await exportFile();
        downloadFile(file);
        saveShareContacts();
        const title = settings.documentTitle || "Timesheet";
        const subject = `${title}${settings.name ? ` – ${settings.name}` : ""}`;
        const greeting = settings.clientName ? `Dear ${settings.clientName},` : "Hello,";
        const body = `${greeting}\n\nPlease find my ${title.toLowerCase()} attached (${file.name}).\n\n${shareMessage()}\n\nRegards,\n${settings.name || ""}`;
        window.open(`mailto:${encodeURIComponent(to).replace(/%40/g, "@")}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`, "_self");
    } catch (err) {
        showShareError(err.message);
    }
});

// ---------- Delete all (needs the phrase typed in) ----------

const DELETE_PHRASE = "DELETE ALL";

$("deleteAllBtn").addEventListener("click", async () => {
    // Count every record, not just the period on screen.
    const all = await request("/api/entries/");
    if (!all.length) return showBanner("There are no records to delete.");
    $("deleteAllCount").textContent = `${all.length} ${all.length === 1 ? "record" : "records"}`;
    $("deleteAllInput").value = "";
    $("deleteAllConfirm").disabled = true;
    $("deleteAllError").hidden = true;
    $("deleteAllDialog").showModal();
    $("deleteAllInput").focus();
});

$("deleteAllInput").addEventListener("input", () => {
    $("deleteAllConfirm").disabled = $("deleteAllInput").value.trim() !== DELETE_PHRASE;
});

$("deleteAllCancel").addEventListener("click", () => $("deleteAllDialog").close());

$("deleteAllForm").addEventListener("submit", async (ev) => {
    ev.preventDefault();
    if ($("deleteAllInput").value.trim() !== DELETE_PHRASE) return;
    try {
        const r = await request("/api/entries/delete-all", json("POST", { confirmation: $("deleteAllInput").value }));
        $("deleteAllDialog").close();
        resetForm();
        await loadEntries();
        showBanner(`Deleted ${r.deleted} ${r.deleted === 1 ? "record" : "records"}.` +
            (r.backupFile ? ` A backup was saved as App_Data/${r.backupFile}.` : ""));
    } catch (err) {
        $("deleteAllError").textContent = err.message;
        $("deleteAllError").hidden = false;
    }
});

// ---------- Timer (survives page reloads via localStorage) ----------

function getTimerStart() {
    try { return Number(localStorage.getItem(TIMER_KEY)) || null; } catch { return null; }
}

function setTimerStart(value) {
    try {
        if (value) localStorage.setItem(TIMER_KEY, String(value));
        else localStorage.removeItem(TIMER_KEY);
    } catch { /* storage unavailable: timer still works until reload */ }
}

let timerStart = getTimerStart();

function tick() {
    const secs = timerStart ? Math.floor((Date.now() - timerStart) / 1000) : 0;
    $("timerDisplay").textContent = `${pad(Math.floor(secs / 3600))}:${pad(Math.floor((secs % 3600) / 60))}:${pad(secs % 60)}`;
}

function updateTimerUi() {
    const running = Boolean(timerStart);
    document.querySelector(".timer-card").classList.toggle("running", running);
    $("timerBtn").textContent = running ? "Stop" : "Start";
    $("timerHint").textContent = running
        ? `Started at ${toHm(new Date(timerStart))}.`
        : "Start when you begin work. Stop fills in the date, start and end.";
    clearInterval(timerInterval);
    if (running) timerInterval = setInterval(tick, 1000);
    tick();
}

$("timerBtn").addEventListener("click", () => {
    if (timerStart) {
        // Stop: fill the form so the user only has to add the project / task.
        const started = new Date(timerStart);
        timerStart = null;
        setTimerStart(null);
        if (!editingId) {
            $("date").value = toIsoDate(started);
            $("start").value = toHm(started);
            $("end").value = toHm(new Date());
            syncDuration();
        }
        $("task").focus();
    } else {
        timerStart = Date.now();
        setTimerStart(timerStart);
    }
    updateTimerUi();
});

// ---------- Start ----------

resetForm();
updateTimerUi();
Promise.all([loadSettings(), loadEntries()])
    .then(render)
    .catch((err) => showBanner(err.message, true));
