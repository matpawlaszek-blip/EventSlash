import bpy
import bmesh
import math
import random
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


mat_skin = get_or_create_mat("Mat_Pomegranate_Skin", (0.76, 0.07, 0.16, 1.0), metallic=0.05, roughness=0.38)
mat_crown = get_or_create_mat("Mat_Pomegranate_Crown", (0.58, 0.05, 0.11, 1.0), metallic=0.05, roughness=0.50)
mat_pith = get_or_create_mat("Mat_Pomegranate_Pith", (0.96, 0.88, 0.74, 1.0), metallic=0.0, roughness=0.65)
mat_aril = get_or_create_mat("Mat_Pomegranate_Aril", (0.92, 0.04, 0.14, 1.0), metallic=0.10, roughness=0.18)

for obj in list(bpy.data.objects):
    if obj.name.startswith("Pomegranate_"):
        bpy.data.objects.remove(obj, do_unlink=True)


def build_pomegranate_mesh(name, mode="whole"):
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for m in (mat_skin, mat_crown, mat_pith, mat_aril):
        obj.data.materials.append(m)

    bm = bmesh.new()

    if mode == "piece":
        # Irregular exploded chunk of pomegranate: outer crimson rind wedge + cream pith + ruby seed cluster
        ret_wedge = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.26)
        for v in ret_wedge["verts"]:
            ang = math.atan2(v.co.y, v.co.x)
            v.co.x *= 1.18
            v.co.y *= 0.88
            v.co.z *= 0.78
            if v.co.z > 0:
                v.co.z *= 0.75

        bm.faces.ensure_lookup_table()
        for f in bm.faces:
            c = f.calc_center_median()
            if c.z < -0.02:
                f.material_index = 0  # Mat_Pomegranate_Skin
            elif abs(c.x) > 0.16 or abs(c.y) > 0.14:
                f.material_index = 2  # Mat_Pomegranate_Pith
            else:
                f.material_index = 3  # Mat_Pomegranate_Aril
            f.smooth = True

        # Add 7 protruding glossy ruby aril seeds on top of the chunk
        aril_offsets = [
            (0.0, 0.0, 0.14),
            (0.11, 0.05, 0.11),
            (-0.10, 0.06, 0.11),
            (0.08, -0.08, 0.10),
            (-0.09, -0.07, 0.10),
            (0.0, 0.11, 0.09),
            (0.02, -0.11, 0.09),
        ]
        for ox, oy, oz in aril_offsets:
            ret_a = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.075)
            for v in ret_a["verts"]:
                v.co.x = v.co.x * 0.85 + ox
                v.co.y = v.co.y * 1.15 + oy
                v.co.z = v.co.z * 0.95 + oz
                for f in v.link_faces:
                    f.material_index = 3  # Mat_Pomegranate_Aril
                    f.smooth = True

        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        return obj

    # 1. Main slightly hexagonal/organic pomegranate body (radius 0.56)
    bmesh.ops.create_uvsphere(bm, u_segments=30, v_segments=20, radius=0.56)
    for v in bm.verts:
        ang = math.atan2(v.co.y, v.co.x)
        r_xy = math.hypot(v.co.x, v.co.y)
        # Subtle 6-sided pomegranate ribbing
        rib = 1.0 + 0.045 * math.cos(ang * 6.0) * (1.0 - abs(v.co.z / 0.58))
        v.co.x *= rib
        v.co.y *= rib
        v.co.z *= 0.95

    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        f.material_index = 0  # Mat_Pomegranate_Skin
        f.smooth = True

    # 2. Iconic 6-pointed calyx crown at top (+Z)
    ret_neck = bmesh.ops.create_cone(
        bm, cap_ends=True, cap_tris=False, segments=18, radius1=0.17, radius2=0.22, depth=0.16
    )
    for v in ret_neck["verts"]:
        # Flare out into 6 crown petals at the top rim
        if v.co.z > 0.02:
            ang = math.atan2(v.co.y, v.co.x)
            petal = max(0.0, math.cos(ang * 6.0))
            v.co.z += petal * 0.095
            v.co.x *= 1.0 + petal * 0.18
            v.co.y *= 1.0 + petal * 0.18
        v.co.z += 0.54
        for f in v.link_faces:
            f.material_index = 1  # Mat_Pomegranate_Crown
            f.smooth = True

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
                f.material_index = 2  # Creamy pith on the flat cut face

        # Add ruby aril seed pockets on the cut plane
        sign_x = 0.02 if mode == "half_a" else -0.02
        for ring_r in (0.14, 0.28, 0.39):
            steps = 6 if ring_r < 0.2 else 10
            for s in range(steps):
                a = (2.0 * math.pi * s) / steps + ring_r
                ay = math.cos(a) * ring_r
                az = math.sin(a) * ring_r * 0.9
                ret_seed = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.055)
                for v in ret_seed["verts"]:
                    v.co.x = v.co.x * 0.45 + sign_x
                    v.co.y += ay
                    v.co.z += az
                    for f in v.link_faces:
                        f.material_index = 3  # Mat_Pomegranate_Aril
                        f.smooth = True

    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return obj


whole = build_pomegranate_mesh("Pomegranate_Whole", "whole")
half_a = build_pomegranate_mesh("Pomegranate_Half_A", "half_a")
half_b = build_pomegranate_mesh("Pomegranate_Half_B", "half_b")
piece = build_pomegranate_mesh("Pomegranate_Piece", "piece")

whole.location = (8.0, 0.0, 0.0)
half_a.location = (8.0, -1.8, 0.0)
half_b.location = (8.0, 1.8, 0.0)
piece.location = (8.0, 3.2, 0.0)

for obj in (whole, half_a, half_b, piece):
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
print("Saved blend file with Pomegranate models successfully!")
