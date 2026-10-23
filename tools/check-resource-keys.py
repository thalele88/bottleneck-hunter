"""Falha se alguma chave de cor do tema (Themes/Dark.xaml) for reaproveitada como chave de outro recurso.

O WPF resolve {DynamicResource X} pelo último dicionário que tem a chave X. Se um Style e um Brush
usam o mesmo nome, o app quebra ao abrir ("'System.Windows.Style' não é um valor válido para a propriedade 'Background'").
"""
import glob
import re
import sys

ROOT = "src/BottleneckHunter"
theme = set(re.findall(r'x:Key="([^"]+)"', open(f"{ROOT}/Themes/Dark.xaml", encoding="utf-8").read()))
light = set(re.findall(r'x:Key="([^"]+)"', open(f"{ROOT}/Themes/Light.xaml", encoding="utf-8").read()))

problems = [f"Dark.xaml e Light.xaml têm chaves diferentes: {sorted(theme ^ light)}"] if theme != light else []
for path in glob.glob(f"{ROOT}/**/*.xaml", recursive=True):
    if path.replace("\\", "/").endswith(("Themes/Dark.xaml", "Themes/Light.xaml")):
        continue
    for key in re.findall(r'x:Key="([^"]+)"', open(path, encoding="utf-8").read()):
        if key in theme:
            problems.append(f"{path}: a chave '{key}' já é uma cor do tema")

print("\n".join(problems) or "Chaves de recurso ok.")
sys.exit(1 if problems else 0)
