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
| `found_by_agent`, `found_by_model` | Who gets the credit for finding it (empty when the developer found it) |
| `stage` | **escaped** (found after delivery), **review** (caught by another agent before the developer saw it), **pre-push** (broken build stopped before a push) |
| `description` | Short description |
| `caused_by_task` | `id` from tasks.csv |
| `caused_by_agent`, `caused_by_model` | Copied from that task |
| `confidence` | high, medium, low: how sure the attribution is |
| `status` | open, fixed |

## suggestions.csv

Ideas that came from an agent, not from the developer (features, tools, process changes).

| Column | Meaning |
|---|---|
| `id`, `when`, `agent`, `model` | As above |
| `suggestion` | The idea |
| `kind` | feature, tooling, process |
| `adopted` | yes, no, pending |
| `board_id` | Tracker row it became, if any |

## Contribution score

The developer's rule (2026-11-18): **finding new bugs and suggesting meaningful features should score best.** `analyze.py` scores each agent
and model: +3 per defect found in another agent's work before the developer saw it, +2 per defect found in its own work, +3 per adopted
suggestion, +1 per pending suggestion, +1 per verified task, +0.5 per task ready for review, -2 per escaped defect (confidence-weighted),
-1 per pre-push break, -0.5 per own defect caught at review.

## Who may edit a defect row

Only the finder (or the reviewer) writes a defect's `found_by`, `stage` and attribution. The agent that caused it may add context in its log entry but must not change those fields; disagreements go to the reviewer or the developer. (Added 2026-11-18 after an agent re-filed a defect the developer had seen as its own pre-push catch.)
