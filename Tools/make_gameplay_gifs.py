"""Turn the gameplay clips recorded by the pose-check harness (-lr2GifRun) into README GIFs.

Input : <run>/gif_<clip>/f_000.jpg ... (game view with HUD, about 12 frames per second), default run Logs/LR2_AimTest_C15
Output: Docs/Media/gifs/<clip>.gif (640 px wide, shared palette per clip, loops) and a mid-clip still <clip>_still.png
Usage : python Tools/make_gameplay_gifs.py [run folder]
"""
from pathlib import Path
import sys

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Docs" / "Media" / "gifs"
WIDTH = 640
FRAME_MS = 85  # recorded at about 12 fps


def build(clip_dir: Path) -> None:
    files = sorted(clip_dir.glob("f_*.jpg"))
    if len(files) < 4:
        print("skip", clip_dir.name, len(files), "frames")
        return
    frames = []
    for f in files:
        im = Image.open(f).convert("RGB")
        h = round(im.height * WIDTH / im.width)
        frames.append(im.resize((WIDTH, h), Image.LANCZOS))
    pal = frames[len(frames) // 2].quantize(colors=160, method=Image.Quantize.MEDIANCUT)
    q = [fr.quantize(palette=pal, dither=Image.Dither.FLOYDSTEINBERG) for fr in frames]
    name = clip_dir.name.replace("gif_", "")
    OUT.mkdir(parents=True, exist_ok=True)
    out = OUT / f"{name}.gif"
    q[0].save(out, save_all=True, append_images=q[1:], duration=[FRAME_MS] * (len(q) - 1) + [900], loop=0, optimize=True)
    still = Image.open(files[len(files) // 2]).convert("RGB")
    still.save(OUT / f"{name}_still.png")
    print(f"{out.relative_to(ROOT)}: {len(q)} frames, {out.stat().st_size // 1024} KB")


def main() -> None:
    run = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "Logs" / "LR2_AimTest_C15"
    for d in sorted(run.glob("gif_*")):
        build(d)


if __name__ == "__main__":
    main()
