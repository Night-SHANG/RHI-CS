"""Export the approved four-tile SVG to a multi-size ICO using only the Python standard library."""
import struct
import zlib
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "src/Rhi.App/Assets"
tiles = ET.fromstring((root / "app-icon.svg").read_text(encoding="utf-8"))

def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

def png(size):
    pixels = bytearray()
    for y in range(size):
        pixels.append(0)
        for x in range(size):
            hits = []
            for sy in range(4):
                for sx in range(4):
                    px, py = (x + (sx + .5) / 4) * 256 / size, (y + (sy + .5) / 4) * 256 / size
                    for tile in tiles:
                        tx, ty, w, h, r = [float(tile.attrib[k]) for k in ("x", "y", "width", "height", "rx")]
                        cx, cy = max(tx + r, min(px, tx + w - r)), max(ty + r, min(py, ty + h - r))
                        if (px - cx) ** 2 + (py - cy) ** 2 <= r * r:
                            color = tile.attrib["fill"]
                            hits.append(tuple(int(color[i:i+2], 16) for i in (1, 3, 5)))
                            break
            pixels.extend((*hits[0], round(255 * len(hits) / 16)) if hits else (0, 0, 0, 0))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(pixels), 9)) + chunk(b"IEND", b""))

sizes = (16, 20, 24, 32, 40, 48, 64, 128, 256)
images = [png(size) for size in sizes]
offset = 6 + 16 * len(sizes)
directory = bytearray(struct.pack("<HHH", 0, 1, len(sizes)))
for size, data in zip(sizes, images):
    directory.extend(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
    offset += len(data)
(root / "app.ico").write_bytes(bytes(directory) + b"".join(images))
print("Generated app.ico:", list(sizes))
