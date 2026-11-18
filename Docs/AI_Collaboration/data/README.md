# Research data: which model produces what quality of work

Two hand-maintained ledgers feed [`../analysis/analyze.py`](../analysis/analyze.py), which writes the charts in
[`../figures/`](../figures/). Agents append a row whenever they finish an item (`tasks.csv`) or cause or find a defect
(`bugs.csv`); see rule 7 and request R9 on the board. Rows before 2026-11-17 23:25 were reconstructed afterwards from the
developer's prompts, the board archive and git; their `source` says so.

## tasks.csv

| Column | Meaning |
|---|---|
| `id` | Unique ID (`C..` Claude Code, `G..` Antigravity/"Gemini", `P..` Copilot) |
| `when` | Local time (America/New_York) the work was delivered, `YYYY-MM-DD HH:MM` |
| `agent` | Board name: Claude, Gemini, Copilot |
| `model` | Exact model id, or the developer's name for it (e.g. `argon`), or `unknown` |
| `title` | What was built or fixed |
| `kind` | feature, fix, tune, tooling, docs, review |
| `board_id` | Tracker ID (B.., P..) if any |
| `outcome` | verified, reopened, ready-for-review, open, delivered (pre-tracker work that shipped without review) |
| `source` | Where the row comes from (board row, log time, prompt time, reconstruction) |

## bugs.csv

| Column | Meaning |
|---|---|
| `id` | Unique ID |
| `found` | Local time it was reported |
| `found_by` | user, qa-bot, claude-review, copilot-review, gemini-review, push-check, claude-self |
| `stage` | **escaped** (found after delivery), **review** (caught by another agent before the developer saw it), **pre-push** (broken build stopped before a push) |
| `description` | Short description |
| `caused_by_task` | `id` from tasks.csv |
| `caused_by_agent`, `caused_by_model` | Copied from that task |
| `confidence` | high, medium, low: how sure the attribution is |
| `status` | open, fixed |
