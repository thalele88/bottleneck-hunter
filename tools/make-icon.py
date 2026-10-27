"""Gera src/BottleneckHunter/Assets/app.ico (o mesmo alvo laranja do logo do app), em vários tamanhos."""
import math
from PIL import Image, ImageDraw

BG = (14, 15, 18, 255)
ACCENT = (255, 122, 26, 255)
SOFT = (72, 41, 20, 255)  # laranja a ~24% sobre o fundo (ImageDraw não mistura alfa)

def draw(size: int) -> Image.Image:
    s = size * 4  # desenha grande e reduz, para bordas suaves
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = s * 0.04
    d.rounded_rectangle([pad, pad, s - pad, s - pad], radius=s * 0.22, fill=BG)
    c, r = s / 2, s * 0.34
    d.ellipse([c - r, c - r, c + r, c + r], fill=SOFT)
    # Anel tracejado (como no logo da barra lateral)
    w = max(2, int(s * 0.055))
    ring = s * 0.27
    for i in range(10):
        a0 = i * 36 + 4
        d.arc([c - ring, c - ring, c + ring, c + ring], a0, a0 + 26, fill=ACCENT, width=w)
    dot = s * 0.085
    d.ellipse([c - dot, c - dot, c + dot, c + dot], fill=ACCENT)
    return img.resize((size, size), Image.LANCZOS)

sizes = [16, 24, 32, 48, 64, 128, 256]
big = draw(256)
big.save("src/BottleneckHunter/Assets/app.ico", sizes=[(n, n) for n in sizes],
         append_images=[draw(n) for n in sizes[:-1]])
print("ok")
