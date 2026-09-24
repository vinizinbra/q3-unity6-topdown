"""
Low-poly rigged "CapKid" hero (backwards cap, spiky orange hair, blue jacket, cargo pants).

Run:
  Blender -b -P build_capkid.py -- <out_dir> <blend_path> [preview_dir]
  -> <out_dir>/CapKid.fbx + CapKid_Palette.png

Built in T-pose, Blender Z-up facing -Y (character's left = +X), feet at z=0, ~1.65 tall.
One material driven by a 4x4-cell palette texture (UVs sit at cell centers) + matching vertex
colors. Rigid-ish skinning on a Unity-Humanoid-named skeleton (loft parts blend at joints).
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = argv[0] if len(argv) > 0 else "//"
BLEND_PATH = argv[1] if len(argv) > 1 else ""
PREVIEW_DIR = argv[2] if len(argv) > 2 else ""
NAME = "CapKid"

# ---------------------------------------------------------------- palette (sRGB)
PALETTE = ["F3B58E", "F2701E", "D24E10", "26399A",
           "2448B0", "1B2452", "F0F0F4", "F28A1E",
           "F7C51E", "15151C", "E0268F", "DCDCE4",
           "11152E", "1A2870", "D98D68", "1A2146"]
(SKIN, HAIR, HAIR_DARK, CAP, JACKET, NAVY, WHITE, ORANGE,
 YELLOW, BLACK, MAGENTA, SOLE, BELT, CAP_DARK, SKIN_DARK, SCARF) = range(16)
CELLS = 4
CELL_PX = 8


def lin(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


# ---------------------------------------------------------------- part builders
PARTS = []   # (object) each carries vertex groups + a face int attribute "pal"


def new_bm():
    bm = bmesh.new()
    bm.faces.layers.int.new("pal")
    return bm


def to_obj(bm, name, weights):
    """weights: callable(vertex_index, co) -> {bone: w} or dict for rigid parts"""
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for v in me.vertices:
        wd = weights if isinstance(weights, dict) else weights(v.index)
        for bone, w in wd.items():
            vg = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
            vg.add([v.index], w, "REPLACE")
    PARTS.append(obj)
    return obj


def cbox(center, size, chamfer, pal, bone, rot=(0, 0, 0)):
    """Chamfered box (the kit's main low-poly primitive)."""
    bm = new_bm()
    M = Matrix.Translation(center) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((*size, 1))
    res = bmesh.ops.create_cube(bm, size=1.0, matrix=M)
    if chamfer > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=chamfer, offset_type="OFFSET",
                        segments=1, profile=0.5, affect="EDGES")
    L = bm.faces.layers.int["pal"]
    for f in bm.faces:
        f[L] = pal
    return to_obj(bm, "box", {bone: 1.0})


def cone(base, tip, r, pal, bone, sides=4):
    bm = new_bm()
    base, tip = Vector(base), Vector(tip)
    ax = (tip - base).normalized()
    ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
    u = ax.cross(ref).normalized(); w = ax.cross(u)
    ring = [bm.verts.new(base + (u * math.cos(a) + w * math.sin(a)) * r)
            for a in (2 * math.pi * i / sides + math.pi / sides for i in range(sides))]
    t = bm.verts.new(tip)
    for i in range(sides):
        bm.faces.new([ring[i], ring[(i + 1) % sides], t])
    bm.faces.new(ring[::-1])
    L = bm.faces.layers.int["pal"]
    for f in bm.faces:
        f[L] = pal
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return to_obj(bm, "cone", {bone: 1.0})


def rod(p0, p1, r, pal, bone, sides=4):
    bm = new_bm()
    p0, p1 = Vector(p0), Vector(p1)
    ax = (p1 - p0).normalized()
    ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
    u = ax.cross(ref).normalized(); w = ax.cross(u)
    rings = [[bm.verts.new(p + (u * math.cos(a) + w * math.sin(a)) * r)
              for a in (2 * math.pi * i / sides + math.pi / sides for i in range(sides))] for p in (p0, p1)]
    for i in range(sides):
        j = (i + 1) % sides
        bm.faces.new([rings[0][i], rings[0][j], rings[1][j], rings[1][i]])
    bm.faces.new(rings[0][::-1]); bm.faces.new(rings[1])
    L = bm.faces.layers.int["pal"]
    for f in bm.faces:
        f[L] = pal
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return to_obj(bm, "rod", {bone: 1.0})


def loft(rings, pal, axis="Z", sides=8, cap_start=True, cap_end=True):
    """rings: [(center, ru, rv, {bone: w})]. axis Z: u=X v=Y ; axis X: u=Z v=Y.
    Vertex angles start at pi/sides so a flat face points at -Y (the character's front)."""
    bm = new_bm()
    U, V = (Vector((1, 0, 0)), Vector((0, 1, 0))) if axis == "Z" else (Vector((0, 0, 1)), Vector((0, 1, 0)))
    vr, weights = [], []
    for c, ru, rv, wd in rings:
        ring = []
        for k in range(sides):
            a = math.pi / sides + 2 * math.pi * k / sides
            ring.append(bm.verts.new(Vector(c) + U * ru * math.cos(a) + V * rv * math.sin(a)))
            weights.append(wd)
        vr.append(ring)
    for r0, r1 in zip(vr, vr[1:]):
        for k in range(sides):
            j = (k + 1) % sides
            bm.faces.new([r0[k], r0[j], r1[j], r1[k]])
    if cap_start:
        bm.faces.new(vr[0][::-1])
    if cap_end:
        bm.faces.new(vr[-1])
    L = bm.faces.layers.int["pal"]
    for f in bm.faces:
        f[L] = pal
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return to_obj(bm, "loft", lambda i: weights[i])


def plate(pts_xz, y_of_z, depth, pal, bone):
    """Flat polygon on the body's front (x, z) at y = y_of_z(z), extruded back by depth."""
    bm = new_bm()
    front = [bm.verts.new((x, y_of_z(z), z)) for x, z in pts_xz]
    back = [bm.verts.new((x, y_of_z(z) + depth, z)) for x, z in pts_xz]
    n = len(front)
    bm.faces.new(front)
    bm.faces.new(back[::-1])
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([front[j], front[i], back[i], back[j]])
    L = bm.faces.layers.int["pal"]
    for f in bm.faces:
        f[L] = pal
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return to_obj(bm, "plate", {bone: 1.0})


# ---------------------------------------------------------------- skeleton
def side_name(s, n):
    return ("Left" if s > 0 else "Right") + n

BONES = [("Hips", None, (0, 0, 0.80), (0, 0, 0.90)),
         ("Spine", "Hips", (0, 0, 0.90), (0, 0, 1.02)),
         ("Chest", "Spine", (0, 0, 1.02), (0, 0, 1.18)),
         ("Neck", "Chest", (0, 0, 1.18), (0, 0, 1.28)),
         ("Head", "Neck", (0, 0, 1.28), (0, 0, 1.62))]
for s in (1, -1):
    BONES += [(side_name(s, "Shoulder"), "Chest", (0.03 * s, 0, 1.15), (0.16 * s, 0, 1.17)),
              (side_name(s, "UpperArm"), side_name(s, "Shoulder"), (0.16 * s, 0, 1.17), (0.42 * s, 0, 1.17)),
              (side_name(s, "LowerArm"), side_name(s, "UpperArm"), (0.42 * s, 0, 1.17), (0.61 * s, 0, 1.17)),
              (side_name(s, "Hand"), side_name(s, "LowerArm"), (0.61 * s, 0, 1.17), (0.73 * s, 0, 1.17)),
              (side_name(s, "UpperLeg"), "Hips", (0.10 * s, 0, 0.80), (0.10 * s, 0, 0.46)),
              (side_name(s, "LowerLeg"), side_name(s, "UpperLeg"), (0.10 * s, 0, 0.46), (0.10 * s, 0, 0.11)),
              (side_name(s, "Foot"), side_name(s, "LowerLeg"), (0.10 * s, 0, 0.11), (0.10 * s, -0.12, 0.04)),
              (side_name(s, "Toes"), side_name(s, "Foot"), (0.10 * s, -0.12, 0.04), (0.10 * s, -0.21, 0.04))]


# ---------------------------------------------------------------- model
def front_y(ry):
    return -ry * math.cos(math.pi / 8)


def build_body():
    # --- torso: belt, pelvis, jacket
    loft([((0, 0, 0.74), 0.15, 0.11, {"Hips": 1}), ((0, 0, 0.80), 0.18, 0.12, {"Hips": 1}),
          ((0, 0, 0.88), 0.165, 0.115, {"Hips": 1})], NAVY)
    loft([((0, 0, 0.845), 0.176, 0.126, {"Hips": 1}), ((0, 0, 0.895), 0.176, 0.126, {"Hips": 1})], BELT)
    cbox((0, -0.125, 0.87), (0.07, 0.02, 0.045), 0.006, YELLOW, "Hips")
    jacket = [((0, 0, 0.87), 0.19, 0.135, {"Hips": 0.6, "Spine": 0.4}),
              ((0, 0, 0.96), 0.17, 0.12, {"Spine": 1}),
              ((0, 0, 1.07), 0.18, 0.12, {"Spine": 0.3, "Chest": 0.7}),
              ((0, 0, 1.17), 0.19, 0.115, {"Chest": 1}),
              ((0, 0, 1.21), 0.11, 0.085, {"Chest": 0.5, "Neck": 0.5})]
    loft(jacket, JACKET)
    # open jacket -> white shirt V + magenta lining edges
    zs = [r[0][2] for r in jacket[:4]]
    ys = [front_y(r[2]) for r in jacket[:4]]
    def jy(z):
        for i in range(len(zs) - 1):
            if zs[i] <= z <= zs[i + 1]:
                f = (z - zs[i]) / (zs[i + 1] - zs[i])
                return ys[i] + (ys[i + 1] - ys[i]) * f - 0.006
        return ys[-1] - 0.006
    shirt = [(-0.045, 1.15), (0.03, 1.15), (0.066, 0.885), (-0.056, 0.885)]
    plate(shirt, jy, 0.02, WHITE, "Chest")
    for (x0, z0), (x1, z1) in ((shirt[0], shirt[3]), (shirt[1], shirt[2])):
        rod((x0, jy(z0) - 0.004, z0), (x1, jy(z1) - 0.004, z1), 0.008, MAGENTA, "Spine")
    cbox((-0.125, -0.118, 0.95), (0.022, 0.012, 0.055), 0.004, ORANGE, "Spine")

    # --- neck, scarf, head
    loft([((0, 0.0, 1.17), 0.045, 0.045, {"Neck": 1}), ((0, 0.0, 1.30), 0.045, 0.045, {"Neck": 1})], SKIN, sides=6)
    loft([((0, 0.005, 1.14), 0.14, 0.125, {"Chest": 1}), ((0, 0.0, 1.21), 0.135, 0.118, {"Chest": 0.5, "Neck": 0.5}),
          ((0, 0.0, 1.27), 0.112, 0.102, {"Neck": 1}), ((0, 0.0, 1.31), 0.094, 0.09, {"Neck": 0.5, "Head": 0.5})], SCARF)
    cbox((0.06, -0.115, 1.13), (0.08, 0.035, 0.11), 0.012, SCARF, "Chest", rot=(0.2, 0, 0.25))
    loft([((0, -0.01, 1.27), 0.075, 0.07, {"Head": 1}), ((0, 0, 1.31), 0.115, 0.11, {"Head": 1}),
          ((0, 0, 1.40), 0.14, 0.13, {"Head": 1}), ((0, 0, 1.50), 0.14, 0.13, {"Head": 1}),
          ((0, 0, 1.57), 0.10, 0.10, {"Head": 1})], SKIN)
    fy = front_y(0.13)
    for s in (1, -1):
        cbox((0.045 * s, fy - 0.002, 1.405), (0.026, 0.008, 0.042), 0, BLACK, "Head")          # eyes
        cbox((0.052 * s, fy - 0.002, 1.448), (0.055, 0.008, 0.013), 0, BLACK, "Head", rot=(0, -0.38 * s, 0))
        cbox((0.145 * s, 0.0, 1.40), (0.026, 0.045, 0.062), 0.008, SKIN, "Head")               # ears
        cbox((0.152 * s, -0.004, 1.40), (0.008, 0.022, 0.03), 0, SKIN_DARK, "Head")
        cbox((0.133 * s, -0.035, 1.45), (0.022, 0.05, 0.08), 0.006, HAIR, "Head")              # sideburns
    cbox((0, fy + 0.004, 1.322), (0.036, 0.008, 0.006), 0, BLACK, "Head")                      # mouth
    cbox((0, 0.075, 1.43), (0.2, 0.08, 0.1), 0.02, HAIR, "Head")                              # nape hair
    # spiky fringe poking out of the snapback opening
    base = Vector((0.03, -0.10, 1.50))
    cbox(base, (0.13, 0.07, 0.08), 0.015, HAIR, "Head")
    for tip, r, pal in (((0.21, -0.21, 1.66), 0.05, HAIR), ((0.25, -0.12, 1.55), 0.045, HAIR_DARK),
                        ((0.10, -0.23, 1.63), 0.045, HAIR), ((0.17, -0.09, 1.71), 0.045, HAIR),
                        ((-0.03, -0.20, 1.57), 0.04, HAIR_DARK), ((0.26, -0.03, 1.63), 0.04, HAIR)):
        cone(base, tip, r, pal, "Head")

    # --- backwards cap
    loft([((0, 0.01, 1.465), 0.152, 0.147, {"Head": 1}), ((0, 0.01, 1.54), 0.15, 0.145, {"Head": 1}),
          ((0, 0.01, 1.60), 0.12, 0.115, {"Head": 1}), ((0, 0.01, 1.635), 0.06, 0.058, {"Head": 1})], CAP)
    cbox((0, 0.01, 1.642), (0.03, 0.03, 0.018), 0.005, CAP, "Head")
    cbox((0, 0.20, 1.478), (0.2, 0.17, 0.022), 0.01, CAP, "Head", rot=(0.15, 0, 0))            # bill (back)
    cbox((0, 0.20, 1.466), (0.19, 0.16, 0.004), 0, MAGENTA, "Head", rot=(0.15, 0, 0))          # bill underside
    cbox((0, -0.144, 1.488), (0.11, 0.014, 0.034), 0.004, CAP_DARK, "Head")                     # snap band
    for x in (-0.03, 0.0, 0.03):
        cbox((x, -0.152, 1.488), (0.012, 0.006, 0.012), 0, BLACK, "Head")

    for s in (1, -1):
        sn = lambda n: side_name(s, n)
        # --- arms (T-pose along X)
        loft([((0.15 * s, 0, 1.17), 0.077, 0.077, {"Chest": 0.4, sn("UpperArm"): 0.6}),
              ((0.21 * s, 0, 1.17), 0.08, 0.078, {sn("UpperArm"): 1}),
              ((0.40 * s, 0, 1.17), 0.066, 0.064, {sn("UpperArm"): 0.5, sn("LowerArm"): 0.5}),
              ((0.44 * s, 0, 1.17), 0.068, 0.066, {sn("LowerArm"): 0.8, sn("UpperArm"): 0.2})],
             JACKET, axis="X", sides=6)
        loft([((0.43 * s, 0, 1.17), 0.056, 0.055, {sn("LowerArm"): 1}),
              ((0.61 * s, 0, 1.17), 0.05, 0.05, {sn("LowerArm"): 0.6, sn("Hand"): 0.4})],
             WHITE, axis="X", sides=6)
        cbox((0.645 * s, 0, 1.17), (0.09, 0.095, 0.095), 0.016, NAVY, sn("Hand"))              # fingerless glove
        cbox((0.703 * s, -0.004, 1.163), (0.045, 0.085, 0.082), 0.013, SKIN, sn("Hand"))       # curled fingers
        cbox((0.662 * s, -0.052, 1.19), (0.055, 0.028, 0.032), 0.007, SKIN, sn("Hand"))        # thumb
        cbox((0.608 * s, 0, 1.17), (0.012, 0.1, 0.1), 0.004, MAGENTA, sn("Hand"))              # glove cuff

        # --- legs: baggy cargo pants
        x = 0.10 * s
        loft([((x, 0, 0.82), 0.11, 0.12, {"Hips": 0.5, sn("UpperLeg"): 0.5}),
              ((x, 0, 0.70), 0.118, 0.122, {sn("UpperLeg"): 1}),
              ((x, 0, 0.47), 0.095, 0.10, {sn("UpperLeg"): 0.5, sn("LowerLeg"): 0.5}),
              ((x, 0, 0.30), 0.1, 0.108, {sn("LowerLeg"): 1}),
              ((x, 0, 0.19), 0.1, 0.105, {sn("LowerLeg"): 1})], NAVY)
        cbox((0.212 * s, 0.0, 0.60), (0.03, 0.12, 0.13), 0.01, CAP_DARK, sn("UpperLeg"))       # cargo pocket
        cbox((0.229 * s, -0.012, 0.63), (0.01, 0.028, 0.055), 0.003, ORANGE, sn("UpperLeg"))

        # --- chunky sneakers
        f = sn("Foot")
        cbox((x, -0.05, 0.032), (0.165, 0.34, 0.064), 0.016, SOLE, f)
        cbox((x, 0.06, 0.036), (0.168, 0.09, 0.026), 0.004, ORANGE, f)
        cbox((x, -0.03, 0.12), (0.148, 0.28, 0.13), 0.024, NAVY, f)
        cbox((x, -0.16, 0.09), (0.152, 0.11, 0.08), 0.024, WHITE, sn("Toes"))
        cbox((x, 0.03, 0.20), (0.14, 0.14, 0.09), 0.024, WHITE, f)
        for yy in (-0.08, -0.02):
            cbox((x, yy, 0.155), (0.156, 0.038, 0.07), 0.008, ORANGE, f)
        cbox((x, 0.105, 0.19), (0.05, 0.022, 0.08), 0.006, ORANGE, f)


def build_armature():
    arm_data = bpy.data.armatures.new(NAME + "_Rig")
    arm = bpy.data.objects.new(NAME + "_Rig", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for name, parent, h, t in BONES:
        b = arm_data.edit_bones.new(name)
        b.head, b.tail = h, t
        if parent:
            b.parent = eb[parent]
            b.use_connect = (Vector(eb[parent].tail) - Vector(h)).length < 1e-4
        eb[name] = b
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def palette_image(path):
    size = CELLS * CELL_PX
    img = bpy.data.images.new(NAME + "_Palette", size, size, alpha=False)
    px = [0.0] * (size * size * 4)
    for i, h in enumerate(PALETTE):
        c = [int(h[k:k + 2], 16) / 255 for k in (0, 2, 4)]  # PNG stores sRGB
        cx, cy = i % CELLS, i // CELLS
        for y in range(cy * CELL_PX, (cy + 1) * CELL_PX):
            for x in range(cx * CELL_PX, (cx + 1) * CELL_PX):
                o = (y * size + x) * 4
                px[o:o + 4] = [c[0], c[1], c[2], 1.0]
    img.pixels = px
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def finalize(arm, img):
    bpy.ops.object.select_all(action="DESELECT")
    for o in PARTS:
        o.select_set(True)
    bpy.context.view_layer.objects.active = PARTS[0]
    bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = NAME
    me = body.data
    me.name = NAME
    pal = [d.value for d in me.attributes["pal"].data]
    # create every layer first: adding an attribute reallocates storage and orphans older refs
    me.color_attributes.new("Color", "FLOAT_COLOR", "CORNER")
    me.uv_layers.new(name="UVMap")
    uv, col = me.uv_layers["UVMap"], me.color_attributes["Color"]
    for p in me.polygons:
        i = pal[p.index]
        u, v = ((i % CELLS) + 0.5) / CELLS, ((i // CELLS) + 0.5) / CELLS
        c = lin(PALETTE[i])
        for li in p.loop_indices:
            uv.data[li].uv = (u, v)
            col.data[li].color = (*c, 1.0)
        p.material_index = 0
        p.use_smooth = False
    me.attributes.remove(me.attributes["pal"])

    mat = bpy.data.materials.new(NAME + "_Mat")
    mat.use_nodes = True
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.interpolation = "Closest"
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.8
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    me.materials.clear(); me.materials.append(mat)

    body.parent = arm
    mod = body.modifiers.new("Armature", "ARMATURE"); mod.object = arm
    return body


# ---------------------------------------------------------------- run
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

os.makedirs(OUT_DIR, exist_ok=True)
img = palette_image(os.path.join(OUT_DIR, NAME + "_Palette.png"))
build_body()
arm = build_armature()
body = finalize(arm, img)
print(f"[capkid] tris={sum(len(p.vertices) - 2 for p in body.data.polygons)} verts={len(body.data.vertices)} "
      f"groups={len(body.vertex_groups)} bones={len(arm.data.bones)}")
unweighted = [v.index for v in body.data.vertices if not v.groups]
print(f"[capkid] unweighted verts: {len(unweighted)}")

bpy.ops.object.select_all(action="DESELECT")
body.select_set(True); arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT_DIR, NAME + ".fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
    axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", add_leaf_bones=False,
    bake_anim=False, colors_type="SRGB", path_mode="STRIP", armature_nodetype="NULL")
if BLEND_PATH:
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

# ---------------------------------------------------------------- preview (poses the rig like the reference art)
if PREVIEW_DIR:
    scn = bpy.context.scene

    def rot(name, axis, deg):
        pb = arm.pose.bones[name]
        h = pb.head.copy()
        pb.matrix = Matrix.Translation(h) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-h) @ pb.matrix
        bpy.context.view_layer.update()

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0; sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-30))
    scn.collection.objects.link(sun)
    world = bpy.data.worlds.new("W"); scn.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (*lin("DDE3F0"), 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    scn.render.engine = "CYCLES"; scn.cycles.samples = 48; scn.cycles.use_denoising = True
    scn.cycles.device = "CPU"; scn.view_settings.view_transform = "Standard"
    scn.render.resolution_x, scn.render.resolution_y = 1000, 1200
    scn.render.film_transparent = False

    def shoot(fname, loc, target, lens=50):
        cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scn.collection.objects.link(cam)
        cam.location = loc; cam.data.lens = lens
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scn.camera = cam
        scn.render.filepath = os.path.join(PREVIEW_DIR, fname)
        bpy.ops.render.render(write_still=True)

    shoot("capkid_tpose.png", (1.2, -3.6, 1.4), (0, 0, 0.85), 45)

    # reference pose: right arm points at the viewer, left fist by the hip, wide stance
    rot("Chest", "Z", -12); rot("Head", "Z", 10); rot("Head", "X", -4)
    rot("RightUpperArm", "Z", 78); rot("RightUpperArm", "X", 12)
    rot("RightLowerArm", "Z", 8); rot("RightLowerArm", "X", 6)
    rot("LeftUpperArm", "Y", 72); rot("LeftUpperArm", "Z", 18)
    rot("LeftLowerArm", "Z", 95)
    rot("LeftUpperLeg", "Y", -9); rot("RightUpperLeg", "Y", 8); rot("RightUpperLeg", "X", -6)
    rot("LeftFoot", "Z", 12); rot("RightFoot", "Z", -12)
    shoot("capkid_pose.png", (-0.9, -3.3, 1.55), (0, 0, 0.88), 50)
    shoot("capkid_pose_back.png", (1.8, 2.6, 1.8), (0, 0, 0.88), 50)
