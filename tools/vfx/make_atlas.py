"""把 frames/ 底下依序命名的 PNG 拼成 N x N 序列圖（Unity Texture Sheet Animation 用）。

用法: python make_atlas.py <frames_dir> <out.png> [grid=8]
格子順序：左上 → 右，逐列往下（Unity 預設的 Whole Sheet 播放順序）。
每格邊緣 6% 內 alpha 以 smoothstep 漸變到 0，避免播放時看到方形邊界。
"""
import sys
from pathlib import Path
from PIL import Image, ImageChops

frames_dir, out_path = Path(sys.argv[1]), Path(sys.argv[2])
grid = int(sys.argv[3]) if len(sys.argv) > 3 else 8

frames = sorted(frames_dir.glob("*.png"))
need = grid * grid
if len(frames) != need:
    sys.exit(f"需要 {need} 張，找到 {len(frames)} 張")

w, h = Image.open(frames[0]).size

def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)

edge = max(2, int(min(w, h) * 0.06))
bottom = int(h * 0.22)  # 模擬地板把煙底部切平了，底部用較長的漸層淡掉
mask = Image.new("L", (w, h), 255)
mpx = mask.load()
for y in range(h):
    for x in range(w):
        side = smooth(min(x, y, w - 1 - x) / edge)
        base = smooth((h - 1 - y) / bottom)
        mpx[x, y] = int(255 * min(side, base))

atlas = Image.new("RGBA", (w * grid, h * grid), (0, 0, 0, 0))
for i, p in enumerate(frames):
    im = Image.open(p).convert("RGBA")
    im.putalpha(ImageChops.multiply(im.getchannel("A"), mask))
    atlas.paste(im, ((i % grid) * w, (i // grid) * h))

atlas.save(out_path, optimize=True)
print(f"atlas {atlas.size} from {len(frames)} frames -> {out_path} ({out_path.stat().st_size // 1024} KB)")
