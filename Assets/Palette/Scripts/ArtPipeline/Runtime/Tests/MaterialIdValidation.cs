using System.Collections.Generic;
using System.Text;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Palette.ArtPipeline
{
    /// <summary>
    /// Robustness checks for the material-id transport. The id makes this trip:
    ///
    ///   StableId hash (int, 24-bit)
    ///     -> float in a MaterialPropertyBlock  (ModelColoringApplier)
    ///     -> shader reads it as a float
    ///     -> shader recovers the int via (uint)(materialId + 0.5)  (PaletteMaterialTag.cginc)
    ///
    /// Every step must be lossless or colours break. These helpers mirror the
    /// exact conversions the C# and shader sides do, so we can verify the whole
    /// chain from C# (we can't run the shader here, but we can reproduce its math).
    ///
    /// All checks are non-throwing: they return false + a human-readable report so
    /// callers can decide whether to log, fail a build, etc.
    /// </summary>
    public static class MaterialIdValidation
    {
        // 24-bit: the largest id that is exactly representable as a float. Matches
        // the mask in Materials.StableId.
        public const int MaxId = 0x00FFFFFF;

        /// <summary>The float we put into the property block (see ModelColoringApplier).</summary>
        public static float EncodeForShader(int id) => id;

        /// <summary>
        /// Mirrors PaletteMaterialTag.cginc: (int)round(materialId). round() not
        /// +0.5 - the id is an exact integer float, but +0.5 ties-to-even at single
        /// precision for ids >= 2^23 and decodes odd ids to id+1.
        /// </summary>
        public static int DecodeAsShader(float shaderValue) => (int)System.MathF.Round(shaderValue);

        /// <summary>
        /// Full validity + round-trip check for a single id. False (with a reason)
        /// if it is negative, out of range, non-finite as a shader value, or does
        /// not survive the float round-trip.
        /// </summary>
        public static bool TryValidateId(int id, out string error)
        {
            float encoded = EncodeForShader(id);

            if (float.IsNaN(encoded) || float.IsInfinity(encoded))
            {
                error = $"id {id} encodes to a non-finite shader value";
                return false;
            }
            if (id < 0)
            {
                error = $"id {id} is negative";
                return false;
            }
            if (id > MaxId)
            {
                error = $"id {id} exceeds the 24-bit max {MaxId} (would lose float precision)";
                return false;
            }

            int decoded = DecodeAsShader(encoded);
            if (decoded != id)
            {
                error = $"id {id} does not round-trip through the shader (got {decoded})";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// The hash -> int -> float -> int transfer is lossless. Uses boundary
        /// values plus real StableId hashes. Returns false + report on failure.
        /// </summary>
        public static bool RunTransferTest(out string error)
        {
            var samples = new List<int>
            {
                0, 1, 2, 255, 256, 65535, 65536, MaxId / 2, MaxId - 1, MaxId,
            };

            // Real hashes - exactly what the import pipeline produces.
            foreach (var name in new[]
                     { "window", "Glass", "building.wall", "car_metal",
                       "", " ", "A", "a", "Material.001", "Solid" })
            {
                samples.Add(Materials.StableId(name));
            }

            return ValidateIds(samples, out error);
        }

        /// <summary>
        /// The given ids are all valid (no negatives, out-of-range, NaN/Inf, or
        /// non-round-tripping values). Returns false + an aggregated report listing
        /// every offender.
        /// </summary>
        public static bool ValidateIds(IReadOnlyList<int> ids, out string error)
        {
            error = null;
            if (ids == null)
                return true;

            List<string> errors = null;
            for (int i = 0; i < ids.Count; i++)
            {
                if (!TryValidateId(ids[i], out var e))
                {
                    errors ??= new List<string>();
                    errors.Add(e);
                }
            }

            if (errors == null)
                return true;

            error = "invalid material id(s):\n - " + string.Join("\n - ", errors);
            return false;
        }

        #if UNITY_EDITOR
        [MenuItem("Palette/Art Pipeline/Tests/Material ID Roundtrip")]
        public static void RunEditorPipelineTest()
        {
            if (RunPipelineTests(out var report))
                UnityEngine.Debug.Log("[Palette] Material ID validation: all tests passed.\n" + report);
            else
                UnityEngine.Debug.LogError("[Palette] Material ID validation: some tests failed.\n" + report);
        }
        #endif

        /// <summary>
        /// The full pipeline test: the transfer round-trip, that the validator
        /// actually rejects bad ids, and that StableId stays in range and is
        /// deterministic. Returns false + a per-section report. 
        /// </summary>
        public static bool RunPipelineTests(out string report)
        {
            var sb = new StringBuilder();
            bool ok = true;

            // 1. Transfer round-trip (hash -> int -> float -> int).
            if (RunTransferTest(out var transferError))
            {
                sb.AppendLine("[pass] transfer round-trip");
            }
            else
            {
                ok = false;
                sb.AppendLine("[FAIL] transfer round-trip");
                sb.AppendLine("       " + transferError.Replace("\n", "\n       "));
            }

            // 2. The validator must REJECT known-bad ids (regression guard for the
            //    24-bit mask / sign handling).
            var badIds = new[] { -1, MaxId + 1, int.MaxValue, int.MinValue };
            var leaked = new List<int>();
            foreach (var bad in badIds)
                if (TryValidateId(bad, out _))
                    leaked.Add(bad);

            if (leaked.Count == 0)
            {
                sb.AppendLine("[pass] validator rejects bad ids");
            }
            else
            {
                ok = false;
                sb.AppendLine("[FAIL] validator accepted bad ids: " + string.Join(", ", leaked));
            }

            // 3. StableId is in [0, MaxId] and deterministic.
            var rangeErrors = new List<string>();
            foreach (var name in new[]
                     { "window", "Glass", "building.wall", "car_metal",
                       "Material.001", "Solid", "beach.water", "vehicle.metal" })
            {
                int a = Materials.StableId(name);
                int b = Materials.StableId(name);
                if (a != b)
                    rangeErrors.Add($"'{name}' not deterministic ({a} vs {b})");
                if (a < 0 || a > MaxId)
                    rangeErrors.Add($"'{name}' -> {a} out of [0, {MaxId}]");
            }

            if (rangeErrors.Count == 0)
            {
                sb.AppendLine("[pass] StableId range + determinism");
            }
            else
            {
                ok = false;
                sb.AppendLine("[FAIL] StableId range + determinism");
                foreach (var e in rangeErrors)
                    sb.AppendLine("       - " + e);
            }

            report = sb.ToString().TrimEnd();
            return ok;
        }
    }
}
