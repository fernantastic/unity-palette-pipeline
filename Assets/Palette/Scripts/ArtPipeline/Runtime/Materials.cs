using UnityEngine;

namespace Palette.ArtPipeline
{
    public static class Materials
    {
        // Per-object id sent to the shader, derived from the *incoming* (source)
        // material name. Objects imported with the same-named material share it.
        public static int PROP_MATERIAL_ID = Shader.PropertyToID("_MaterialId");

        /// <summary>
        /// Deterministic 32-bit id for a source material name (FNV-1a). Stable
        /// across runs, platforms and import sessions - unlike string.GetHashCode -
        /// so the same name always maps to the same shader id. Both the import
        /// post-processor and <see cref="ModelColoring"/> use this, which is what
        /// lets them agree on which entry drives which object.
        /// </summary>
        public static int StableId(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
                return 0;

            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;
                uint hash = offset;
                foreach (char c in materialName)
                {
                    hash ^= c;
                    hash *= prime;
                }
                // Keep ids within 24 bits so they round-trip exactly as a float
                // through a MaterialPropertyBlock / shader uniform (floats lose
                // precision above 2^24). Collisions are negligible for the number
                // of distinct materials in a model.
                return (int)(hash & 0x00FFFFFFu);
            }
        }

    }
}
