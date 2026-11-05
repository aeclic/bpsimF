"""Compare a fixed-input water replay with the reference player's screenshot."""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image


def metrics(delta):
    per_pixel = delta.max(axis=1) if delta.ndim == 2 else delta.max(axis=2)
    return {
        "mae": float(delta.mean()),
        "p99": float(np.percentile(per_pixel, 99)),
        "max": int(delta.max()),
        "within_2": float((per_pixel <= 2).mean()),
        "exact": float((per_pixel == 0).mean()),
    }


parser = argparse.ArgumentParser()
parser.add_argument("directory", nargs="?", type=Path,
                    default=Path(__file__).resolve().parents[2] / "_codex_work/water-reference")
args = parser.parse_args()
reference = np.asarray(Image.open(args.directory / "reference-frame.png").convert("RGB"), dtype=np.int16)
replay = np.asarray(Image.open(args.directory / "port-replay.png").convert("RGB"), dtype=np.int16)
if reference.shape != replay.shape:
    raise SystemExit(f"image dimensions differ: reference={reference.shape}, replay={replay.shape}")

draw = json.loads((args.directory / "draw-inputs.json").read_text(encoding="utf-8"))
surface = next(record for record in draw["records"] if record["kind"] == "WaterSurface")
uniforms = surface["uniforms"]
vp = uniforms["hlslcc_mtx4x4unity_MatrixVP[0]"]
model = uniforms["hlslcc_mtx4x4unity_ObjectToWorld[0]"]
distance = vp[15] + model[14]
camera_y = -vp[13] / vp[5] / 8
zoom = reference.shape[0] * 0.5 * vp[5] / distance * 8
surface_y = reference.shape[0] * 0.5 - (0 - camera_y) * zoom
y = np.arange(reference.shape[0])[:, None]
waterline = np.abs(y - surface_y) <= 12
underwater = y > surface_y + 12
delta = np.abs(reference - replay)
report = {
    "size": [int(reference.shape[1]), int(reference.shape[0])],
    "waterline_y": float(surface_y),
    "all": metrics(delta),
    "waterline": metrics(delta[np.broadcast_to(waterline, delta.shape[:2])]),
    "underwater": metrics(delta[np.broadcast_to(underwater, delta.shape[:2])]),
}
Image.fromarray(np.minimum(delta * 8, 255).astype(np.uint8)).save(args.directory / "reference-difference-x8.png")
(args.directory / "reference-comparison.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
