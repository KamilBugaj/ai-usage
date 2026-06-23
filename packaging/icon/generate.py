"""
Generate app icon files from code.
Run from repo root: python packaging/icon/generate.py
Outputs: master.png, app.ico, icon-256.png, icon-512.png, icon.icns
"""
import struct, io, os
from PIL import Image, ImageDraw

SIZES = [16, 32, 48, 64, 128, 256, 512, 1024]
BG   = (15, 17, 23, 255)    # #0f1117
BAR1 = (124, 58, 237, 255)  # #7c3aed
BAR2 = (167, 110, 255, 255) # #a76eff

def draw_icon(size):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    d   = ImageDraw.Draw(img)
    s   = size / 256.0

    # background
    br = max(1, round(54 * s))
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=br, fill=BG)

    # three ascending bars (SVG source coords on 256px canvas)
    # bar: x, y_top, w, h  (y measured from top; bars bottom-align at y=220)
    bars = [
        (52,  168, 44,  52,  BAR1),  # left
        (106, 128, 44,  92,  BAR1),  # mid
        (160,  80, 44, 140,  BAR2),  # right (brighter)
    ]
    rx = max(1, round(6 * s))
    for x, y, w, h, color in bars:
        x0, y0 = round(x * s), round(y * s)
        x1, y1 = round((x + w) * s) - 1, round((y + h) * s) - 1
        d.rounded_rectangle([x0, y0, x1, y1], radius=rx, fill=color)

    return img


def save_ico(path, size_img_pairs):
    entries = []
    for size, img in size_img_pairs:
        buf = io.BytesIO()
        img.convert('RGBA').save(buf, format='PNG')
        entries.append((size, buf.getvalue()))

    header = 6
    dir_sz = 16 * len(entries)
    offset = header + dir_sz
    offsets = []
    for _, data in entries:
        offsets.append(offset)
        offset += len(data)

    with open(path, 'wb') as f:
        f.write(struct.pack('<HHH', 0, 1, len(entries)))
        for i, (size, data) in enumerate(entries):
            w = size if size < 256 else 0
            h = size if size < 256 else 0
            f.write(struct.pack('<BBBBHHII', w, h, 0, 0, 1, 32, len(data), offsets[i]))
        for _, data in entries:
            f.write(data)


def save_icns(path, imgs):
    type_map = [(256, b'ic08'), (512, b'ic09'), (1024, b'ic10')]
    entries = []
    for sz, tc in type_map:
        buf = io.BytesIO()
        imgs[sz].convert('RGBA').save(buf, format='PNG')
        entries.append((tc, buf.getvalue()))
    total = 8 + sum(8 + len(d) for _, d in entries)
    with open(path, 'wb') as f:
        f.write(b'icns')
        f.write(struct.pack('>I', total))
        for tc, d in entries:
            f.write(tc)
            f.write(struct.pack('>I', 8 + len(d)))
            f.write(d)


os.makedirs('packaging/icon', exist_ok=True)

imgs = {s: draw_icon(s) for s in SIZES}

imgs[1024].save('packaging/icon/master.png')

ico_pairs = [(s, imgs[s]) for s in [16, 32, 48, 64, 128, 256]]
save_ico('packaging/icon/app.ico', ico_pairs)

imgs[256].save('packaging/icon/icon-256.png')
imgs[512].save('packaging/icon/icon-512.png')

save_icns('packaging/icon/icon.icns', imgs)

for name in ['master.png', 'app.ico', 'icon.icns', 'icon-256.png', 'icon-512.png']:
    p = f'packaging/icon/{name}'
    print(f'{p}: {os.path.getsize(p):,} bytes')
