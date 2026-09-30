using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;
using Palette.ArtPipeline;

namespace Palette
{
    // Plain Unity inspector for Palette so the ColorSlot PropertyDrawer below is
    // actually used (otherwise TriInspector's fallback editor renders ColorSlot
    // generically and never invokes the drawer).
    [CustomEditor(typeof(Palette))]
    public class PaletteEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
        }
    }

    // Adds an "Assign" button to every Palette.ColorSlots.ColorSlot. Pressing it
    // enters a scene-view picking mode: hover a renderer/submesh that has a
    // PaletteMaterialId and click to set that material's ModelColoring entry
    // colorSlot to this slot's index.
    [CustomPropertyDrawer(typeof(Palette.ColorSlots.ColorSlot))]
    public class ColorSlotDrawer : PropertyDrawer
    {
        const float ButtonHeight = 20f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float h = EditorGUIUtility.singleLineHeight; // foldout
            if (property.isExpanded)
            {
                foreach (var child in Children(property))
                    h += EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing;
                h += ButtonHeight + 4f;
            }
            return h;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                float y = line.yMax + EditorGUIUtility.standardVerticalSpacing;

                foreach (var child in Children(property))
                {
                    float ch = EditorGUI.GetPropertyHeight(child, true);
                    EditorGUI.PropertyField(new Rect(position.x, y, position.width, ch), child, true);
                    y += ch + EditorGUIUtility.standardVerticalSpacing;
                }

                int slotIndex = GetSlotIndex(property.propertyPath);
                var btn = EditorGUI.IndentedRect(new Rect(position.x, y, position.width, ButtonHeight));
                DrawAssignButton(btn, property.serializedObject.targetObject, slotIndex);

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        // Iterates the immediate child properties of a serialized class property
        // WITHOUT re-entering this drawer (we never PropertyField the slot itself).
        static System.Collections.Generic.IEnumerable<SerializedProperty> Children(SerializedProperty property)
        {
            var child = property.Copy();
            var end = property.GetEndProperty();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                yield return child.Copy();
            }
        }

        static void DrawAssignButton(Rect rect, Object owner, int slotIndex)
        {
            if (slotIndex < 0)
            {
                EditorGUI.LabelField(rect, "(slot index unknown)");
                return;
            }

            bool active = ColorSlotAssigner.IsPaintingSlot(slotIndex);
            var prev = GUI.backgroundColor;
            // GUI.backgroundColor = active ? new Color(1f, 0.78f, 0.2f) : prev;

            string text = active
                ? $"● Painting slot {slotIndex} — Esc / right-click in scene to stop"
                : $"Paint meshes with this slot ({slotIndex})";

            // if (GUI.Button(rect, text))
            // {
            //     if (active)
            //     {
            //         Tools.current = Tool.Move; // exit the coloring tool
            //     }
            //     else
            //     {
            //         // Same painting flow as the toolbar: activate the tool + pick the slot.
            //         ToolManager.SetActiveTool<ColoringToolToolbar>();
            //         ColorSlotAssigner.SetSlot(slotIndex);
            //     }
            // }

            GUI.backgroundColor = prev;
        }

        // Maps the serialized path of a ColorSlot to its palette index, matching
        // Palette.ColorSlots.TryGetColor: KeyColor=0, SecondaryColor=1, slots[i]=i+2.
        static readonly Regex SlotElementRegex = new Regex(@"slots\.Array\.data\[(\d+)\]$");

        static int GetSlotIndex(string path)
        {
            if (path.EndsWith("KeyColor")) return 0;
            if (path.EndsWith("SecondaryColor")) return 1;
            var m = SlotElementRegex.Match(path);
            if (m.Success) return int.Parse(m.Groups[1].Value) + 2;
            return -1;
        }
    }

    // Shared painting logic for the coloring tool. The ColoringToolToolbar drives
    // it (calls HandleSceneGUI from OnToolGUI); the Palette inspector buttons and
    // the toolbar's slot buttons just pick the slot. Static so state survives
    // drawer/tool rebuilds.
    static class ColorSlotAssigner
    {
        static int _slot;
        static bool _active;

        // Hover resolution, refreshed only on mouse interaction (never during
        // Repaint - see HandleSceneGUI). The click commits exactly this, so what
        // you see in the overlay is what gets assigned.
        static GameObject _hover;
        static int _hoverSubmesh = -1;
        static int _hoverMaterialId = -1;
        // The asset is held by reference; the per-material entry is always looked
        // up from it at commit time (never cached - the materialData objects get
        // replaced by 'Load materials from the model', undo, reimport, etc.).
        static ModelColoring _hoverColoring;

        public static int CurrentSlot => _slot;
        public static bool IsPaintingSlot(int slot) => _active && _slot == slot;

        // Called by the tool's OnActivated / OnWillBeDeactivated.
        public static void SetActive(bool active)
        {
            _active = active;
            if (!active)
                ClearHover();
            PushPreview();
            SceneView.RepaintAll();
        }

        // Called by the toolbar / inspector slot buttons.
        public static void SetSlot(int slot)
        {
            _slot = Mathf.Max(0, slot);
            PushPreview();
            SceneView.RepaintAll();
        }

        // Leaving the tool deactivates it (-> OnWillBeDeactivated -> SetActive(false)).
        static void Exit() => Tools.current = Tool.Move;

        static void ClearHover()
        {
            _hover = null;
            _hoverSubmesh = -1;
            _hoverMaterialId = -1;
            _hoverColoring = null;
        }

        // Push the hover preview to the shader (via Environment globals).
        static void PushPreview()
        {
            Environment.EDITOR_COLORING_TOOL_ACTIVE = _active;
            Environment.EDITOR_COLORING_TOOL_PREVIEW_SLOT = _slot;
            Environment.EDITOR_COLORING_TOOL_PREVIEW_MATERIAL_ID = _hoverMaterialId;
            Environment.ApplyColoringToolGlobals();
        }

        public static void HandleSceneGUI(SceneView sv)
        {
            if (!_active) return;

            var e = Event.current;

            // Own the scene so clicks don't change the selection.
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            // IMPORTANT: PickGameObject renders the scene to pick. Calling it during
            // Repaint nests a render inside the GUI frame -> d3d12 "finalizing
            // rendering when not inside a frame" + recursive-OnGUI errors. So only
            // pick on mouse interaction, resolve+cache the full chain, and the
            // overlay/click read from the cache.
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                var go = HandleUtility.PickGameObject(e.mousePosition, out int submesh);
                ResolveHover(go, submesh);
                PushPreview();
                sv.Repaint();
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                var go = HandleUtility.PickGameObject(e.mousePosition, out int submesh);
                ResolveHover(go, submesh);
                PushPreview();
                Commit();
                e.Use();
            }
        }

        static void ResolveHover(GameObject go, int submesh)
        {
            ClearHover();
            _hover = go;
            _hoverSubmesh = submesh;
            if (go == null)
                return;

            var tag = go.GetComponent<PaletteMaterialId>();
            if (tag == null || tag.materialIds == null || submesh < 0 || submesh >= tag.materialIds.Length)
                return;
            _hoverMaterialId = tag.materialIds[submesh];

            var applier = go.GetComponentInParent<ModelColoringApplier>();
            if (applier == null || applier.coloring == null)
                return;
            _hoverColoring = applier.coloring;
        }

        static void Commit()
        {
            if (_hover == null)
                return;
            if (_hoverMaterialId < 0)
            {
                Debug.LogWarning($"[Palette] '{_hover.name}' has no material id under the cursor.", _hover);
                return;
            }
            if (_hoverColoring == null)
            {
                Debug.LogWarning($"[Palette] No ModelColoringApplier/coloring above '{_hover.name}'.", _hover);
                return;
            }

            // Look the entry up from the live asset right now - never a cached one.
            var entry = FindEntry(_hoverColoring, _hoverMaterialId);
            if (entry == null)
            {
                Debug.LogWarning(
                    $"[Palette] No coloring entry for material id {_hoverMaterialId} in '{_hoverColoring.name}'. " +
                    "Run 'Load materials from the model' on the ModelColoring.", _hoverColoring);
                return;
            }

            if (entry.properties == null)
                entry.properties = new ModelColoring.MaterialProperties();

            Undo.RecordObject(_hoverColoring, "Assign Colour Slot");
            entry.properties.colorSlot = _slot;
            EditorUtility.SetDirty(_hoverColoring);
        }

        static ModelColoring.MaterialColoringData FindEntry(ModelColoring coloring, int materialId)
        {
            if (coloring == null || coloring.materialData == null)
                return null;

            foreach (var d in coloring.materialData)
            {
                if (d != null && d.materialId == materialId)
                    return d;
            }
            return null;
        }
    }
}
