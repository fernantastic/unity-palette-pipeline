using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TriInspector;
#if UNITY_EDITOR
using UnityEditor;
#endif

using Palette.ArtPipeline;

namespace Palette
{
    [CreateAssetMenu(menuName = "Palette/Model Coloring")]
    public class ModelColoring : ScriptableObject
    {
        [System.Serializable]
        public class MaterialColoringData
        {
            [ReadOnly]
            public string assetMaterialName;

            // Stable shader id for this source material name (see Materials.StableId).
            // Matches the id MaterialPostProcessor stamps onto every object that
            // was imported with this material name.
            [ReadOnly]
            public int materialId;

            public MaterialProperties properties;
        }

        [System.Serializable]
        public class MaterialProperties
        {
            [Range(0,32)] public int colorSlot;
        }

        // The imported model asset (.fbx/.blend) this coloring drives.
        public GameObject model;

        public MaterialColoringData[] materialData;

        [Button("Load materials from the model")]
        void RefreshMaterialsFromModelButton()
        {
#if UNITY_EDITOR
            RefreshMaterialsFromModel();
#endif
        }

#if UNITY_EDITOR
        public void RefreshMaterialsFromModel()
        {
            if (model == null)
            {
                Debug.LogWarning("[Palette] ModelColoring: assign a Model first.", this);
                return;
            }

            var path = AssetDatabase.GetAssetPath(model);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[Palette] ModelColoring: '{path}' is not a model asset.", this);
                return;
            }

            // Start from what we already have so existing art direction is kept.
            var list = materialData != null
                ? new List<MaterialColoringData>(materialData)
                : new List<MaterialColoringData>();

            var existing = new HashSet<string>(
                list.Where(d => d != null && !string.IsNullOrEmpty(d.assetMaterialName))
                    .Select(d => d.assetMaterialName));

            int added = 0;
            foreach (var name in GetSourceMaterialNames(importer))
            {
                if (!existing.Add(name))
                    continue;   // already assigned

                list.Add(new MaterialColoringData
                {
                    assetMaterialName = name,
                    materialId = Materials.StableId(name),
                    properties = new MaterialProperties(),
                });
                added++;
            }

            materialData = list.ToArray();
            EditorUtility.SetDirty(this);
            Debug.Log($"[Palette] ModelColoring: added {added} new material(s), " +
                      $"{materialData.Length} total.", this);
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Unique original material names embedded in the model, read straight
        /// from the importer's serialized source-material identifiers (so it works
        /// regardless of how those materials are later remapped on import).
        /// </summary>
        static IEnumerable<string> GetSourceMaterialNames(ModelImporter importer)
        {
            var seen = new HashSet<string>();
            var so = new SerializedObject(importer);
            var materials = so.FindProperty("m_Materials");
            if (materials == null || !materials.isArray)
                yield break;

            for (int i = 0; i < materials.arraySize; i++)
            {
                var id = materials.GetArrayElementAtIndex(i);
                var nameProp = id.FindPropertyRelative("name");
                if (nameProp == null)
                    continue;

                var name = nameProp.stringValue;
                if (!string.IsNullOrEmpty(name) && seen.Add(name))
                    yield return name;
            }
        }
#endif
    }
}
