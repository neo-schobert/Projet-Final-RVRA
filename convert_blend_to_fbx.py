"""
Script Blender (background mode) — converti le .blend courant en FBX Unity-compatible.
Usage :
  blender.exe --background "file.blend" --python convert_blend_to_fbx.py -- "output.fbx"
"""
import bpy
import sys
import os


def unhide_all():
    """
    Force tous les objets et collections visibles avant l'export.
    En mode background, les collections masquées (icône oeil dans l'Outliner)
    sont exclues de l'export → maillages absents dans le FBX.
    On lève aussi hide_viewport sur chaque objet pour la même raison.
    """
    # Rendre toutes les collections visibles dans tous les view layers
    for view_layer in bpy.context.scene.view_layers:
        for layer_collection in _iter_layer_collections(view_layer.layer_collection):
            layer_collection.hide_viewport = False
            layer_collection.exclude = False

    # Rendre chaque objet visible (hide_viewport = masqué dans le viewport)
    for obj in bpy.data.objects:
        obj.hide_viewport = False
        obj.hide_set(False)   # hide_set(False) = équivalent clic oeil en scène


def _iter_layer_collections(layer_col):
    """Parcours récursif de l'arbre des LayerCollections."""
    yield layer_col
    for child in layer_col.children:
        yield from _iter_layer_collections(child)


def export_fbx(output_path: str):
    # S'assurer que le dossier de destination existe
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    # Blender 4.2+ : le module FBX est une extension, on l'active si besoin
    try:
        bpy.ops.preferences.addon_enable(module="io_scene_fbx")
    except Exception:
        pass  # déjà actif ou nom différent selon la version

    # Forcer la visibilité de tous les objets/collections
    unhide_all()

    n_meshes = sum(1 for o in bpy.data.objects if o.type == "MESH")
    print(f"[BlendToFBX] {n_meshes} mesh(es) trouvé(s) dans la scène")
    print(f"[BlendToFBX] Export → {output_path}")

    bpy.ops.export_scene.fbx(
        filepath            = output_path,
        # ── Sélection ──────────────────────────────────────────────────────
        use_selection       = False,     # tout exporter
        use_visible         = False,     # inclure les objets masqués aussi
        # ── Échelle / axes (Unity = Y-up, right-handed) ────────────────────
        global_scale        = 1.0,
        apply_unit_scale    = True,
        apply_scale_options = "FBX_SCALE_NONE",
        bake_space_transform= True,      # applique la conversion d'axes
        axis_forward        = "-Z",
        axis_up             = "Y",
        # ── Types d'objets ─────────────────────────────────────────────────
        object_types        = {"MESH", "ARMATURE", "EMPTY"},
        # ── Géométrie ──────────────────────────────────────────────────────
        use_mesh_modifiers  = True,
        mesh_smooth_type    = "FACE",
        use_tspace          = True,
        use_custom_props    = False,
        # ── Armature ───────────────────────────────────────────────────────
        add_leaf_bones          = False,
        primary_bone_axis       = "Y",
        secondary_bone_axis     = "X",
        use_armature_deform_only= False,
        # ── Animation ──────────────────────────────────────────────────────
        bake_anim           = False,     # pas d'animation pour les assets statiques
        # ── Textures : copiées dans <nom_fbx>_textures/ ────────────────────
        path_mode           = "COPY",
        embed_textures      = False,
    )

    print(f"[BlendToFBX] ✓ Terminé : {output_path}")


# ── Point d'entrée ────────────────────────────────────────────────────────────
if "--" in sys.argv:
    extra = sys.argv[sys.argv.index("--") + 1:]
    if extra:
        export_fbx(extra[0])
    else:
        print("[BlendToFBX] ERREUR : chemin FBX de sortie manquant après '--'")
else:
    print("[BlendToFBX] ERREUR : passer le chemin FBX après '--'")
