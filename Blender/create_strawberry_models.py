import bpy
import bmesh
import math
import os
from mathutils import Vector

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


mat_skin = get_or_create_mat("Mat_Strawberry_Skin", (0.90, 0.06, 0.12, 1.0), metallic=0.04, roughness=0.28)
mat_flesh = get_or_create_mat("Mat_Strawberry_Flesh", (0.90, 0.06, 0.12, 1.0), metallic=0.04, roughness=0.32)
mat_seed = get_or_create_mat("Mat_Strawberry_Seed", (0.96, 0.82, 0.22, 1.0), metallic=0.05, roughness=0.35)
mat_leaf = get_or_create_mat("Mat_Leaf_Green", (0.18, 0.58, 0.12, 1.0), metallic=0.0, roughness=0.50)

for obj in list(bpy.data.objects):
    if obj.name.startswith("Strawberry_"):
        bpy.data.objects.remove(obj, do_unlink=True)


def strawberry_profile_scale(zn):
    """
    Maps normalized vertical coordinate zn in [-1, +1] to horizontal XY scale
    for a classic heart-conical strawberry silhouette (plump shoulder near +0.35, tapered tip at -1.0).
    """
    t = (zn + 1.0) * 0.5  # 0 at bottom tip, 1 at top crown
    if t < 0.72:
        s = t / 0.72
        return 0.18 + 0.88 * (s ** 0.68)
    else:
        s = (t - 0.72) / 0.28
        return 1.06 * math.cos(s * math.pi * 0.44)


def build_strawberry_mesh(name, mode="whole"):
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for m in (mat_skin, mat_flesh, mat_seed, mat_leaf):
        obj.data.materials.append(m)

    bm = bmesh.new()

    # 1. Conical-heart strawberry body
    bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=20, radius=0.50)
    body_verts = list(bm.verts)
    for v in body_verts:
        zn = max(-1.0, min(1.0, v.co.z / 0.50))
        rad_scale = strawberry_profile_scale(zn)
        v.co.x *= rad_scale
        v.co.y *= rad_scale
        v.co.z *= 1.12
        if zn > 0.86:
            v.co.z -= (zn - 0.86) * 0.22

    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        f.material_index = 0  # Mat_Strawberry_Skin
        f.smooth = True

    bm.normal_update()

    # 2. Embed golden-yellow strawberry seeds directly INTO the surface vertices of the strawberry body
    #    so they sit flush against the skin and never float or stick out!
    seed_sites = []
    for idx, v in enumerate(body_verts):
        if -0.44 <= v.co.z <= 0.40:
            # Pick a staggered subset of body vertices with minimum spacing >= 0.115
            pos = v.co.copy()
            nrm = v.normal.copy()
            if all((pos - sp).length >= 0.115 for sp, _ in seed_sites):
                seed_sites.append((pos, nrm))
                # Slightly dimple the body vertex inward where the seed sits
                v.co -= nrm * 0.006

    for pos, nrm in seed_sites:
        ret_s = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.015)
        # Flatten the seed along the surface normal and sink its center slightly inside the skin surface
        embed_center = pos - nrm * 0.005
        for sv in ret_s["verts"]:
            offset = sv.co.copy()
            # Compress height along normal so seed is flat and flush with the berry skin
            normal_comp = offset.dot(nrm)
            tangent_comp = offset - nrm * normal_comp
            flat_offset = tangent_comp * 0.95 + nrm * (normal_comp * 0.45)
            sv.co = embed_center + flat_offset
            for f in sv.link_faces:
                f.material_index = 2  # Mat_Strawberry_Seed
                f.smooth = True

    # 3. Leafy green 7-pointed calyx crown + short stem at the top (+Z)
    num_leaves = 7
    for i in range(num_leaves):
        ang = (2.0 * math.pi * i) / num_leaves
        ca = math.cos(ang)
        sa = math.sin(ang)
        ret_l = bmesh.ops.create_cone(
            bm, cap_ends=True, cap_tris=False, segments=8, radius1=0.11, radius2=0.015, depth=0.34
        )
        for v in ret_l["verts"]:
            lx = v.co.x * 0.9
            ly = v.co.z + 0.17
            lz = v.co.y * 0.25 - (ly * ly) * 0.36
            wx = ca * ly - sa * lx
            wy = sa * ly + ca * lx
            wz = 0.50 + lz
            v.co.x = wx
            v.co.y = wy
            v.co.z = wz
            for f in v.link_faces:
                f.material_index = 3  # Mat_Leaf_Green
                f.smooth = True

    ret_stem = bmesh.ops.create_cone(
        bm, cap_ends=True, cap_tris=False, segments=10, radius1=0.045, radius2=0.032, depth=0.18
    )
    for v in ret_stem["verts"]:
        v.co.x += v.co.z * 0.18
        v.co.z += 0.56
        for f in v.link_faces:
            f.material_index = 3  # Mat_Leaf_Green
            f.smooth = True

    # 4. Bisect into Half_A and Half_B if requested
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
                f.material_index = 1  # Mat_Strawberry_Flesh

    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return obj


whole = build_strawberry_mesh("Strawberry_Whole", "whole")
half_a = build_strawberry_mesh("Strawberry_Half_A", "half_a")
half_b = build_strawberry_mesh("Strawberry_Half_B", "half_b")

whole.location = (10.0, 0.0, 0.0)
half_a.location = (10.0, -1.8, 0.0)
half_b.location = (10.0, 1.8, 0.0)

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
print("Saved blend file with flush-seeded Strawberry models successfully!")
