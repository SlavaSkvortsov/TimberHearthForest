#!/usr/bin/env python3
"""
Augment Timber Hearth tree spawn data with additional trees in sparse areas.

Workflow:
1) Use `treeSpawnData.original.json` as the baseline if it exists.
2) If it does not exist, create it as a copy of `treeSpawnData.json`.
3) Generate new points into `treeSpawnData.generated.json`.
4) Overwrite `treeSpawnData.json` with the generated file.

Usage:
    python augment_tree_spawns.py

Edit the constants below to tune behavior.
"""

from __future__ import annotations

import json
import math
import random
import shutil
from pathlib import Path
from typing import List, Sequence, Tuple

Vec3 = Tuple[float, float, float]
PropRow = List[float]  # [x, y, z, rx, ry, rz]

# ----------------------------
# User-tweakable constants
# ----------------------------
# Set to this script's folder by default. Works on Windows/Linux/macOS.
ASSETS_DIR = Path(__file__).resolve().parent

# Number of trees to add each run.
ADD_COUNT = 200

# Quality/speed tradeoff: higher tests more candidates per new tree.
CANDIDATES_PER_TREE = 512

# Placement jitter in local-space units.
RADIAL_JITTER = 8.0
TANGENTIAL_JITTER = 12.0

# Random pitch/roll jitter for generated rotations (degrees).
ROTATION_JITTER_DEG = 12.0

# Set to an int for reproducible output, or None for a new random result each run.
SEED = None


def _vec_len(v: Vec3) -> float:
    return math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])


def _vec_add(a: Vec3, b: Vec3) -> Vec3:
    return a[0] + b[0], a[1] + b[1], a[2] + b[2]


def _vec_sub(a: Vec3, b: Vec3) -> Vec3:
    return a[0] - b[0], a[1] - b[1], a[2] - b[2]


def _vec_scale(v: Vec3, s: float) -> Vec3:
    return v[0] * s, v[1] * s, v[2] * s


def _vec_norm(v: Vec3) -> Vec3:
    length = _vec_len(v)
    if length <= 1e-8:
        return 0.0, 1.0, 0.0
    inv = 1.0 / length
    return v[0] * inv, v[1] * inv, v[2] * inv


def _dist(a: Vec3, b: Vec3) -> float:
    dx = a[0] - b[0]
    dy = a[1] - b[1]
    dz = a[2] - b[2]
    return math.sqrt(dx * dx + dy * dy + dz * dz)


def _random_unit(rng: random.Random) -> Vec3:
    z = rng.uniform(-1.0, 1.0)
    theta = rng.uniform(0.0, 2.0 * math.pi)
    r_xy = math.sqrt(max(0.0, 1.0 - z * z))
    return r_xy * math.cos(theta), r_xy * math.sin(theta), z


def _nearest_distance(point: Vec3, points: Sequence[Vec3]) -> float:
    best = float("inf")
    for p in points:
        d = _dist(point, p)
        if d < best:
            best = d
    return best


def _median(values: Sequence[float]) -> float:
    if not values:
        return 1.0
    s = sorted(values)
    n = len(s)
    m = n // 2
    if n % 2 == 1:
        return s[m]
    return 0.5 * (s[m - 1] + s[m])


def _clamp(x: float, lo: float, hi: float) -> float:
    return lo if x < lo else hi if x > hi else x


def _choose_sparse_point(
    all_points: Sequence[Vec3],
    base_radius: float,
    radius_std: float,
    rng: random.Random,
    candidates_per_tree: int,
) -> Vec3:
    # Farthest-point style: sample many candidates and keep the one
    # with the largest nearest-neighbor distance.
    best_p: Vec3 | None = None
    best_score = -1.0

    for _ in range(candidates_per_tree):
        unit = _random_unit(rng)
        sampled_r = base_radius + rng.gauss(0.0, radius_std)
        sampled_r = max(1.0, sampled_r)
        p = _vec_scale(unit, sampled_r)
        score = _nearest_distance(p, all_points)
        if score > best_score:
            best_score = score
            best_p = p

    # Safety fallback
    if best_p is None:
        return _vec_scale(_random_unit(rng), base_radius)
    return best_p


def _estimate_rotation_from_surface_normal(normal: Vec3) -> Vec3:
    # Approximate pitch/yaw from outward normal so trees are "standing out"
    # of the planet surface; roll is randomized later.
    nx, ny, nz = _vec_norm(normal)
    yaw = math.degrees(math.atan2(nx, nz))
    pitch = math.degrees(math.atan2(-ny, math.sqrt(nx * nx + nz * nz)))
    roll = 0.0
    return pitch % 360.0, yaw % 360.0, roll


def _round3(v: float) -> float:
    return round(v, 3)


def _load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def _write_json(path: Path, obj: dict) -> None:
    with path.open("w", encoding="utf-8") as f:
        json.dump(obj, f, indent=2)
        f.write("\n")


def _ensure_baseline(data_path: Path, original_path: Path) -> Path:
    if original_path.exists():
        return original_path
    if not data_path.exists():
        raise FileNotFoundError(
            f"Could not find '{data_path.name}' and no baseline "
            f"'{original_path.name}' exists."
        )
    shutil.copy2(data_path, original_path)
    return original_path


def augment_spawns(
    baseline_rows: List[PropRow],
    add_count: int,
    candidates_per_tree: int,
    radial_jitter: float,
    tangential_jitter: float,
    rot_jitter_deg: float,
    rng: random.Random,
) -> List[PropRow]:
    if add_count <= 0:
        return baseline_rows.copy()
    if not baseline_rows:
        raise ValueError("Baseline has no props.")

    # Work in local Timber Hearth coordinates.
    points: List[Vec3] = [(r[0], r[1], r[2]) for r in baseline_rows]
    rows_out = baseline_rows.copy()

    radii = [_vec_len(p) for p in points]
    base_radius = _median(radii)
    radius_std = max(0.1, radial_jitter)

    for _ in range(add_count):
        sparse = _choose_sparse_point(
            points,
            base_radius=base_radius,
            radius_std=radius_std,
            rng=rng,
            candidates_per_tree=candidates_per_tree,
        )

        # Add a small radial jitter + tangent-ish jitter to avoid overly regular spacing.
        normal = _vec_norm(sparse)
        radial = _vec_scale(normal, rng.uniform(-radial_jitter, radial_jitter))
        tangent_noise = _vec_scale(_random_unit(rng), tangential_jitter)
        planted = _vec_add(sparse, _vec_add(radial, tangent_noise))

        # Snap approximately back toward the planetary shell.
        target_radius = base_radius + rng.uniform(-radial_jitter, radial_jitter)
        planted = _vec_scale(_vec_norm(planted), target_radius)

        # Rotation based on surface normal + random yaw/roll jitter.
        rx, ry, rz = _estimate_rotation_from_surface_normal(planted)
        rx = (rx + rng.uniform(-rot_jitter_deg, rot_jitter_deg)) % 360.0
        ry = (ry + rng.uniform(0.0, 360.0)) % 360.0
        rz = (rz + rng.uniform(-rot_jitter_deg, rot_jitter_deg)) % 360.0

        new_row: PropRow = [
            _round3(planted[0]),
            _round3(planted[1]),
            _round3(planted[2]),
            _round3(rx),
            _round3(ry),
            _round3(rz),
        ]
        rows_out.append(new_row)
        points.append((new_row[0], new_row[1], new_row[2]))

    return rows_out


def main() -> int:
    assets_dir: Path = ASSETS_DIR.resolve()
    data_path = assets_dir / "treeSpawnData.json"
    original_path = assets_dir / "treeSpawnData.original.json"
    generated_path = assets_dir / "treeSpawnData.generated.json"

    baseline_path = _ensure_baseline(data_path, original_path)
    baseline_obj = _load_json(baseline_path)

    props = baseline_obj.get("props")
    if not isinstance(props, list):
        raise ValueError(f"Expected a top-level 'props' array in '{baseline_path.name}'.")

    rows: List[PropRow] = []
    for i, row in enumerate(props):
        if not isinstance(row, list) or len(row) != 6:
            raise ValueError(f"Invalid row at index {i}: expected [x,y,z,rx,ry,rz].")
        rows.append([float(v) for v in row])

    add_count = max(0, ADD_COUNT)
    candidates = max(32, CANDIDATES_PER_TREE)
    radial_jitter = _clamp(RADIAL_JITTER, 0.0, 200.0)
    tangential_jitter = _clamp(TANGENTIAL_JITTER, 0.0, 400.0)
    rot_jitter = _clamp(ROTATION_JITTER_DEG, 0.0, 90.0)

    rng = random.Random(SEED)
    augmented = augment_spawns(
        baseline_rows=rows,
        add_count=add_count,
        candidates_per_tree=candidates,
        radial_jitter=radial_jitter,
        tangential_jitter=tangential_jitter,
        rot_jitter_deg=rot_jitter,
        rng=rng,
    )

    out_obj = dict(baseline_obj)
    out_obj["props"] = augmented

    _write_json(generated_path, out_obj)
    shutil.copy2(generated_path, data_path)

    print(f"Baseline used:    {baseline_path}")
    print(f"Generated file:   {generated_path}")
    print(f"Active file set:  {data_path}")
    print(f"Original count:   {len(rows)}")
    print(f"Added:            {add_count}")
    print(f"Final count:      {len(augmented)}")
    if SEED is not None:
        print(f"Seed:             {SEED}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
