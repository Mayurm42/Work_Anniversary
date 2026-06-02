Built using M365 Copilot Cowork as part of Vibecoding Excercise

# README — Work Anniversary Wall

A shared online celebration board for any colleague's work anniversary. Single self-contained web app — team members sign in, post tributes, and react in real time. Everyone sees the same live wall from any device or browser.

---

## Features
- **Tributes** — text messages with optional photos, image links, GIFs, and celebration effects
- **Reactions** — ?? Cheers · ?? Bravo · ?? Grateful · ?? Celebrate
- **Shared storage** — all posts sync online so every user/device sees the same wall
- **Board effects** — floating Balloons / Confetti / Hearts / Sparkles / Petals (per-user choice)
- **Editable banner** — customize the honoree's name, years, and tagline (admin-gated)
- **Admin tools** — PIN reset, delete old posts, full backup export/import

---

## Sign-in (end users)
1. Open the wall link.
2. **New users:** enter a **name**, choose a **4-digit PIN**, and set a one-word **recovery word**.
3. **Returning users:** enter **name + PIN**.
4. **Forgot PIN?** ? enter name + recovery word ? set a new PIN.

> Sessions persist in the browser, so users stay signed in until they log out or clear data.

---

## Admin Access
| Item | Value |
|------|-------|
| **Open admin panel** | Tap **?? Admin** in the top bar |
| **Default passcode** | `admin2026` |

> ?? **Change the default passcode** before sharing the wall widely. The passcode syncs to all devices, so update it once from the admin panel.

### Admin tools
- **Edit banner** — change honoree name, font, number of years, and tagline
- **Reset a colleague's PIN** — for anyone locked out who can't self-recover
- **Delete old posts** — bulk-remove posts older than *N* days
- **Export backup** — download the full wall as a JSON file
- **Import backup** — restore the wall from a previously exported JSON file

---

## Backup & Restore
- **Export:** Admin ? **Export backup** ? saves a `.json` file containing all posts, users, banner, and settings.
- **Import:** Admin ? **Import backup** ? select a previously exported `.json` file. Imported data **merges** with the current wall (nothing is wiped).
- **Recommendation:** export a backup before deleting old posts or handing off the wall to another organizer.

---

## Hosting & Storage
- **App:** single static `index.html` front-end + a small API backend (`GET /api/wall`, `POST /api/wall`).
- **Sync indicator:** top-bar pill shows **Synced** (green, shared backend reachable) or **Offline** (changes saved locally until reconnect).
- **Azure App Service notes:**
  - State is saved to `HOME/data/state.json` (persistent writable area).
  - Set **Always On = On**.
  - Keep **Scale out = 1 instance** (single shared state file).

---

