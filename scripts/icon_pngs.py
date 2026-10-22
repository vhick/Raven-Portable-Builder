"""Create PortableApps Menu icons only from the original extracted Raven ICO."""
import pathlib
import sys
from PIL import Image


def main(ico, dest):
    dest = pathlib.Path(dest)
    dest.mkdir(parents=True, exist_ok=True)
    img = Image.open(ico)
    sizes = img.ico.sizes() if hasattr(img, "ico") else {(img.width, img.height)}
    for target in [16, 32, 128]:
        size = min(sizes, key=lambda s: (abs(s[0] - target), -s[0]))
        source = img.ico.getimage(size) if hasattr(img, "ico") else img.copy()
        source.convert("RGBA").resize((target, target), Image.Resampling.LANCZOS).save(dest / f"appicon_{target}.png")
    print("Generated 16/32/128 PNGs from original Raven icon.")


if __name__ == "__main__":
    if len(sys.argv) != 3: raise SystemExit("Usage: icon_pngs.py icon.ico destination")
    main(sys.argv[1], sys.argv[2])
