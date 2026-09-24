"""
Shared low-poly character kit for Blender 4.2+ build scripts (import from a script run with -P).

Conventions: Blender Z-up, character faces -Y (its left = +X), feet at z=0, built in T-pose.
Each primitive becomes its own object with rigid or per-ring bone weights plus a face int
attribute "pal" (palette index); `finish()` joins everything, writes palette-cell UVs + vertex
colors, one palette material, and skins it to an armature with Unity-Humanoid bone names.
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix, Euler


def lin(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def side_name(s, n):
    return ("Left" if s > 0 else "Right") + n


def front_y(ry, sides=8):
    """y of the flat front face of a loft ring with depth radius ry"""
    return -ry * math.cos(math.pi / sides)


class Kit:
    def __init__(self, name, palette, cells=4, cell_px=8):
        self.name, self.palette, self.cells, self.cell_px = name, palette, cells, cell_px
        self.parts = []

    # ------------------------------------------------------------ primitives
    def _bm(self):
        bm = bmesh.new()
        bm.faces.layers.int.new("pal")
        return bm

    def _obj(self, bm, pal, weights):
        L = bm.faces.layers.int["pal"]
        for f in bm.faces:
            f[L] = pal
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        me = bpy.data.meshes.new("part")
        bm.to_mesh(me); bm.free()
        obj = bpy.data.objects.new("part", me)
        bpy.context.scene.collection.objects.link(obj)
        for v in me.vertices:
            wd = weights if isinstance(weights, dict) else weights(v.index)
            for bone, w in wd.items():
                vg = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
                vg.add([v.index], w, "REPLACE")
        self.parts.append(obj)
        return obj

    def box(self, center, size, pal, bone, chamfer=0.0, rot=(0, 0, 0)):
        bm = self._bm()
        M = Matrix.Translation(center) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((*size, 1))
        bmesh.ops.create_cube(bm, size=1.0, matrix=M)
        if chamfer > 0:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=chamfer, offset_type="OFFSET",
                            segments=1, profile=0.5, affect="EDGES")
        return self._obj(bm, pal, {bone: 1.0})

    def wedge(self, center, size, pal, bone, taper=0.5, rot=(0, 0, 0)):
        """Box whose top face is scaled by `taper` (cheap rounded-ish shapes)."""
        bm = self._bm()
        M = Matrix.Translation(center) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((*size, 1))
        bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts:
            if v.co.z > 0:
                v.co.x *= taper; v.co.y *= taper
            v.co = M @ v.co
        return self._obj(bm, pal, {bone: 1.0})

    def cone(self, base, tip, r, pal, bone, sides=4):
        bm = self._bm()
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
        return self._obj(bm, pal, {bone: 1.0})

    def loft(self, rings, pal, axis="Z", sides=8, cap_start=True, cap_end=True, flat_front=False):
        """rings: [(center, ru, rv, {bone: w})]. axis Z: u=X v=Y ; axis X: u=Z v=Y.
        Vertex angles start at pi/sides (a flat face points at -Y for sides % 4 == 0);
        flat_front=True forces a flat -Y face for any side count."""
        bm = self._bm()
        U, V = (Vector((1, 0, 0)), Vector((0, 1, 0))) if axis == "Z" else (Vector((0, 0, 1)), Vector((0, 1, 0)))
        vr, weights = [], []
        for c, ru, rv, wd in rings:
            ring = []
            for k in range(sides):
                a = (-math.pi / 2 if flat_front else 0.0) + math.pi / sides + 2 * math.pi * k / sides
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
        return self._obj(bm, pal, lambda i: weights[i])

    def blade(self, base, tip, width, thick, pal, bone, bend=(0, 0, 0), up=(0, 0, 1)):
        """Flattened 4-sided spike (hair locks, fins): base quad -> mid quad (bent) -> tip."""
        bm = self._bm()
        base, tip = Vector(base), Vector(tip)
        ax = (tip - base).normalized()
        wdir = ax.cross(Vector(up))
        if wdir.length < 1e-4:
            wdir = ax.cross(Vector((1, 0, 0)))
        wdir.normalize()
        tdir = ax.cross(wdir).normalized()
        mid = base.lerp(tip, 0.45) + Vector(bend)
        def quad(c, w, t):
            return [bm.verts.new(c + wdir * w * sx + tdir * t * sy) for sx, sy in ((1, 1), (-1, 1), (-1, -1), (1, -1))]
        q0, q1 = quad(base, width, thick), quad(mid, width * 0.8, thick * 0.8)
        tv = bm.verts.new(tip)
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new([q0[i], q0[j], q1[j], q1[i]])
            bm.faces.new([q1[i], q1[j], tv])
        bm.faces.new(q0[::-1])
        return self._obj(bm, pal, {bone: 1.0})

    def rodline(self, pts, r, pal, bone, sides=4):
        """Chain of thin prisms through pts (trim piping, seams)."""
        for p0, p1 in zip(pts, pts[1:]):
            self.rod(p0, p1, r, pal, bone, sides)

    def rod(self, p0, p1, r, pal, bone, sides=4):
        bm = self._bm()
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
        return self._obj(bm, pal, {bone: 1.0})

    def plate(self, pts_xz, y_of_z, depth, pal, bone):
        """Flat polygon on the body's front (x, z) at y = y_of_z(z), extruded back by depth."""
        bm = self._bm()
        front = [bm.verts.new((x, y_of_z(z), z)) for x, z in pts_xz]
        back = [bm.verts.new((x, y_of_z(z) + depth, z)) for x, z in pts_xz]
        n = len(front)
        bm.faces.new(front); bm.faces.new(back[::-1])
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new([front[j], front[i], back[i], back[j]])
        return self._obj(bm, pal, {bone: 1.0})

    # ------------------------------------------------------------ rig + finish
    def armature(self, bones):
        """bones: [(name, parent, head, tail)]"""
        arm = bpy.data.objects.new(self.name + "_Rig", bpy.data.armatures.new(self.name + "_Rig"))
        bpy.context.scene.collection.objects.link(arm)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode="EDIT")
        eb = {}
        for name, parent, h, t in bones:
            b = arm.data.edit_bones.new(name)
            b.head, b.tail = h, t
            if parent:
                b.parent = eb[parent]
                b.use_connect = (Vector(eb[parent].tail) - Vector(h)).length < 1e-4
            eb[name] = b
        bpy.ops.object.mode_set(mode="OBJECT")
        return arm

    def palette_image(self, path):
        size = self.cells * self.cell_px
        img = bpy.data.images.new(self.name + "_Palette", size, size, alpha=False)
        px = [0.0] * (size * size * 4)
        for i, h in enumerate(self.palette):
            c = [int(h[k:k + 2], 16) / 255 for k in (0, 2, 4)]  # PNG stores sRGB
            cx, cy = i % self.cells, i // self.cells
            for y in range(cy * self.cell_px, (cy + 1) * self.cell_px):
                for x in range(cx * self.cell_px, (cx + 1) * self.cell_px):
                    o = (y * size + x) * 4
                    px[o:o + 4] = [c[0], c[1], c[2], 1.0]
        img.pixels = px
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
        return img

    def finish(self, arm, img):
        bpy.ops.object.select_all(action="DESELECT")
        for o in self.parts:
            o.select_set(True)
        bpy.context.view_layer.objects.active = self.parts[0]
        bpy.ops.object.join()
        body = bpy.context.view_layer.objects.active
        body.name = body.data.name = self.name
        me = body.data
        pal = [d.value for d in me.attributes["pal"].data]
        # create every layer first: adding an attribute reallocates storage and orphans older refs
        me.color_attributes.new("Color", "FLOAT_COLOR", "CORNER")
        me.uv_layers.new(name="UVMap")
        uv, col = me.uv_layers["UVMap"], me.color_attributes["Color"]
        n = self.cells
        for p in me.polygons:
            i = pal[p.index]
            u, v = ((i % n) + 0.5) / n, ((i // n) + 0.5) / n
            c = lin(self.palette[i])
            for li in p.loop_indices:
                uv.data[li].uv = (u, v)
                col.data[li].color = (*c, 1.0)
            p.material_index = 0
            p.use_smooth = False
        me.attributes.remove(me.attributes["pal"])

        mat = bpy.data.materials.new(self.name + "_Mat")
        mat.use_nodes = True
        nt = mat.node_tree
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.interpolation = "Closest"
        bsdf = nt.nodes["Principled BSDF"]
        bsdf.inputs["Roughness"].default_value = 0.8
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        me.materials.clear(); me.materials.append(mat)

        body.parent = arm
        body.modifiers.new("Armature", "ARMATURE").object = arm
        return body

    def export(self, body, arm, out_dir):
        bpy.ops.object.select_all(action="DESELECT")
        body.select_set(True); arm.select_set(True)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(out_dir, self.name + ".fbx"), use_selection=True,
            object_types={"ARMATURE", "MESH"}, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
            bake_space_transform=False, axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
            add_leaf_bones=False, bake_anim=False, colors_type="SRGB", path_mode="STRIP",
            armature_nodetype="NULL")


def reset_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)


def pose_rot(arm, name, axis, deg):
    """Rotate a pose bone about a world axis through its head (armature at identity)."""
    pb = arm.pose.bones[name]
    h = pb.head.copy()
    pb.matrix = Matrix.Translation(h) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()


def preview_setup(bg="DDE3F0", res=(1000, 1100)):
    scn = bpy.context.scene
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0; sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-30))
    scn.collection.objects.link(sun)
    world = bpy.data.worlds.new("W"); scn.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (*lin(bg), 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    scn.render.engine = "CYCLES"; scn.cycles.samples = 40; scn.cycles.use_denoising = True
    scn.cycles.device = "CPU"; scn.view_settings.view_transform = "Standard"
    scn.render.resolution_x, scn.render.resolution_y = res


def shoot(path, loc, target, lens=50, ortho=None):
    scn = bpy.context.scene
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scn.collection.objects.link(cam)
    cam.location = loc; cam.data.lens = lens
    if ortho:
        cam.data.type = "ORTHO"; cam.data.ortho_scale = ortho
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    scn.camera = cam
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)
