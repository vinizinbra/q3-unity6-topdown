"""
Low-poly post-apocalyptic sci-fi platform tile kit (Blender 4.2+).

Run:
  Blender -b -P build_scifi_tilekit.py -- <fbx_out_dir> <blend_out_path> [preview_dir]

Same layout contract as build_tilekit.py (Blender Z-up, top surface at z=1):
  SciFi_Center     1x1x1   plain cube                       pivot bottom-center
  SciFi_Edge       1x1x1   wall on -Y                       pivot bottom-center
  SciFi_CornerEdge 1x1x1   convex 45deg-chamfer corner, walls on -X/-Y
  SciFi_InnerEdge  2x1x2   concave chamfer corner, void quadrant at -X/-Y, pivot = 2x2 center

Walls are one plated profile swept along a polyline (mitered at corners so every wall stays
planar). Every path end uses the "seam groove" profile, so pieces match vertex-for-vertex and a
panel line lands on every tile seam.
"""
import bpy, bmesh, math, random, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
FBX_DIR = argv[0] if len(argv) > 0 else "//fbx"
BLEND_PATH = argv[1] if len(argv) > 1 else ""
PREVIEW_DIR = argv[2] if len(argv) > 2 else ""

# ---------------------------------------------------------------- look knobs
SURFACE, WALL, TRIM, GLOW = 0, 1, 2, 3
COLORS = {
    SURFACE: (0.560, 0.520, 0.440),   # dusty concrete deck
    WALL: (0.165, 0.180, 0.205),      # dark gunmetal plating
    TRIM: (0.520, 0.245, 0.105),      # rusted steel trim
    GLOW: (0.150, 0.900, 1.000),      # cyan light strip
}
GLOW_EMISSION = 6.0
GLOW_DEAD_CHANCE = 0.35       # chance a strip segment is burnt out
CHIP_CHANCE = 0.55            # chance a panel gets a chipped lip
CHAMFER_CONVEX = 0.30
CHAMFER_CONCAVE = 0.40
GROOVE = 0.025                # panel-seam inset
GROOVE_HALF = 0.02            # half width of a panel seam


def profile(groove=False, chip=False):
    """(u = inward distance from tile boundary, z, material of band above, tint), bottom -> top."""
    g = GROOVE if groove else 0.0
    c = 0.05 if chip else 0.0
    P = [
        (0.09, 0.00, WALL, 0.55),       # foot
        (0.07, 0.02, WALL, 0.55),
        (0.07, 0.14, WALL, 0.70),       # plinth
        (0.10 + g, 0.17, WALL, 0.90),
        (0.10 + g, 0.40, WALL, 0.90),   # lower panel
        (0.12 + g, 0.42, GLOW, 1.00),   # light strip recess
        (0.12 + g, 0.46, WALL, 0.80),
        (0.10 + g, 0.48, WALL, 0.90),
        (0.08 + g, 0.50, WALL, 1.00),   # upper panel
        (0.08 + g, 0.78, TRIM, 0.85),
        (0.03, 0.83, TRIM, 1.00),       # rusted trim band
        (0.03, 0.90, TRIM, 0.80),
        (0.00 + c, 0.93 - c * 0.6, TRIM, 1.00),   # lip
        (0.00 + c, 0.96 - c * 0.6, TRIM, 1.10),
        (0.04 + c * 1.6, 1.00, TRIM, 0.90),       # top chamfer -> edge plate
        (0.16, 1.00, None, 1.0),
    ]
    return P


# ---------------------------------------------------------------- paths (polyline, solid on the LEFT)
C1, C2 = CHAMFER_CONVEX, CHAMFER_CONCAVE
PIECES = {
    "SciFi_Edge": dict(path=[(-0.5, -0.5), (0.5, -0.5)], corners=[(0.5, 0.5), (-0.5, 0.5)], seed=3),
    "SciFi_CornerEdge": dict(path=[(-0.5, 0.5), (-0.5, -0.5 + C1), (-0.5 + C1, -0.5), (0.5, -0.5)],
                             corners=[(0.5, 0.5)], seed=7),
    "SciFi_InnerEdge": dict(path=[(-1, 0), (-C2, 0), (0, -C2), (0, -1)],
                            corners=[(1, -1), (1, 1), (-1, 1)], seed=13),
}


def left_normal(d):
    return Vector((-d.y, d.x))


def build_samples(path, rng):
    """Returns [(point, offset_dir, groove, chip)] along the polyline."""
    pts = [Vector(p) for p in path]
    dirs = [(pts[i + 1] - pts[i]).normalized() for i in range(len(pts) - 1)]
    lens = [(pts[i + 1] - pts[i]).length for i in range(len(pts) - 1)]
    out = []

    def add(p, n, groove=False, chip=False):
        out.append((p, n, groove, chip))

    for si, (a, d, L) in enumerate(zip(pts, dirs, lens)):
        n = left_normal(d)
        # vertex at segment start (mitered if interior)
        if si == 0:
            add(a, n, groove=True)
            add(a + d * GROOVE_HALF, n, groove=True)
            add(a + d * (GROOVE_HALF + 0.02), n)
        else:
            m = (left_normal(dirs[si - 1]) + n).normalized()
            add(a, m / m.dot(n))
        # interior features: a panel seam at the middle of long runs, random chips per panel
        feats = []
        if L >= 0.5:
            feats.append((L / 2, "seam"))
        panels = [(0.12, L / 2 - 0.12), (L / 2 + 0.12, L - 0.12)] if L >= 0.5 else [(0.12, L - 0.12)]
        for lo, hi in panels:
            if hi - lo > 0.16 and rng.random() < CHIP_CHANCE:
                feats.append((rng.uniform(lo + 0.06, hi - 0.06), "chip"))
        for t, kind in sorted(feats):
            if kind == "seam":
                for off, gr in ((-GROOVE_HALF - 0.02, False), (-GROOVE_HALF, True), (GROOVE_HALF, True), (GROOVE_HALF + 0.02, False)):
                    add(a + d * (t + off), n, groove=gr)
            else:
                w = rng.uniform(0.05, 0.09)
                add(a + d * (t - w), n); add(a + d * t, n, chip=True); add(a + d * (t + w), n)
    b, d = pts[-1], dirs[-1]
    n = left_normal(d)
    add(b - d * (GROOVE_HALF + 0.02), n)
    add(b - d * GROOVE_HALF, n, groove=True)
    add(b, n, groove=True)
    return out


# ---------------------------------------------------------------- materials
def make_mat(name, rgb, emission=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = 0.75
    bsdf.inputs["Metallic"].default_value = 0.2 if emission == 0 else 0.0
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*rgb, 1)
        bsdf.inputs["Emission Strength"].default_value = emission
    m.diffuse_color = (*rgb, 1)
    return m

MATS = [make_mat("SciFiSurface", COLORS[SURFACE]), make_mat("SciFiWall", COLORS[WALL]),
        make_mat("SciFiTrim", COLORS[TRIM]), make_mat("SciFiGlow", COLORS[GLOW], GLOW_EMISSION)]


def finish(bm, name, face_tint):
    col = bm.loops.layers.color.new("Color")
    for f in bm.faces:
        base, t = COLORS[f.material_index], face_tint.get(f, 1.0)
        for l in f.loops:
            l[col] = (min(1, base[0] * t), min(1, base[1] * t), min(1, base[2] * t), 1.0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4],
                          quad_method="BEAUTY", ngon_method="BEAUTY")
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for m in MATS:
        me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = False
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def build_swept(name, path, corners, seed):
    rng = random.Random(seed)
    samples = build_samples(path, rng)
    n = len(samples) - 1
    bm = bmesh.new()
    grid, prof0 = None, None
    for i, (p, off, groove, chip) in enumerate(samples):
        prof = profile(groove, chip)
        if grid is None:
            grid, prof0 = [[None] * (n + 1) for _ in prof], prof
        for k, (u, z, _, _) in enumerate(prof):
            grid[k][i] = bm.verts.new((p.x + off.x * u, p.y + off.y * u, z))
    K = len(grid) - 1
    top_c = [bm.verts.new((c[0], c[1], 1.0)) for c in corners]
    bot_c = [bm.verts.new((c[0], c[1], 0.0)) for c in corners]

    tint = {}
    def face(vs, mat, t=1.0):
        f = bm.faces.new(vs); f.material_index = mat; tint[f] = t

    glow_alive = [rng.random() >= GLOW_DEAD_CHANCE for _ in range(n)]
    for k in range(K):
        mat, t = prof0[k][2], prof0[k][3]
        for i in range(n):
            m, tt = mat, t
            if mat == GLOW and not glow_alive[i]:
                m, tt = WALL, 0.35
            face([grid[k][i], grid[k][i + 1], grid[k + 1][i + 1], grid[k + 1][i]], m, tt)
    face([grid[K][i] for i in range(n + 1)] + top_c, SURFACE)
    face([grid[0][i] for i in range(n + 1)] + bot_c, WALL, 0.4)
    face([grid[k][n] for k in range(K + 1)] + [top_c[0], bot_c[0]], WALL, 0.6)
    for j in range(len(corners) - 1):
        face([bot_c[j], top_c[j], top_c[j + 1], bot_c[j + 1]], WALL, 0.6)
    face([bot_c[-1]] + [grid[k][0] for k in range(K + 1)] + [top_c[-1]], WALL, 0.6)
    return finish(bm, name, tint)


def build_center():
    bm = bmesh.new()
    v = [bm.verts.new((x, y, z)) for z in (0, 1) for y in (-0.5, 0.5) for x in (-0.5, 0.5)]
    tint = {}
    for idx, mat in (((4, 5, 7, 6), SURFACE), ((0, 2, 3, 1), WALL), ((0, 1, 5, 4), WALL),
                     ((1, 3, 7, 5), WALL), ((3, 2, 6, 7), WALL), ((2, 0, 4, 6), WALL)):
        f = bm.faces.new([v[i] for i in idx]); f.material_index = mat; tint[f] = 1.0 if mat == SURFACE else 0.6
    return finish(bm, "SciFi_Center", tint)


# ---------------------------------------------------------------- build
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

pieces = {"SciFi_Center": build_center()}
for name, d in PIECES.items():
    pieces[name] = build_swept(name, d["path"], d["corners"], d["seed"])

for name, o in pieces.items():
    bb = [Vector(c) for c in o.bound_box]
    lo = [min(p[a] for p in bb) for a in range(3)]
    hi = [max(p[a] for p in bb) for a in range(3)]
    print(f"[tilekit] {name}: tris={sum(len(p.vertices) - 2 for p in o.data.polygons)} "
          f"bounds x[{lo[0]:.3f},{hi[0]:.3f}] y[{lo[1]:.3f},{hi[1]:.3f}] z[{lo[2]:.3f},{hi[2]:.3f}]")

os.makedirs(FBX_DIR, exist_ok=True)
for name, o in pieces.items():
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(FBX_DIR, name + ".fbx"), use_selection=True,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
        add_leaf_bones=False, bake_anim=False, colors_type="SRGB")

if BLEND_PATH:
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

# ---------------------------------------------------------------- preview render
if PREVIEW_DIR:
    scn = bpy.context.scene
    col = bpy.data.collections.new("Preview"); scn.collection.children.link(col)
    for o in pieces.values():
        o.hide_render = True

    def place(name, x, y, rot, z=0.0):
        o = bpy.data.objects.new(name + "_inst", pieces["SciFi_" + name].data)
        o.location = (x, y, z); o.rotation_euler = (0, 0, math.radians(rot))
        col.objects.link(o)

    place("InnerEdge", 1.5, 1.5, 0)
    place("CornerEdge", 2, 0, 0); place("Edge", 3, 0, 0); place("Edge", 4, 0, 0)
    place("CornerEdge", 5, 0, 90); place("Edge", 5, 1, 90); place("Edge", 5, 2, 90)
    place("CornerEdge", 5, 3, 180)
    for x in (1, 2, 3, 4):
        place("Edge", x, 3, 180)
    place("CornerEdge", 0, 3, 270); place("CornerEdge", 0, 2, 0)
    for x, y in ((3, 1), (4, 1), (3, 2), (4, 2)):
        place("Center", x, y, 0)
    place("CornerEdge", 3, 1, 0, 1); place("CornerEdge", 4, 1, 90, 1)
    place("CornerEdge", 4, 2, 180, 1); place("CornerEdge", 3, 2, 270, 1)
    for i, n in enumerate(["Center", "Edge", "CornerEdge"]):
        place(n, i * 1.8, -4.5, 0)
    place("InnerEdge", 6.4, -4.5, 0)

    ground = bpy.data.meshes.new("Ground")
    bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=30); bm.to_mesh(ground); bm.free()
    ground.materials.append(make_mat("PreviewWasteland", (0.20, 0.15, 0.12)))
    go = bpy.data.objects.new("Ground", ground); go.location = (2.5, 0, 0.0); col.objects.link(go)

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0; sun.data.angle = math.radians(6); sun.data.color = (1.0, 0.85, 0.7)
    sun.rotation_euler = (math.radians(52), math.radians(10), math.radians(-35))
    col.objects.link(sun)
    world = bpy.data.worlds.new("W"); scn.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.40, 0.32, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.45

    scn.render.engine = "CYCLES"
    scn.cycles.samples = 48; scn.cycles.use_denoising = True; scn.cycles.device = "CPU"
    scn.view_settings.view_transform = "Standard"
    scn.render.resolution_x, scn.render.resolution_y = 1600, 1000

    def shoot(fname, loc, target):
        cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); col.objects.link(cam)
        cam.location = loc; cam.data.lens = 40
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scn.camera = cam
        scn.render.filepath = os.path.join(PREVIEW_DIR, fname)
        bpy.ops.render.render(write_still=True)

    shoot("scifi_island.png", (0.5, -5.5, 7.5), (2.5, 0.8, 0.5))
    shoot("scifi_pieces.png", (3.0, -10.5, 5.0), (3.0, -4.6, 0.4))
    shoot("scifi_closeup.png", (-0.6, -2.4, 2.6), (1.4, 0.4, 0.8))
