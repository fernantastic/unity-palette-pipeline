using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TriInspector;

namespace Palette.ArtPipeline
{
    [CreateAssetMenu(menuName = "Palette/Material Database")]
    public class MaterialDatabase : ScriptableObject
    {
        static MaterialDatabase _lastCurrent;
        public static MaterialDatabase current
        {
            get
            {
                if (_lastCurrent != null)
                    return _lastCurrent;

                #if UNITY_EDITOR
                var guids = UnityEditor.AssetDatabase.FindAssets("t:MaterialDatabase");
                if (guids.Length == 0)
                {
                    Debug.LogWarning("[Palette] Can't find Material Database asset, create one.");
                    return null;
                }
                var ed = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialDatabase>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
                if (ed)
                {
                    _lastCurrent = ed;
                    return ed;
                }
                #endif

                Debug.LogWarning("[Palette] Error loading Material Database");
                return null;

            }
        }

        [Button("Refresh Surface Types")]
        void PopulateStraySurfaceTypes()
        {
            if (artDataJson == null)
            {
                Debug.LogError("[Palette] artDataJson is not assigned!");
                return;
            }

            var artData = JsonUtility.FromJson<ArtData>(artDataJson.text);
            if (artData == null || artData.scenes == null)
            {
                Debug.LogError("[Palette] Failed to parse artDataJson or scenes is null!");
                return;
            }

            if (sceneData == null)
            {
                sceneData = System.Array.Empty<SceneMaterialData>();
            }

            foreach (var scene in artData.scenes)
            {
                var matScene = sceneData.FirstOrDefault(s => s.id == scene.id);
                if (matScene == null)
                {
                    matScene = new SceneMaterialData();
                    matScene.id = scene.id;
                    matScene.surfaceTypes = System.Array.Empty<SurfaceTypeMaterialData>();
                    sceneData = sceneData.Append(matScene).ToArray();
                }
                else if (matScene.surfaceTypes == null)
                {
                    matScene.surfaceTypes = System.Array.Empty<SurfaceTypeMaterialData>();
                }

                if (scene.surface_types == null)
                {
                    continue;
                }

                foreach (var surfaceType in scene.surface_types)
                {
                    if (!matScene.surfaceTypes.Any(st => st.id == surfaceType.id))
                    {
                        var matSurfaceType = new SurfaceTypeMaterialData();
                        matSurfaceType.id = surfaceType.id;
                        matSurfaceType.color = Color.white; // default color, can be changed later
                        matScene.surfaceTypes = matScene.surfaceTypes.Append(matSurfaceType).ToArray();
                    }
                }
            }
        }

        [Header("Data")]
        public TextAsset artDataJson;
        
        [System.Serializable]
        public class SceneMaterialData
        {
            [ReadOnly]
            public string id;
            
            public SurfaceTypeMaterialData[] surfaceTypes;
        }

        [System.Serializable]
        public class SurfaceTypeMaterialData
        {
            [ReadOnly]
            public string id;

            public Color color;
            
            public bool useOverrideMaterial = false;
            
            [ShowIf(nameof(useOverrideMaterial))]
            public Material overrideMaterial;
        }
        [Header("Surfaces")]
        public SceneMaterialData[] sceneData;

        
    }
}
