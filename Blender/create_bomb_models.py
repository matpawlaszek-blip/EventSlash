import bpy
import bmesh
import math
import os

blend_path = r"c:\Users\matpa\Desktop\EventSlash\Blender\EventSlash_Fruits.blend"
export_dir = r"c:\Users\matpa\Desktop\EventSlash\Unity\EventSlash\Assets\Models\Fruits"
os.makedirs(export_dir, exist_ok=True)

if os.path.exists(blend_path):
    bpy.ops.wm.open_mainfile(filepath=blend_path)

def get_or_create_mat(name, rgba, metallic=0.0, roughness=0.5, emission_rgba=None, emission_strength=0.0):
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
        if emission_rgba and "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = emission_rgba
            bsdf.inputs["Emission Strength"].default_value = emission_strength
    mat.diffuse_color = rgba
    return mat

mat_body  = get_or_create_mat("Mat_Bomb_Body", (0.10, 0.11, 0.13, 1.0), metallic=0.75, roughness=0.28)
mat_band  = get_or_create_mat("Mat_Bomb_DangerRed", (0.90, 0.12, 0.08, 1.0), metallic=0.35, roughness=0.35)
mat_cap   = get_or_create_mat("Mat_Bomb_Cap", (0.72, 0.54, 0.20, 1.0), metallic=0.80, roughness=0.30)
mat_fuse  = get_or_create_mat("Mat_Bomb_Fuse", (0.68, 0.56, 0.36, 1.0), metallic=0.0, roughness=0.85)
mat_spark = get_or_create_mat("Mat_Bomb_Spark", (1.0, 0.65, 0.08, 1.0), metallic=0.0, roughness=0.2, emission_rgba=(1.0, 0.45, 0.05, 1.0), emission_strength=4.0)

for obj in list(bpy.data.objects):
    if obj.name.startswith("Bomb_"):
        bpy.data.objects.remove(obj, do_unlink=True)

def build_bomb_mesh(name, mode="whole"):
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for m in (mat_body, mat_band, mat_cap, mat_fuse, mat_spark):
        obj.data.materials.append(m)

    bm = bmesh.new()

    # 1. Main iron sphere body (radius 0.52)
    bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=18, radius=0.52)
    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        zc = f.calc_center_median().z
        if abs(zc) < 0.095:
            f.material_index = 1  # Mat_Bomb_DangerRed
        else:
            f.material_index = 0  # Mat_Bomb_Body
        f.smooth = True

    if mode in ("whole", "half_a"):
        # 2. Top brass collar/nozzle at top of sphere
        ret_cap = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=20, radius1=0.19, radius2=0.15, depth=0.18)
        for v in ret_cap["verts"]:
            v.co.z += 0.52
            v.co.x += 0.04
        bm.faces.ensure_lookup_table()
        for f in bm.faces:
            if f.calc_center_median().z > 0.46 and f.material_index == 0:
                if abs(f.calc_center_median().x - 0.04) < 0.22 and abs(f.calc_center_median().y) < 0.22:
                    f.material_index = 2  # Mat_Bomb_Cap

        # 3. Curved fuse cord segments
        fuse_pts = [
            (0.04, 0.0, 0.60),
            (0.10, 0.0, 0.72),
            (0.20, 0.03, 0.82),
            (0.30, 0.05, 0.86),
        ]
        for i in range(len(fuse_pts) - 1):
            p0 = fuse_pts[i]
            p1 = fuse_pts[i + 1]
            mx = 0.5 * (p0[0] + p1[0])
            my = 0.5 * (p0[1] + p1[1])
            mz = 0.5 * (p0[2] + p1[2])
            ret_f = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=10, radius1=0.042, radius2=0.038, depth=0.15)
            for v in ret_f["verts"]:
                tx = v.co.x + v.co.z * 0.45
                tz = v.co.z - v.co.x * 0.25
                v.co.x = tx + mx
                v.co.y += my
                v.co.z = tz + mz
                for f in v.link_faces:
                    f.material_index = 3  # Mat_Bomb_Fuse

        # 4. Glowing spark star at fuse tip
        ret_s = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.105)
        for idx, v in enumerate(ret_s["verts"]):
            if idx % 2 == 0:
                v.co *= 1.48
            v.co.x += 0.32
            v.co.y += 0.05
            v.co.z += 0.88
            for f in v.link_faces:
                f.material_index = 4  # Mat_Bomb_Spark

    if mode in ("half_a", "half_b"):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        clear_inner = (mode == "half_a")
        clear_outer = (mode == "half_b")
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
                f.material_index = 4  # Glowing explosive core inside cut bomb!

    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return obj

whole  = build_bomb_mesh("Bomb_Whole", "whole")
half_a = build_bomb_mesh("Bomb_Half_A", "half_a")
half_b = build_bomb_mesh("Bomb_Half_B", "half_b")

whole.location = (6.0, 0.0, 0.0)
half_a.location = (6.0, -1.8, 0.0)
half_b.location = (6.0, 1.8, 0.0)

for obj in (whole, half_a, half_b):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    orig_loc = obj.location.copy()
    obj.location = (0.0, 0.0, 0.0)
    out_fbx = os.path.join(export_dir, f"{obj.name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=out_fbx,
        use_selection=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        bake_space_transform=True
    )
    obj.location = orig_loc
    print(f"Exported: {out_fbx}")

bpy.ops.wm.save_as_mainfile(filepath=blend_path)
print("Saved blend file with Bomb models successfully!")
