"""Render the LR2 Bot Brain simulations as small, deterministic GIFs.

The constants mirror the interactive artifact in Docs/Bot/bot-brain.html and the
runtime notes in Docs/LR2_Bot_Explainer.md. Run from the repository root:
    python Tools/make_bot_gifs.py
"""
from pathlib import Path
import math

import matplotlib.pyplot as plt
from matplotlib.animation import FuncAnimation, PillowWriter
import numpy as np


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Docs" / "Media" / "bot"
FPS = 24


def save_animation(name, frames, draw, interval=1000 / FPS):
    OUT.mkdir(parents=True, exist_ok=True)
    fig, ax = plt.subplots(figsize=(7, 3.8), dpi=120)
    fig.patch.set_facecolor("#111512")
    animation = FuncAnimation(fig, lambda i: draw(ax, frames[i]), frames=len(frames),
                              interval=interval, repeat=True)
    animation.save(OUT / name, writer=PillowWriter(fps=FPS))
    plt.close(fig)


def perception():
    frames = []
    meter = 0.0
    for i in range(72):
        distance = 10.0 + 18.0 * (0.5 + 0.5 * math.sin(i / 18.0))
        angle = 70.0 * math.sin(i / 24.0)
        in_cone = abs(angle) <= 55.0 or (distance < 6.0 and abs(angle) < 100.0)
        rate = 0.9 + 9.0 / max(distance, 1.5)
        if in_cone:
            meter = min(1.25, meter + rate * 0.1)
        else:
            meter = max(0.0, meter - 0.05)
        frames.append((distance, angle, meter, in_cone))
    return frames


def draw_perception(ax, frame):
    distance, angle, meter, visible = frame
    ax.clear()
    ax.set_facecolor("#181e1a")
    ax.set_xlim(-80, 80)
    ax.set_ylim(0, 75)
    ax.axvspan(-55, 55, color="#2c6aa0", alpha=0.16)
    ax.scatter([0], [0], c="#6eaee6", s=55, label="bot")
    x = distance * math.sin(math.radians(angle))
    y = distance * math.cos(math.radians(angle))
    ax.scatter([x], [y], c="#ff7d3b" if visible else "#b3261e", s=65, label="target")
    ax.plot([0, x], [0, y], color="#c8cdc5", alpha=0.35)
    ax.set_title(f"Perception: detection meter {meter:.2f} / 1.00", color="#e6ebe4")
    ax.set_xlabel("angle from gaze (degrees)", color="#a3ada2")
    ax.set_ylabel("distance (m)", color="#a3ada2")
    ax.tick_params(colors="#a3ada2")
    ax.legend(facecolor="#181e1a", labelcolor="#e6ebe4", loc="upper right")


def fitts():
    frames = []
    distance, target_distance, width, skill, stress = 60.0, 20.0, 0.34, 7.0, 0.20
    half = math.degrees(math.atan2(width, max(1.5, target_distance)))
    index = math.log2(1.0 + distance / max(0.4, half))
    skill_scale = max(0.62, min(1.55, 7.5 / max(2.5, skill)))
    move_time = max(0.085, min(0.38, (0.065 + 0.042 * index) * skill_scale))
    sigma = max(0.35, min(6.5, distance * (0.045 + 0.045 * stress) * skill_scale))
    endpoint = distance + sigma * 0.35
    previous_position = 0.0
    for i in range(96):
        t = i / 120.0
        u = max(0.0, min(1.0, t / move_time))
        s = u ** 3 * (10.0 + u * (-15.0 + 6.0 * u))
        position = endpoint * s
        velocity = (position - previous_position) * 120.0
        previous_position = position
        frames.append((t, distance - position, position, abs(velocity)))
    return frames


def draw_fitts(ax, frame):
    t, error, position, velocity = frame
    ax.clear()
    ax.set_facecolor("#181e1a")
    ax.set_xlim(0, 0.8)
    ax.set_ylim(-5, 65)
    ax.axhline(0, color="#5cc46a", alpha=0.55)
    ax.scatter([t], [error], c="#ff7d3b", s=50)
    ax.plot([0, t], [60, error], color="#6eaee6", alpha=0.50)
    ax.text(0.02, 58, f"speed {velocity:.0f} deg/s", color="#a3ada2", fontsize=9)
    ax.set_title("Fitts flick + minimum-jerk speed: 10u³ - 15u⁴ + 6u⁵", color="#e6ebe4")
    ax.set_xlabel("time (s)", color="#a3ada2")
    ax.set_ylabel("remaining aim error (degrees)", color="#a3ada2")
    ax.tick_params(colors="#a3ada2")


def recoil():
    frames = []
    aim_speed = 7.0
    climb = 1.05 * 0.55
    interval = 60.0 / 700.0
    off = 0.0
    raw = 0.0
    velocity = 0.0
    heat = 0.0
    for i in range(120):
        t = i / 120.0
        if i < 10 and abs(t - round(t / interval) * interval) < 0.004:
            off += climb
            raw += climb
            heat = min(1.0, heat + 0.13)
        heat = max(0.0, heat - 0.95 / 120.0)
        error = -off
        if -error > 0.2 and t < 0.10:
            error *= 0.12
        elif -error > 0.3 and heat > 0.1:
            strength = max(0.55, min(0.92, aim_speed / 10.5)) * (1.0 - heat * 0.22)
            error *= max(0.45, min(0.94, strength))
        velocity += (aim_speed * aim_speed * error - 2.0 * 0.8 * aim_speed * velocity) / 120.0
        velocity = max(-900.0, min(900.0, velocity))
        off += max(-720.0 / 120.0, min(720.0 / 120.0, velocity / 120.0))
        frames.append((t, off, raw))
    return frames


def draw_recoil(ax, frame):
    t, controlled, raw = frame
    ax.clear()
    ax.set_facecolor("#181e1a")
    ax.set_xlim(0, 1.0)
    ax.set_ylim(0, 12)
    ax.plot([0, t], [0, controlled], color="#6eaee6", linewidth=2.5, label="bot pull-down")
    ax.plot([0, t], [0, raw], color="#a3ada2", linestyle="--", label="no pull-down")
    ax.set_title("ADS recoil: delayed human-scale compensation", color="#e6ebe4")
    ax.set_xlabel("time (s)", color="#a3ada2")
    ax.set_ylabel("muzzle climb (degrees)", color="#a3ada2")
    ax.tick_params(colors="#a3ada2")
    ax.legend(facecolor="#181e1a", labelcolor="#e6ebe4")


def adaptive():
    skill = 0.48
    streak = 0
    history = []
    events = ["win", "headshot", "win", "loss", "loss", "win", "win", "loss"]
    for event in events:
        if event in ("win", "headshot"):
            streak = max(1, streak + 1)
            skill = min(0.96, skill + 0.045 + (0.03 if streak >= 2 else 0)
                        + (0.02 if event == "headshot" else 0) + 0.25 * 0.03)
        else:
            streak = min(-1, streak - 1)
            skill = max(0.12, skill - 0.040 - (0.035 if streak <= -2 else 0))
        history.append(skill)
    return history


def draw_adaptive(ax, frame):
    history = frame
    ax.clear()
    ax.set_facecolor("#181e1a")
    ax.set_xlim(0, max(8, len(history) - 1))
    ax.set_ylim(0.1, 1.0)
    ax.plot(range(len(history)), history, color="#ff7d3b", linewidth=2.5)
    ax.scatter([len(history) - 1], [history[-1]], color="#ff7d3b", s=45)
    ax.axhline(0.12, color="#5cc46a", alpha=0.5)
    ax.axhline(0.96, color="#5cc46a", alpha=0.5)
    ax.set_title(f"Adaptive skill over a match: {history[-1]:.2f}", color="#e6ebe4")
    ax.set_xlabel("duel", color="#a3ada2")
    ax.set_ylabel("effective skill", color="#a3ada2")
    ax.tick_params(colors="#a3ada2")


def watch_bot():
    frames = []
    for i in range(116):
        if i < 5:
            goal = "Patrol"
        elif i < 23:
            goal = "Spot"
        elif i < 47:
            goal = "Flick"
        elif i < 77:
            goal = "Burst"
        elif i < 101:
            goal = "Flank"
        else:
            goal = "Hunt"

        if i < 23:
            bot_x = -18 + i * 1.55
            bot_y = -8 + math.sin(i * 0.22) * 2
            player_x = 20
            player_y = 10
        elif i < 47:
            u = (i - 23) / 24.0
            bot_x = 17 - u * 10
            bot_y = -6 + u * 6
            player_x = 20 - u * 3
            player_y = 10 - u * 12
        elif i < 77:
            u = (i - 47) / 30.0
            bot_x = 7 + u * 7
            bot_y = 0 - u * 2
            player_x = 17 - u * 5
            player_y = -2 - u * 3
        elif i < 101:
            u = (i - 77) / 24.0
            bot_x = 14 - u * 18
            bot_y = -2 - u * 8
            player_x = 12 - u * 8
            player_y = -5 - u * 2
        else:
            u = (i - 101) / 15.0
            bot_x = -4 - u * 8
            bot_y = -10 + u * 5
            player_x = 4
            player_y = -7

        meter = 0.0 if i < 5 else min(1.25, max(0.0, (i - 5) / 18.0))
        aim_error = 18 * math.exp(-max(0, i - 23) / 6.0) if i >= 23 else 18
        frames.append((bot_x, bot_y, player_x, player_y, goal, meter, aim_error))
    return frames


def draw_watch_bot(ax, frame):
    bot_x, bot_y, player_x, player_y, goal, meter, aim_error = frame
    ax.clear()
    ax.set_facecolor("#181e1a")
    ax.set_xlim(-26, 26)
    ax.set_ylim(-18, 18)
    ax.add_patch(plt.Rectangle((-22, -14), 44, 28, fill=False, edgecolor="#2d3630", linewidth=1))
    ax.plot([-12, -12, 7, 7, 16, 16],
            [-10, 6, 6, 12, 12, -12],
            color="#c8cdc5", linewidth=2, alpha=0.35)
    ax.scatter([bot_x], [bot_y], c="#6eaee6", s=110, label="bot")
    ax.scatter([player_x], [player_y], c="#ff7d3b", s=90, label="player")
    ax.plot([bot_x, player_x], [bot_y, player_y], color="#a3ada2", linewidth=1, alpha=0.45)

    ax.set_title(f"Watch a bot play - {goal}", color="#e6ebe4")
    ax.text(-24, 16.1, f"detection {meter:.2f}", color="#ff7d3b", fontsize=9)
    ax.text(-24, 14.3, f"aim error {aim_error:.1f} deg", color="#6eaee6", fontsize=9)
    ax.axhspan(-16.5, -14.8,
               xmin=0.0, xmax=min(1.0, meter / 1.25),
               color="#ff7d3b", alpha=0.85)
    ax.tick_params(colors="#a3ada2")
    ax.legend(facecolor="#181e1a", labelcolor="#e6ebe4", loc="lower right", fontsize=8)


def main():
    save_animation("watch-a-bot-play.gif", watch_bot(), draw_watch_bot)
    save_animation("perception-meter.gif", perception(), draw_perception)
    save_animation("fitts-minimum-jerk.gif", fitts(), draw_fitts)
    save_animation("recoil-pulldown.gif", recoil(), draw_recoil)
    history = adaptive()
    save_animation("adaptive-skill.gif", [history[:i] for i in range(1, len(history) + 1)], draw_adaptive)
    print(f"Wrote GIFs to {OUT}")


if __name__ == "__main__":
    main()
