using UnityEngine;

namespace Palette.ArtPipeline
{
    /// <summary>
    /// Persistent, serialized link from a renderer back to its source materials,
    /// stamped onto each imported renderer by <c>MaterialPostProcessor</c>.
    ///
    /// A <see cref="MaterialPropertyBlock"/> set during import is NOT serialized,
    /// so the import-time <c>_MaterialId</c> is gone on an instantiated/built
    /// renderer. This component carries the id (per material slot / submesh) into
    /// the prefab and re-stamps it onto its own renderer's property block in
    /// <see cref="Apply"/> - which runs when the component is enabled, when it is
    /// validated in the editor, and right after the importer adds/refreshes it.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class PaletteMaterialId : MonoBehaviour
    {
        // One entry per material slot / submesh, in slot order.
        public int[] materialIds;

        // Parallel to materialIds; the original (source) material names, kept for
        // inspector readability and debugging.
        public string[] sourceMaterialNames;

        /// <summary>
        /// Stamp the material id of each submesh into its own per-submesh property
        /// block. Id only - no colors. The shared material is untouched, so batching
        /// is preserved.
        /// </summary>
        public void Apply()
        {
            if (materialIds == null || materialIds.Length == 0)
                return;

            var renderer = GetComponent<Renderer>();
            if (renderer == null)
                return;

            // Surface bad ids (negative, out of range, non-round-tripping) but keep
            // going so a single bad value doesn't blank the whole renderer.
            if (!MaterialIdValidation.ValidateIds(materialIds, out var error))
                Debug.LogError("[Palette] " + error, this);

            int submeshCount = renderer.sharedMaterials != null
                ? renderer.sharedMaterials.Length
                : 0;

            for (int submesh = 0; submesh < materialIds.Length; submesh++)
            {
                // A FRESH block per submesh. Reusing one instance and Get/Set-ing it
                // per slot lets the last submesh's value bleed onto the others.
                var block = new MaterialPropertyBlock();
                bool perSubmesh = submesh < submeshCount;

                // Preserve any existing per-submesh properties before adding ours.
                if (perSubmesh)
                    renderer.GetPropertyBlock(block, submesh);
                else
                    renderer.GetPropertyBlock(block);

                // Sent as a float (the shader reads materialId as a float); ids are
                // kept within 24 bits by Materials.StableId so this is exact.
                block.SetFloat(Materials.PROP_MATERIAL_ID,
                               MaterialIdValidation.EncodeForShader(materialIds[submesh]));

                if (perSubmesh)
                    renderer.SetPropertyBlock(block, submesh);
                else
                    renderer.SetPropertyBlock(block);
            }
        }

        void OnEnable()
        {
            Apply();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Can't touch renderers/property blocks during OnValidate - defer.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                    Apply();
            };
        }
#endif
    }
}
