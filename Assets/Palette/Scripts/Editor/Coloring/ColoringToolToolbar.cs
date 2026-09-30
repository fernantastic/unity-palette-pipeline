using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;

namespace Palette
{
    [EditorTool("Palette Coloring")]
    public class ColoringToolToolbar : EditorTool
    {
        GUIContent _icon;

        void OnEnable()
        {
            // https://github.com/halak/unity-editor-icons
            _icon = new GUIContent(
                EditorGUIUtility.IconContent("ClothInspector.PaintTool").image,
                "Palette Coloring"
            );
        }

        public override GUIContent toolbarIcon => _icon;

        // Drive the shared preview lifecycle from the tool's active state.
        public override void OnActivated() => ColorSlotAssigner.SetActive(true);
        public override void OnWillBeDeactivated() => ColorSlotAssigner.SetActive(false);

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView sv)
                return;

            DrawSlotBar(sv, out var barRect);

            // A click on the toolbar must not paint the scene behind it. If a slot
            // button was clicked it already consumed the event; otherwise consume
            // background clicks inside the bar.
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && barRect.Contains(e.mousePosition))
                e.Use();

            // Reuse the exact hover / preview / click-to-assign logic.
            ColorSlotAssigner.HandleSceneGUI(sv);
        }

        // ------------------------------------------------------------------
        // Slot bar
        // ------------------------------------------------------------------

        void DrawSlotBar(SceneView sv, out Rect barRect)
        {
            var env = Object.FindFirstObjectByType<Environment>();
            int count = env != null ? env.SlotCount : 0;

            const float bw = 58f, bh = 34f, spacing = 2f, pad = 10f;
            float width = count > 0
                ? pad * 2f + count * (bw + spacing)
                : 280f;
            barRect = new Rect((sv.position.width - width) * 0.5f, 6f, width, bh + 14f);

            Handles.BeginGUI();
            GUILayout.BeginArea(barRect, GUI.skin.window);
            GUILayout.BeginHorizontal();

            if (count == 0)
            {
                GUILayout.Label("No Palette - add an Environment with a Palette.");
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    if (i > 0)
                        GUILayout.Space(spacing);

                    bool selected = i == ColorSlotAssigner.CurrentSlot;
                    Color color = env.TryGetSlotColor(i, out var c) ? c : Color.gray;
                    color.a = 1f;
                    var label = env.TryGetSlotLabel(i, out var l) ? l : "";

                    // Reserve the layout space; we paint + handle clicks ourselves.
                    // We don't use GUILayout.Button because it only fires when the
                    // press STARTED on it - so a drag that releases over the swatch
                    // wouldn't count. We treat any MouseUp over the swatch as a click.
                    Rect r = GUILayoutUtility.GetRect(bw, bh, GUILayout.Width(bw), GUILayout.Height(bh));
                    r.y -= 15;

                    if (selected)
                    {
                        EditorGUI.DrawRect(r, Color.white); // outline
                        EditorGUI.DrawRect(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), color);
                    }
                    else
                    {
                        EditorGUI.DrawRect(r, color);
                    }

                    // Index label in a contrasting colour.
                    SwatchLabel.normal.textColor = Luminance(color) > 0.5f ? Color.black : Color.white;
                    if (!string.IsNullOrEmpty(l))
                        GUI.Label(r, l, SwatchLabel);
                    else
                        GUI.Label(r, i.ToString(), SwatchLabel);

                    var e = Event.current;
                    if (e.button == 0 && r.Contains(e.mousePosition) &&
                        (e.type == EventType.MouseUp || e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
                    {
                        if (e.type == EventType.MouseUp)
                            ColorSlotAssigner.SetSlot(i);
                        e.Use(); // consume down/drag/up over the swatch so the scene isn't painted
                    }
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        static float Luminance(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        static GUIStyle _swatchLabel;
        static GUIStyle SwatchLabel =>
            _swatchLabel ??= new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
            };
    }
}
