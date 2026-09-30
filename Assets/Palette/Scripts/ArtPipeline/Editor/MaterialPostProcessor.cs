using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;

namespace Palette.ArtPipeline
{
    public class MaterialPostProcessor : AssetPostprocessor
    {
        const string Root = "Assets/Palette";
        static readonly string ColoringFolder = $"{Root}/ScriptableObjects/Models/Imported";

        // Only models living under one of these folders go through the
        // material-id / coloring pipeline. Everything else imports untouched.
        static readonly string[] SceneModelsFolders =
        {
            $"{Root}/Models/PipelineProcessed",
        };

        enum ImportType
        {
            None,
            SceneModel,
        }

        // Single source of truth for "does this asset belong to our pipeline".
        // Static so the static OnPostprocessAllAssets can use the same decision.
        static ImportType GetImportType(string path)
        {
            if (string.IsNullOrEmpty(path))
                return ImportType.None;

            foreach (var folder in SceneModelsFolders)
            {
                // Match a real folder boundary so "PipelineProcessed" doesn't also
                // catch "PipelineProcessedOld".
                if (path.StartsWith(folder + "/", StringComparison.Ordinal))
                    return ImportType.SceneModel;
            }
            return ImportType.None;
        }

        static bool IsSceneModel(string path) =>
            GetImportType(path) == ImportType.SceneModel;

        static bool IsModelAsset(string path) =>
            AssetImporter.GetAtPath(path) is ModelImporter;

        // Per-import accumulation: each renderer -> its source material ids/names
        // in submesh order (OnAssignMaterialModel is called per submesh).
        readonly Dictionary<Renderer, List<(int id, string name)>> _perRenderer =
            new Dictionary<Renderer, List<(int id, string name)>>();

        // ------------------------------------------------------------------
        // Per-material: remap to the shared material and capture the id.
        // ------------------------------------------------------------------

        void OnPreprocessModel()
        {
            if (!IsSceneModel(assetPath))
                return; // not our pipeline - leave import settings untouched

            var importer = (ModelImporter)assetImporter;
            importer.importLights = false;
        }

        Material OnAssignMaterialModel(Material material, Renderer renderer)
        {
            if (!IsSceneModel(assetPath))
                return null; // not our pipeline - null means "don't remap, keep default"

            return ProcessSceneModelMaterial(material, renderer);
        }

        Material ProcessSceneModelMaterial(Material material, Renderer renderer)
        {
            if (EditorReferences.current == null)
                return null; // no persistent replacement available - keep default

            var importer = (ModelImporter)assetImporter;
            var projectMaterial = EditorReferences.current.materialSolid;

            // Remap the incoming source material to our shared project material.
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(material), projectMaterial);

            int materialId = Materials.StableId(material.name);

            if (renderer != null)
            {
                if (!_perRenderer.TryGetValue(renderer, out var list))
                {
                    list = new List<(int, string)>();
                    _perRenderer[renderer] = list;
                }
                list.Add((materialId, material.name));
            }

            return projectMaterial;
        }

        // ------------------------------------------------------------------
        // Post model build: persist ids, add applier, link coloring.
        // ------------------------------------------------------------------

        void OnPostprocessModel(GameObject root)
        {
            if (!IsSceneModel(assetPath))
                return; // not our pipeline - don't add tags/applier

            foreach (var kv in _perRenderer)
            {
                var renderer = kv.Key;
                if (renderer == null)
                    continue;

                var tag = renderer.GetComponent<PaletteMaterialId>();
                if (tag == null)
                    tag = renderer.gameObject.AddComponent<PaletteMaterialId>();

                tag.materialIds = kv.Value.Select(e => e.id).ToArray();
                tag.sourceMaterialNames = kv.Value.Select(e => e.name).ToArray();

                // Apply right after stamping (also runs on every reimport, since
                // OnPostprocessModel runs each import).
                tag.Apply();
            }

            var applier = root.GetComponent<ModelColoringApplier>();
            if (applier == null)
                applier = root.AddComponent<ModelColoringApplier>();

            // Link an existing coloring for this model, if any. On first import
            // there isn't one yet - OnPostprocessAllAssets creates it afterward
            // and links it directly (creating assets isn't safe from here).
            var coloring = FindColoringForModel(assetPath);
            if (coloring != null)
                applier.coloring = coloring;
        }

        // ------------------------------------------------------------------
        // After the import batch: create the coloring asset (creation isn't safe
        // inside the model callback), link it to the applier, and handle models
        // moved into the folder.
        // ------------------------------------------------------------------

        static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                           string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (!IsSceneModel(path) || !IsModelAsset(path))
                    continue;

                var coloring = FindColoringForModel(path);
                if (coloring == null)
                    coloring = CreateColoringForModel(path);

                // Link right away instead of waiting on a forced reimport - a
                // delayCall scheduled here can be dropped by a domain reload
                // (e.g. scripts recompiling), silently leaving it unlinked.
                LinkColoringToApplier(path, coloring);
            }

            // A model dragged/moved *into* the pipeline folder isn't re-imported by
            // Unity, so the per-material/model callbacks never run for it. Force a
            // reimport so the whole pipeline applies (which then creates coloring).
            for (int i = 0; i < moved.Length; i++)
            {
                var newPath = moved[i];
                var oldPath = i < movedFrom.Length ? movedFrom[i] : null;

                if (IsSceneModel(newPath) && !IsSceneModel(oldPath) && IsModelAsset(newPath))
                {
                    var modelPath = newPath;
                    EditorApplication.delayCall += () =>
                        AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
                }
            }
        }

        static ModelColoring FindColoringForModel(string modelPath)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ModelColoring"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var mc = AssetDatabase.LoadAssetAtPath<ModelColoring>(p);
                if (mc != null && mc.model != null &&
                    AssetDatabase.GetAssetPath(mc.model) == modelPath)
                {
                    return mc;
                }
            }
            return null;
        }

        static ModelColoring CreateColoringForModel(string modelPath)
        {
            EnsureFolder(ColoringFolder);

            var coloring = ScriptableObject.CreateInstance<ModelColoring>();
            coloring.model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            coloring.RefreshMaterialsFromModel();

            var modelName = Path.GetFileNameWithoutExtension(modelPath);
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{ColoringFolder}/{modelName} Coloring.asset");

            AssetDatabase.CreateAsset(coloring, assetPath);
            AssetDatabase.SaveAssets();
            return coloring;
        }

        // Assign directly on the already-imported model asset rather than
        // relying on OnPostprocessModel running again on a future reimport.
        static void LinkColoringToApplier(string modelPath, ModelColoring coloring)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var applier = root != null ? root.GetComponent<ModelColoringApplier>() : null;
            if (applier == null || applier.coloring == coloring)
                return;

            applier.coloring = coloring;
            EditorUtility.SetDirty(root);
            AssetDatabase.SaveAssets();
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            var parts = folder.Split('/');
            var current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
