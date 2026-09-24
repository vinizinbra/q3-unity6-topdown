"""
Procedural low-poly cliff tile kit (Blender 4.2+).

Run:
  Blender -b -P build_tilekit.py -- <fbx_out_dir> <blend_out_path> [preview_dir]

Pieces (Blender Z-up, pivot = bottom-center of the footprint, top surface at z=1):
  Tile_Center     1x1x1   plain cube
  Tile_Edge       1x1x1   wall on -Y
  Tile_CornerEdge 1x1x1   convex corner, walls on -X and -Y
  Tile_InnerEdge  2x1x2   concave corner (L of 3 solid cells), void quadrant at -X/-Y,
                          pivot at the center of the 2x2 footprint (a grid vertex)

Every wall is the same layered-strata profile swept along a path. The profile is forced to
its base shape at every path end, so any seam between two pieces matches vertex-for-vertex.
"""
import bpy, bmesh, math, random, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
FBX_DIR = argv[0] if len(argv) > 0 else "//fbx"
BLEND_PATH = argv[1] if len(argv) > 1 else ""
PREVIEW_DIR = argv[2] if len(argv) > 2 else ""

# ---------------------------------------------------------------- look knobs
SURFACE_COLOR = (0.965, 0.780, 0.470)   # light sand top
WALL_COLOR = (0.880, 0.470, 0.170)      # warm orange cliff
# (z0, z1, base inset) per stratum, bottom -> top. Cap slab sits above the last one.
LAYERS = [(0.00, 0.22, 0.085), (0.22, 0.42, 0.040), (0.42, 0.62, 0.070), (0.62, 0.78, 0.030)]
LAYER_TINT = [0.78, 0.86, 0.92, 0.97]
CAP_Z = 0.78
TOP_ROUND = 0.10           # radius of the rounded top lip
GROOVE = 0.045             # extra inset of the crease between strata
WOBBLE = 0.045             # per-stratum organic in/out variation (inward only -> stays in bounds)
CAP_WOBBLE = 0.020
CORNER_RADIUS = 0.35       # plan radius of convex + concave corners
SAMPLES_PER_UNIT = 12
KEY_STEP = 3               # wobble key every N samples


# ---------------------------------------------------------------- profile
def profile(ws):
    """Cross-section (u = inward distance from tile boundary, z) bottom -> top.
    ws: wobble per stratum + cap. Returns points, band material ids, band tints."""
    bs = [L[2] + ws[k] for k, L in enumerate(LAYERS)] + [ws[len(LAYERS)]]
    pts, mats, tints = [], [], []

    def add(u, z, mat=1, tint=1.0):
        pts.append((u, z)); mats.append(mat); tints.append(tint)

    add(bs[0] + 0.02, 0.0, 1, LAYER_TINT[0])
    for k, (z0, z1, _) in enumerate(LAYERS):
        b, t = bs[k], LAYER_TINT[k]
        if k > 0:
            add(b + 0.012, z0 + 0.018, 1, t)
        add(b, z0 + 0.045, 1, t)
        add(b, z1 - 0.045, 1, t)
        add(b + 0.012, z1 - 0.018, 1, t)
        add(max(b, bs[k + 1]) + GROOVE, z1, 1, LAYER_TINT[k + 1] if k + 1 < len(LAYERS) else 1.0)
    c = bs[-1]
    add(c + 0.012, CAP_Z + 0.018)
    add(c, CAP_Z + 0.05)
    add(c, 1.0 - TOP_ROUND)
    for deg in (30, 60, 90):
        a = math.radians(deg)
        add(c + TOP_ROUND * (1 - math.cos(a)), 1.0 - TOP_ROUND + TOP_ROUND * math.sin(a))
    # band j sits between point j and j+1; last lip segment takes the surface color
    mats[-2] = 0
    return pts, mats[:-1], tints[:-1]


# ---------------------------------------------------------------- paths (solid is on the LEFT)
def line(a, b):
    return ("line", Vector(a), Vector(b))

def arc(c, r, a0, a1):
    return ("arc", Vector(c), r, a0, a1)

def seg_len(s):
    return (s[2] - s[1]).length if s[0] == "line" else abs(s[4] - s[3]) * s[2]

def seg_eval(s, t):
    if s[0] == "line":
        d = (s[2] - s[1]).normalized()
        return s[1].lerp(s[2], t), d
    _, c, r, a0, a1 = s
    a = a0 + (a1 - a0) * t
    sign = 1 if a1 > a0 else -1
    return c + Vector((math.cos(a), math.sin(a))) * r, Vector((-math.sin(a), math.cos(a))) * sign

def sample(segs):
    lens = [seg_len(s) for s in segs]
    total = sum(lens)
    n = max(KEY_STEP, round(total * SAMPLES_PER_UNIT / KEY_STEP) * KEY_STEP)
    out = []
    for i in range(n + 1):
        d = total * i / n
        for s, L in zip(segs, lens):
            if d <= L + 1e-9 or s is segs[-1]:
                out.append(seg_eval(s, min(1.0, d / L)))
                break
            d -= L
    return out

R = CORNER_RADIUS
PIECES = {
    "Tile_Edge": dict(
        segs=[line((-0.5, -0.5), (0.5, -0.5))],
        corners=[(0.5, 0.5), (-0.5, 0.5)], seed=11),
    "Tile_CornerEdge": dict(
        segs=[line((-0.5, 0.5), (-0.5, -0.5 + R)),
              arc((-0.5 + R, -0.5 + R), R, math.pi, 1.5 * math.pi),
              line((-0.5 + R, -0.5), (0.5, -0.5))],
        corners=[(0.5, 0.5)], seed=23),
    "Tile_InnerEdge": dict(
        segs=[line((-1, 0), (-R, 0)),
              arc((-R, -R), R, 0.5 * math.pi, 0.0),
              line((0, -R), (0, -1))],
        corners=[(1, -1), (1, 1), (-1, 1)], seed=37),
}


# ---------------------------------------------------------------- materials
def make_mat(name, rgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = 0.85
    m.diffuse_color = (*rgb, 1)
    return m

MATS = [make_mat("TileSurface", SURFACE_COLOR), make_mat("TileWall", WALL_COLOR)]
MAT_RGB = [SURFACE_COLOR, WALL_COLOR]


def finish(bm, name, face_tint):
    col = bm.loops.layers.color.new("Color")
    for f in bm.faces:
        base, t = MAT_RGB[f.material_index], face_tint.get(f, 1.0)
        for l in f.loops:
            l[col] = (base[0] * t, base[1] * t, base[2] * t, 1.0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="BEAUTY")
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for m in MATS:
        me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = False
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def wobble_track(rng, n, amp):
    keys = [0.0] + [rng.uniform(0, amp) for _ in range(n // KEY_STEP - 1)] + [0.0]
    out = []
    for i in range(n + 1):
        k, f = divmod(i, KEY_STEP)
        if k >= len(keys) - 1:
            out.append(keys[-1]); continue
        f = f / KEY_STEP
        f = f * f * (3 - 2 * f)
        out.append(keys[k] + (keys[k + 1] - keys[k]) * f)
    return out


def build_swept(name, segs, corners, seed):
    rng = random.Random(seed)
    samples = sample(segs)
    n = len(samples) - 1
    tracks = [wobble_track(rng, n, WOBBLE) for _ in LAYERS] + [wobble_track(rng, n, CAP_WOBBLE)]

    bm = bmesh.new()
    grid, band_mats, band_tints = None, None, None
    for i, (p, t) in enumerate(samples):
        nrm = Vector((-t.y, t.x))
        pts, band_mats, band_tints = profile([tr[i] for tr in tracks])
        if grid is None:
            grid = [[None] * (n + 1) for _ in pts]
        for k, (u, z) in enumerate(pts):
            grid[k][i] = bm.verts.new((p.x + nrm.x * u, p.y + nrm.y * u, z))
    K = len(grid) - 1
    top_c = [bm.verts.new((c[0], c[1], 1.0)) for c in corners]
    bot_c = [bm.verts.new((c[0], c[1], 0.0)) for c in corners]

    tint = {}
    def face(vs, mat, t=1.0):
        f = bm.faces.new(vs); f.material_index = mat; tint[f] = t
        return f

    for k in range(K):
        for i in range(n):
            face([grid[k][i], grid[k][i + 1], grid[k + 1][i + 1], grid[k + 1][i]], band_mats[k], band_tints[k])
    face([grid[K][i] for i in range(n + 1)] + top_c, 0)
    face([grid[0][i] for i in range(n + 1)] + bot_c, 1, 0.75)
    face([grid[k][n] for k in range(K + 1)] + [top_c[0], bot_c[0]], 1, 0.9)
    for j in range(len(corners) - 1):
        face([bot_c[j], top_c[j], top_c[j + 1], bot_c[j + 1]], 1, 0.9)
    face([bot_c[-1]] + [grid[k][0] for k in range(K + 1)] + [top_c[-1]], 1, 0.9)
    return finish(bm, name, tint)


def build_center():
    bm = bmesh.new()
    v = [bm.verts.new((x, y, z)) for z in (0, 1) for y in (-0.5, 0.5) for x in (-0.5, 0.5)]
    tint = {}
    for idx, mat in (((4, 5, 7, 6), 0), ((0, 2, 3, 1), 1), ((0, 1, 5, 4), 1),
                     ((1, 3, 7, 5), 1), ((3, 2, 6, 7), 1), ((2, 0, 4, 6), 1)):
        f = bm.faces.new([v[i] for i in idx]); f.material_index = mat; tint[f] = 1.0 if mat == 0 else 0.9
    return finish(bm, "Tile_Center", tint)


# ---------------------------------------------------------------- build
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

pieces = {"Tile_Center": build_center()}
for name, d in PIECES.items():
    pieces[name] = build_swept(name, d["segs"], d["corners"], d["seed"])

for name, o in pieces.items():
    bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
    lo = [min(p[a] for p in bb) for a in range(3)]
    hi = [max(p[a] for p in bb) for a in range(3)]
    print(f"[tilekit] {name}: tris={sum(len(p.vertices) - 2 for p in o.data.polygons)} "
          f"bounds x[{lo[0]:.3f},{hi[0]:.3f}] y[{lo[1]:.3f},{hi[1]:.3f}] z[{lo[2]:.3f},{hi[2]:.3f}]")

# ---------------------------------------------------------------- export
os.makedirs(FBX_DIR, exist_ok=True)
for name, o in pieces.items():
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(FBX_DIR, name + ".fbx"), use_selection=True,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", use_mesh_modifiers=True,
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
        src = pieces[name]
        o = bpy.data.objects.new(name + "_inst", src.data)
        o.location = (x, y, z); o.rotation_euler = (0, 0, math.radians(rot))
        col.objects.link(o)

    # L-shaped island with a concave notch + a small terrace on top
    place("Tile_InnerEdge", 1.5, 1.5, 0)
    place("Tile_CornerEdge", 2, 0, 0); place("Tile_Edge", 3, 0, 0); place("Tile_Edge", 4, 0, 0)
    place("Tile_CornerEdge", 5, 0, 90); place("Tile_Edge", 5, 1, 90); place("Tile_Edge", 5, 2, 90)
    place("Tile_CornerEdge", 5, 3, 180)
    for x in (1, 2, 3, 4):
        place("Tile_Edge", x, 3, 180)
    place("Tile_CornerEdge", 0, 3, 270); place("Tile_CornerEdge", 0, 2, 0)
    for x, y in ((3, 1), (4, 1), (3, 2), (4, 2)):
        place("Tile_Center", x, y, 0)
    place("Tile_CornerEdge", 3, 1, 0, 1); place("Tile_CornerEdge", 4, 1, 90, 1)
    place("Tile_CornerEdge", 4, 2, 180, 1); place("Tile_CornerEdge", 3, 2, 270, 1)

    # showcase row
    for i, n in enumerate(["Tile_Center", "Tile_Edge", "Tile_CornerEdge"]):
        place(n, 0 + i * 1.8, -4.5, 0)
    place("Tile_InnerEdge", 6.4, -4.5, 0)

    water = bpy.data.meshes.new("Water")
    bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=30); bm.to_mesh(water); bm.free()
    wm = make_mat("PreviewWater", (0.22, 0.62, 0.66))
    water.materials.append(wm)
    wo = bpy.data.objects.new("Water", water); wo.location = (2.5, 0, 0.18); col.objects.link(wo)

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 2.6; sun.data.angle = math.radians(8)
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    col.objects.link(sun)
    world = bpy.data.worlds.new("W"); scn.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.28, 0.30, 0.48, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.55

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

    shoot("island.png", (0.5, -5.5, 7.5), (2.5, 0.8, 0.5))
    shoot("pieces.png", (3.0, -10.5, 5.0), (3.0, -4.6, 0.4))
    shoot("closeup.png", (-0.6, -2.4, 2.6), (1.4, 0.4, 0.8))
