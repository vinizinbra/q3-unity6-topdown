"""
Max (mini / chibi) - low-poly rigged hero for a mobile top-down game.

Run:
  Blender -b -P build_max_mini.py -- <out_dir> <blend_path> [preview_dir]
  -> <out_dir>/MaxMini.fbx + MaxMini_Palette.png

~1.08 tall, head ~45% of the height so it reads from a top-down camera. Unity-Humanoid bone names.
"""
import bpy, os, sys, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import importlib, lowpoly_char
importlib.reload(lowpoly_char)
from lowpoly_char import Kit, side_name, front_y, reset_scene, pose_rot, preview_setup, shoot

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = argv[0] if len(argv) > 0 else "//"
BLEND_PATH = argv[1] if len(argv) > 1 else ""
PREVIEW_DIR = argv[2] if len(argv) > 2 else ""

PALETTE = ["F6C29C", "F2701E", "D24E10", "27479A",
           "1B2F68", "1F3A5E", "162A45", "1A2744",
           "F2F2F5", "F28A1E", "F7C51E", "15151C",
           "11152E", "DCDCE4", "DE9A74", "1B2C4A"]
(SKIN, HAIR, HAIR_DARK, CAP, CAP_DARK, JACKET, JACKET_DARK, PANTS,
 WHITE, ORANGE, YELLOW, BLACK, BELT, SOLE, SKIN_DARK, SCARF) = range(16)

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


def build(k):
    # ---- hips / belt / jacket
    k.loft([((0, 0, 0.30), 0.12, 0.085, {"Hips": 1}), ((0, 0, 0.40), 0.135, 0.09, {"Hips": 1})], PANTS)
    k.loft([((0, 0, 0.385), 0.139, 0.095, {"Hips": 1}), ((0, 0, 0.415), 0.139, 0.095, {"Hips": 1})], BELT)
    k.box((0, front_y(0.095) - 0.006, 0.40), (0.05, 0.014, 0.034), YELLOW, "Hips")
    jacket = [((0, 0, 0.39), 0.15, 0.105, {"Hips": 0.6, "Spine": 0.4}),
              ((0, 0, 0.47), 0.14, 0.10, {"Spine": 1}),
              ((0, 0, 0.56), 0.15, 0.10, {"Chest": 1}),
              ((0, 0, 0.60), 0.09, 0.07, {"Chest": 0.5, "Neck": 0.5})]
    k.loft(jacket, JACKET)
    jy = lambda z: front_y(0.105) - 0.004   # just proud of the jacket front across its span
    k.plate([(-0.038, 0.535), (0.038, 0.535), (0.048, 0.43), (-0.048, 0.43)], jy, 0.02, WHITE, "Chest")

    # ---- scarf collar
    k.loft([((0, 0.004, 0.53), 0.13, 0.105, {"Chest": 1}), ((0, 0, 0.60), 0.12, 0.095, {"Chest": 0.5, "Neck": 0.5}),
            ((0, 0, 0.645), 0.10, 0.085, {"Neck": 0.5, "Head": 0.5})], SCARF)

    # ---- big head
    k.loft([((0, -0.005, 0.625), 0.10, 0.09, {"Head": 1}), ((0, 0, 0.67), 0.18, 0.16, {"Head": 1}),
            ((0, 0, 0.78), 0.21, 0.19, {"Head": 1}), ((0, 0, 0.90), 0.21, 0.19, {"Head": 1}),
            ((0, 0, 0.96), 0.16, 0.15, {"Head": 1})], SKIN)
    fy = front_y(0.19)
    for s in (1, -1):
        k.box((0.066 * s, fy - 0.003, 0.785), (0.036, 0.01, 0.058), BLACK, "Head")                    # eyes
        k.box((0.058 * s, fy - 0.009, 0.8), (0.012, 0.004, 0.016), WHITE, "Head")                      # glint
        k.box((0.072 * s, fy - 0.003, 0.848), (0.08, 0.01, 0.019), BLACK, "Head", rot=(0, -0.38 * s, 0))
        k.box((0.215 * s, 0.0, 0.785), (0.03, 0.06, 0.085), SKIN, "Head", chamfer=0.008)              # ears
        k.box((0.199 * s, -0.055, 0.84), (0.026, 0.07, 0.1), HAIR, "Head")                             # sideburns
    k.box((0, fy - 0.003, 0.70), (0.042, 0.01, 0.009), BLACK, "Head")                                 # mouth
    k.box((-0.02, -0.165, 0.86), (0.30, 0.05, 0.035), HAIR, "Head")                                    # fringe

    # ---- backwards cap
    # cap sits back so its front is flush with the face: eyes stay visible from the top-down camera
    k.loft([((0, 0.035, 0.85), 0.226, 0.216, {"Head": 1}), ((0, 0.035, 0.95), 0.222, 0.212, {"Head": 1}),
            ((0, 0.035, 1.02), 0.17, 0.162, {"Head": 1}), ((0, 0.035, 1.06), 0.08, 0.078, {"Head": 1})], CAP)
    k.box((0, 0.035, 1.066), (0.036, 0.036, 0.02), CAP_DARK, "Head")
    k.box((0, 0.295, 0.868), (0.28, 0.2, 0.03), CAP, "Head", chamfer=0.01, rot=(0.15, 0, 0))            # bill
    k.box((0, -0.18, 0.895), (0.15, 0.02, 0.045), CAP_DARK, "Head")                                    # snap band
    for x in (-0.04, 0.0, 0.04):
        k.box((x, -0.191, 0.895), (0.016, 0.006, 0.016), BLACK, "Head")

    # ---- spiky hair bursting out to the side
    base = (0.13, -0.14, 0.92)
    k.box(base, (0.17, 0.11, 0.1), HAIR, "Head")
    for tip, r, pal in (((0.34, -0.19, 1.04), 0.065, HAIR), ((0.38, -0.07, 0.93), 0.06, HAIR_DARK),
                        ((0.23, -0.27, 1.07), 0.06, HAIR), ((0.30, -0.03, 1.09), 0.055, HAIR),
                        ((0.37, -0.20, 0.85), 0.05, HAIR_DARK)):
        k.cone(base, tip, r, pal, "Head")

    for s in (1, -1):
        sn = lambda n: side_name(s, n)
        # ---- stubby arms + fists
        k.loft([((0.12 * s, 0, 0.54), 0.052, 0.05, {"Chest": 0.4, sn("UpperArm"): 0.6}),
                ((0.21 * s, 0, 0.54), 0.046, 0.045, {sn("UpperArm"): 0.5, sn("LowerArm"): 0.5}),
                ((0.295 * s, 0, 0.54), 0.044, 0.043, {sn("LowerArm"): 1})], JACKET, axis="X", sides=6)
        k.box((0.33 * s, 0, 0.54), (0.075, 0.075, 0.075), SKIN, sn("Hand"), chamfer=0.014)

        # ---- short legs
        x = 0.075 * s
        k.loft([((x, 0, 0.34), 0.07, 0.072, {"Hips": 0.5, sn("UpperLeg"): 0.5}),
                ((x, 0, 0.20), 0.062, 0.064, {sn("UpperLeg"): 0.5, sn("LowerLeg"): 0.5}),
                ((x, 0, 0.09), 0.068, 0.07, {sn("LowerLeg"): 1})], PANTS, sides=6)

        # ---- chunky white/orange sneakers
        f = sn("Foot")
        k.box((x, -0.02, 0.022), (0.125, 0.2, 0.044), SOLE, f, chamfer=0.01)
        k.box((x, -0.005, 0.075), (0.115, 0.165, 0.07), WHITE, f, chamfer=0.016)
        k.box((x, 0.02, 0.08), (0.119, 0.045, 0.055), ORANGE, f)
        k.box((x, -0.085, 0.06), (0.105, 0.035, 0.03), ORANGE, sn("Toes"))


reset_scene()
os.makedirs(OUT_DIR, exist_ok=True)
kit = Kit("MaxMini", PALETTE)
img = kit.palette_image(os.path.join(OUT_DIR, "MaxMini_Palette.png"))
build(kit)
arm = kit.armature(BONES)
body = kit.finish(arm, img)
print(f"[max] tris={sum(len(p.vertices) - 2 for p in body.data.polygons)} verts={len(body.data.vertices)} "
      f"unweighted={sum(1 for v in body.data.vertices if not v.groups)}")
kit.export(body, arm, OUT_DIR)
if BLEND_PATH:
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

if PREVIEW_DIR:
    preview_setup()
    shoot(os.path.join(PREVIEW_DIR, "maxmini_tpose.png"), (0.9, -2.6, 0.9), (0, 0, 0.55), 50)
    # relaxed idle
    pose_rot(arm, "LeftUpperArm", "Y", 68); pose_rot(arm, "RightUpperArm", "Y", -68)
    pose_rot(arm, "LeftLowerArm", "X", -15); pose_rot(arm, "RightLowerArm", "X", -15)
    pose_rot(arm, "LeftUpperLeg", "Y", -4); pose_rot(arm, "RightUpperLeg", "Y", 4)
    pose_rot(arm, "Head", "Z", 8)
    shoot(os.path.join(PREVIEW_DIR, "maxmini_front.png"), (-0.7, -2.4, 0.85), (0, 0, 0.52), 50)
    shoot(os.path.join(PREVIEW_DIR, "maxmini_back.png"), (1.1, 2.2, 1.0), (0, 0, 0.52), 50)
    # mobile top-down gameplay camera (orthographic, ~55deg down)
    shoot(os.path.join(PREVIEW_DIR, "maxmini_topdown.png"), (0, -2.2, 3.2), (0, 0, 0.4), ortho=1.6)
