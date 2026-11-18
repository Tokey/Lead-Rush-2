"""Plot where the real bot AI's shots land on a stationary and on a strafing player (pose-check -lr2AimTest).

Input : the harness CSV (default Logs/LR2_AimTest_C11/bot_aim_shots.csv): one row per bot shot with the point where the
        shot crossed the vertical plane through the player (perpendicular to the line of fire), lateral and height in m,
        the player's sideways speed, the same offset signed along the player's motion, and the real hit result and zone.
Output: Docs/Media/bot/bot_aim_real_{light,dark}.png and a copy of the CSV (the table view) in Docs/Media/bot/.
Palette: validated two-slot categorical (dataviz reference palette, light and dark), misses as hollow rings so outcome is
         never colour alone.
"""
from pathlib import Path
import csv
import shutil
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Circle, Rectangle

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Docs" / "Media" / "bot"
THEMES = {
    "light": dict(surface="#fcfcfb", ink="#0b0b0b", ink2="#52514e", muted="#898781", grid="#e1e0d9", base="#c3c2b7",
                  body="#2a78d6", head="#eb6834", sil="#ecebe6"),
    "dark": dict(surface="#1a1a19", ink="#f0efec", ink2="#c3c2b7", muted="#898781", grid="#2c2c2a", base="#383835",
                 body="#3987e5", head="#d95926", sil="#262624"),
}
XLIM, YLIM = (-1.2, 1.2), (0.0, 2.2)


def load(path: Path):
    rows = []
    with path.open(newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            rows.append(dict(phase=r["phase"], lat=float(r["lateral_m"]), h=float(r["height_m"]), dist=float(r["distance_m"]),
                             v=float(r["target_lateral_speed"]), lead=float(r["lead_lateral_m"]), hit=r["hit"] == "1", zone=r["zone"]))
    return rows


def stats(rows, moving):
    n = len(rows)
    hits = sum(r["hit"] for r in rows)
    head = sum(r["hit"] and r["zone"] == "Head" for r in rows)
    misses = [r for r in rows if not r["hit"]]
    behind = sum(1 for r in misses if abs(r["v"]) > 0.5 and r["lead"] < -0.25)
    dist = sum(r["dist"] for r in rows) / n if n else 0
    return n, hits, head, len(misses), behind, dist


def silhouette(ax, t):
    for x0, y0, w, h in [(-0.2, 0.0, 0.4, 0.95), (-0.27, 0.95, 0.54, 0.55)]:
        ax.add_patch(Rectangle((x0, y0), w, h, facecolor=t["sil"], edgecolor=t["base"], linewidth=1, zorder=0))
    ax.add_patch(Circle((0, 1.66), 0.12, facecolor=t["sil"], edgecolor=t["base"], linewidth=1, zorder=0))


def style(ax, t):
    ax.set_facecolor(t["surface"])
    for sp in ax.spines.values():
        sp.set_color(t["base"])
    ax.tick_params(colors=t["muted"], labelsize=9)
    ax.grid(True, color=t["grid"], linewidth=0.8)
    ax.set_axisbelow(True)


def panel(ax, hax, rows, t, moving, title):
    style(ax, t)
    silhouette(ax, t)
    key = "lead" if moving else "lat"
    inside = [r for r in rows if XLIM[0] <= r[key] <= XLIM[1] and YLIM[0] <= r["h"] <= YLIM[1]]
    misses = [r for r in inside if not r["hit"]]
    body = [r for r in inside if r["hit"] and r["zone"] != "Head"]
    head = [r for r in inside if r["hit"] and r["zone"] == "Head"]
    ax.scatter([r[key] for r in misses], [r["h"] for r in misses], s=38, facecolors="none", edgecolors=t["muted"], linewidths=1.4, label="Miss", zorder=2)
    ax.scatter([r[key] for r in body], [r["h"] for r in body], s=38, c=t["body"], edgecolors=t["surface"], linewidths=1.2, label="Hit, body or limbs", zorder=3)
    ax.scatter([r[key] for r in head], [r["h"] for r in head], s=38, c=t["head"], edgecolors=t["surface"], linewidths=1.2, label="Hit, head", zorder=4)
    ax.set_xlim(*XLIM); ax.set_ylim(*YLIM); ax.set_aspect("equal")
    ax.set_ylabel("height above the feet (m)", color=t["ink2"], fontsize=10)
    ax.set_title(title, color=t["ink"], fontsize=12, loc="left", pad=8)
    if moving:
        ax.axvline(0, color=t["base"], linewidth=1, zorder=1)
        ax.text(XLIM[0] + 0.04, 2.1, "behind your motion", color=t["ink2"], fontsize=9, va="top")
        ax.text(XLIM[1] - 0.04, 2.1, "ahead of it", color=t["ink2"], fontsize=9, va="top", ha="right")
    out = len(rows) - len(inside)
    if out:
        ax.text(XLIM[1] - 0.04, 0.06, f"{out} shots off the chart", color=t["muted"], fontsize=8, ha="right")
    # lateral distribution of all shots on the same x scale
    style(hax, t)
    vals = [min(max(r[key], XLIM[0]), XLIM[1]) for r in rows]
    bins = [XLIM[0] + i * 0.1 for i in range(25)]
    hax.hist(vals, bins=bins, color=t["body"], edgecolor=t["surface"], linewidth=2)
    hax.set_xlim(*XLIM)
    hax.set_ylabel("shots", color=t["ink2"], fontsize=10)
    hax.set_xlabel("sideways offset (m), + = " + ("ahead of your motion" if moving else "right of you from the bot's view"), color=t["ink2"], fontsize=10)
    for x in (-0.27, 0.27):
        hax.axvline(x, color=t["base"], linewidth=1, linestyle=(0, (3, 3)))


def plot(rows, theme, out_png, meta):
    t = THEMES[theme]
    fig = plt.figure(figsize=(12, 7.4), dpi=110, facecolor=t["surface"])
    gs = fig.add_gridspec(2, 2, height_ratios=[3.3, 1], hspace=0.28, wspace=0.16, left=0.06, right=0.99, top=0.86, bottom=0.08)
    st = [r for r in rows if r["phase"] == "stationary"]
    mv = [r for r in rows if r["phase"] == "moving"]
    s1, s2 = stats(st, False), stats(mv, True)
    t1 = f"Standing still: {s1[0]} shots, {100 * s1[1] / max(1, s1[0]):.0f} % hit ({s1[2]} to the head)"
    t2 = f"Strafing: {s2[0]} shots, {100 * s2[1] / max(1, s2[0]):.0f} % hit, {100 * s2[4] / max(1, s2[3]):.0f} % of misses behind you"
    a1, a2 = fig.add_subplot(gs[0, 0]), fig.add_subplot(gs[0, 1])
    h1, h2 = fig.add_subplot(gs[1, 0]), fig.add_subplot(gs[1, 1])
    panel(a1, h1, st, t, False, t1)
    panel(a2, h2, mv, t, True, t2)
    fig.text(0.06, 0.955, "Where the bot's shots cross your body, in the real game", color=t["ink"], fontsize=15, weight="bold")
    fig.text(0.06, 0.915, meta, color=t["ink2"], fontsize=10)
    handles, labels = a1.get_legend_handles_labels()
    leg = fig.legend(handles, labels, loc="upper right", bbox_to_anchor=(0.99, 0.975), ncol=3, frameon=False, fontsize=10)
    for txt in leg.get_texts():
        txt.set_color(t["ink2"])
    fig.savefig(out_png, facecolor=t["surface"])
    plt.close(fig)


def main():
    src = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "Logs" / "LR2_AimTest_C11" / "bot_aim_shots.csv"
    rows = load(src)
    OUT.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(src, OUT / "bot_aim_shots.csv")
    d = sum(r["dist"] for r in rows) / max(1, len(rows))
    meta = (f"Real BotController AI (perception, goal ladder, human aim model, recoil pull-down) with an MK18 at about {d:.0f} m, "
            f"TestRange, 12 s each. One point per shot where it crossed the plane through you. Data: bot_aim_shots.csv")
    for theme in THEMES:
        plot(rows, theme, OUT / f"bot_aim_real_{theme}.png", meta)
        print("wrote", (OUT / f"bot_aim_real_{theme}.png").relative_to(ROOT))
    for ph in ("stationary", "moving"):
        sub = [r for r in rows if r["phase"] == ph]
        n, hits, head, miss, behind, dist = stats(sub, ph == "moving")
        print(f"{ph}: shots {n} hits {hits} head {head} misses {miss} behind {behind} mean distance {dist:.1f} m")


if __name__ == "__main__":
    main()
