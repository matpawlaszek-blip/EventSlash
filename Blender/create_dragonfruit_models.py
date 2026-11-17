import bpy
import bmesh
import math
import os

blend_path = r"c:\Users\matpa\Desktop\EventSlash\Blender\EventSlash_Fruits.blend"
export_dir = r"c:\Users\matpa\Desktop\EventSlash\Unity\EventSlash\Assets\Models\Fruits"
os.makedirs(export_dir, exist_ok=True)

if os.path.exists(blend_path):
    bpy.ops.wm.open_mainfile(filepath=blend_path)


def get_or_create_mat(name, rgba, metallic=0.0, roughness=0.45):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = rgba
        if "Metallic" in bsdf.inputs:
            bsdf.inputs["Metallic"].default_value = metallic
        if "Roughness" in bsdf.inputs:
            bsdf.inputs["Roughness"].default_value = roughness
    mat.diffuse_color = rgba
    return mat


mat_skin = get_or_create_mat("Mat_Dragonfruit_Skin", (0.94, 0.07, 0.40, 1.0), metallic=0.04, roughness=0.34)
mat_scale_tip = get_or_create_mat("Mat_Dragonfruit_ScaleTip", (0.38, 0.84, 0.14, 1.0), metallic=0.02, roughness=0.42)
mat_flesh = get_or_create_mat("Mat_Dragonfruit_Flesh", (0.97, 0.97, 0.98, 1.0), metallic=0.0, roughness=0.40)
mat_seed = get_or_create_mat("Mat_Seed_Black", (0.05, 0.05, 0.05, 1.0), metallic=0.05, roughness=0.25)

for obj in list(bpy.data.objects):
    if obj.name.startswith("Dragonfruit_"):
        bpy.data.objects.remove(obj, do_unlink=True)


def build_dragonfruit_mesh(name, mode="whole"):
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for m in (mat_skin, mat_scale_tip, mat_flesh, mat_seed):
        obj.data.materials.append(m)

    bm = bmesh.new()

    # 1. Oval magenta dragonfruit body (radius 0.48, elongated 1.26x in Z)
    bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=20, radius=0.48)
    for v in bm.verts:
        ang = math.atan2(v.co.y, v.co.x)
        rib = 1.0 + 0.035 * math.cos(ang * 7.0)
        v.co.x *= rib * 0.94
        v.co.y *= rib * 0.94
        v.co.z *= 1.26

    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        f.material_index = 0  # Mat_Dragonfruit_Skin
        f.smooth = True

    # 2. Iconic curved flame-like dragonfruit scales (bracts) with lime-green tips
    scale_rings = [
        (-0.34, 6, 0.24, 0.42),
        (-0.08, 7, 0.28, 0.36),
        (0.18, 7, 0.28, 0.32),
        (0.42, 6, 0.26, 0.24),
    ]
    for r_idx, (z_base, count, fin_len, flare) in enumerate(scale_rings):
        zn = max(-0.98, min(0.98, z_base / (0.48 * 1.26)))
        r_ring = 0.48 * 0.94 * math.sqrt(max(0.04, 1.0 - zn * zn))
        ang_offset = (r_idx % 2) * (math.pi / count)

        for i in range(count):
            ang = (2.0 * math.pi * i) / count + ang_offset
            ca = math.cos(ang)
            sa = math.sin(ang)

            ret_fin = bmesh.ops.create_cone(
                bm, cap_ends=True, cap_tris=False, segments=8, radius1=0.095, radius2=0.012, depth=fin_len
            )
            for v in ret_fin["verts"]:
                # Local fin coords: z in [-fin_len/2, +fin_len/2]
                t = (v.co.z + fin_len * 0.5) / fin_len  # 0 at base, 1 at tip
                # Flatten tangentially, curve outward and upward like a dragon scale
                tang = v.co.x * 1.15
                thick = v.co.y * 0.38
                rad_out = r_ring - 0.03 + t * fin_len * flare + (t * t) * 0.08 + thick
                z_out = z_base + t * fin_len * 0.88

                v.co.x = ca * rad_out - sa * tang
                v.co.y = sa * rad_out + ca * tang
                v.co.z = z_out

                for f in v.link_faces:
                    # Base of scale is hot pink skin, outer half of scale is lime-green tip!
                    f.material_index = 1 if t > 0.38 else 0
                    f.smooth = True

    # 3. Top green crown tuft at +Z pole
    for i in range(5):
        ang = (2.0 * math.pi * i) / 5.0
        ca = math.cos(ang)
        sa = math.sin(ang)
        ret_top = bmesh.ops.create_cone(
            bm, cap_ends=True, cap_tris=False, segments=8, radius1=0.08, radius2=0.014, depth=0.24
        )
        for v in ret_top["verts"]:
            t = (v.co.z + 0.12) / 0.24
            r_out = 0.06 + t * 0.11
            v.co.x = ca * r_out - sa * (v.co.x * 0.8)
            v.co.y = sa * r_out + ca * (v.co.x * 0.8)
            v.co.z = 0.55 + t * 0.20
            for f in v.link_faces:
                f.material_index = 1  # Mat_Dragonfruit_ScaleTip
                f.smooth = True

    # 4. Bisect into Half_A and Half_B with white flesh + speckled black seeds on the cut face
    if mode in ("half_a", "half_b"):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        clear_inner = mode == "half_a"
        clear_outer = mode == "half_b"
        ret_cut = bmesh.ops.bisect_plane(
            bm,
            geom=geom,
            dist=0.0001,
            plane_co=(0.0, 0.0, 0.0),
            plane_no=(1.0, 0.0, 0.0),
            use_snap_center=False,
            clear_outer=clear_outer,
            clear_inner=clear_inner,
        )
        cut_edges = [e for e in ret_cut["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
        if cut_edges:
            ret_fill = bmesh.ops.contextual_create(bm, geom=cut_edges)
            for f in ret_fill.get("faces", []):
                f.material_index = 2  # Mat_Dragonfruit_Flesh (snow white)

        # Add flush black seeds on the flat cut face (X = 0 plane)
        sign_x = 0.004 if mode == "half_a" else -0.004
        for ring_r in (0.09, 0.19, 0.29):
            steps = 5 if ring_r < 0.12 else (9 if ring_r < 0.22 else 13)
            for s in range(steps):
                a = (2.0 * math.pi * s) / steps + ring_r * 2.0
                sy = math.cos(a) * ring_r
                sz = math.sin(a) * ring_r * 1.22
                ret_s = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.016)
                for v in ret_s["verts"]:
                    v.co.x = v.co.x * 0.25 + sign_x
                    v.co.y += sy
                    v.co.z += sz
                    for f in v.link_faces:
                        f.material_index = 3  # Mat_Seed_Black
                        f.smooth = True

    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return obj


whole = build_dragonfruit_mesh("Dragonfruit_Whole", "whole")
half_a = build_dragonfruit_mesh("Dragonfruit_Half_A", "half_a")
half_b = build_dragonfruit_mesh("Dragonfruit_Half_B", "half_b")

whole.location = (12.0, 0.0, 0.0)
half_a.location = (12.0, -1.8, 0.0)
half_b.location = (12.0, 1.8, 0.0)

for obj in (whole, half_a, half_b):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    orig_loc = obj.location.copy()
    obj.location = (0.0, 0.0, 0.0)
    out_fbx = os.path.join(export_dir, f"{obj.name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=out_fbx,
        use_selection=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
    )
    obj.location = orig_loc
    print(f"Exported: {out_fbx}")

bpy.ops.wm.save_as_mainfile(filepath=blend_path)
print("Saved blend file with Dragonfruit models successfully!")
