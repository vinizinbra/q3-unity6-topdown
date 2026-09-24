"""
Ultra-low-poly post-apocalyptic sci-fi platform kit with variants (Blender 4.2+).

Run:
  Blender -b -P build_scifi_lowpoly_tilekit.py -- <fbx_out_dir> <blend_out_path> [preview_dir]

Layout contract (Blender Z-up, top surface at z=1, pivot bottom-center of the footprint):
  SciFiLP_Center          1x1x1  plain cube
  SciFiLP_Edge_A..D       1x1x1  wall on -Y       A plain / B vent+hazard / C pipes / D damaged
  SciFiLP_Corner_A..D     1x1x1  walls on -X,-Y   A chamfer / B beacon / C pillar / D broken round
  SciFiLP_InnerEdge       2x1x2  concave chamfer, void quadrant at -X/-Y, pivot = 2x2 center

All walls share one 7-row profile and every path end uses the plain profile, so any variant
connects to any other. Greebles never cross a tile border or leave the bounding box.
"""
import bpy, bmesh, math, random, os, sys
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
FBX_DIR = argv[0] if len(argv) > 0 else "//fbx"
BLEND_PATH = argv[1] if len(argv) > 1 else ""
PREVIEW_DIR = argv[2] if len(argv) > 2 else ""

# ---------------------------------------------------------------- look knobs
SURFACE, WALL, TRIM, GLOW, HAZARD = range(5)
COLORS = {
    SURFACE: (0.560, 0.520, 0.440),   # dusty concrete deck
    WALL: (0.165, 0.180, 0.205),      # gunmetal plating
    TRIM: (0.520, 0.245, 0.105),      # rusted steel
    GLOW: (0.150, 0.900, 1.000),      # cyan light
    HAZARD: (0.850, 0.620, 0.080),    # faded hazard yellow
}
GLOW_EMISSION = 6.0
WALL_U = 0.10                          # wall panel inset from the tile border


def profile(groove=False, notch=False):
    """Rows (u = inward distance from tile border, z), bottom -> top."""
    g = 0.025 if groove else 0.0
    if notch:  # bitten-off lip: the trim caves in and the deck edge is chewed back
        return [(0.06, 0.00), (0.06, 0.14), (0.10, 0.18), (0.10, 0.66),
                (0.15, 0.72), (0.17, 0.88), (0.25, 1.00)]
    return [(0.06, 0.00), (0.06, 0.14), (WALL_U + g, 0.18), (WALL_U + g, 0.80),
            (0.02, 0.86), (0.02, 0.95), (0.08, 1.00)]

# material + tint of band j (between row j and j+1)
BANDS = [(WALL, 0.55), (WALL, 0.75), (WALL, 1.0), (TRIM, 0.75), (TRIM, 1.0), (TRIM, 1.15)]


# ---------------------------------------------------------------- path sampling
def left_normal(d):
    return Vector((-d.y, d.x))


def build_samples(path, feats):
    """path: polyline, solid on the LEFT. feats: (t, kind, arg) with t = distance along path.
    Returns list of dicts {p, off, groove, notch, stripe} (stripe = band-0 override for the
    segment starting at that sample)."""
    pts = [Vector(p) for p in path]
    dirs = [(pts[i + 1] - pts[i]).normalized() for i in range(len(pts) - 1)]
    lens = [(pts[i + 1] - pts[i]).length for i in range(len(pts) - 1)]
    cum = [0.0]
    for L in lens:
        cum.append(cum[-1] + L)

    marks = {}  # t -> flags
    def mark(t, **kw):
        key = round(t, 6)
        marks.setdefault(key, {}).update(kw)

    for t in cum:
        mark(t)
    for t, kind, arg in feats:
        if kind == "groove":
            mark(t - 0.03); mark(t - 0.01, groove=True); mark(t + 0.01, groove=True); mark(t + 0.03)
        elif kind == "notch":
            mark(t - arg); mark(t, notch=True); mark(t + arg)
        elif kind == "stripes":
            t1, w = arg
            k, x = 0, t
            while x < t1 - 1e-6:
                mark(x, stripe=HAZARD if k % 2 == 0 else WALL); mark(min(x + w, t1))
                x += w; k += 1

    out = []
    for t in sorted(marks):
        flags = marks[t]
        si = max(0, min(len(lens) - 1, next((i for i in range(len(lens)) if t <= cum[i + 1] + 1e-6), len(lens) - 1)))
        p = pts[si] + dirs[si] * (t - cum[si])
        n = left_normal(dirs[si])
        # exactly on an interior vertex -> miter so both walls stay planar
        for vi in range(1, len(pts) - 1):
            if abs(t - cum[vi]) < 1e-6:
                p = pts[vi]
                a, b = left_normal(dirs[vi - 1]), left_normal(dirs[vi])
                m = (a + b).normalized()
                n = m / m.dot(b)
        out.append(dict(p=p, off=n, groove=flags.get("groove", False),
                        notch=flags.get("notch", False), stripe=flags.get("stripe")))
    return out, cum[-1]


# ---------------------------------------------------------------- mesh helpers
class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.tint = {}

    def face(self, vs, mat, t=1.0):
        f = self.bm.faces.new(vs)
        f.material_index = mat
        self.tint[f] = t
        return f

    def box(self, center, tangent, half, mat, tint=1.0, roll=0.0, yaw=0.0):
        """Oriented box. tangent: 2D wall direction; half = (along, outward, up)."""
        tx = Vector((tangent[0], tangent[1], 0)).normalized()
        nx = Vector((tx.y, -tx.x, 0))  # outward = right of the solid-on-left path
        up = Vector((0, 0, 1))
        rot = Matrix.Rotation(roll, 3, nx) @ Matrix.Rotation(yaw, 3, up)
        ax = [rot @ tx * half[0], rot @ nx * half[1], rot @ up * half[2]]
        c = Vector(center)
        v = [self.bm.verts.new(c + ax[0] * sx + ax[1] * sy + ax[2] * sz)
             for sz in (-1, 1) for sy in (-1, 1) for sx in (-1, 1)]
        for idx in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            self.face([v[i] for i in idx], mat, tint)

    def prism(self, p0, p1, r, mat, tint=1.0, sides=6):
        p0, p1 = Vector(p0), Vector(p1)
        ax = (p1 - p0).normalized()
        ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
        u = ax.cross(ref).normalized(); w = ax.cross(u)
        rings = []
        for p in (p0, p1):
            rings.append([self.bm.verts.new(p + (u * math.cos(a) + w * math.sin(a)) * r)
                          for a in (2 * math.pi * i / sides for i in range(sides))])
        for i in range(sides):
            j = (i + 1) % sides
            self.face([rings[0][i], rings[0][j], rings[1][j], rings[1][i]], mat, tint)
        self.face(rings[0], mat, tint * 0.8); self.face(rings[1][::-1], mat, tint * 0.8)

    def swept(self, path, corners, feats=()):
        samples, total = build_samples(path, feats)
        n = len(samples) - 1
        grid = None
        for i, s in enumerate(samples):
            prof = profile(s["groove"], s["notch"])
            if grid is None:
                grid = [[None] * (n + 1) for _ in prof]
            for k, (u, z) in enumerate(prof):
                grid[k][i] = self.bm.verts.new((s["p"].x + s["off"].x * u, s["p"].y + s["off"].y * u, z))
        K = len(grid) - 1
        top_c = [self.bm.verts.new((c[0], c[1], 1.0)) for c in corners]
        bot_c = [self.bm.verts.new((c[0], c[1], 0.0)) for c in corners]
        for k in range(K):
            for i in range(n):
                mat, t = BANDS[k]
                if k == 0 and samples[i]["stripe"] is not None:
                    mat, t = samples[i]["stripe"], (0.9 if samples[i]["stripe"] == HAZARD else 0.45)
                self.face([grid[k][i], grid[k][i + 1], grid[k + 1][i + 1], grid[k + 1][i]], mat, t)
        self.face([grid[K][i] for i in range(n + 1)] + top_c, SURFACE)
        self.face([grid[0][i] for i in range(n + 1)] + bot_c, WALL, 0.4)
        self.face([grid[k][n] for k in range(K + 1)] + [top_c[0], bot_c[0]], WALL, 0.6)
        for j in range(len(corners) - 1):
            self.face([bot_c[j], top_c[j], top_c[j + 1], bot_c[j + 1]], WALL, 0.6)
        self.face([bot_c[-1]] + [grid[k][0] for k in range(K + 1)] + [top_c[-1]], WALL, 0.6)
        return total

    def finish(self, name):
        bm = self.bm
        col = bm.loops.layers.color.new("Color")
        for f in bm.faces:
            base, t = COLORS[f.material_index], self.tint.get(f, 1.0)
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


def make_mat(name, rgb, emission=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = 0.8
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*rgb, 1)
        bsdf.inputs["Emission Strength"].default_value = emission
    m.diffuse_color = (*rgb, 1)
    return m

MATS = [make_mat("SciFiLPSurface", COLORS[SURFACE]), make_mat("SciFiLPWall", COLORS[WALL]),
        make_mat("SciFiLPTrim", COLORS[TRIM]), make_mat("SciFiLPGlow", COLORS[GLOW], GLOW_EMISSION),
        make_mat("SciFiLPHazard", COLORS[HAZARD])]


# ---------------------------------------------------------------- pieces
EDGE_PATH = [(-0.5, -0.5), (0.5, -0.5)]
EDGE_CORNERS = [(0.5, 0.5), (-0.5, 0.5)]
WALL_Y = -0.5 + WALL_U   # edge wall panel plane


def corner_path(kind, size):
    if kind == "square":
        mid = [(-0.5, -0.5)]
    elif kind == "chamfer":
        mid = [(-0.5, -0.5 + size), (-0.5 + size, -0.5)]
    else:  # low-poly round, 3 facets
        c = Vector((-0.5 + size, -0.5 + size))
        mid = [tuple(c + Vector((math.cos(a), math.sin(a))) * size)
               for a in (math.pi + i * math.pi / 6 for i in range(4))]
    return [(-0.5, 0.5)] + mid + [(0.5, -0.5)]

CORNER_CORNERS = [(0.5, 0.5)]
DIAG_T = (0.7071, -0.7071)           # chamfer/diagonal wall direction (solid on the left)
INWARD_DIAG = Vector((0.7071, 0.7071))


def edge_a():
    b = Builder(); b.swept(EDGE_PATH, EDGE_CORNERS, [(0.5, "groove", None)])
    return b.finish("SciFiLP_Edge_A")


def edge_b():
    b = Builder()
    b.swept(EDGE_PATH, EDGE_CORNERS, [(0.08, "stripes", (0.92, 0.12))])
    b.box((0, WALL_Y - 0.005, 0.49), (1, 0), (0.29, 0.03, 0.15), WALL, 0.55)        # vent frame
    for z in (0.41, 0.49, 0.57):
        b.box((0, WALL_Y - 0.04, z), (1, 0), (0.24, 0.006, 0.018), GLOW)           # lit slits
    return b.finish("SciFiLP_Edge_B")


def edge_c():
    b = Builder(); b.swept(EDGE_PATH, EDGE_CORNERS)
    y = WALL_Y - 0.045
    for z in (0.34, 0.54):
        b.prism((-0.42, y, z), (0.42, y, z), 0.035, TRIM, 0.9)
    for x in (-0.44, 0.44):
        b.box((x, WALL_Y - 0.03, 0.44), (1, 0), (0.02, 0.04, 0.16), WALL, 0.7)     # flanges
    for x in (-0.18, 0.2):
        b.box((x, WALL_Y - 0.035, 0.44), (1, 0), (0.025, 0.045, 0.13), HAZARD, 0.8)  # clamps
    return b.finish("SciFiLP_Edge_C")


def edge_d():
    b = Builder(); b.swept(EDGE_PATH, EDGE_CORNERS, [(0.62, "notch", 0.15)])
    for x, dx, dz in ((0.55, -0.04, 0.2), (0.62, 0.03, 0.24), (0.69, 0.07, 0.17)):
        b.prism((x - 0.5, -0.36, 0.74), (x - 0.5 + dx, -0.47, 0.74 + dz), 0.012, TRIM, 0.6, sides=4)
    b.box((-0.22, WALL_Y - 0.012, 0.43), (1, 0), (0.13, 0.012, 0.13), TRIM, 0.7, roll=math.radians(14))
    return b.finish("SciFiLP_Edge_D")


def corner_a():
    b = Builder(); b.swept(corner_path("chamfer", 0.3), CORNER_CORNERS)
    return b.finish("SciFiLP_Corner_A")


def corner_b():
    b = Builder()
    ch = 0.26
    total = b.swept(corner_path("chamfer", ch), CORNER_CORNERS,
                    # stripes stay >= 0.1 away from the miter vertices, or the inward offset folds over
                    [(0.1, "stripes", (0.1 + 0.12 * 5, 0.12)),
                     (1 - ch + ch * 1.4142 + 0.1, "stripes", (1 - ch + ch * 1.4142 + 0.1 + 0.12 * 5, 0.12))])
    mid = Vector((-0.5 + ch / 2, -0.5 + ch / 2))
    p = mid + INWARD_DIAG * (WALL_U - 0.03)
    b.box((p.x, p.y, 0.52), DIAG_T, (0.075, 0.05, 0.1), HAZARD, 0.85)            # beacon housing
    p = mid + INWARD_DIAG * (WALL_U - 0.075)
    b.box((p.x, p.y, 0.52), DIAG_T, (0.045, 0.012, 0.06), GLOW)                  # beacon lens
    return b.finish("SciFiLP_Corner_B")


def corner_c():
    b = Builder(); b.swept(corner_path("square", 0), CORNER_CORNERS)
    b.box((-0.41, -0.41, 0.41), (1, 0), (0.075, 0.075, 0.41), WALL, 0.5)          # buttress
    b.box((-0.41, -0.41, 0.22), (1, 0), (0.082, 0.082, 0.05), HAZARD, 0.9)        # hazard band
    b.box((-0.41, -0.41, 0.62), (1, 0), (0.082, 0.082, 0.02), GLOW)               # light ring
    return b.finish("SciFiLP_Corner_C")


def corner_d():
    b = Builder()
    r = 0.4
    path = corner_path("round", r)
    straight = 1 - r
    arc_len = sum((Vector(path[i + 1]) - Vector(path[i])).length for i in range(1, 4))
    b.swept(path, CORNER_CORNERS, [(straight + arc_len / 2, "notch", 0.16)])
    c = Vector((-0.5 + r, -0.5 + r)) + Vector((-0.7071, -0.7071)) * (r - 0.14)
    for ang, dz in ((-0.5, 0.2), (0.0, 0.25), (0.5, 0.17)):
        d = Vector((-0.7071, -0.7071)); d.rotate(Matrix.Rotation(ang, 2))
        b.prism((c.x, c.y, 0.7), (c.x + d.x * 0.1, c.y + d.y * 0.1, 0.7 + dz), 0.012, TRIM, 0.6, sides=4)
    return b.finish("SciFiLP_Corner_D")


def inner_edge():
    b = Builder()
    C = 0.4
    b.swept([(-1, 0), (-C, 0), (0, -C), (0, -1)], [(1, -1), (1, 1), (-1, 1)])
    return b.finish("SciFiLP_InnerEdge")


def center():
    b = Builder()
    v = [b.bm.verts.new((x, y, z)) for z in (0, 1) for y in (-0.5, 0.5) for x in (-0.5, 0.5)]
    for idx, mat in (((4, 5, 7, 6), SURFACE), ((0, 2, 3, 1), WALL), ((0, 1, 5, 4), WALL),
                     ((1, 3, 7, 5), WALL), ((3, 2, 6, 7), WALL), ((2, 0, 4, 6), WALL)):
        b.face([v[i] for i in idx], mat, 1.0 if mat == SURFACE else 0.6)
    return b.finish("SciFiLP_Center")


# ---------------------------------------------------------------- build + export
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

pieces = {}
for fn in (center, edge_a, edge_b, edge_c, edge_d, corner_a, corner_b, corner_c, corner_d, inner_edge):
    o = fn(); pieces[o.name] = o

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
        o = bpy.data.objects.new(name + "_inst", pieces["SciFiLP_" + name].data)
        o.location = (x, y, z); o.rotation_euler = (0, 0, math.radians(rot))
        col.objects.link(o)

    place("InnerEdge", 1.5, 1.5, 0)
    place("Corner_B", 2, 0, 0); place("Edge_B", 3, 0, 0); place("Edge_D", 4, 0, 0)
    place("Corner_C", 5, 0, 90); place("Edge_C", 5, 1, 90); place("Edge_A", 5, 2, 90)
    place("Corner_D", 5, 3, 180)
    for x, v in zip((1, 2, 3, 4), "ACBD"):
        place("Edge_" + v, x, 3, 180)
    place("Corner_A", 0, 3, 270); place("Corner_D", 0, 2, 0)
    for x, y in ((3, 1), (4, 1), (3, 2), (4, 2)):
        place("Center", x, y, 0)
    place("Corner_A", 3, 1, 0, 1); place("Corner_B", 4, 1, 90, 1)
    place("Corner_C", 4, 2, 180, 1); place("Corner_D", 3, 2, 270, 1)
    for i, v in enumerate("ABCD"):
        place("Edge_" + v, i * 1.6, -4.0, 0)
        place("Corner_" + v, i * 1.6, -6.0, 0)
    place("Center", 6.6, -4.0, 0); place("InnerEdge", 7.2, -6.2, 0)

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

    def shoot(fname, loc, target, lens=40):
        cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); col.objects.link(cam)
        cam.location = loc; cam.data.lens = lens
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scn.camera = cam
        scn.render.filepath = os.path.join(PREVIEW_DIR, fname)
        bpy.ops.render.render(write_still=True)

    shoot("scifilp_island.png", (0.5, -5.0, 7.0), (2.5, 1.2, 0.5))
    shoot("scifilp_pieces.png", (3.6, -12.5, 6.0), (3.6, -5.2, 0.4), 32)
    shoot("scifilp_closeup.png", (-0.6, -2.4, 2.6), (1.4, 0.4, 0.8))
