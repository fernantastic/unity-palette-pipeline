
using UnityEngine;
using UnityEditor;

namespace Palette
{
    [CreateAssetMenu(menuName = "Palette/Editor References")]
    public class EditorReferences : ScriptableObject
    {
        static EditorReferences _lastCurrent;
        public static EditorReferences current
        {
            get
            {
                if (_lastCurrent != null)
                    return _lastCurrent;

                var guids = AssetDatabase.FindAssets("t:EditorReferences");
                if (guids.Length == 0)
                {
                    Debug.LogWarning("[Palette] Can't find Editor References asset, create one.");
                    return null;
                }
                var ed = AssetDatabase.LoadAssetAtPath<EditorReferences>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (ed)
                {
                    _lastCurrent = ed;
                    return ed;
                }

                Debug.LogWarning("[Palette] Error loading Editor References");
                return null;

            }
        }
        public Material materialSolid;
        public Material materialGlass;


        public ArtPipeline.MaterialDatabase surfaceDatabase;
        public Palette defaultPalette;


        
    }
}
