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
            stats[k] = dict(tasks=0, escaped=0.0, escaped_n=0, review=0, prepush=0)
        return stats[k]

    for t in tasks:
        if t["outcome"].strip() != "open":
            row(key(t["agent"], t["model"]))["tasks"] += 1
    for b in bugs:
        r = row(key(b["caused_by_agent"], b["caused_by_model"]))
        stage = b["stage"].strip()
        if stage == "escaped":
            r["escaped"] += WEIGHT.get(b["confidence"].strip(), 0.3)
            r["escaped_n"] += 1
        elif stage == "review":
            r["review"] += 1
        elif stage == "pre-push":
            r["prepush"] += 1
    for r in stats.values():
        n = max(1, r["tasks"])
        r["escaped_per10"] = 10.0 * r["escaped"] / n
        r["review_per10"] = 10.0 * r["review"] / n
    return stats


def bar_chart(stats, metric, title, subtitle, xlabel, fname, fmt="{:.1f}"):
    items = [(k, v) for k, v in stats.items() if v["tasks"] > 0]
    items.sort(key=lambda kv: kv[1][metric])
    names = [f"{k}  (n={v['tasks']})" for k, v in items]
    vals = [v[metric] for _, v in items]
    vmax = max(vals + [1e-6])
    for theme, c in THEMES.items():
        fig, ax = plt.subplots(figsize=(8.6, 0.5 * len(items) + 2.0), dpi=150)
        fig.patch.set_facecolor(c["surface"])
        ax.set_facecolor(c["surface"])
        ax.barh(names, vals, height=0.5, color=c["bar"])
        for i, v in enumerate(vals):
            ax.text(v + vmax * 0.015, i, fmt.format(v), va="center", ha="left", color=c["ink"], fontsize=9)
        ax.set_xlim(0, vmax * 1.2)
        ax.xaxis.grid(True, color=c["grid"], linewidth=0.8)
        ax.set_axisbelow(True)
        for side in ("top", "right", "left"):
            ax.spines[side].set_visible(False)
        ax.spines["bottom"].set_color(c["grid"])
        ax.tick_params(colors=c["ink2"], labelsize=9, length=0)
        for lbl in ax.get_yticklabels():
            lbl.set_color(c["ink"])
        ax.set_xlabel(xlabel, color=c["ink2"], fontsize=9)
        ax.set_title(title, loc="left", color=c["ink"], fontsize=12, fontweight="bold", pad=24)
        ax.text(0, 1.02, subtitle, transform=ax.transAxes, color=c["ink2"], fontsize=8.5, va="bottom")
        fig.tight_layout()
        fig.savefig(os.path.join(FIG, f"{fname}_{theme}.png"), facecolor=c["surface"])
        plt.close(fig)


def summary(stats):
    lines = [
        "| Agent · model | Tasks delivered | Escaped defects, weighted (count) | Escaped per 10 tasks | Caught at review | Pre-push breaks |",
        "|---|---|---|---|---|---|",
    ]
    for k, r in sorted(stats.items(), key=lambda kv: -kv[1]["tasks"]):
        lines.append(f"| {k} | {r['tasks']} | {r['escaped']:.1f} ({r['escaped_n']}) | {r['escaped_per10']:.1f} | "
                     f"{r['review']} | {r['prepush']} |")
    return "\n".join(lines)


def main():
    os.makedirs(FIG, exist_ok=True)
    stats = compute()
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
    print(table)


if __name__ == "__main__":
    main()
