"""Compare fixed-input WebGL and D3D water frames, retaining visible errors."""
import json
import os
from pathlib import Path
import numpy as np
from PIL import Image
root = Path(os.environ.get('WATER_OUTPUT_DIR', Path(__file__).resolve().parents[2] / '_codex_work/water-comparison'))
a = np.asarray(Image.open(root/'web-composite.png').convert('RGB')).astype(np.int16)
b = np.asarray(Image.open(root/'unity-composite.png').convert('RGB')).astype(np.int16)
delta = np.abs(a-b)
regions = {'all': delta, 'surface': delta[389:510], 'underwater': delta[513:]}
report = {name: {'mae': float(error.mean()), 'max': int(error.max()), 'p99': float(np.percentile(error, 99)),
                 'within_2': float((error.max(axis=2) <= 2).mean())} for name,error in regions.items()}
passes = {}
for name in ('surface', 'underwater'):
    web_path, unity_path = root/f'web-{name}.png', root/f'unity-{name}.png'
    if not web_path.exists() or not unity_path.exists():
        continue
    web_pass = np.asarray(Image.open(web_path).convert('RGBA')).astype(np.int16)
    unity_pass = np.asarray(Image.open(unity_path).convert('RGBA')).astype(np.int16)
    visible = (web_pass[:,:,3] > 0) | (unity_pass[:,:,3] > 0)
    rgb_error = np.abs(web_pass[:,:,:3] - unity_pass[:,:,:3])[visible]
    alpha_error = np.abs(web_pass[:,:,3] - unity_pass[:,:,3])[visible]
    passes[name] = {'visible_pixels': int(visible.sum()), 'rgb_mae': float(rgb_error.mean()),
                    'rgb_max': int(rgb_error.max()), 'rgb_p99': float(np.percentile(rgb_error, 99)),
                    'alpha_mae': float(alpha_error.mean()), 'alpha_max': int(alpha_error.max())}
if passes:
    report['passes'] = passes
Image.fromarray(np.minimum(delta*8,255).astype(np.uint8)).save(root/'difference-x8.png')
(root/'pixel-comparison.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report))
