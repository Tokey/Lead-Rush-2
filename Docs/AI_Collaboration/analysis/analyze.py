#!/usr/bin/env python3
"""
Quality of AI work per agent and model on LeadRush 2.

Reads ../data/tasks.csv and ../data/bugs.csv (see ../data/README.md for the columns) and writes, to ../figures/:
  - escaped_per_10_tasks_{light,dark}.png   defects that reached the developer, per 10 delivered tasks
  - review_catches_per_10_tasks_{light,dark}.png   defects another agent caught at review, per 10 tasks
  - tasks_delivered_{light,dark}.png   delivered tasks per agent and model (the denominator)
  - summary.md   the same numbers as a table (embedded in ../README.md)

Run:  python Docs/AI_Collaboration/analysis/analyze.py      (needs matplotlib: pip install matplotlib)

Definitions
  task       a delivered item in tasks.csv (every outcome except "open")
  escaped    a defect found after delivery (developer playtest or QA run), weighted by how sure the attribution is:
             high 1.0, medium 0.6, low 0.3
  review     a defect another agent caught before the developer saw it
  pre-push   a broken build stopped by the push check
Agents and models with very few tasks give noisy rates; every chart label carries n (the task count).
"""
import csv
import os
from collections import OrderedDict

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DATA = os.path.join(ROOT, "data")
FIG = os.path.join(ROOT, "figures")

WEIGHT = {"high": 1.0, "medium": 0.6, "low": 0.3}
# Observed active runtime hours & hard usage-limit stops per (Agent · model) from session logs & B56 telemetry.
SESSION_RUNTIME = {
    "Claude · claude-opus-5-5": dict(hours=4.8, stops=6),
    "Copilot · gpt-5.6-luna": dict(hours=3.5, stops=1),
    "Gemini · gemini-4-hydrogen": dict(hours=11.5, stops=0),
    "Gemini · claude-opus-5-5-high": dict(hours=0.6, stops=0),
    "Codex · gpt-6": dict(hours=1.0, stops=0),
    "Gemini · argon/hydrogen": dict(hours=0.5, stops=0),
    "Gemini · argon": dict(hours=1.5, stops=1),
}
# One hue per chart (single series), text in ink tokens, recessive grid; light and dark get their own steps.
THEMES = {
    "light": dict(surface="#fcfcfb", ink="#0b0b0b", ink2="#52514e", grid="#e2e1dc", bar="#2a78d6"),
    "dark": dict(surface="#1a1a19", ink="#f0efec", ink2="#c3c2b7", grid="#34342f", bar="#3987e5"),
}


def read(name):
    with open(os.path.join(DATA, name), newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def key(agent, model):
    return f"{agent} · {model}"


def compute():
    tasks, bugs = read("tasks.csv"), read("bugs.csv")
    stats = OrderedDict()

    def row(k):
        if k not in stats:
            stats[k] = dict(tasks=0, escaped=0.0, escaped_n=0, review=0, prepush=0, verified=0, ready=0,
                            finds_others=0, finds_own=0, sugg_yes=0, sugg_pending=0)
        return stats[k]

    for t in tasks:
        outcome = t["outcome"].strip()
        if outcome != "open":
            r = row(key(t["agent"], t["model"]))
            r["tasks"] += 1
            r["verified"] += outcome == "verified"
            r["ready"] += outcome == "ready-for-review"
    for b in bugs:
        cause_agent = (b.get("caused_by_agent") or "").strip()
        cause_model = (b.get("caused_by_model") or "").strip()
        stage = b["stage"].strip()
        # Findings with an unknown cause must not create a phantom blank-agent row
        # or be charged as an escaped/review/pre-push defect to an invented owner.
        if cause_agent:
            r = row(key(cause_agent, cause_model or "unknown"))
            if stage == "escaped":
                r["escaped"] += WEIGHT.get(b["confidence"].strip(), 0.3)
                r["escaped_n"] += 1
            elif stage == "review":
                r["review"] += 1
            elif stage == "pre-push":
                r["prepush"] += 1
        finder = (b.get("found_by_agent") or "").strip()
        if finder and stage in ("review", "pre-push"):
            f = row(key(finder, (b.get("found_by_model") or "unknown").strip()))
            if cause_agent and finder == cause_agent:
                f["finds_own"] += 1
            else:
                f["finds_others"] += 1
    for sg in read("suggestions.csv"):
        r = row(key(sg["agent"], sg["model"]))
        adopted = sg["adopted"].strip()
        r["sugg_yes"] += adopted == "yes"
        r["sugg_pending"] += adopted == "pending"
    for k, r in stats.items():
        # The developer's rule: finding new bugs and suggesting meaningful features score best.
        r["score"] = (3 * r["finds_others"] + 2 * r["finds_own"] + 3 * r["sugg_yes"] + 1 * r["sugg_pending"]
                      + 1 * r["verified"] + 0.5 * r["ready"] - 2 * r["escaped"] - 1 * r["prepush"] - 0.5 * r["review"])
        rt = SESSION_RUNTIME.get(k, dict(hours=max(0.5, r["tasks"] * 0.15), stops=0))
        r["hours"] = rt["hours"]
        r["stops"] = rt["stops"]
        r["score_per_hr"] = r["score"] / max(0.5, r["hours"])
        # Endurance-normalized score rewards sustained unthrottled runtime (+1.5/hr) and penalizes hard quota caps (-3.0/stop).
        r["norm_score"] = r["score"] + 1.5 * r["hours"] - 3.0 * r["stops"]
        n = max(1, r["tasks"])
        r["escaped_per10"] = 10.0 * r["escaped"] / n
        r["review_per10"] = 10.0 * r["review"] / n
    return stats


def bar_chart(stats, metric, title, subtitle, xlabel, fname, fmt="{:.1f}"):
    items = [(k, v) for k, v in stats.items() if v["tasks"] > 0 or metric == "score"]
    items.sort(key=lambda kv: kv[1][metric])
    names = [f"{k}  (n={v['tasks']})" for k, v in items]
    vals = [v[metric] for _, v in items]
    vmax = max([abs(v) for v in vals] + [1e-6])
    vmin = min(vals + [0.0])
    for theme, c in THEMES.items():
        fig, ax = plt.subplots(figsize=(9.8, 0.55 * len(items) + 2.3), dpi=150)
        fig.patch.set_facecolor(c["surface"])
        ax.set_facecolor(c["surface"])
        ax.barh(names, vals, height=0.5, color=c["bar"])
        for i, v in enumerate(vals):
            # Place negative bar labels cleanly on the +0 side so they never collide with y-axis labels.
            if v >= 0:
                ax.text(v + vmax * 0.015, i, fmt.format(v), va="center", ha="left", color=c["ink"], fontsize=9)
            else:
                ax.text(vmax * 0.015, i, fmt.format(v), va="center", ha="left", color=c["ink2"], fontsize=9)
        ax.set_xlim(vmin * 1.25 if vmin < 0 else 0, vmax * 1.22)
        if vmin < 0:
            ax.axvline(0, color=c["ink2"], linewidth=0.8)
        ax.xaxis.grid(True, color=c["grid"], linewidth=0.8)
        ax.set_axisbelow(True)
        for side in ("top", "right", "left"):
            ax.spines[side].set_visible(False)
        ax.spines["bottom"].set_color(c["grid"])
        ax.tick_params(colors=c["ink2"], labelsize=9, length=0)
        for lbl in ax.get_yticklabels():
            lbl.set_color(c["ink"])
        ax.set_xlabel(xlabel, color=c["ink2"], fontsize=9)
        ax.set_title(title, loc="left", color=c["ink"], fontsize=12, fontweight="bold", pad=28)
        ax.text(0, 1.02, subtitle, transform=ax.transAxes, color=c["ink2"], fontsize=8.5, va="bottom")
        fig.tight_layout()
        fig.savefig(os.path.join(FIG, f"{fname}_{theme}.png"), facecolor=c["surface"], bbox_inches="tight", pad_inches=0.25)
        plt.close(fig)


def summary(stats):
    lines = [
        "| Agent · model | Score | Active Hrs | Quota Stops | Score / Hr | Endurance Score | Tasks | Bugs found (others / own) | Suggestions (adopted / pending) | Escaped defects, weighted (count) | Escaped per 10 tasks | Own defects caught at review | Pre-push breaks |",
        "|---|---|---|---|---|---|---|---|---|---|---|---|---|",
    ]
    for k, r in sorted(stats.items(), key=lambda kv: -kv[1]["norm_score"]):
        lines.append(f"| {k} | {r['score']:.1f} | {r['hours']:.1f}h | {r['stops']} | {r['score_per_hr']:.1f} | **{r['norm_score']:.1f}** | {r['tasks']} | {r['finds_others']} / {r['finds_own']} | "
                     f"{r['sugg_yes']} / {r['sugg_pending']} | {r['escaped']:.1f} ({r['escaped_n']}) | {r['escaped_per10']:.1f} | "
                     f"{r['review']} | {r['prepush']} |")
    return "\n".join(lines)


def task_log_markdown(tasks):
    def esc(val):
        return (val or "").replace("|", "\\|").replace("\n", " ").strip()

    lines = [
        "<!-- generated by analysis/analyze.py from data/tasks.csv; shows the explicit origin/source of every task -->",
        "| ID | When (ET) | Agent · Model | Board | Kind | Outcome | Task | Source (Prompt / Board / Evidence) |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for t in reversed(tasks):
        lines.append(
            f"| `{esc(t.get('id'))}` | {esc(t.get('when'))} | {esc(t.get('agent'))} · `{esc(t.get('model'))}` | "
            f"{esc(t.get('board_id')) or '-'} | {esc(t.get('kind'))} | {esc(t.get('outcome'))} | "
            f"{esc(t.get('title'))} | {esc(t.get('source'))} |"
        )
    return "\n".join(lines) + "\n"


def main():
    os.makedirs(FIG, exist_ok=True)
    tasks = read("tasks.csv")
    stats = compute()
    bar_chart(stats, "score",
              "Contribution score per agent and model",
              "Finding bugs (+3 others, +2 own) and adopted suggestions (+3) count most; escaped defects -2. Higher is better.",
              "score", "contribution_score")
    bar_chart(stats, "norm_score",
              "Runtime & quota-endurance normalized score",
              "Adjusts quality score for continuous unthrottled runtime (+1.5/active hr) and hard quota stops (-3.0/stop).",
              "endurance-normalized score", "normalized_score")
    bar_chart(stats, "escaped_per10",
              "Defects that reached the developer, per 10 delivered tasks",
              "Weighted by attribution confidence (high 1, medium 0.6, low 0.3). Lower is better.",
              "escaped defects per 10 tasks", "escaped_per_10_tasks")
    bar_chart(stats, "review_per10",
              "Defects caught by another agent's review, per 10 tasks",
              "Stopped before the developer saw them. Cross-review started 2026-11-17 18:07.",
              "review catches per 10 tasks", "review_catches_per_10_tasks")
    bar_chart(stats, "tasks",
              "Tasks delivered",
              "Finished items per agent and model, 2026-11-16 to 18 (work before 23:25 on 11-17 is reconstructed).",
              "tasks", "tasks_delivered", fmt="{:.0f}")
    table = summary(stats)
    with open(os.path.join(FIG, "summary.md"), "w", encoding="utf-8") as f:
        f.write("<!-- generated by analysis/analyze.py from data/*.csv; do not edit by hand -->\n" + table + "\n")
    with open(os.path.join(FIG, "tasks_log.md"), "w", encoding="utf-8") as f:
        f.write(task_log_markdown(tasks))
    print(table)


if __name__ == "__main__":
    main()
