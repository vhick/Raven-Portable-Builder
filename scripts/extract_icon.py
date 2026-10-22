"""Extract the ORIGINAL multi-resolution ICO group embedded in Raven.exe.
No generic icon and no low-resolution ExtractAssociatedIcon workaround.
"""
from pathlib import Path
import struct
import sys


def extract_groups(exe):
    import pefile  # Installed on the Windows GitHub runner; packaging tests need no PE dependency.
    pe = pefile.PE(str(exe), fast_load=False)
    if not hasattr(pe, "DIRECTORY_ENTRY_RESOURCE"):
        raise RuntimeError("Upstream executable contains no PE icon resources")
    root = pe.DIRECTORY_ENTRY_RESOURCE.entries
    ico_items = {}
    for kind in root:
        if kind.id != pefile.RESOURCE_TYPE["RT_ICON"]:
            continue
        for id_entry in kind.directory.entries:
            for lang in id_entry.directory.entries:
                r = lang.data.struct
                ico_items[id_entry.id] = pe.get_data(r.OffsetToData, r.Size)
    output_groups = []
    for kind in root:
        if kind.id != pefile.RESOURCE_TYPE["RT_GROUP_ICON"]:
            continue
        for group in kind.directory.entries:
            for lang in group.directory.entries:
                r = lang.data.struct
                group_bytes = pe.get_data(r.OffsetToData, r.Size)
                reserved, icon_type, count = struct.unpack_from("<HHH", group_bytes, 0)
                if reserved != 0 or icon_type != 1 or count < 1:
                    continue
                parts = []
                for i in range(count):
                    width, height, colors, zero, planes, bpp, _, icon_id = struct.unpack_from("<BBBBHHIH", group_bytes, 6 + i * 14)
                    bitmap = ico_items.get(icon_id)
                    if bitmap:
                        parts.append((width, height, colors, zero, planes, bpp, bitmap))
                if parts:
                    output_groups.append(parts)
    if not output_groups:
        raise RuntimeError("No extractable original icon group found in upstream binary")
    return max(output_groups, key=lambda items: (len(items), sum(len(p[-1]) for p in items)))


def save_ico(parts, output):
    header = struct.pack("<HHH", 0, 1, len(parts))
    offset = 6 + 16 * len(parts)
    entries = []
    data = []
    for w, h, colors, zero, planes, bpp, bitmap in parts:
        entries.append(struct.pack("<BBBBHHII", w, h, colors, zero, planes, bpp, len(bitmap), offset))
        data.append(bitmap)
        offset += len(bitmap)
    Path(output).parent.mkdir(parents=True, exist_ok=True)
    Path(output).write_bytes(header + b"".join(entries) + b"".join(data))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("Usage: extract_icon.py Raven.exe output.ico")
    images = extract_groups(Path(sys.argv[1]))
    save_ico(images, sys.argv[2])
    print("Original upstream icon extracted (%d resolutions)" % len(images))
