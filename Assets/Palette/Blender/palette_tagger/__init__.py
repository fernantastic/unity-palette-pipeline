"""Palette Tagger - Blender extension.

Adds a "Palette" tab to the 3D Viewport sidebar (alongside Item / Tool / View)
for assigning game *surface types* to the **materials** used in the scene.

Source of truth is the project's ``Assets/Palette/Data/art_data.json`` file.
This extension only ever READS that file - it never writes to it.

Assignment model: one material -> one surface type. Each material stores its
assignment as a string custom property in the form ``"{sceneID}.{surfaceTypeID}"``
(e.g. ``"beach.water"``). The matching unique integer id (a deterministic index
derived from the JSON) is baked per-polygon into the ``PaletteTag``
vertex-color attribute's R channel - each face inherits the id of the material in
its slot - so the assignment survives FBX export / Unity import even for meshes
with multiple material slots (submeshes). See PROPOSAL_unity_import.md.
"""

import json
import os

import bpy
from bpy.props import (
    BoolProperty,
    CollectionProperty,
    EnumProperty,
    StringProperty,
)
from bpy.types import (
    AddonPreferences,
    Operator,
    Panel,
    PropertyGroup,
)


# ------------------------------------------------------------------
# Constants
# ------------------------------------------------------------------

# Where the source-of-truth data lives, relative to the project root.
DATA_REL_PATH = os.path.join("Assets", "Palette", "Data", "art_data.json")

# Name of the vertex-color attribute used to carry the int id (per polygon).
VCOL_ATTR = "PaletteTag"

# Name of the per-material string custom property holding "scene.surface".
MAT_TAG_PROP = "palette_surface_tag"


# ------------------------------------------------------------------
# Loaded data (module-level cache)
# ------------------------------------------------------------------

class _DataCache:
    """Parsed view over art_data.json. Rebuilt whenever the file is (re)loaded."""

    def __init__(self):
        self.loaded = False
        self.status = "No project folder set."
        self.path = ""
        # ordered list of scene ids
        self.scene_ids = []
        # scene_id -> [surface_id, ...] (in file order)
        self.scene_surfaces = {}
        # ordered list of full tags "scene.surface" (canonical order)
        self.full_tags = []
        # full tag -> unique int id (1-based; 0 means unassigned)
        self.tag_to_int = {}
        # int id -> full tag
        self.int_to_tag = {}

    def clear(self):
        self.__init__()

    def load(self, project_root):
        self.clear()
        if not project_root:
            self.status = "No project folder set."
            return
        path = os.path.join(project_root, DATA_REL_PATH)
        self.path = path
        if not os.path.isfile(path):
            self.status = "art_data.json not found at:\n%s" % path
            return
        try:
            with open(path, "r", encoding="utf-8") as fh:
                raw = json.load(fh)
        except Exception as exc:  # noqa: BLE001 - surface any read/parse error in UI
            self.status = "Failed to read art_data.json: %s" % exc
            return

        next_id = 1
        for scene in raw.get("scenes", []):
            scene_id = scene.get("id")
            if not scene_id:
                continue
            self.scene_ids.append(scene_id)
            surfaces = []
            for st in scene.get("surface_types", []):
                surf_id = st.get("id")
                if not surf_id:
                    continue
                surfaces.append(surf_id)
                full = "%s.%s" % (scene_id, surf_id)
                self.full_tags.append(full)
                self.tag_to_int[full] = next_id
                self.int_to_tag[next_id] = full
                next_id += 1
            self.scene_surfaces[scene_id] = surfaces

        self.loaded = True
        self.status = "Loaded %d scenes / %d surface types." % (
            len(self.scene_ids),
            len(self.full_tags),
        )


DATA = _DataCache()


# Enum-item callbacks must keep a Python reference to the returned list, or
# Blender will free the underlying strings and corrupt the UI / crash. We cache
# the most recently returned lists here.
_enum_cache = {
    "scene": [],
    "surface": [],
    "search": [],
}


# ------------------------------------------------------------------
# Preferences (persisted across .blend files)
# ------------------------------------------------------------------

def _on_project_root_changed(self, context):
    DATA.load(self.project_root)


class PALETTE_AddonPreferences(AddonPreferences):
    bl_idname = __package__

    project_root: StringProperty(
        name="Project Folder",
        description="Root of the Unity project. The extension reads "
                    "Assets/Palette/Data/art_data.json from here",
        subtype="DIR_PATH",
        default="",
        update=_on_project_root_changed,
    )

    def draw(self, context):
        layout = self.layout
        _draw_configuration(layout, self)


def get_prefs():
    return bpy.context.preferences.addons[__package__].preferences


# ------------------------------------------------------------------
# Enum item providers
# ------------------------------------------------------------------

def scene_items(self, context):
    items = [("ALL", "All", "Every scene")]
    for sid in DATA.scene_ids:
        items.append((sid, sid, "Scene '%s'" % sid))
    _enum_cache["scene"] = items
    return items


def surface_items(self, context):
    wm = getattr(context, "window_manager", None) or bpy.context.window_manager
    scene_filter = getattr(wm, "palette_scene_filter", "ALL")

    items = []
    if not DATA.loaded:
        items = [("NONE", "<no data loaded>", "")]
        _enum_cache["surface"] = items
        return items

    if scene_filter == "ALL":
        for full in DATA.full_tags:
            sid, surf = full.split(".", 1)
            items.append((full, "%s  /  %s" % (sid, surf), full))
    else:
        for surf in DATA.scene_surfaces.get(scene_filter, []):
            full = "%s.%s" % (scene_filter, surf)
            items.append((full, surf, full))

    if not items:
        items = [("NONE", "<no surface types>", "")]
    _enum_cache["surface"] = items
    return items


def search_items(self, context):
    """All tags as a flat searchable list. Name puts scene first and is space
    separated so a query like 'beach water' fuzzy-matches 'beach water'."""
    items = []
    for full in DATA.full_tags:
        sid, surf = full.split(".", 1)
        label = "%s %s" % (sid, surf)
        items.append((full, label, full))
    if not items:
        items = [("NONE", "<no data loaded>", "")]
    _enum_cache["search"] = items
    return items


def _pending_tag(wm):
    """The currently chosen full tag, or '' if none / invalid."""
    tag = getattr(wm, "palette_surface_filter", "NONE")
    if tag in DATA.tag_to_int:
        return tag
    return ""


# ------------------------------------------------------------------
# Material tagging + per-polygon vertex-color baking
# ------------------------------------------------------------------

def get_material_tag(mat):
    if mat is None:
        return ""
    return mat.get(MAT_TAG_PROP, "")


def iter_scene_mesh_objects(scene):
    for obj in scene.objects:
        if obj.type == "MESH" and obj.data is not None:
            yield obj


def scene_materials(scene):
    """Unique materials used by the mesh objects in the scene, in first-seen
    order. We always collect from the meshes first, then their materials."""
    seen = set()
    result = []
    for obj in iter_scene_mesh_objects(scene):
        for slot in obj.material_slots:
            mat = slot.material
            if mat is not None and mat.name not in seen:
                seen.add(mat.name)
                result.append(mat)
    return result


def selected_object_materials(context):
    """Unique materials used by the selected mesh objects, across every material
    slot / submesh, in first-seen order."""
    seen = set()
    result = []
    for obj in context.selected_objects:
        if obj.type != "MESH" or obj.data is None:
            continue
        for slot in obj.material_slots:
            mat = slot.material
            if mat is not None and mat.name not in seen:
                seen.add(mat.name)
                result.append(mat)
    return result


def bake_object_vertex_colors(obj):
    """Bake each polygon's material surface-id into the PaletteTag R channel."""
    if obj.type != "MESH" or obj.data is None:
        return
    mesh = obj.data
    attrs = mesh.color_attributes
    if VCOL_ATTR not in attrs:
        attrs.new(name=VCOL_ATTR, type="BYTE_COLOR", domain="CORNER")
    col = attrs[VCOL_ATTR]
    slots = obj.material_slots
    for poly in mesh.polygons:
        mat = None
        if poly.material_index < len(slots):
            mat = slots[poly.material_index].material
        int_id = DATA.tag_to_int.get(get_material_tag(mat), 0)
        r = int_id / 255.0
        for li in poly.loop_indices:
            col.data[li].color = (r, 0.0, 0.0, 1.0)
    mesh.update()


def rebake_material(scene, mat):
    """Re-bake every scene mesh that uses this material."""
    for obj in iter_scene_mesh_objects(scene):
        if any(slot.material == mat for slot in obj.material_slots):
            bake_object_vertex_colors(obj)


def assign_tag_to_material(scene, mat, tag):
    if mat is None:
        return
    mat[MAT_TAG_PROP] = tag
    rebake_material(scene, mat)


# ------------------------------------------------------------------
# Shared UI helpers
# ------------------------------------------------------------------

def _draw_configuration(layout, prefs):
    box = layout.box()
    box.label(text="Configuration", icon="PREFERENCES")
    box.prop(prefs, "project_root")
    row = box.row(align=True)
    row.operator("palette.autodetect_root", icon="VIEWZOOM")
    row.operator("palette.reload_data", icon="FILE_REFRESH")
    box.operator("palette.bake_all", icon="GROUP_VCOL")
    for line in DATA.status.split("\n"):
        box.label(text=line, icon="INFO" if DATA.loaded else "ERROR")


def _draw_selector(layout, wm, context):
    """The 'scene and surface type search box' shared by the panel and finder."""
    box = layout.box()
    box.label(text="Scene & Surface Type", icon="VIEWZOOM")

    if not DATA.loaded:
        box.label(text="Load a project folder first.", icon="ERROR")
        return

    row = box.row(align=True)
    row.prop(wm, "palette_scene_filter", text="Scene")

    row = box.row(align=True)
    row.prop(wm, "palette_surface_filter", text="Surface")
    row.operator("palette.search_tag", text="", icon="VIEWZOOM")

    tag = _pending_tag(wm)
    if tag:
        box.label(text="Selected: %s  (id %d)" % (tag, DATA.tag_to_int[tag]))
    else:
        box.label(text="No surface type selected.", icon="ERROR")


def _draw_material_row(layout, mat, with_assign=True):
    tag = get_material_tag(mat)
    row = layout.row(align=True)
    row.alert = not tag
    row.label(text=mat.name, icon="MATERIAL")
    row.label(text=tag if tag else "UNASSIGNED")
    if with_assign:
        op = row.operator("palette.assign_material", text="Assign")
        op.mat_name = mat.name


# ------------------------------------------------------------------
# Operators
# ------------------------------------------------------------------

class PALETTE_OT_reload_data(Operator):
    bl_idname = "palette.reload_data"
    bl_label = "Reload Data"
    bl_description = "Re-read art_data.json from the project folder"

    def execute(self, context):
        DATA.load(get_prefs().project_root)
        self.report({"INFO"}, DATA.status.replace("\n", " "))
        return {"FINISHED"}


class PALETTE_OT_autodetect_root(Operator):
    bl_idname = "palette.autodetect_root"
    bl_label = "Auto-Detect"
    bl_description = ("Find the project root by walking up from this .blend "
                      "until Assets/Palette/Data/art_data.json is found")

    def execute(self, context):
        start = bpy.data.filepath
        if not start:
            self.report({"WARNING"}, "Save the .blend file first.")
            return {"CANCELLED"}
        cur = os.path.dirname(os.path.abspath(start))
        while True:
            if os.path.isfile(os.path.join(cur, DATA_REL_PATH)):
                get_prefs().project_root = cur  # triggers reload
                self.report({"INFO"}, "Project root: %s" % cur)
                return {"FINISHED"}
            parent = os.path.dirname(cur)
            if parent == cur:
                break
            cur = parent
        self.report({"WARNING"}, "Could not locate art_data.json above this file.")
        return {"CANCELLED"}


class PALETTE_OT_bake_all(Operator):
    bl_idname = "palette.bake_all"
    bl_label = "Bake Vertex Colors"
    bl_description = ("Re-bake the PaletteTag vertex colors for every mesh in "
                      "the scene from its materials' surface types")

    def execute(self, context):
        count = 0
        for obj in iter_scene_mesh_objects(context.scene):
            bake_object_vertex_colors(obj)
            count += 1
        self.report({"INFO"}, "Baked %d mesh(es)." % count)
        return {"FINISHED"}


class PALETTE_OT_search_tag(Operator):
    bl_idname = "palette.search_tag"
    bl_label = "Search Surface Type"
    bl_description = "Type to search across all scenes (e.g. 'beach water')"
    bl_property = "tag_search"

    tag_search: EnumProperty(name="Surface Type", items=search_items)

    def execute(self, context):
        tag = self.tag_search
        if tag not in DATA.tag_to_int:
            return {"CANCELLED"}
        wm = context.window_manager
        sid = tag.split(".", 1)[0]
        # Set scene filter so the surface dropdown shows this scene, then select.
        wm.palette_scene_filter = sid
        wm.palette_surface_filter = tag
        for area in context.screen.areas:
            area.tag_redraw()
        return {"FINISHED"}

    def invoke(self, context, event):
        if not DATA.loaded:
            self.report({"WARNING"}, "Load a project folder first.")
            return {"CANCELLED"}
        context.window_manager.invoke_search_popup(self)
        return {"FINISHED"}


class PALETTE_OT_assign_material(Operator):
    bl_idname = "palette.assign_material"
    bl_label = "Assign to Material"
    bl_description = "Assign the selected surface type to this material"

    mat_name: StringProperty()

    def execute(self, context):
        wm = context.window_manager
        tag = _pending_tag(wm)
        if not tag:
            self.report({"WARNING"}, "Select a surface type first.")
            return {"CANCELLED"}
        mat = bpy.data.materials.get(self.mat_name)
        if mat is None:
            self.report({"WARNING"}, "Material no longer exists.")
            return {"CANCELLED"}
        assign_tag_to_material(context.scene, mat, tag)
        self.report({"INFO"}, "%s -> %s" % (mat.name, tag))
        return {"FINISHED"}


class PALETTE_OT_scan_untagged_materials(Operator):
    bl_idname = "palette.scan_untagged_materials"
    bl_label = "Scan Scene"
    bl_description = ("Collect the materials used by every mesh in the scene and "
                      "list the ones without a surface type")

    def execute(self, context):
        wm = context.window_manager
        wm.palette_untagged.clear()
        count = 0
        for mat in scene_materials(context.scene):
            if not get_material_tag(mat):
                item = wm.palette_untagged.add()
                item.mat_name = mat.name
                item.selected = False
                count += 1
        self.report({"INFO"}, "%d untagged material(s) found." % count)
        return {"FINISHED"}


def _frame_selection(context):
    for area in context.screen.areas:
        if area.type == "VIEW_3D":
            region = next((r for r in area.regions if r.type == "WINDOW"), None)
            if region is None:
                continue
            with context.temp_override(area=area, region=region):
                try:
                    bpy.ops.view3d.view_selected()
                except RuntimeError:
                    pass
            return


class PALETTE_OT_select_material_objects(Operator):
    bl_idname = "palette.select_material_objects"
    bl_label = "Select"
    bl_description = ("Select every object that uses this material and frame it "
                      "in the viewport. Hold Shift to add to the selection")

    mat_name: StringProperty()
    extend: BoolProperty(default=False)

    def invoke(self, context, event):
        self.extend = event.shift
        return self.execute(context)

    def execute(self, context):
        mat = bpy.data.materials.get(self.mat_name)
        if mat is None:
            self.report({"WARNING"}, "Material no longer exists.")
            return {"CANCELLED"}
        if not self.extend:
            for o in context.selected_objects:
                o.select_set(False)
        first = None
        for obj in iter_scene_mesh_objects(context.scene):
            if any(slot.material == mat for slot in obj.material_slots):
                obj.select_set(True)
                first = first or obj
        if first is not None:
            context.view_layer.objects.active = first
            _frame_selection(context)
        else:
            self.report({"WARNING"}, "No objects use '%s'." % self.mat_name)
        return {"FINISHED"}


class PALETTE_OT_assign_untagged_selected(Operator):
    bl_idname = "palette.assign_untagged_selected"
    bl_label = "Assign to Selected"
    bl_description = ("Assign the surface type to every checked material in the "
                      "list and remove them from it")

    def execute(self, context):
        wm = context.window_manager
        tag = _pending_tag(wm)
        if not tag:
            self.report({"WARNING"}, "Select a surface type first.")
            return {"CANCELLED"}

        remove_indices = []
        assigned = 0
        for i, item in enumerate(wm.palette_untagged):
            if not item.selected:
                continue
            mat = bpy.data.materials.get(item.mat_name)
            if mat is not None:
                assign_tag_to_material(context.scene, mat, tag)
                assigned += 1
            remove_indices.append(i)

        if assigned == 0:
            self.report({"WARNING"}, "Tick one or more materials in the list.")
            return {"CANCELLED"}

        for i in reversed(remove_indices):
            wm.palette_untagged.remove(i)

        self.report({"INFO"}, "Assigned %s to %d material(s)." % (tag, assigned))
        return {"FINISHED"}


class PALETTE_OT_open_finder(Operator):
    bl_idname = "palette.open_finder"
    bl_label = "Open Untagged Materials Finder"
    bl_description = ("Open the Untagged Materials Finder and scan the scene's "
                      "meshes for materials without a surface type")

    def execute(self, context):
        context.window_manager.palette_finder_open = True
        bpy.ops.palette.scan_untagged_materials()
        return {"FINISHED"}


class PALETTE_OT_close_finder(Operator):
    bl_idname = "palette.close_finder"
    bl_label = "Close"
    bl_description = "Close the Untagged Materials Finder"

    def execute(self, context):
        context.window_manager.palette_finder_open = False
        return {"FINISHED"}


# ------------------------------------------------------------------
# Sidebar panel ("Palette" tab, like Item / Tool / View)
# ------------------------------------------------------------------

class PALETTE_PT_panel(Panel):
    bl_label = "Palette"
    bl_idname = "PALETTE_PT_panel"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Palette"

    def draw(self, context):
        layout = self.layout
        wm = context.window_manager
        prefs = get_prefs()

        _draw_configuration(layout, prefs)

        layout.separator()
        _draw_selector(layout, wm, context)

        box = layout.box()
        mats = selected_object_materials(context)
        box.label(text="Materials in Selection (%d)" % len(mats),
                  icon="MATERIAL")
        if not mats:
            box.label(text="Select mesh objects to list their materials.",
                      icon="INFO")
        else:
            for mat in mats:
                _draw_material_row(box, mat)

        layout.separator()
        layout.operator("palette.open_finder", icon="VIEWZOOM")


class PALETTE_PT_finder(Panel):
    """On-demand 'window' for bulk-tagging untagged materials.

    Implemented as a togglable panel rather than a popup: Blender popups close on
    the first operator click, which would break the scan -> multi-select ->
    assign workflow. This panel stays open and live until you press Close.
    """

    bl_label = "Untagged Materials Finder"
    bl_idname = "PALETTE_PT_finder"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Palette"
    bl_options = {"DEFAULT_CLOSED"}

    @classmethod
    def poll(cls, context):
        return context.window_manager.palette_finder_open

    def draw(self, context):
        layout = self.layout
        wm = context.window_manager

        row = layout.row(align=True)
        row.operator("palette.scan_untagged_materials", icon="ZOOM_SELECTED")
        row.operator("palette.close_finder", text="", icon="X")

        _draw_selector(layout, wm, context)
        layout.operator("palette.assign_untagged_selected", icon="CHECKMARK")

        box = layout.box()
        n = len(wm.palette_untagged)
        box.label(text="Untagged materials (%d):" % n)
        if n == 0:
            box.label(text="Run Scan Scene to populate.", icon="INFO")
        for item in wm.palette_untagged:
            mat = bpy.data.materials.get(item.mat_name)
            row = box.row(align=True)
            row.prop(item, "selected", text="")
            sub = row.row(align=True)
            sub.alert = True
            sub.label(text=item.mat_name, icon="MATERIAL")
            op = row.operator("palette.select_material_objects",
                              text="Select", icon="RESTRICT_SELECT_OFF")
            op.mat_name = item.mat_name


# ------------------------------------------------------------------
# Property group for the finder list
# ------------------------------------------------------------------

class PALETTE_UntaggedMaterial(PropertyGroup):
    mat_name: StringProperty()
    selected: BoolProperty(name="Select", default=False)


# ------------------------------------------------------------------
# Registration
# ------------------------------------------------------------------

classes = (
    PALETTE_AddonPreferences,
    PALETTE_UntaggedMaterial,
    PALETTE_OT_reload_data,
    PALETTE_OT_autodetect_root,
    PALETTE_OT_bake_all,
    PALETTE_OT_search_tag,
    PALETTE_OT_assign_material,
    PALETTE_OT_scan_untagged_materials,
    PALETTE_OT_select_material_objects,
    PALETTE_OT_assign_untagged_selected,
    PALETTE_OT_open_finder,
    PALETTE_OT_close_finder,
    PALETTE_PT_panel,
    PALETTE_PT_finder,
)


def register():
    for c in classes:
        bpy.utils.register_class(c)

    bpy.types.WindowManager.palette_scene_filter = EnumProperty(
        name="Scene",
        description="Filter surface types by scene ('All' shows every scene)",
        items=scene_items,
    )
    bpy.types.WindowManager.palette_surface_filter = EnumProperty(
        name="Surface Type",
        description="Surface type to assign",
        items=surface_items,
    )
    bpy.types.WindowManager.palette_untagged = CollectionProperty(
        type=PALETTE_UntaggedMaterial,
    )
    bpy.types.WindowManager.palette_finder_open = BoolProperty(
        name="Finder Open",
        default=False,
    )

    # Load data for the currently-saved project root, if any.
    try:
        DATA.load(get_prefs().project_root)
    except Exception:  # noqa: BLE001 - prefs may not be ready at register time
        pass


def unregister():
    del bpy.types.WindowManager.palette_finder_open
    del bpy.types.WindowManager.palette_untagged
    del bpy.types.WindowManager.palette_surface_filter
    del bpy.types.WindowManager.palette_scene_filter

    for c in reversed(classes):
        bpy.utils.unregister_class(c)


if __name__ == "__main__":
    register()
