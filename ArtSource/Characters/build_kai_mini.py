"""
Kai (mini / chibi) - low-poly rigged hero for a mobile top-down game.

Run:
  Blender -b -P build_kai_mini.py -- <out_dir> <blend_path> [preview_dir]
  -> <out_dir>/KaiMini.fbx + KaiMini_Palette.png

White swept spiky hair, black blindfold, long black high-collar coat with purple piping and a
diamond emblem, purple-trimmed boots. Same scale / skeleton / palette pipeline as MaxMini.
"""
import bpy, os, sys, math
from mathutils import Vector
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import importlib, lowpoly_char
importlib.reload(lowpoly_char)
from lowpoly_char import Kit, side_name, front_y, reset_scene, pose_rot, preview_setup, shoot

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = argv[0] if len(argv) > 0 else "//"
BLEND_PATH = argv[1] if len(argv) > 1 else ""
PREVIEW_DIR = argv[2] if len(argv) > 2 else ""

PALETTE = ["F4D2B8", "DFAE8E", "EEF1F6", "A9B2C2",
           "1D1C26", "2E2D3B", "7C3FE6", "4F24A8",
           "24232F", "16151C", "0F0F14", "B690FF",
           "7E4F4C", "101014", "3A2E5C", "D3D9E3"]
(SKIN, SKIN_DARK, HAIR, HAIR_SHADE, COAT, COAT_LIGHT, PURPLE, PURPLE_DARK,
 PANTS, BOOT, BLINDFOLD, LILAC, MOUTH, BLACK, SOLE, HAIR_MID) = range(16)

BONES = [("Hips", None, (0, 0, 0.33), (0, 0, 0.41)),
         ("Spine", "Hips", (0, 0, 0.41), (0, 0, 0.49)),
         ("Chest", "Spine", (0, 0, 0.49), (0, 0, 0.59)),
         ("Neck", "Chest", (0, 0, 0.59), (0, 0, 0.64)),
         ("Head", "Neck", (0, 0, 0.64), (0, 0, 1.0))]
for s in (1, -1):
    BONES += [(side_name(s, "Shoulder"), "Chest", (0.03 * s, 0, 0.53), (0.12 * s, 0, 0.54)),
              (side_name(s, "UpperArm"), side_name(s, "Shoulder"), (0.12 * s, 0, 0.54), (0.21 * s, 0, 0.54)),
              (side_name(s, "LowerArm"), side_name(s, "UpperArm"), (0.21 * s, 0, 0.54), (0.29 * s, 0, 0.54)),
              (side_name(s, "Hand"), side_name(s, "LowerArm"), (0.29 * s, 0, 0.54), (0.37 * s, 0, 0.54)),
              (side_name(s, "UpperLeg"), "Hips", (0.075 * s, 0, 0.33), (0.075 * s, 0, 0.20)),
              (side_name(s, "LowerLeg"), side_name(s, "UpperLeg"), (0.075 * s, 0, 0.20), (0.075 * s, 0, 0.08)),
              (side_name(s, "Foot"), side_name(s, "LowerLeg"), (0.075 * s, 0, 0.08), (0.075 * s, -0.06, 0.02)),
              (side_name(s, "Toes"), side_name(s, "Foot"), (0.075 * s, -0.06, 0.02), (0.075 * s, -0.11, 0.02))]

N = 10  # sides for body/head lofts (rounder silhouette than MaxMini's 8)


def fy(ry):
    return front_y(ry, N)


def ring_face(rx, ry, deg, z, out=0.004):
    """Center of the loft face at angle `deg` (-90 = front) on a flat-front N-gon ring."""
    a = math.radians(deg)
    k = math.cos(math.pi / N) + out / max(rx, ry)
    return Vector((rx * math.cos(a) * k, ry * math.sin(a) * k, z))


def build(k):
    # ---- long coat (flares toward the hem)
    coat = [((0, 0.0, 0.18), 0.195, 0.145, {"Hips": 1}),
            ((0, 0.0, 0.30), 0.168, 0.122, {"Hips": 1}),
            ((0, 0.0, 0.40), 0.150, 0.106, {"Hips": 0.5, "Spine": 0.5}),
            ((0, 0.0, 0.48), 0.150, 0.104, {"Spine": 1}),
            ((0, 0.0, 0.565), 0.168, 0.110, {"Chest": 1}),
            ((0, 0.0, 0.605), 0.10, 0.08, {"Chest": 0.5, "Neck": 0.5})]
    k.loft(coat, COAT, sides=N, flat_front=True)
    k.loft([((0, 0, 0.172), 0.2, 0.15, {"Hips": 1}), ((0, 0, 0.205), 0.194, 0.144, {"Hips": 1})],
           PURPLE, sides=N, flat_front=True)                                                       # hem trim
    # front opening: purple piping down the placket, splitting into an inverted V at the hem
    placket = [(0.0, fy(r[2]) - 0.006, r[0][2]) for r in coat[1:5]]
    k.rodline(placket, 0.009, PURPLE, "Spine")
    for s in (1, -1):
        k.rod((0.0, fy(0.122) - 0.006, 0.30), (0.05 * s, fy(0.145) - 0.006, 0.19), 0.009, PURPLE, "Hips")
    k.plate([(0.0, 0.295), (0.045, 0.195), (-0.045, 0.195)], lambda z: fy(0.145) + 0.004 - (0.30 - z) * 0.0,
            0.03, PANTS, "Hips")                                                                     # split gap
    # diamond emblem on the left chest
    ey = fy(0.108) - 0.004
    k.plate([(0.07, 0.545), (0.098, 0.505), (0.07, 0.465), (0.042, 0.505)], lambda z: ey, 0.012, PURPLE, "Chest")
    k.plate([(0.07, 0.530), (0.087, 0.505), (0.07, 0.480), (0.053, 0.505)], lambda z: ey - 0.004, 0.006, LILAC, "Chest")
    # pocket slits with purple piping
    for s in (1, -1):
        c = ring_face(0.16, 0.115, -90 + 36 * s, 0.34, out=0.006)
        t = Vector((math.cos(math.radians(36 * s)), math.sin(math.radians(36 * s)) * 0.7, 0)).normalized() * 0.04
        k.rod(c - t, c + t, 0.008, PURPLE, "Hips")
    # belt peeking at the waist seam
    k.loft([((0, 0, 0.395), 0.153, 0.109, {"Hips": 1}), ((0, 0, 0.412), 0.153, 0.109, {"Hips": 1})],
           COAT_LIGHT, sides=N, flat_front=True)

    # ---- high standing collar (purple lined)
    k.loft([((0, 0.006, 0.55), 0.135, 0.112, {"Chest": 1}), ((0, 0.004, 0.62), 0.128, 0.104, {"Chest": 0.4, "Neck": 0.6}),
            ((0, 0.004, 0.67), 0.13, 0.106, {"Neck": 0.5, "Head": 0.5})], COAT, sides=N, flat_front=True)
    k.loft([((0, 0.004, 0.665), 0.134, 0.11, {"Neck": 0.5, "Head": 0.5}),
            ((0, 0.004, 0.68), 0.134, 0.11, {"Neck": 0.5, "Head": 0.5})], PURPLE, sides=N, flat_front=True)
    k.box((0, fy(0.106) - 0.004, 0.62), (0.016, 0.01, 0.1), PURPLE, "Neck")                          # collar seam

    # ---- head
    k.loft([((0, -0.005, 0.635), 0.10, 0.09, {"Head": 1}), ((0, 0, 0.665), 0.168, 0.152, {"Head": 1}),
            ((0, 0, 0.72), 0.205, 0.19, {"Head": 1}), ((0, 0, 0.80), 0.215, 0.20, {"Head": 1}),
            ((0, 0, 0.88), 0.21, 0.195, {"Head": 1}), ((0, 0, 0.94), 0.17, 0.16, {"Head": 1}),
            ((0, 0, 0.975), 0.10, 0.10, {"Head": 1})], SKIN, sides=N, flat_front=True)
    k.box((0, fy(0.19) - 0.002, 0.69), (0.03, 0.008, 0.007), MOUTH, "Head")                           # mouth
    for s in (1, -1):
        k.box((0.218 * s, 0.005, 0.765), (0.03, 0.058, 0.08), SKIN, "Head", chamfer=0.008)            # ears
    # blindfold wrapping the head + knot and tails at the back
    k.loft([((0, 0, 0.758), 0.222, 0.207, {"Head": 1}), ((0, 0, 0.836), 0.222, 0.207, {"Head": 1})],
           BLINDFOLD, sides=N, flat_front=True)
    k.box((0, fy(0.207) - 0.003, 0.80), (0.2, 0.006, 0.008), COAT_LIGHT, "Head")                       # sheen line
    k.box((0, 0.21, 0.795), (0.06, 0.04, 0.06), BLINDFOLD, "Head", chamfer=0.01)
    for s in (1, -1):
        k.blade((0.018 * s, 0.215, 0.78), (0.07 * s, 0.30, 0.64), 0.028, 0.008, BLINDFOLD, "Head",
                up=(1, 0, 0))

    # ---- white hair: skull cap + swept blade spikes (up and toward his left, grey underlayer)
    k.loft([((0, 0.01, 0.826), 0.226, 0.212, {"Head": 1}), ((0, 0.01, 0.92), 0.232, 0.218, {"Head": 1}),
            ((0.01, 0.01, 0.995), 0.19, 0.18, {"Head": 1}), ((0.02, 0.01, 1.035), 0.11, 0.10, {"Head": 1})],
           HAIR, sides=N, flat_front=True)
    spikes = [  # base, tip, width, thick, color -- one compact tuft swept up and toward his left
        ((0.02, -0.04, 1.00), (0.22, -0.06, 1.15), 0.08, 0.035, HAIR),
        ((-0.07, 0.01, 1.01), (0.08, 0.02, 1.17), 0.08, 0.035, HAIR),
        ((0.10, 0.03, 0.98), (0.30, 0.05, 1.08), 0.075, 0.03, HAIR),
        ((0.14, -0.10, 0.95), (0.32, -0.14, 1.00), 0.06, 0.028, HAIR_MID),
        ((-0.14, -0.03, 0.97), (-0.08, -0.04, 1.13), 0.065, 0.03, HAIR_MID),
        ((0.03, 0.13, 0.97), (0.12, 0.26, 1.07), 0.065, 0.028, HAIR),
        ((-0.11, 0.13, 0.94), (-0.17, 0.26, 0.99), 0.055, 0.025, HAIR_MID),
        ((-0.19, 0.02, 0.93), (-0.28, 0.02, 0.99), 0.05, 0.022, HAIR_SHADE),
        # short fringe that stops above the blindfold (keeps it readable top-down)
        ((-0.05, -0.18, 0.94), (-0.09, -0.218, 0.855), 0.055, 0.02, HAIR),
        ((0.05, -0.19, 0.945), (0.03, -0.222, 0.86), 0.05, 0.02, HAIR_MID),
        ((0.14, -0.15, 0.94), (0.18, -0.2, 0.86), 0.045, 0.02, HAIR),
        ((-0.15, -0.13, 0.93), (-0.21, -0.16, 0.845), 0.045, 0.02, HAIR_SHADE),
        # grey side lock on his right
        ((-0.19, -0.06, 0.87), (-0.235, -0.10, 0.74), 0.04, 0.02, HAIR_SHADE),
    ]
    for base, tip, w, t, pal in spikes:
        k.blade(base, tip, w, t, pal, "Head", bend=(0, 0, 0.02))

    for s in (1, -1):
        sn = lambda n: side_name(s, n)
        # ---- coat sleeves (flared) with purple cuffs, pale hands
        k.loft([((0.12 * s, 0, 0.54), 0.06, 0.058, {"Chest": 0.4, sn("UpperArm"): 0.6}),
                ((0.21 * s, 0, 0.54), 0.052, 0.05, {sn("UpperArm"): 0.5, sn("LowerArm"): 0.5}),
                ((0.285 * s, 0, 0.54), 0.058, 0.056, {sn("LowerArm"): 1})], COAT, axis="X", sides=8)
        k.loft([((0.278 * s, 0, 0.54), 0.061, 0.059, {sn("LowerArm"): 1}),
                ((0.295 * s, 0, 0.54), 0.061, 0.059, {sn("LowerArm"): 1})], PURPLE, axis="X", sides=8)
        k.box((0.33 * s, 0, 0.535), (0.07, 0.068, 0.07), SKIN, sn("Hand"), chamfer=0.014)
        k.box((0.315 * s, -0.04, 0.55), (0.03, 0.022, 0.026), SKIN, sn("Hand"), chamfer=0.005)         # thumb

        # ---- legs (visible under the coat)
        x = 0.07 * s
        k.loft([((x, 0, 0.34), 0.066, 0.068, {"Hips": 0.5, sn("UpperLeg"): 0.5}),
                ((x, 0, 0.20), 0.058, 0.06, {sn("UpperLeg"): 0.5, sn("LowerLeg"): 0.5}),
                ((x, 0, 0.10), 0.058, 0.06, {sn("LowerLeg"): 1})], PANTS, sides=8)

        # ---- boots: dark upper, purple collar + sole stripe
        f = sn("Foot")
        k.box((x, -0.02, 0.02), (0.122, 0.2, 0.04), SOLE, f, chamfer=0.01)
        k.box((x, -0.005, 0.07), (0.112, 0.17, 0.07), BOOT, f, chamfer=0.016)
        k.box((x, -0.075, 0.05), (0.108, 0.07, 0.05), BOOT, sn("Toes"), chamfer=0.016)
        k.loft([((x, 0.005, 0.10), 0.062, 0.066, {f: 1}), ((x, 0.005, 0.135), 0.066, 0.07, {f: 1})],
               PURPLE, sides=8)
        k.box((x, -0.02, 0.044), (0.126, 0.204, 0.01), PURPLE, f)


reset_scene()
os.makedirs(OUT_DIR, exist_ok=True)
kit = Kit("KaiMini", PALETTE)
img = kit.palette_image(os.path.join(OUT_DIR, "KaiMini_Palette.png"))
build(kit)
arm = kit.armature(BONES)
body = kit.finish(arm, img)
print(f"[kai] tris={sum(len(p.vertices) - 2 for p in body.data.polygons)} verts={len(body.data.vertices)} "
      f"unweighted={sum(1 for v in body.data.vertices if not v.groups)}")
kit.export(body, arm, OUT_DIR)
if BLEND_PATH:
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

if PREVIEW_DIR:
    preview_setup()
    shoot(os.path.join(PREVIEW_DIR, "kaimini_tpose.png"), (0.9, -2.6, 0.9), (0, 0, 0.55), 50)
    # hands tucked toward the coat pockets, like the character sheet
    pose_rot(arm, "LeftUpperArm", "Y", 74); pose_rot(arm, "RightUpperArm", "Y", -74)
    pose_rot(arm, "LeftUpperArm", "Z", -10); pose_rot(arm, "RightUpperArm", "Z", 10)
    pose_rot(arm, "LeftLowerArm", "X", -18); pose_rot(arm, "RightLowerArm", "X", -18)
    pose_rot(arm, "Head", "Z", -6)
    shoot(os.path.join(PREVIEW_DIR, "kaimini_front.png"), (-0.7, -2.4, 0.85), (0, 0, 0.55), 50)
    shoot(os.path.join(PREVIEW_DIR, "kaimini_back.png"), (1.1, 2.2, 1.0), (0, 0, 0.55), 50)
    shoot(os.path.join(PREVIEW_DIR, "kaimini_topdown.png"), (0, -2.2, 3.2), (0, 0, 0.42), ortho=1.7)
