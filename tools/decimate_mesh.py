#!/usr/bin/env python3
"""
decimate_mesh.py - reduce a high-poly room-scan mesh so it renders fast on Quest 3.

Why: a ~500k-triangle photogrammetry scan is mostly sub-pixel triangles. On the
Quest's tiled GPU every triangle costs at least a 2x2 pixel quad, so hundreds of
thousands of tiny triangles cause massive quad-overdraw and tank the framerate the
moment the mesh fills the view. Dropping to ~20-50k triangles fixes it.

This uses Open3D's quadric-error decimation (same dependency the cluster export
script already needs: Python 3.11 + Open3D + NumPy).

    pip install open3d numpy

Usage (from the repo root):

    # target an absolute triangle count (recommended)
    python tools/decimate_mesh.py Assets/StreamingAssets/clusters/mesh-3hz-4.obj --target 30000

    # or a fraction of the original
    python tools/decimate_mesh.py Assets/StreamingAssets/clusters/mesh-3hz-4.obj --ratio 0.06

    # custom output path (defaults to <input>_decim<TARGET>.obj next to the input)
    python tools/decimate_mesh.py in.obj --target 20000 -o out.obj

IMPORTANT — texture/UVs: Open3D's quadric decimation does NOT preserve UV mapping,
so the decimated OBJ will render UNTEXTURED. That is fine for a quick "does fewer
triangles fix the framerate?" test. For the final, texture-preserving version use
Blender (Decimate modifier -> Collapse) or MeshLab (Quadric Edge Collapse
Decimation *with texture*) instead.
"""

import argparse
import os
import sys

try:
    import open3d as o3d
except ImportError:
    sys.exit("Open3D not found. Install it with:  pip install open3d numpy")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Decimate a mesh with Open3D quadric-error simplification."
    )
    parser.add_argument("input", help="path to the source .obj/.ply mesh")
    parser.add_argument(
        "-o", "--output",
        help="output path (default: <input>_decim<TARGET>.obj beside the input)",
    )
    group = parser.add_mutually_exclusive_group()
    group.add_argument(
        "--target", type=int, default=30000,
        help="target triangle count (default: 30000)",
    )
    group.add_argument(
        "--ratio", type=float,
        help="keep this fraction of the original triangles, e.g. 0.06 (overrides --target)",
    )
    args = parser.parse_args()

    if not os.path.isfile(args.input):
        sys.exit(f"Input file not found: {args.input}")

    print(f"Loading {args.input} ...")
    mesh = o3d.io.read_triangle_mesh(args.input)
    src_tris = len(mesh.triangles)
    src_verts = len(mesh.vertices)
    if src_tris == 0:
        sys.exit("Loaded mesh has 0 triangles - is this a valid mesh file?")
    print(f"  source: {src_verts:,} verts / {src_tris:,} tris")

    if args.ratio is not None:
        if not (0.0 < args.ratio < 1.0):
            sys.exit("--ratio must be between 0 and 1 (exclusive)")
        target = max(4, int(src_tris * args.ratio))
    else:
        target = args.target

    if target >= src_tris:
        sys.exit(
            f"Target ({target:,}) >= source ({src_tris:,}); nothing to decimate. "
            "Pick a smaller --target or --ratio."
        )

    print(f"Decimating to ~{target:,} triangles (quadric error) ...")
    out = mesh.simplify_quadric_decimation(target_number_of_triangles=target)

    # Clean up any degenerate geometry the collapse may leave behind.
    out.remove_degenerate_triangles()
    out.remove_duplicated_triangles()
    out.remove_duplicated_vertices()
    out.remove_unreferenced_vertices()
    out.compute_vertex_normals()

    result_tris = len(out.triangles)
    result_verts = len(out.vertices)

    if args.output:
        output = args.output
    else:
        base, _ = os.path.splitext(args.input)
        output = f"{base}_decim{result_tris}.obj"

    # write_vertex_normals so Unity gets smooth normals without RecalculateNormals.
    ok = o3d.io.write_triangle_mesh(output, out, write_vertex_normals=True)
    if not ok:
        sys.exit(f"Failed to write output mesh to {output}")

    reduction = 100.0 * (1.0 - result_tris / src_tris)
    print(
        f"  result: {result_verts:,} verts / {result_tris:,} tris "
        f"({reduction:.1f}% fewer triangles)"
    )
    print(f"Wrote {output}")
    print(
        "\nNote: this output is UNTEXTURED (Open3D drops UVs). Use it to confirm the "
        "framerate recovers, then make the textured final version in Blender/MeshLab."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
