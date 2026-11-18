"""Render 1:1 visual replicas of every interactive canvas in Docs/Bot/bot-brain.html
into animated GIFs under Docs/Media/bot/.

Run from the repository root:
    python Tools/make_bot_gifs.py
"""
from __future__ import annotations

import math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Docs" / "Media" / "bot"

# Palette matching :root[data-theme="dark"] in Docs/Bot/bot-brain.html
BG = "#111512"
PANEL = "#181e1a"
PANEL2 = "#1f2621"
GRID = "#222a24"
LINE = "#2d3630"
INK = "#e6ebe4"
INK2 = "#a3ada2"
ACCENT = "#ff7d3b"   # Player / Flick / Accent Orange
BOT_C = "#6eaee6"    # Bot Blue
GOOD = "#5cc46a"     # LOS / Hit / Green
BAD = "#ff6b60"      # Blocked / Headshot / Bad Red
WARN = "#e2b54a"     # Flank / Contested Amber
PURPLE = "#b388ff"   # Claude Purple

# Arena geometry identical to Docs/Bot/bot-brain.html
W_ARENA, H_ARENA = 32.0, 18.0
WALLS = [
    (12.0, 1.5, 1.2, 6.5),
    (18.5, 10.5, 7.0, 1.2),
    (12.0, 12.5, 1.2, 4.0),
]
BIG_WALLS = [(x - 0.55, y - 0.55, w + 1.1, h + 1.1) for x, y, w, h in WALLS]
TACTICAL = [
    (5.0, 9.0), (8.0, 3.5), (8.0, 15.0), (12.6, 10.2), (16.0, 5.0),
    (16.0, 14.5), (22.0, 7.5), (22.0, 15.0), (27.0, 4.0), (28.5, 14.0), (29.5, 9.0),
]
NODES = list(TACTICAL)
for rx, ry, rw, rh in WALLS:
    for cx, cy in ((rx - 1, ry - 1), (rx + rw + 1, ry - 1), (rx - 1, ry + rh + 1), (rx + rw + 1, ry + rh + 1)):
        if 0.6 < cx < W_ARENA - 0.6 and 0.6 < cy < H_ARENA - 0.6:
            NODES.append((cx, cy))

GOALC = {
    "Patrol": INK2,
    "Investigate": GOOD,
    "Engage": ACCENT,
    "Flank": WARN,
    "Hunt": BOT_C,
}


def hex_rgba(h: str, alpha: int = 255):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), alpha)


def load_font(size: int, bold: bool = False):
    candidates = (
        [
            "C:/Windows/Fonts/consolab.ttf" if bold else "C:/Windows/Fonts/consola.ttf",
            "C:/Windows/Fonts/segoeuib.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf",
            "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf",
        ]
    )
    for path in candidates:
        if Path(path).exists():
            try:
                return ImageFont.truetype(path, size)
            except Exception:
                pass
    return ImageFont.load_default()


FONT_SM = load_font(20, False)   # 2x supersampled -> 10 px final
FONT_MD = load_font(24, False)   # -> 12 px final
FONT_LG = load_font(30, True)    # -> 15 px final
FONT_XL = load_font(34, True)    # -> 17 px final


def clamp(v, a, b):
    return max(a, min(b, v))


def lerp(a, b, t):
    return a + (b - a) * t


def wrap_ang(a):
    while a > math.pi:
        a -= 2.0 * math.pi
    while a < -math.pi:
        a += 2.0 * math.pi
    return a


def mulberry(seed: int):
    s = seed & 0xFFFFFFFF
    def nxt():
        nonlocal s
        s = (s + 0x6D2B79F5) & 0xFFFFFFFF
        t = ((s ^ (s >> 15)) * (1 | s)) & 0xFFFFFFFF
        t = (t + ((t ^ (t >> 7)) * (61 | t)) ^ t) & 0xFFFFFFFF
        return ((t ^ (t >> 14)) & 0xFFFFFFFF) / 4294967296.0
    return nxt


def seg_rect(ax, ay, bx, by, r):
    rx, ry, rw, rh = r
    t0, t1 = 0.0, 1.0
    dx, dy = bx - ax, by - ay
    for p, q in ((-dx, ax - rx), (dx, rx + rw - ax), (-dy, ay - ry), (dy, ry + rh - ay)):
        if p == 0.0:
            if q < 0.0:
                return False
        else:
            t = q / p
            if p < 0.0:
                if t > t1:
                    return False
                if t > t0:
                    t0 = t
            else:
                if t < t0:
                    return False
                if t < t1:
                    t1 = t
    return t0 <= t1


def clear_move(a, b):
    return not any(seg_rect(a[0], a[1], b[0], b[1], r) for r in BIG_WALLS)


def clear_los(a, b):
    return not any(seg_rect(a[0], a[1], b[0], b[1], r) for r in WALLS)


def route(frm, to):
    pts = [frm] + NODES + [to]
    n = len(pts)
    d = [float("inf")] * n
    prev = [-1] * n
    done = [False] * n
    d[0] = 0.0
    for _ in range(n):
        u = -1
        for i in range(n):
            if not done[i] and d[i] < float("inf") and (u < 0 or d[i] < d[u]):
                u = i
        if u < 0:
            break
        done[u] = True
        if u == n - 1:
            break
        for v in range(n):
            if not done[v] and clear_move(pts[u], pts[v]):
                nd = d[u] + math.hypot(pts[u][0] - pts[v][0], pts[u][1] - pts[v][1])
                if nd < d[v]:
                    d[v] = nd
                    prev[v] = u
    if d[n - 1] == float("inf"):
        return [to]
    out = []
    v = n - 1
    while v > 0:
        out.insert(0, pts[v])
        v = prev[v]
    return out


def new_canvas(w: int, h: int, bg: str = PANEL):
    img = Image.new("RGBA", (w * 2, h * 2), hex_rgba(bg))
    return img, ImageDraw.Draw(img, "RGBA"), w * 2, h * 2


def finish_frame(img: Image.Image, target_w: int, target_h: int) -> Image.Image:
    return img.resize((target_w, target_h), Image.Resampling.LANCZOS).convert("P", palette=Image.Palette.ADAPTIVE, colors=128)


def save_gif(name: str, pil_frames, fps: int = 20):
    OUT.mkdir(parents=True, exist_ok=True)
    path = OUT / name
    dur = int(round(1000 / fps))
    pil_frames[0].save(
        path,
        save_all=True,
        append_images=pil_frames[1:],
        duration=dur,
        loop=0,
        optimize=True,
    )
    print(f"wrote {path.name} ({len(pil_frames)} frames)")


def draw_grid_walls(draw, ox, oy, aw, ah):
    k = aw / W_ARENA
    draw.rectangle((ox, oy, ox + aw, oy + ah), fill=hex_rgba(PANEL), outline=hex_rgba(LINE), width=2)
    for x in range(0, int(W_ARENA) + 1, 2):
        px = ox + x * k
        draw.line((px, oy, px, oy + ah), fill=hex_rgba(GRID), width=2)
    for y in range(0, int(H_ARENA) + 1, 2):
        py = oy + y * k
        draw.line((ox, py, ox + aw, py), fill=hex_rgba(GRID), width=2)
    for rx, ry, rw, rh in WALLS:
        draw.rectangle((ox + rx * k, oy + ry * k, ox + (rx + rw) * k, oy + (ry + rh) * k), fill=hex_rgba(INK2))
    return k


def draw_dashed_line(draw, p0, p1, color, width=3, dash=12, gap=10):
    x0, y0 = p0
    x1, y1 = p1
    dist = math.hypot(x1 - x0, y1 - y0)
    if dist < 1e-3:
        return
    dx, dy = (x1 - x0) / dist, (y1 - y0) / dist
    pos = 0.0
    while pos < dist:
        e = min(dist, pos + dash)
        draw.line((x0 + dx * pos, y0 + dy * pos, x0 + dx * e, y0 + dy * e), fill=color, width=width)
        pos += dash + gap


def draw_pie(base_img, cx, cy, r_px, a0_rad, a1_rad, fill_rgba):
    ov = Image.new("RGBA", base_img.size, (0, 0, 0, 0))
    od = ImageDraw.Draw(ov, "RGBA")
    od.pieslice((cx - r_px, cy - r_px, cx + r_px, cy + r_px), math.degrees(a0_rad), math.degrees(a1_rad), fill=fill_rgba)
    return Image.alpha_composite(base_img, ov)


def draw_translucent_ellipse(base_img, bbox, fill_rgba, outline_rgba=None, width=4):
    ov = Image.new("RGBA", base_img.size, (0, 0, 0, 0))
    od = ImageDraw.Draw(ov, "RGBA")
    od.ellipse(bbox, fill=fill_rgba, outline=outline_rgba, width=width)
    return Image.alpha_composite(base_img, ov)


def draw_translucent_line(base_img, pts, fill_rgba, width=30):
    ov = Image.new("RGBA", base_img.size, (0, 0, 0, 0))
    od = ImageDraw.Draw(ov, "RGBA")
    od.line(pts, fill=fill_rgba, width=width, joint="curve")
    return Image.alpha_composite(base_img, ov)


# ------------------------------------------------------------------ 1. Watch a bot play
def make_watch_gif():
    W_PX, H_PX = 840, 620
    pr = [(25.0, 4.5), (29.3, 9.5), (24.0, 15.6), (16.5, 15.2), (21.0, 8.5)]
    wrnd = mulberry(42)
    Bt = {
        "x": 5.0, "y": 9.0, "yaw": 0.0, "vel": 0.0, "goal": "Patrol",
        "path": [], "meter": 0.0, "seenAt": -99.0, "last": None,
        "noise": None, "noiseAt": -99.0, "flankCd": 0.0, "flick": None,
        "nextShot": 0.0, "burstLeft": 0, "think": 0.0, "look": 0.0,
        "visits": {}, "vis": False, "err": 90.0,
    }
    Pt = {"x": 25.0, "y": 4.5, "wp": 0, "wait": 0.0, "lastShot": -99.0, "anchor": (25.0, 4.5, 0.0), "moving": False}
    T, logs, tracers = 0.0, [], []

    def ang(a, b):
        return math.atan2(b[1] - a[1], b[0] - a[0])

    def set_route(to, goal):
        Bt["path"] = route((Bt["x"], Bt["y"]), to)
        Bt["goal"] = goal

    def step(dt):
        nonlocal T, tracers
        T += dt
        # Player pauses near corner for ~7s midway so we showcase Flank cleanly, otherwise moves
        camp = 8.0 <= T <= 15.5
        Pt["moving"] = False
        if not camp:
            if Pt["wait"] > 0:
                Pt["wait"] -= dt
            else:
                tg = pr[Pt["wp"]]
                dx, dy = tg[0] - Pt["x"], tg[1] - Pt["y"]
                dd = math.hypot(dx, dy)
                if dd < 0.15:
                    Pt["wp"] = (Pt["wp"] + 1) % len(pr)
                    Pt["wait"] = 1.4
                else:
                    s = min(dd, 3.2 * dt)
                    Pt["x"] += dx / dd * s
                    Pt["y"] += dy / dd * s
                    Pt["moving"] = True
        if math.hypot(Pt["x"] - Pt["anchor"][0], Pt["y"] - Pt["anchor"][1]) > 3.0:
            Pt["anchor"] = (Pt["x"], Pt["y"], T)
        pd = math.hypot(Pt["x"] - Bt["x"], Pt["y"] - Bt["y"])
        los = clear_los((Bt["x"], Bt["y"]), (Pt["x"], Pt["y"]))
        if los and pd < 26 and T - Pt["lastShot"] > 1.3:
            Pt["lastShot"] = T
            if pd < 30:
                Bt["noise"] = (Pt["x"] + (wrnd() - 0.5) * pd * 0.16, Pt["y"] + (wrnd() - 0.5) * pd * 0.16)
                Bt["noiseAt"] = T

        Bt["look"] -= dt
        if Bt["look"] <= 0:
            Bt["look"] += 0.1
            a = abs(wrap_ang(ang((Bt["x"], Bt["y"]), (Pt["x"], Pt["y"])) - Bt["yaw"])) * 180.0 / math.pi
            in_cone = a <= 55.0 or (pd < 6.0 and a < 100.0)
            Bt["vis"] = in_cone and los and pd <= 70.0
            if Bt["vis"]:
                rate = 0.9 + 9.0 / max(pd, 1.5)
                if Pt["moving"]:
                    rate *= 1.35
                if T - Pt["lastShot"] < 1.0:
                    rate *= 2.2
                if a < 12.0:
                    rate *= 1.4
                Bt["meter"] = min(1.25, Bt["meter"] + rate * 0.1)
            else:
                Bt["meter"] = max(0.0, Bt["meter"] - 0.05)
            if Bt["vis"] and Bt["meter"] >= 1.0:
                Bt["seenAt"] = T
                Bt["last"] = (Pt["x"], Pt["y"])
            logs.append((T, Bt["meter"], Bt["goal"], min(60.0, abs(wrap_ang(ang((Bt["x"], Bt["y"]), (Pt["x"], Pt["y"])) - Bt["yaw"])) * 180.0 / math.pi)))
            while logs and logs[0][0] < T - 12.0:
                logs.pop(0)

        Bt["think"] -= dt
        if Bt["think"] <= 0:
            Bt["think"] += 0.25 * (0.85 + wrnd() * 0.3)
            spotted = Bt["vis"] and Bt["meter"] >= 1.0
            camp_for = T - Pt["anchor"][2]
            if Bt["goal"] == "Flank" and Bt["path"] and not spotted:
                pass
            elif spotted and camp_for > 5.0 and T > Bt["flankCd"]:
                to_p = ang((Pt["x"], Pt["y"]), (Bt["x"], Bt["y"]))
                best, bs = None, -1.0
                for n in TACTICAL:
                    dd = math.hypot(n[0] - Pt["x"], n[1] - Pt["y"])
                    if dd < 4 or dd > 18:
                        continue
                    off = abs(abs(wrap_ang(ang((Pt["x"], Pt["y"]), n) - to_p)) - math.pi / 2.0)
                    sc = 2.0 - off - abs(dd - 10.0) * 0.05
                    if sc > bs and clear_los(n, (Pt["x"], Pt["y"])):
                        bs = sc
                        best = n
                if best:
                    set_route(best, "Flank")
                    Bt["flankCd"] = T + 18.0
                else:
                    Bt["goal"] = "Engage"
                    Bt["path"] = []
            elif spotted:
                Bt["goal"] = "Engage"
                Bt["path"] = []
            elif Bt["last"] and T - Bt["seenAt"] <= 10.0:
                if Bt["goal"] != "Hunt" or not Bt["path"]:
                    set_route(Bt["last"], "Hunt")
            elif Bt["noise"] and T - Bt["noiseAt"] < 8.0:
                if Bt["goal"] != "Investigate":
                    set_route(Bt["noise"], "Investigate")
            elif Bt["goal"] != "Patrol" or not Bt["path"]:
                best, bs = TACTICAL[0], -1e9
                for n in TACTICAL:
                    dd = math.hypot(n[0] - Bt["x"], n[1] - Bt["y"])
                    since = min(60.0, T - Bt["visits"].get(n, -60.0))
                    sc = since * 0.5 - abs(dd - 18.0) * 0.3 + wrnd() * 4.0
                    if dd > 3.0 and sc > bs:
                        bs = sc
                        best = n
                Bt["visits"][best] = T
                set_route(best, "Patrol")
            if Bt["goal"] == "Hunt" and not Bt["path"] and not Bt["vis"]:
                Bt["last"] = None
                Bt["goal"] = "Patrol"
            if Bt["goal"] == "Investigate" and not Bt["path"]:
                Bt["noise"] = None
                Bt["goal"] = "Patrol"

        if Bt["path"] and Bt["goal"] != "Engage":
            tg = Bt["path"][0]
            dx, dy = tg[0] - Bt["x"], tg[1] - Bt["y"]
            dd = math.hypot(dx, dy)
            sp = 3.0 if Bt["goal"] == "Patrol" else 4.6
            if dd < 0.25:
                Bt["path"].pop(0)
            else:
                s = min(dd, sp * dt)
                Bt["x"] += dx / dd * s
                Bt["y"] += dy / dd * s

        if Bt["goal"] == "Engage":
            want = ang((Bt["x"], Bt["y"]), (Pt["x"], Pt["y"]))
        elif Bt["goal"] == "Hunt" and Bt["last"]:
            want = ang((Bt["x"], Bt["y"]), Bt["last"])
        elif Bt["goal"] == "Investigate" and Bt["noise"]:
            want = ang((Bt["x"], Bt["y"]), Bt["noise"])
        elif Bt["path"]:
            want = ang((Bt["x"], Bt["y"]), Bt["path"][0])
        else:
            want = Bt["yaw"]

        err_deg = wrap_ang(want - Bt["yaw"]) * 180.0 / math.pi
        if Bt["flick"] is None and abs(err_deg) > 10.5:
            half = math.degrees(math.atan2(0.34, max(1.5, pd)))
            fid = math.log2(1.0 + abs(err_deg) / max(0.4, half))
            mt = clamp((0.065 + 0.042 * fid) * clamp(7.5 / 7.0, 0.62, 1.55), 0.085, 0.38)
            sc = ((wrnd() + wrnd()) - 1.0) * clamp(abs(err_deg) * 0.05, 0.35, 6.5)
            Bt["flick"] = (T, mt, Bt["yaw"], Bt["yaw"] + math.radians(err_deg + sc))
        if Bt["flick"] is not None:
            t0, mt, fr, to = Bt["flick"]
            u = clamp((T - t0) / mt, 0.0, 1.0)
            s = u ** 3 * (10.0 + u * (-15.0 + 6.0 * u))
            ny = fr + (to - fr) * s
            step_r = ny - Bt["yaw"]
            Bt["vel"] = step_r / dt
            if u >= 1.0:
                Bt["flick"] = None
        else:
            w = 7.0
            Bt["vel"] += (w * w * math.radians(err_deg) - 2.0 * 0.8 * w * Bt["vel"]) * dt
            step_r = Bt["vel"] * dt
        Bt["yaw"] = wrap_ang(Bt["yaw"] + clamp(step_r, -4 * math.pi * dt, 4 * math.pi * dt))
        Bt["err"] = abs(wrap_ang(ang((Bt["x"], Bt["y"]), (Pt["x"], Pt["y"])) - Bt["yaw"])) * 180.0 / math.pi

        tol = max(3.5, math.degrees(math.atan2(0.45, max(pd, 0.5))))
        if Bt["goal"] == "Engage" and Bt["err"] <= tol and T >= Bt["nextShot"]:
            if pd < 13.0:
                Bt["burstLeft"] = 1
            elif Bt["burstLeft"] <= 0:
                Bt["burstLeft"] = 3 + int(wrnd() * 4)
            miss = (wrnd() - 0.5) * 2.0 * math.radians(Bt["err"])
            L = min(pd + 2.0, 40.0)
            tracers.append((Bt["x"], Bt["y"], Bt["x"] + math.cos(Bt["yaw"] + miss) * L, Bt["y"] + math.sin(Bt["yaw"] + miss) * L, T))
            Bt["burstLeft"] -= 1
            Bt["nextShot"] = T + (60.0 / 700.0 if (pd < 13.0 or Bt["burstLeft"] > 0) else 0.35 + wrnd() * 0.2)
        tracers = [tr for tr in tracers if T - tr[4] < 0.09]

    frames = []
    steps_per_frame = 3  # 1/60 * 3 = 20 fps real-time
    for fi in range(180):  # 9 s full loop through Patrol -> Engage -> Flank -> Hunt
        for _ in range(steps_per_frame):
            step(1.0 / 60.0)
        if fi % 2 != 0:
            continue
        img, d, w2, h2 = new_canvas(W_PX, H_PX, BG)
        pad = 24
        aw = w2 - pad * 2
        ah = int(aw * H_ARENA / W_ARENA)
        k = draw_grid_walls(d, pad, pad, aw, ah)
        ox, oy = pad, pad
        bx, by = ox + Bt["x"] * k, oy + Bt["y"] * k
        px, py = ox + Pt["x"] * k, oy + Pt["y"] * k
        img = draw_pie(img, bx, by, 14 * k, Bt["yaw"] - math.radians(55), Bt["yaw"] + math.radians(55), hex_rgba(BOT_C, 34))
        d = ImageDraw.Draw(img, "RGBA")
        for nx, ny in TACTICAL:
            npx, npy = ox + nx * k, oy + ny * k
            d.ellipse((npx - 5, npy - 5, npx + 5, npy + 5), fill=hex_rgba(INK2, 95))
        gc = GOALC.get(Bt["goal"], INK2)
        if Bt["path"]:
            pts_px = [(bx, by)] + [(ox + q[0] * k, oy + q[1] * k) for q in Bt["path"]]
            for i in range(len(pts_px) - 1):
                draw_dashed_line(d, pts_px[i], pts_px[i + 1], hex_rgba(gc, 230), width=3)
        if Bt["vis"]:
            d.line((bx, by, px, py), fill=hex_rgba(GOOD, 140), width=3)
        if Bt["last"] and not Bt["vis"]:
            lx, ly = ox + Bt["last"][0] * k, oy + Bt["last"][1] * k
            d.line((lx - 11, ly - 11, lx + 11, ly + 11), fill=hex_rgba(BOT_C), width=4)
            d.line((lx + 11, ly - 11, lx - 11, ly + 11), fill=hex_rgba(BOT_C), width=4)
            d.text((lx + 14, ly - 14), "last seen", fill=hex_rgba(BOT_C), font=FONT_SM)
        for tx0, ty0, tx1, ty1, _ in tracers:
            d.line((ox + tx0 * k, oy + ty0 * k, ox + tx1 * k, oy + ty1 * k), fill=hex_rgba(ACCENT), width=3)
        if T - Pt["lastShot"] < 0.6:
            rr = 20 + (T - Pt["lastShot"]) * 75
            d.ellipse((px - rr, py - rr, px + rr, py + rr), outline=hex_rgba(ACCENT, 160), width=3)
        d.ellipse((px - 13, py - 13, px + 13, py + 13), fill=hex_rgba(ACCENT))
        d.ellipse((bx - 13, by - 13, bx + 13, by + 13), fill=hex_rgba(BOT_C))
        d.line((bx, by, bx + math.cos(Bt["yaw"]) * 38, by + math.sin(Bt["yaw"]) * 38), fill=hex_rgba(BOT_C), width=6)
        d.text((bx + 18, by - 26), Bt["goal"].upper(), fill=hex_rgba(gc), font=FONT_LG)

        # 3-lane bottom strip (DETECTION / GOAL / AIM ERROR) identical to cv-strip
        sy = oy + ah + 20
        sh = h2 - sy - pad
        L = ox + 170
        R = ox + aw - 16
        d.rectangle((ox, sy, ox + aw, sy + sh), fill=hex_rgba(PANEL), outline=hex_rgba(LINE), width=2)
        lanes = [("DETECTION", 0.04, 0.32), ("GOAL", 0.40, 0.58), ("AIM ERROR", 0.66, 0.94)]
        for nm, a, b in lanes:
            d.text((ox + 12, sy + sh * (a + b) / 2 - 12), nm, fill=hex_rgba(INK2), font=FONT_MD)
            d.line((L, sy + sh * b, R, sy + sh * b), fill=hex_rgba(GRID), width=2)
        y_m = lambda m: sy + sh * (0.32 - 0.28 * m / 1.25)
        y_e = lambda e: sy + sh * (0.94 - 0.28 * e / 60.0)
        x_t = lambda tt: L + (R - L) * (1.0 - (T - tt) / 12.0)
        draw_dashed_line(d, (L, y_m(1.0)), (R, y_m(1.0)), hex_rgba(INK2, 130), width=2, dash=6, gap=6)
        if len(logs) > 1:
            d.line([(x_t(g[0]), y_m(g[1])) for g in logs], fill=hex_rgba(ACCENT), width=4)
            for i in range(1, len(logs)):
                c = GOALC.get(logs[i][2], INK2)
                d.rectangle((x_t(logs[i - 1][0]), sy + sh * 0.41, x_t(logs[i][0]) + 1, sy + sh * 0.56), fill=hex_rgba(c))
            d.line([(x_t(g[0]), y_e(g[3])) for g in logs], fill=hex_rgba(BOT_C), width=4)
        d.text((L, sy + sh - 24), "-12 s", fill=hex_rgba(INK2), font=FONT_SM)
        d.text((R - 50, sy + sh - 24), "now", fill=hex_rgba(INK2), font=FONT_SM)
        frames.append(finish_frame(img, W_PX, H_PX))
    save_gif("watch-a-bot-play.gif", frames, fps=12)


# ------------------------------------------------------------------ 2. Perception meter
def make_perception_gif():
    W_PX, H_PX = 820, 460
    frames = []
    meter, spotted = 0.0, False
    for fi in range(84):
        t = fi * 0.1
        # Player patrols across gap in cover and fires at t=3.0s
        if t < 3.6:
            plx = 16.0 + t * 2.2
            ply = 13.5 - t * 2.1
        else:
            plx = 24.0 - (t - 3.6) * 2.8
            ply = 6.0 + math.sin((t - 3.6) * 1.4) * 3.0
        gaze = math.sin(t * 0.45) * 0.32
        dx, dy = plx - 5.0, ply - 9.0
        dist = math.hypot(dx, dy)
        ang = abs(math.degrees(wrap_ang(math.atan2(dy, dx) - gaze)))
        in_cone = ang <= 55.0 or (dist < 6.0 and ang < 100.0)
        blocked = any(seg_rect(5.0, 9.0, plx, ply, r) for r in WALLS)
        vis = in_cone and not blocked
        shot = 2.8 <= t <= 3.8
        rate = (0.9 + 9.0 / max(dist, 1.5)) * (2.2 if shot else 1.0) * (1.35 if True else 1.0) * (1.4 if ang < 12 else 1.0)
        meter = min(1.25, meter + rate * 0.1) if vis else max(0.0, meter - 0.05)
        if not spotted and meter >= 1.0:
            spotted = True
        if spotted and meter <= 0.05:
            spotted = False

        img, d, w2, h2 = new_canvas(W_PX, H_PX, BG)
        pad = 20
        aw = w2 - pad * 2
        ah = h2 - pad * 2
        k = draw_grid_walls(d, pad, pad, aw, ah)
        ox, oy = pad, pad
        bx, by = ox + 5.0 * k, oy + 9.0 * k
        px, py = ox + plx * k, oy + ply * k
        img = draw_pie(img, bx, by, 26 * k, gaze - math.radians(55), gaze + math.radians(55), hex_rgba(BOT_C, 36))
        img = draw_pie(img, bx, by, 6 * k, gaze - math.radians(100), gaze + math.radians(100), hex_rgba(BOT_C, 28))
        d = ImageDraw.Draw(img, "RGBA")
        d.ellipse((bx - 6 * k, by - 6 * k, bx + 6 * k, by + 6 * k), outline=hex_rgba(BOT_C, 180), width=2)
        if vis:
            d.line((bx, by, px, py), fill=hex_rgba(GOOD), width=4)
        else:
            draw_dashed_line(d, (bx, by), (px, py), hex_rgba(BAD), width=4)
        d.ellipse((bx - 13, by - 13, bx + 13, by + 13), fill=hex_rgba(BOT_C))
        d.line((bx, by, bx + math.cos(gaze) * 44, by + math.sin(gaze) * 44), fill=hex_rgba(BOT_C), width=6)
        if shot:
            sr = 22 + (t - 2.8) * 60
            d.ellipse((px - sr, py - sr, px + sr, py + sr), outline=hex_rgba(ACCENT), width=3)
        d.ellipse((px - 15, py - 15, px + 15, py + 15), fill=hex_rgba(ACCENT))
        d.text((bx + 6 * k * 0.72, by - 6 * k * 0.72), "6 m (±100°)", fill=hex_rgba(INK2), font=FONT_SM)
        # Detection bar matching cv-see
        mbx, mby, mbw, mbh = ox + 20, oy + ah - 42, 440, 20
        d.rectangle((mbx, mby, mbx + mbw, mby + mbh), fill=hex_rgba(GRID))
        d.rectangle((mbx, mby, mbx + int(mbw * meter / 1.25), mby + mbh), fill=hex_rgba(BAD if spotted else ACCENT))
        tick_x = mbx + int(mbw / 1.25)
        d.line((tick_x, mby - 6, tick_x, mby + mbh + 6), fill=hex_rgba(INK), width=3)
        st_lbl = "SPOTTED" if spotted else ("SEEING" if vis else "OCCLUDED")
        d.text((mbx, mby - 32), f"detection {meter:.2f} (1.00 = {st_lbl})  dist={dist:.1f}m  ang={ang:.0f}°", fill=hex_rgba(INK), font=FONT_MD)
        frames.append(finish_frame(img, W_PX, H_PX))
    save_gif("perception-meter.gif", frames, fps=14)


# ------------------------------------------------------------------ 3. Fitts minimum jerk
def make_fitts_gif():
    W_PX, H_PX = 820, 520
    def sim(seed):
        rnd = mulberry(seed)
        D, dist, w, stress = 60.0, 20.0, 7.0, 0.25
        half = math.degrees(math.atan2(0.34, dist))
        fid = math.log2(1.0 + D / max(0.4, half))
        skill = clamp(7.5 / w, 0.62, 1.55)
        mt = clamp((0.065 + 0.042 * fid) * skill, 0.085, 0.38)
        sigma = clamp(D * (0.045 + 0.045 * stress) * skill, 0.35, 6.5)
        mu = lerp(-0.075, 0.035, stress) * D
        end_err = mu + ((rnd() + rnd()) - 1.0) * sigma
        dt, T, aim, v, t, flick, phase = 1 / 120.0, 0.9, 0.0, 0.0, 0.0, True, rnd() * 6
        pts, vel = [], []
        while t <= T:
            if flick:
                u = clamp(t / mt, 0.0, 1.0)
                s = u ** 3 * (10.0 + u * (-15.0 + 6.0 * u))
                nxt = (D + end_err) * s
                step = nxt - aim
                v = step / dt
                if u >= 1.0 or abs(D - nxt) < 2.2:
                    flick = False
            else:
                err = D - aim
                phase += dt * (7.5 + stress * 4.0)
                err += math.sin(phase * 1.37) * min(1.6, abs(err) * 0.032 * (0.55 + stress * 0.65)) * 1.15
                v += (w * w * err - 2.0 * 0.8 * w * v) * dt
                v = clamp(v, -900.0, 900.0)
                step = v * dt
            step = clamp(step, -720.0 * dt, 720.0 * dt)
            aim += step
            pts.append((t, D - aim, flick))
            vel.append((t, abs(step / dt)))
            t += dt
        return pts, vel, half, mt, D

    main_pts, main_vel, half, mt, D = sim(7)
    ghosts = [sim(7 + g * 101)[0] for g in range(1, 7)]
    vmax = max(v[1] for v in main_vel)
    frames = []
    for end_idx in range(3, len(main_pts), 2):
        img, d, w2, h2 = new_canvas(W_PX, H_PX, BG)
        L, R, T, B = 95, w2 - 24, 24, h2 - 240
        X = lambda tt: L + (R - L) * tt / 0.9
        Y = lambda e: T + (B - T) * (1.0 - (e + D * 0.15) / (D * 1.15))
        d.rectangle((20, 16, w2 - 20, B + 50), fill=hex_rgba(PANEL), outline=hex_rgba(LINE), width=2)
        ov_f = Image.new("RGBA", img.size, (0, 0, 0, 0))
        ImageDraw.Draw(ov_f, "RGBA").rectangle((L, Y(half), R, Y(-half)), fill=hex_rgba(ACCENT, 34))
        img = Image.alpha_composite(img, ov_f)
        d = ImageDraw.Draw(img, "RGBA")
        for ms in range(0, 901, 150):
            xx = X(ms / 1000.0)
            d.line((xx, T, xx, B), fill=hex_rgba(GRID), width=2)
            d.text((xx - 24, B + 8), f"{ms}", fill=hex_rgba(INK2), font=FONT_SM)
        for deg in (0, 30, 60):
            d.line((L, Y(deg), R, Y(deg)), fill=hex_rgba(GRID), width=2)
            d.text((28, Y(deg) - 10), f"{deg}°", fill=hex_rgba(INK2), font=FONT_SM)
        for g in ghosts:
            d.line([(X(tt), Y(ee)) for tt, ee, _ in g], fill=hex_rgba(INK2, 55), width=2)
        for i in range(1, end_idx):
            t0, e0, _ = main_pts[i - 1]
            t1, e1, fl = main_pts[i]
            d.line((X(t0), Y(e0), X(t1), Y(e1)), fill=hex_rgba(ACCENT if fl else BOT_C), width=5)
        draw_dashed_line(d, (X(mt), T), (X(mt), B), hex_rgba(INK, 180), width=2, dash=8, gap=6)
        d.text((X(mt) + 8, T + 6), f"flick ends ({int(mt*1000)} ms)", fill=hex_rgba(INK), font=FONT_SM)
        d.text((L + 12, T + 6), "ballistic flick", fill=hex_rgba(ACCENT), font=FONT_SM)
        d.text((L + 12, T + 30), "feedback tracking", fill=hex_rgba(BOT_C), font=FONT_SM)
        # velocity panel
        VT, VB = B + 72, h2 - 20
        d.rectangle((20, VT - 10, w2 - 20, VB), fill=hex_rgba(PANEL), outline=hex_rgba(LINE), width=2)
        VY = lambda vv: VT + (VB - VT - 36) * (1.0 - vv / max(vmax, 1.0))
        if end_idx > 2:
            d.line([(X(tt), VY(vv)) for tt, vv in main_vel[:end_idx]], fill=hex_rgba(ACCENT), width=4)
        d.text((L, VB - 30), f"minimum-jerk turn speed (peak {int(vmax)} deg/s, cap 720)", fill=hex_rgba(INK2), font=FONT_SM)
        frames.append(finish_frame(img, W_PX, H_PX))
    save_gif("fitts-minimum-jerk.gif", frames, fps=18)


# ------------------------------------------------------------------ 4. ESTA/CSKnow/Aim Lab Human Miss Pattern
def make_miss_gif():
    W_PX, H_PX = 820, 540
    frames = []
    rnd = mulberry(19)
    total_steps = 45
    samples = []
    for k in range(total_steps):
        ang = math.radians(-22 + math.sin(k * 0.14) * 35)
        ux, uy = math.cos(ang), math.sin(ang)
        nx, ny = -uy, ux
        amp, stress, heat, strafe = 38.0, 0.25, clamp(k / total_steps, 0, 1) * 0.65, 2.5
        sdn = clamp(amp * (0.045 + 0.045 * stress), 0.35, 6.5)
        mu = lerp(-0.075, 0.035, stress) * amp
        lag = -(strafe * 0.125 / 18.0) * (180.0 / math.pi) * 1.4
        for _ in range(5):
            ea = mu + ((rnd() + rnd()) - 1.0) * sdn
            ep = ((rnd() + rnd()) - 1.0) * 0.44 * sdn
            wv = math.sin(len(samples) * 1.15) * 0.65 * max(0.0, heat - 0.25) * 2.2
            ex = -(ux * ea + nx * ep) * 1.08 + lag + wv
            ey = -(uy * ea + ny * ep) * 0.70 - rnd() * heat * 1.6
            samples.append((ex, ey))

        img, d, w2, h2 = new_canvas(W_PX, H_PX, BG)
        d.rectangle((20, 20, w2 - 20, h2 - 20), fill=hex_rgba(PANEL), outline=hex_rgba(LINE), width=2)
        cx, cy = w2 * 0.5, h2 * 0.48
        px_deg = min(w2, h2) / 13.5
        for g in range(-6, 7, 2):
            d.line((cx + g * px_deg, 32, cx + g * px_deg, h2 - 32), fill=hex_rgba(GRID), width=2)
            d.line((32, cy + g * px_deg, w2 - 32, cy + g * px_deg), fill=hex_rgba(GRID), width=2)
        hr, tw, th = 0.52 * px_deg, 1.65 * px_deg, 2.75 * px_deg
        img = draw_translucent_ellipse(img, (cx - hr, cy - th * 0.42 - 2 * hr, cx + hr, cy - th * 0.42), hex_rgba(BOT_C, 46), hex_rgba(BOT_C), width=3)
        ov_t = Image.new("RGBA", img.size, (0, 0, 0, 0))
        odt = ImageDraw.Draw(ov_t, "RGBA")
        odt.rectangle((cx - tw / 2, cy - th * 0.42, cx + tw / 2, cy + th * 0.58), fill=hex_rgba(BOT_C, 46), outline=hex_rgba(BOT_C), width=3)
        odt.rectangle((cx - tw * 0.42, cy + th * 0.58, cx - tw * 0.07, cy + th * 1.15), fill=hex_rgba(BOT_C, 38), outline=hex_rgba(BOT_C), width=2)
        odt.rectangle((cx + tw * 0.07, cy + th * 0.58, cx + tw * 0.42, cy + th * 1.15), fill=hex_rgba(BOT_C, 38), outline=hex_rgba(BOT_C), width=2)
        img = Image.alpha_composite(img, ov_t)
        d = ImageDraw.Draw(img, "RGBA")
        draw_dashed_line(d, (cx - ux * 300, cy - uy * 300), (cx, cy), hex_rgba(INK2), width=3)
        # Rotated 95% ellipse polygon
        mx = cx + (-ux * mu * 1.08 + lag) * px_deg
        my = cy + (-uy * mu * 0.70 - heat * 1.15) * px_deg
        rx_e = (sdn * 1.08 + max(0.0, heat - 0.25) * 1.4) * px_deg
        ry_e = (sdn * 0.44 * 0.70) * px_deg
        rot = math.atan2(uy * 0.70, ux * 1.08)
        epts = []
        for s in range(65):
            th_e = s * 2 * math.pi / 64.0
            lx, ly = rx_e * math.cos(th_e), ry_e * math.sin(th_e)
            epts.append((mx + lx * math.cos(rot) - ly * math.sin(rot), my + lx * math.sin(rot) + ly * math.cos(rot)))
        d.line(epts, fill=hex_rgba(ACCENT), width=4)
        for sx_d, sy_d in samples:
            sx, sy = cx + sx_d * px_deg, cy + sy_d * px_deg
            in_h = math.hypot(sx - cx, sy - (cy - th * 0.42 - hr)) <= hr
            in_t = (cx - tw / 2 <= sx <= cx + tw / 2) and (cy - th * 0.42 <= sy <= cy + th * 0.58)
            col = BAD if in_h else (GOOD if in_t else ACCENT)
            r_pt = 7 if in_h else 5
            d.ellipse((sx - r_pt, sy - r_pt, sx + r_pt, sy + r_pt), fill=hex_rgba(col, 210))
        d.text((34, 34), "ESTA / CSKnow / Aim Lab Empirical Miss Cloud (sigma_along/sigma_perp=2.27, wrist/arm=1.55:1, 125ms lag)", fill=hex_rgba(INK), font=FONT_SM)
        d.text((34, h2 - 50), f"shots={len(samples)}  spray_heat={int(heat*100)}%  calm_undershoot=-7.5%  red=head green=torso orange=miss", fill=hex_rgba(INK2), font=FONT_SM)
        frames.append(finish_frame(img, W_PX, H_PX))
    save_gif("human-miss-pattern.gif", frames, fps=10)


# ------------------------------------------------------------------ 5. Recoil pull-down
def make_recoil_gif():
    W_PX, H_PX = 820, 440
    rnd = mulberry(3)
    w, shots, climb, interval, dt = 7.0, 10, 1.05 * 0.55, 60.0 / 700.0, 1 / 240.0
    T_tot = shots * interval + 0.55
    delay = 0.065 + rnd() * 0.07
    off, raw, v, heat, t, nxt, fired = 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0
    pts_c, pts_r = [], []
    while t <= T_tot:
        if fired < shots and t >= nxt:
            off += climb
            raw += climb
            heat = min(1.0, heat + 0.13)
            fired += 1
            nxt += interval
        heat = max(0.0, heat - 0.95 * dt)
        err = -off
        if -err > 0.2 and t < delay:
            err *= 0.12
        elif -err > 0.3 and heat > 0.1:
            err *= clamp(clamp(w / 10.5, 0.55, 0.92) * (1.0 - heat * 0.22), 0.45, 0.94)
        v += (w * w * err - 2.0 * 0.8 * w * v) * dt
        v = clamp(v, -900.0, 900.0)
        off += clamp(v * dt, -720.0 * dt, 720.0 * dt)
        pts_c.append((t, off))
        pts_r.append((t, raw))
        t += dt
    ymax = raw * 1.15
    frames = []
    for idx in range(4, len(pts_c), 6):
        img, d, w2, h2 = new_canvas(W_PX, H_PX, BG)
        L, R, T, B = 90, w2 - 24, 30, h2 - 50
        d.rectangle((20, 16, w2 - 20, h2 - 16), fill=hex_rgba(PANEL), outline=hex_rgba(LINE), width=2)
        X = lambda tt: L + (R - L) * tt / T_tot
        Y = lambda yy: T + (B - T) * (1.0 - yy / ymax)
        for deg in range(0, int(ymax) + 1, 2):
            d.line((L, Y(deg), R, Y(deg)), fill=hex_rgba(GRID), width=2)
            d.text((34, Y(deg) - 10), f"{deg}°", fill=hex_rgba(INK2), font=FONT_SM)
        for i in range(shots):
            sx = X(i * interval)
            d.rectangle((sx - 2, B, sx + 2, B + 12), fill=hex_rgba(ACCENT, 130))
        raw_px = [(X(tt), Y(yy)) for tt, yy in pts_r[:idx]]
        for i in range(0, len(raw_px) - 1, 2):
            d.line((raw_px[i], raw_px[min(i + 1, len(raw_px) - 1)]), fill=hex_rgba(INK2), width=4)
        d.line([(X(tt), Y(yy)) for tt, yy in pts_c[:idx]], fill=hex_rgba(BOT_C), width=5)
        draw_dashed_line(d, (X(delay), T), (X(delay), B), hex_rgba(ACCENT), width=3, dash=6, gap=6)
        d.text((X(delay) + 8, B - 32), f"pull-down starts ({int(delay*1000)} ms)", fill=hex_rgba(ACCENT), font=FONT_SM)
        d.text((R - 320, T + 8), "bot with pull-down", fill=hex_rgba(BOT_C), font=FONT_MD)
        d.text((R - 320, T + 34), "uncontrolled climb", fill=hex_rgba(INK2), font=FONT_MD)
        frames.append(finish_frame(img, W_PX, H_PX))
    save_gif("recoil-pulldown.gif", frames, fps=16)


# ------------------------------------------------------------------ 6. Adaptive skill
def make_adaptive_gif():
    W_PX, H_PX = 820, 420
    events = ["start", "win", "headshot", "win", "loss", "loss", "win", "win", "headshot", "loss", "win"]
    sk, st, hist = 0.48, 0, []
    for ev in events:
        if ev in ("win", "headshot"):
            st = max(1, st + 1)
            sk = clamp(sk + 0.045 + (0.03 if st >= 2 else 0) + (0.02 if ev == "headshot" else 0) + 0.25 * 0.03, 0.12, 0.96)
        elif ev == "loss":
            st = min(-1, st - 1)
            sk = clamp(sk - 0.040 - (0.035 if st <= -2 else 0), 0.12, 0.96)
        hist.append((ev, sk))
    frames = []
    for k in range(len(hist)):
        for _ in range(4):
            img, d, w2, h2 = new_canvas(W_PX, H_PX, BG)
            d.rectangle((20, 16, w2 - 20, h2 - 16), fill=hex_rgba(PANEL), outline=hex_rgba(LINE), width=2)
            L, R, T, B = 95, w2 - 40, 46, h2 - 150
            X = lambda i: L + (R - L) * i / max(1, len(hist) - 1)
            Y = lambda s: T + (B - T) * (1.0 - s)
            for ref in (0.12, 0.48, 0.96):
                d.line((L, Y(ref), R, Y(ref)), fill=hex_rgba(GRID), width=2)
                d.text((28, Y(ref) - 10), f"{ref:.2f}", fill=hex_rgba(INK2), font=FONT_SM)
            sub = hist[: k + 1]
            if len(sub) > 1:
                d.line([(X(i), Y(v)) for i, (_, v) in enumerate(sub)], fill=hex_rgba(ACCENT), width=5)
            lx, ly = X(k), Y(sub[-1][1])
            d.ellipse((lx - 9, ly - 9, lx + 9, ly + 9), fill=hex_rgba(ACCENT))
            cur_ev, cur_s = sub[-1]
            e = clamp(cur_s * 0.78 + 0.22 * clamp((0.28 - 0.2) * 1.4, 0, 1), 0, 1)
            d.text((L, B + 18), f"Duel {k}: {cur_ev.upper()}   |   Effective Skill: {e:.2f}   React: {lerp(0.52,0.16,e):.2f}s   AimSpeed: {lerp(4.4,10.2,e):.1f}   SettledErr: {lerp(1.35,0.28,e):.2f}°", fill=hex_rgba(INK), font=FONT_MD)
            d.text((L, B + 54), "Skill updates +0.045 win (+0.03 streak, +0.02 HS) / -0.040 loss (-0.035 streak), clamped [0.12, 0.96]", fill=hex_rgba(INK2), font=FONT_SM)
            frames.append(finish_frame(img, W_PX, H_PX))
    save_gif("adaptive-skill.gif", frames, fps=10)


# ------------------------------------------------------------------ 7. Dual-layer pathfinding
def make_pathfinding_gif():
    W_PX, H_PX = 820, 460
    frames = []
    corners = [(4.5, 9.2), (10.8, 9.6), (15.0, 7.5), (20.5, 5.5)]
    for fi in range(64):
        img, d, w2, h2 = new_canvas(W_PX, H_PX, BG)
        pad = 20
        k = draw_grid_walls(d, pad, pad, w2 - pad * 2, h2 - pad * 2)
        ox, oy = pad, pad
        mode = "L1: Cover/Flank Scorer" if fi < 32 else "L2: NavMesh Corridor + Corner Slice"
        # Corridor ribbon
        cpx = [(ox + p[0] * k, oy + p[1] * k) for p in corners]
        img = draw_translucent_line(img, cpx, hex_rgba(BOT_C, 46), width=38)
        d = ImageDraw.Draw(img, "RGBA")
        for i in range(len(cpx) - 1):
            draw_dashed_line(d, cpx[i], cpx[i + 1], hex_rgba(BOT_C), width=4)
        for i, (cx, cy) in enumerate(cpx):
            d.ellipse((cx - 8, cy - 8, cx + 8, cy + 8), fill=hex_rgba(ACCENT if i == 1 else BOT_C))
        # Animated bot traversing corners & slicing corners[1] ±28 deg
        u = (fi % 32) / 31.0
        seg = min(2, int(u * 3))
        lu = (u * 3) - seg
        bx = lerp(corners[seg][0], corners[seg + 1][0], lu)
        by = lerp(corners[seg][1], corners[seg + 1][1], lu)
        px, py = ox + bx * k, oy + by * k
        look_t = corners[min(seg + 1, 3)]
        g_ang = math.atan2(look_t[1] - by, look_t[0] - bx) + math.sin(fi * 0.28) * math.radians(28)
        d.line((px, py, px + math.cos(g_ang) * 3.8 * k, py + math.sin(g_ang) * 3.8 * k), fill=hex_rgba(WARN), width=5)
        d.ellipse((px - 13, py - 13, px + 13, py + 13), fill=hex_rgba(BOT_C))
        # ADAD strafe arrow + threat + cover point rays
        tx, ty = ox + 23.5 * k, oy + 5.5 * k
        d.ellipse((tx - 13, ty - 13, tx + 13, ty + 13), fill=hex_rgba(ACCENT))
        cov = (ox + 10.6 * k, oy + 4.5 * k)
        draw_dashed_line(d, (tx, ty), cov, hex_rgba(GOOD), width=3)
        d.ellipse((cov[0] - 9, cov[1] - 9, cov[0] + 9, cov[1] + 9), fill=hex_rgba(GOOD))
        d.text((ox + 16, oy + 14), f"{mode}  |  updatePosition=false · repath 0.6s/1m · ADAD 0.65+0.85", fill=hex_rgba(INK), font=FONT_SM)
        frames.append(finish_frame(img, W_PX, H_PX))
    save_gif("dual-pathfinding.gif", frames, fps=12)


# ------------------------------------------------------------------ 8. CTF Zone & Live Tactical Chat
def make_ctf_chat_gif():
    W_PX, H_PX = 820, 460
    frames = []
    prog = -0.35
    for fi in range(60):
        img, d, w2, h2 = new_canvas(W_PX, H_PX, BG)
        pad = 20
        k = draw_grid_walls(d, pad, pad, w2 - pad * 2, h2 - pad * 2)
        ox, oy = pad, pad
        zx, zy = 16.0, 9.0
        px_u = 23.0 - (fi / 59.0) * 6.5  # player rushes from edge into 2.6m core!
        py_u = 9.0
        pdist = abs(px_u - zx)
        pmult = 2.25 if pdist <= 2.6 else lerp(2.25, 0.28, ((pdist - 2.6) / 8.9) ** 0.65)
        emult = 0.62
        diff = pmult - emult
        contested = abs(diff) < 0.65
        state = "CONTESTED" if contested else ("CAPTURING" if diff > 0 else "LOSING")
        if not contested:
            prog = clamp(prog + diff * (1.0 / 7.5) * 0.08, -1.0, 1.0)
        cx, cy = ox + zx * k, oy + zy * k
        r_out, r_core = 8.0 * k, 2.2 * k
        rc = WARN if contested else (GOOD if prog >= 0 else BAD)
        img = draw_translucent_ellipse(img, (cx - r_out, cy - r_out, cx + r_out, cy + r_out), hex_rgba(rc, 28), hex_rgba(rc), width=4)
        img = draw_translucent_ellipse(img, (cx - r_core, cy - r_core, cx + r_core, cy + r_core), hex_rgba(rc, 62), hex_rgba(rc), width=4)
        d = ImageDraw.Draw(img, "RGBA")
        if abs(prog) > 0.02:
            d.arc((cx - r_core - 14, cy - r_core - 14, cx + r_core + 14, cy + r_core + 14), -90, -90 + prog * 360, fill=hex_rgba(GOOD if prog > 0 else BAD), width=8)
        for bi in range(2):
            ba = bi * 2.2 + fi * 0.05
            bx, by = cx + math.cos(ba) * 6.0 * k, cy + math.sin(ba) * 6.0 * k
            d.ellipse((bx - 12, by - 12, bx + 12, by + 12), fill=hex_rgba(BOT_C))
        ppx, ppy = ox + px_u * k, oy + py_u * k
        d.ellipse((ppx - 14, ppy - 14, ppx + 14, ppy + 14), fill=hex_rgba(ACCENT))
        # TacticalChat HUD overlay box bottom-left
        d.rectangle((ox + 16, oy + h2 - 220, ox + 780, oy + h2 - 58), fill=hex_rgba("#0c1015", 225), outline=hex_rgba(LINE), width=2)
        d.text((ox + 28, oy + h2 - 208), "FLAG HQ: New flag at HARBOUR CRANE! Inner core = 2.25x capture!", fill=hex_rgba("#47d9ff"), font=FONT_SM)
        d.text((ox + 28, oy + h2 - 180), "Operator: push flag" if fi > 14 else "BOT-ALPHA: somebody tell AI-04 ADS helps lol", fill=hex_rgba(GOOD), font=FONT_SM)
        d.text((ox + 28, oy + h2 - 152), "BOT-ALLY [TEAM]: Rogering up — crashing the objective with you!" if fi > 26 else "SECTOR: Contested — rush core to overpower edge bots!", fill=hex_rgba(WARN if fi <= 26 else GOOD), font=FONT_SM)
        d.text((ox + 28, oy + h2 - 110), f"ZONE={state}  dist={pdist:.1f}m ({pmult:.2f}x)  progress={int(prog*100)}%", fill=hex_rgba(INK), font=FONT_MD)
        frames.append(finish_frame(img, W_PX, H_PX))
    save_gif("ctf-and-chat.gif", frames, fps=10)


# ------------------------------------------------------------------ 9. AI Multi-Agent Workflow
def make_ai_workflow_gif():
    W_PX, H_PX = 860, 520
    CODEX_C = "#47d9ff"
    nodes = [
        (0.50, 0.13, ACCENT, "Developer (1)", "Prompts · Playtest · Calls"),
        (0.16, 0.43, PURPLE, "Claude Code", "opus-5-5 · Rig & Reviews"),
        (0.50, 0.43, INK, "AGENT_COORDINATION.md", "v3 Lock · Queue · Ledger"),
        (0.84, 0.43, BOT_C, "Gemini (Lead)", "hydrogen · AI/UI/Maps/P3"),
        (0.14, 0.78, WARN, "Unity 6000.5", "1 Lock + ParallelWorker"),
        (0.38, 0.78, GOOD, "GitHub Copilot", "gpt-5.6 · Specs/QA/Push"),
        (0.62, 0.78, CODEX_C, "OpenAI Codex", "gpt-6 · B56/B57 QA/Review"),
        (0.86, 0.78, INK2, "GitHub Main", "dotnet 0/0 -> Push"),
    ]
    edges = [
        (0, 1), (0, 2), (0, 3), (0, 5), (0, 6),
        (1, 2), (3, 2), (5, 2), (6, 2),
        (1, 4), (3, 4), (5, 7), (6, 7), (2, 7),
    ]
    frames = []
    for fi in range(48):
        img, d, w2, h2 = new_canvas(W_PX, H_PX, BG)
        d.rectangle((16, 16, w2 - 16, h2 - 16), fill=hex_rgba(PANEL), outline=hex_rgba(LINE), width=2)
        for ei, (a, b) in enumerate(edges):
            x0, y0 = nodes[a][0] * w2, nodes[a][1] * h2
            x1, y1 = nodes[b][0] * w2, nodes[b][1] * h2
            d.line((x0, y0, x1, y1), fill=hex_rgba(LINE), width=3)
            u = ((fi / 48.0) * 2.0 + ei * 0.11) % 1.0
            px, py = lerp(x0, x1, u), lerp(y0, y1, u)
            d.ellipse((px - 7, py - 7, px + 7, py + 7), fill=hex_rgba(ACCENT))
        for idx, (nx, ny, col, lbl, sub) in enumerate(nodes):
            bx, by = nx * w2, ny * h2
            rw, rh = (390 if idx == 2 else 360), 76
            d.rounded_rectangle((bx - rw / 2, by - rh / 2, bx + rw / 2, by + rh / 2), radius=10, fill=hex_rgba(PANEL2), outline=hex_rgba(col), width=3)
            d.text((bx - rw / 2 + 14, by - 25), lbl, fill=hex_rgba(INK), font=FONT_MD)
            d.text((bx - rw / 2 + 14, by + 6), sub, fill=hex_rgba(INK2), font=FONT_SM)
        d.text((34, h2 - 50), "Protocol v3 (4 Agents): Research First -> Implement -> Cross-Review -> Visible QA -> dotnet 0/0 -> Push", fill=hex_rgba(INK2), font=FONT_SM)
        frames.append(finish_frame(img, W_PX, H_PX))
    save_gif("ai-4agent-workflow.gif", frames, fps=14)


def main():
    make_watch_gif()
    make_perception_gif()
    make_fitts_gif()
    make_miss_gif()
    make_recoil_gif()
    make_adaptive_gif()
    make_pathfinding_gif()
    make_ctf_chat_gif()
    make_ai_workflow_gif()
    print(f"All 9 1:1 artifact GIFs written to {OUT}")


if __name__ == "__main__":
    main()
