"""
Low-poly natural-cliff biome tile kits: Grassland outpost, Desert, Snow (Blender 4.2+).

Run:
  Blender -b -P build_biome_tilekits.py -- <assets_env_dir> <blend_dir> [preview_dir]
  -> <assets_env_dir>/TileKit<Biome>/<Prefix>_*.fbx, <blend_dir>/TileKit<Biome>.blend

Per biome (Blender Z-up, top surface at z=1, pivot bottom-center of the footprint):
  <P>_Center             1x1x1  plain cube
  <P>_Edge_01..08        1x1x1  wall on -Y
      01 cliff / 02 drain pipe / 03 boulder / 04 landslide / 05 flora /
      06 outpost brace + lamp / 07 crack + crystals / 08 ladder
  <P>_Corner_01..04      1x1x1  walls on -X,-Y
      01 faceted round / 02 chamfer + boulder / 03 outpost lamp / 04 flora
  <P>_InnerEdge          2x1x2  concave corner, void quadrant at -X/-Y, pivot = 2x2 center

Every path end uses the zero-jitter base profile, so every variant of a biome connects to every
other. All geometry is clamped into the tile bounds.
"""
import bpy, bmesh, math, random, os, sys
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ENV_DIR = argv[0] if len(argv) > 0 else "//"
BLEND_DIR = argv[1] if len(argv) > 1 else ""
PREVIEW_DIR = argv[2] if len(argv) > 2 else ""

SURFACE, ROCK, ROCK_DARK, FOLIAGE, WOOD, METAL, GLOW = range(7)
SLOT_NAMES = ["Surface", "Rock", "RockDark", "Foliage", "Wood", "Metal", "Glow"]

BIOMES = {
    "Grassland": dict(
        prefix="Grass",
        colors={SURFACE: "6DBE45", ROCK: "9C7E62", ROCK_DARK: "6E5845", FOLIAGE: "3F8A2E",
                WOOD: "7A5230", METAL: "5A6068", GLOW: "FFC860"},
        # (top as fraction of the rock height, inset u); alternate=False -> all strata ROCK
        strata=[(0.35, 0.11), (0.70, 0.05), (1.00, 0.09)], alternate=False,
        cap_z=0.80, cap_drop=0.08, jitter=0.07, flora="vines", ladder_mat=WOOD,
        world="9CC8F0", ground="3E6B34"),
    "Desert": dict(
        prefix="Desert",
        colors={SURFACE: "EBC07A", ROCK: "CF7F45", ROCK_DARK: "A85E32", FOLIAGE: "5E8C45",
                WOOD: "A48462", METAL: "7A5038", GLOW: "3FE6FF"},
        strata=[(0.22, 0.08), (0.45, 0.05), (0.66, 0.09), (0.84, 0.04), (1.00, 0.07)], alternate=True,
        cap_z=0.88, cap_drop=0.03, jitter=0.06, flora="cactus", ladder_mat=WOOD,
        world="F0C890", ground="C79A62"),
    "Snow": dict(
        prefix="Snow",
        colors={SURFACE: "F2F6FF", ROCK: "6F7C8E", ROCK_DARK: "4C5668", FOLIAGE: "A8DDF5",
                WOOD: "5A4030", METAL: "4A5058", GLOW: "FF9A3C"},
        strata=[(0.50, 0.11), (1.00, 0.06)], alternate=False,
        cap_z=0.72, cap_drop=0.11, jitter=0.07, flora="icicles", ladder_mat=METAL,
        world="A8BEE0", ground="C8D4E4"),
}

def lin(hexstr):
    """sRGB hex -> linear RGB (Blender material/vertex color space)"""
    c = [int(hexstr[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


B = None           # active biome config
MATS = {}          # slot -> material for the active biome


# ---------------------------------------------------------------- profile
def strata_z():
    top = B["cap_z"] - B["cap_drop"] - 0.02
    return [(f * top, u) for f, u in B["strata"]]


def profile(j, notch=False, crack=False):
    """j = dict(rows=[per-stratum jitter], cap=cap jitter, drop=cap drip). Returns rows (u, z)
    and band (material, tint) list, bottom -> top."""
    st = strata_z()
    rows, bands = [], []
    rows.append((st[0][1] + 0.03 + j["rows"][0] * 0.5, 0.0))
    z0 = 0.0
    for k, (z1, u) in enumerate(st):
        jk = j["rows"][k] + (0.2 if crack else 0.0)
        rows.append((u + jk, z0 + 0.03))
        rows.append((u + jk * 0.6 + 0.035, z1))
        dark = B["alternate"] and k % 2 == 1
        bands.append((ROCK_DARK, 0.7))                                # tuck -> next bulge (ledge)
        bands.append((ROCK_DARK if dark else ROCK, 0.85 + 0.15 * k / max(1, len(st) - 1)))
        z0 = z1
    cz = B["cap_z"] - j["drop"]
    c = j["cap"]
    rows += [(0.035 + c, cz), (c, cz + 0.04), (c, 0.95), (0.06 + c, 1.0)]
    bands += [(ROCK_DARK, 0.55), (SURFACE, 0.72), (SURFACE, 0.9), (SURFACE, 1.0)]
    if notch:  # landslide: everything above mid-height caves in
        rows = [(u + (0.17 * min(1, (z - 0.4) / 0.4) if z > 0.4 else 0.0), z) for u, z in rows]
        rows[-1] = (max(rows[-1][0], 0.26), 1.0)
    return rows, bands


ZERO_J = None
def zero_jitter():
    return dict(rows=[0.0] * len(B["strata"]), cap=0.0, drop=0.0)


def rand_jitter(rng):
    return dict(rows=[rng.uniform(0, B["jitter"]) for _ in B["strata"]],
                cap=rng.uniform(0, 0.02), drop=rng.uniform(0, B["cap_drop"]))


# ---------------------------------------------------------------- path
def left_normal(d):
    return Vector((-d.y, d.x))


class Path:
    def __init__(self, pts):
        self.pts = [Vector(p) for p in pts]
        self.dirs = [(self.pts[i + 1] - self.pts[i]).normalized() for i in range(len(self.pts) - 1)]
        self.lens = [(self.pts[i + 1] - self.pts[i]).length for i in range(len(self.pts) - 1)]
        self.cum = [0.0]
        for L in self.lens:
            self.cum.append(self.cum[-1] + L)
        self.total = self.cum[-1]

    def seg(self, t):
        for i in range(len(self.lens)):
            if t <= self.cum[i + 1] + 1e-6:
                return i
        return len(self.lens) - 1

    def frame(self, t):
        """point, tangent, inward normal (offset vector, mitered at interior vertices)"""
        i = self.seg(t)
        p = self.pts[i] + self.dirs[i] * (t - self.cum[i])
        n = left_normal(self.dirs[i])
        for vi in range(1, len(self.pts) - 1):
            if abs(t - self.cum[vi]) < 1e-6:
                a, b = left_normal(self.dirs[vi - 1]), left_normal(self.dirs[vi])
                m = (a + b).normalized()
                return self.pts[vi], (self.dirs[vi - 1] + self.dirs[vi]).normalized(), m / m.dot(b)
        return p, self.dirs[i], n

    def world(self, t, u, z):
        p, _, n = self.frame(t)
        n = n.normalized()
        return Vector((p.x + n.x * u, p.y + n.y * u, z))


# ---------------------------------------------------------------- builder
class Builder:
    def __init__(self, seed):
        self.bm = bmesh.new()
        self.tint = {}
        self.rng = random.Random(seed)

    def face(self, vs, mat, t=1.0):
        f = self.bm.faces.new(vs)
        f.material_index = mat
        self.tint[f] = t
        return f

    # ---- primitives
    def box(self, center, tangent, half, mat, tint=1.0, roll=0.0):
        tx = Vector((tangent[0], tangent[1], 0)).normalized()
        nx = Vector((tx.y, -tx.x, 0))
        rot = Matrix.Rotation(roll, 3, nx)
        ax = [rot @ tx * half[0], rot @ nx * half[1], rot @ Vector((0, 0, 1)) * half[2]]
        c = Vector(center)
        v = [self.bm.verts.new(c + ax[0] * sx + ax[1] * sy + ax[2] * sz)
             for sz in (-1, 1) for sy in (-1, 1) for sx in (-1, 1)]
        for idx in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            self.face([v[i] for i in idx], mat, tint)

    def prism(self, p0, p1, r, mat, tint=1.0, sides=6, r1=None):
        p0, p1 = Vector(p0), Vector(p1)
        r1 = r if r1 is None else r1
        ax = (p1 - p0).normalized()
        ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
        u = ax.cross(ref).normalized(); w = ax.cross(u)
        ring = lambda p, rr: [self.bm.verts.new(p + (u * math.cos(a) + w * math.sin(a)) * rr)
                              for a in (2 * math.pi * i / sides for i in range(sides))]
        r0v = ring(p0, r)
        if r1 <= 0:  # cone
            tip = self.bm.verts.new(p1)
            for i in range(sides):
                self.face([r0v[i], r0v[(i + 1) % sides], tip], mat, tint)
        else:
            r1v = ring(p1, r1)
            for i in range(sides):
                j = (i + 1) % sides
                self.face([r0v[i], r0v[j], r1v[j], r1v[i]], mat, tint)
            self.face(r1v[::-1], mat, tint * 0.85)
        self.face(r0v, mat, tint * 0.85)

    def rock(self, center, size, mat, tint=1.0):
        res = bmesh.ops.create_icosphere(self.bm, subdivisions=1, radius=1.0)
        yaw = self.rng.uniform(0, math.pi)
        for v in res["verts"]:
            v.co *= self.rng.uniform(0.8, 1.15)
            v.co = Matrix.Rotation(yaw, 3, "Z") @ Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
            v.co += Vector(center)
        faces = {f for v in res["verts"] for f in v.link_faces}
        for f in faces:
            f.material_index = mat
            self.tint[f] = tint * self.rng.uniform(0.85, 1.05)

    # ---- the cliff
    def cliff(self, path, corners, feats=()):
        """feats: ('notch', t, halfwidth) | ('crack', t)"""
        rng = self.rng
        marks = {}
        def mark(t, **kw):
            marks.setdefault(round(t, 6), {}).update(kw)
        for t in path.cum:
            mark(t, vertex=True)
        busy = []
        for f in feats:
            if f[0] == "notch":
                mark(f[1] - f[2]); mark(f[1], notch=True); mark(f[1] + f[2])
                busy.append((f[1] - f[2], f[1] + f[2]))
            elif f[0] == "crack":
                mark(f[1] - 0.12); mark(f[1] - 0.03, crack=True); mark(f[1] + 0.03, crack=True); mark(f[1] + 0.12)
                busy.append((f[1] - 0.12, f[1] + 0.12))
        # a few random interior facets per straight run, clear of vertices (miter fold) and features
        for i, L in enumerate(path.lens):
            if L < 0.3:
                continue
            for _ in range(max(1, int(L * 3.2))):
                t = path.cum[i] + rng.uniform(0.12, L - 0.12)
                if all(abs(t - m) > 0.09 for m in marks) and all(not (a - 0.06 < t < b + 0.06) for a, b in busy):
                    mark(t)
        ts = sorted(marks)
        samples = []
        for idx, t in enumerate(ts):
            fl = marks[t]
            j = zero_jitter() if idx in (0, len(ts) - 1) else rand_jitter(rng)
            if fl.get("vertex") and idx not in (0, len(ts) - 1):
                j["rows"] = [x * 0.5 for x in j["rows"]]  # keep miter vertices tame
            p, _, off = path.frame(t)
            rows, bands = profile(j, fl.get("notch", False), fl.get("crack", False))
            samples.append((p, off, rows))
        n = len(samples) - 1
        K = len(samples[0][2]) - 1
        grid = [[None] * (n + 1) for _ in range(K + 1)]
        for i, (p, off, rows) in enumerate(samples):
            for k, (u, z) in enumerate(rows):
                grid[k][i] = self.bm.verts.new((p.x + off.x * u, p.y + off.y * u, z))
        for k in range(K):
            mat, t = bands[k]
            for i in range(n):
                self.face([grid[k][i], grid[k][i + 1], grid[k + 1][i + 1], grid[k + 1][i]], mat,
                          t * rng.uniform(0.92, 1.05) if mat != SURFACE else t)
        top_c = [self.bm.verts.new((c[0], c[1], 1.0)) for c in corners]
        bot_c = [self.bm.verts.new((c[0], c[1], 0.0)) for c in corners]
        self.face([grid[K][i] for i in range(n + 1)] + top_c, SURFACE)
        self.face([grid[0][i] for i in range(n + 1)] + bot_c, ROCK_DARK, 0.5)
        self.face([grid[k][n] for k in range(K + 1)] + [top_c[0], bot_c[0]], ROCK_DARK, 0.7)
        for j in range(len(corners) - 1):
            self.face([bot_c[j], top_c[j], top_c[j + 1], bot_c[j + 1]], ROCK_DARK, 0.7)
        self.face([bot_c[-1]] + [grid[k][0] for k in range(K + 1)] + [top_c[-1]], ROCK_DARK, 0.7)

    def finish(self, name, bounds):
        bm = self.bm
        (x0, x1), (y0, y1) = bounds
        for v in bm.verts:  # safety net: nothing leaves the tile's box
            v.co.x = min(max(v.co.x, x0), x1); v.co.y = min(max(v.co.y, y0), y1)
            v.co.z = min(max(v.co.z, 0.0), 1.0)
        used = sorted({f.material_index for f in bm.faces})
        remap = {m: i for i, m in enumerate(used)}
        col = bm.loops.layers.color.new("Color")
        for f in bm.faces:
            base, t = lin(B["colors"][f.material_index]), self.tint.get(f, 1.0)
            for l in f.loops:
                l[col] = (min(1, base[0] * t), min(1, base[1] * t), min(1, base[2] * t), 1.0)
            f.material_index = remap[f.material_index]
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4],
                              quad_method="BEAUTY", ngon_method="BEAUTY")
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me); bm.free()
        for m in used:
            me.materials.append(MATS[m])
        for p in me.polygons:
            p.use_smooth = False
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        return obj


# ---------------------------------------------------------------- props (local wall coords: t along path, u inward, z)
def flora(b, path, ts):
    kind = B["flora"]
    cz = B["cap_z"]
    for t in ts:
        if kind == "vines":
            for dt in (-0.13, -0.07, -0.01, 0.05, 0.11):
                L = b.rng.uniform(0.18, 0.5)
                p = path.world(t + dt, 0.015, cz - L / 2)
                _, tan, _ = path.frame(t + dt)
                b.box(p, tan, (0.026, 0.011, L / 2), FOLIAGE, b.rng.uniform(0.75, 1.05))
            b.rock(path.world(t + 0.03, 0.12, 0.36), (0.13, 0.1, 0.1), FOLIAGE, 0.95)
            b.rock(path.world(t - 0.1, 0.12, 0.33), (0.09, 0.08, 0.07), FOLIAGE, 0.8)
        elif kind == "cactus":
            base = path.world(t, 0.07, 0.0)
            h = b.rng.uniform(0.5, 0.62)
            b.prism(base, base + Vector((0, 0, h)), 0.055, FOLIAGE, 1.0, sides=6)
            _, tan, _ = path.frame(t)
            side = Vector((tan.x, tan.y, 0)) * (1 if b.rng.random() < 0.5 else -1)
            a0 = base + Vector((0, 0, h * 0.55))
            b.prism(a0, a0 + side * 0.12, 0.035, FOLIAGE, 0.9, sides=5)
            b.prism(a0 + side * 0.12, a0 + side * 0.12 + Vector((0, 0, 0.16)), 0.035, FOLIAGE, 0.9, sides=5)
            b.rock(path.world(t + 0.2, 0.1, 0.05), (0.09, 0.07, 0.06), WOOD, 0.8)
        else:  # icicles
            for dt in (-0.2, -0.13, -0.06, 0.01, 0.08, 0.15, 0.21):
                L = b.rng.uniform(0.12, 0.36)
                top = path.world(t + dt, 0.025, cz - 0.04)
                b.prism(top, top - Vector((0, 0, L)), b.rng.uniform(0.025, 0.042), FOLIAGE, 1.0, sides=4, r1=0)


def lamp(b, path, t, z):
    _, tan, _ = path.frame(t)
    b.box(path.world(t, 0.06, z), tan, (0.07, 0.05, 0.09), METAL, 0.8)
    b.box(path.world(t, 0.005, z), tan, (0.045, 0.012, 0.055), GLOW)


def boulder(b, path, t, big=True):
    s = (0.28, 0.19, 0.27) if big else (0.12, 0.1, 0.1)
    b.rock(path.world(t, 0.12, s[2] * 0.75), s, ROCK, 0.95)
    b.rock(path.world(t + 0.27, 0.12, 0.07), (0.1, 0.08, 0.07), ROCK_DARK, 1.0)


# ---------------------------------------------------------------- pieces
EDGE = ([(-0.5, -0.5), (0.5, -0.5)], [(0.5, 0.5), (-0.5, 0.5)])
BOUNDS1 = ((-0.5, 0.5), (-0.5, 0.5))


def round_corner(r=0.4, facets=3):
    c = Vector((-0.5 + r, -0.5 + r))
    mid = [tuple(c + Vector((math.cos(a), math.sin(a))) * r)
           for a in (math.pi + i * (math.pi / 2) / facets for i in range(facets + 1))]
    return [(-0.5, 0.5)] + mid + [(0.5, -0.5)]


def edge(idx, seed):
    b = Builder(seed)
    path = Path(EDGE[0])
    feats = []
    if idx == 4:
        feats = [("notch", 0.55, 0.17)]
    elif idx == 7:
        feats = [("crack", 0.45)]
    b.cliff(path, EDGE[1], feats)
    cz = B["cap_z"]
    if idx == 2:     # drain pipe
        p0, p1 = path.world(0.4, 0.25, 0.4), path.world(0.4, -0.02, 0.4)
        b.prism(p0, p1, 0.07, METAL, 0.9, sides=6)
        b.prism(path.world(0.4, 0.04, 0.4), path.world(0.4, -0.035, 0.4), 0.095, METAL, 0.7, sides=6)
        b.prism(path.world(0.4, 0.2, 0.4), path.world(0.4, -0.04, 0.4), 0.045, ROCK_DARK, 0.25, sides=6)
        _, tan, _ = path.frame(0.4)
        b.box(path.world(0.4, 0.08, 0.4 - 0.13), tan, (0.03, 0.03, 0.03), METAL, 0.6)
    elif idx == 3:
        boulder(b, path, 0.4)
    elif idx == 4:   # landslide rubble
        for dt, s in ((0.42, 0.13), (0.58, 0.1), (0.7, 0.08), (0.52, 0.06)):
            b.rock(path.world(dt, 0.14, s * 0.7), (s, s * 0.8, s * 0.8), ROCK_DARK, 1.0)
    elif idx == 5:
        flora(b, path, [0.3, 0.7] if B["flora"] != "cactus" else [0.3])
    elif idx == 6:   # outpost brace
        _, tan, _ = path.frame(0.5)
        b.box(path.world(0.5, 0.07, 0.45), tan, (0.2, 0.04, 0.17), METAL, 0.75)
        for x, roll in ((0.3, 0.45), (0.7, -0.45)):
            b.box(path.world(x, 0.05, 0.4), tan, (0.03, 0.035, 0.3), WOOD, 0.95, roll=roll)
        for x in (0.36, 0.64):
            for z in (0.33, 0.57):
                b.box(path.world(x, 0.025, z), tan, (0.015, 0.015, 0.015), METAL, 1.3)
        lamp(b, path, 0.5, cz - 0.12)
    elif idx == 7:   # crystals in the crack
        base = path.world(0.45, 0.17, 0.2)
        for dx, dy, h in ((0.0, 0.0, 0.32), (0.05, -0.02, 0.2), (-0.045, -0.015, 0.17), (0.02, -0.05, 0.12)):
            p = base + Vector((dx, dy, 0))
            b.prism(p, p + Vector((dx * 1.2, -0.06, h)), 0.035, GLOW, 1.0, sides=4, r1=0)
    elif idx == 8:   # ladder
        _, tan, _ = path.frame(0.5)
        m = B["ladder_mat"]
        for x in (0.4, 0.62):
            b.box(path.world(x, 0.04, cz / 2), tan, (0.016, 0.03, cz / 2), m, 0.9)
        z = 0.1
        while z < cz - 0.04:
            b.box(path.world(0.51, 0.035, z), tan, (0.11, 0.012, 0.012), m, 1.1)
            z += 0.13
    return b.finish(f"{B['prefix']}_Edge_{idx:02d}", BOUNDS1)


def corner(idx, seed):
    b = Builder(seed)
    pts = round_corner(0.3, 1) if idx == 2 else round_corner(0.4, 3 if idx != 3 else 2)
    path = Path(pts)
    b.cliff(path, [(0.5, 0.5)])
    mid = path.total / 2
    if idx == 2:
        b.rock(path.world(mid, 0.18, 0.15), (0.17, 0.17, 0.15), ROCK, 0.95)
    elif idx == 3:
        lamp(b, path, mid, B["cap_z"] - 0.14)
    elif idx == 4:
        flora(b, path, [mid])
    return b.finish(f"{B['prefix']}_Corner_{idx:02d}", BOUNDS1)


def inner_edge(seed):
    b = Builder(seed)
    C = 0.4
    c = Vector((-C, -C))
    arc = [tuple(c + Vector((math.cos(a), math.sin(a))) * C) for a in (math.pi / 2, math.pi / 4, 0)]
    path = Path([(-1, 0)] + arc + [(0, -1)])
    b.cliff(path, [(1, -1), (1, 1), (-1, 1)])
    return b.finish(f"{B['prefix']}_InnerEdge", ((-1, 1), (-1, 1)))


def center():
    b = Builder(0)
    v = [b.bm.verts.new((x, y, z)) for z in (0, 1) for y in (-0.5, 0.5) for x in (-0.5, 0.5)]
    for idx, mat in (((4, 5, 7, 6), SURFACE), ((0, 2, 3, 1), ROCK_DARK), ((0, 1, 5, 4), ROCK_DARK),
                     ((1, 3, 7, 5), ROCK_DARK), ((3, 2, 6, 7), ROCK_DARK), ((2, 0, 4, 6), ROCK_DARK)):
        b.face([v[i] for i in idx], mat, 1.0 if mat == SURFACE else 0.7)
    return b.finish(f"{B['prefix']}_Center", BOUNDS1)


# ---------------------------------------------------------------- materials
def make_mat(name, rgb, emission=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = 0.85
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*rgb, 1)
        bsdf.inputs["Emission Strength"].default_value = emission
    m.diffuse_color = (*rgb, 1)
    return m


# ---------------------------------------------------------------- run
def reset_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in list(bpy.data.collections):
        bpy.data.collections.remove(coll)
    for me in list(bpy.data.meshes):
        bpy.data.meshes.remove(me)


def render_previews(biome, pieces):
    scn = bpy.context.scene
    col = bpy.data.collections.new("Preview"); scn.collection.children.link(col)
    for o in pieces.values():
        o.hide_render = True
    P = B["prefix"]

    def place(name, x, y, rot, z=0.0):
        o = bpy.data.objects.new(name + "_inst", pieces[f"{P}_{name}"].data)
        o.location = (x, y, z); o.rotation_euler = (0, 0, math.radians(rot))
        col.objects.link(o)

    W, H = 8, 5
    k = 0
    def next_edge():
        nonlocal k
        k += 1
        return f"Edge_{(k - 1) % 8 + 1:02d}"
    place("Corner_01", 0, 0, 0); place("Corner_02", W - 1, 0, 90)
    place("Corner_03", W - 1, H - 1, 180); place("Corner_04", 0, H - 1, 270)
    for x in range(1, W - 1):
        place(next_edge(), x, 0, 0)
    for y in range(1, H - 1):
        place(next_edge(), W - 1, y, 90)
    for x in range(W - 2, 0, -1):
        place(next_edge(), x, H - 1, 180)
    for y in range(H - 2, 0, -1):
        place(next_edge(), 0, y, 270)
    for x in range(1, W - 1):
        for y in range(1, H - 1):
            place("Center", x, y, 0)
    # upper tier
    place("Corner_02", 3, 2, 0, 1); place("Corner_03", 5, 2, 90, 1)
    place("Corner_01", 5, 3, 180, 1); place("Corner_04", 3, 3, 270, 1)
    place("Edge_06", 4, 2, 0, 1); place("Edge_05", 4, 3, 180, 1)
    # lineup
    for i in range(8):
        place(f"Edge_{i + 1:02d}", i * 1.5, -3.5, 0)
    for i in range(4):
        place(f"Corner_{i + 1:02d}", i * 1.5, -5.5, 0)
    place("Center", 6.0, -5.5, 0); place("InnerEdge", 8.2, -5.8, 0)

    ground = bpy.data.meshes.new("Ground")
    bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=40); bm.to_mesh(ground); bm.free()
    ground.materials.append(make_mat(f"Preview{biome}Ground", lin(B["ground"])))
    go = bpy.data.objects.new("Ground", ground); go.location = (4, 0, 0.0); col.objects.link(go)

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.2; sun.data.angle = math.radians(6)
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    col.objects.link(sun)
    world = bpy.data.worlds.new("W"); scn.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (*lin(B["world"]), 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.5

    scn.render.engine = "CYCLES"
    scn.cycles.samples = 40; scn.cycles.use_denoising = True; scn.cycles.device = "CPU"
    scn.view_settings.view_transform = "Standard"
    scn.render.resolution_x, scn.render.resolution_y = 1600, 1000

    def shoot(fname, loc, target, lens=40):
        cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); col.objects.link(cam)
        cam.location = loc; cam.data.lens = lens
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scn.camera = cam
        scn.render.filepath = os.path.join(PREVIEW_DIR, fname)
        bpy.ops.render.render(write_still=True)

    shoot(f"{biome.lower()}_island.png", (3.5, -5.5, 7.5), (3.5, 1.6, 0.5), 32)
    shoot(f"{biome.lower()}_pieces.png", (5.2, -14.5, 6.5), (5.2, -4.6, 0.4), 30)
    shoot(f"{biome.lower()}_closeup.png", (1.2, -3.2, 2.6), (3.0, 0.2, 0.6), 35)


for bi, (biome, cfg) in enumerate(BIOMES.items()):
    reset_scene()
    B = cfg
    MATS = {s: make_mat(f"{cfg['prefix']}{SLOT_NAMES[s]}", lin(cfg["colors"][s]), 5.0 if s == GLOW else 0.0)
            for s in range(7)}
    pieces = {}
    objs = [center()] + [edge(i, 100 * bi + i) for i in range(1, 9)] + \
           [corner(i, 100 * bi + 50 + i) for i in range(1, 5)] + [inner_edge(100 * bi + 99)]
    for o in objs:
        pieces[o.name] = o
        print(f"[tilekit] {o.name}: tris={sum(len(p.vertices) - 2 for p in o.data.polygons)} "
              f"mats={[m.name for m in o.data.materials]}")
    out = os.path.join(ENV_DIR, f"TileKit{biome}")
    os.makedirs(out, exist_ok=True)
    for name, o in pieces.items():
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(out, name + ".fbx"), use_selection=True,
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
            axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
            add_leaf_bones=False, bake_anim=False, colors_type="SRGB")
    if BLEND_DIR:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BLEND_DIR, f"TileKit{biome}.blend"))
    if PREVIEW_DIR:
        render_previews(biome, pieces)
