# Security Policy

## Supported versions

Only the latest code on the `main` branch receives security fixes.

| Version | Supported |
|---|---|
| `main` (latest) | ✅ |
| Older commits | ❌ |

## How the app is meant to be run

Working Hours Log is a **single-user app meant to run locally** on your own computer (`http://localhost:5028`).

- It has **no login or user accounts**. Anyone who can reach the server can read, change, export and delete all records.
- File uploads (Excel import, logo) skip anti-forgery checks, because the app is designed for local use only.
- Data is stored unencrypted in `TimeLogger/App_Data/` as JSON files.

**Do not expose it to the internet or a shared network** (for example by port-forwarding, binding to `0.0.0.0`, or deploying it to a public server) without first adding authentication, HTTPS and anti-forgery protection. Reports that only apply when the app is exposed this way are still welcome, but they will be treated as hardening suggestions.

## Reporting a vulnerability

Please **do not open a public issue** for security problems.

Report privately using GitHub's private vulnerability reporting:
**[Report a vulnerability](https://github.com/mkzaimhussian/WorkingHoursLog/security/advisories/new)**
(or go to the repository's **Security** tab → **Report a vulnerability**).

Please include:

- What the problem is and where it is (file, endpoint or page)
- Steps to reproduce it, or a proof of concept
- What an attacker could do with it
- Any suggested fix, if you have one

Please don't include real timesheet data or personal information in a report.

## What to expect

- You'll get an acknowledgement within **7 days**.
- We'll keep you updated while we investigate and fix the issue.
- Once a fix is released, we're happy to credit you, unless you'd prefer to stay anonymous.

## Keeping your data safe

- Keep `TimeLogger/App_Data/` out of Git. It's already in `.gitignore`.
- Back up `App_Data` regularly. The "Delete all records" action saves a backup first, but other deletes cannot be undone.
- Treat exported PDF/CSV files as confidential. They contain your hours, rates and client details.
