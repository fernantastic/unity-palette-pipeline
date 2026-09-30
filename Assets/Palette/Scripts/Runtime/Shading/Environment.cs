using UnityEngine;

namespace Palette
{
    [ExecuteAlways]
    public class Environment : MonoBehaviour
    {
        [SerializeField] private Light _sun;

        public Light Sun => _sun;

        [Range(0,1)] public float TimeOfDay = .92f;
        [Range(0,1)] public float SunRotation = 0.0f;

        public Palette palette;

        const int MAX_SLOTS = 32;
        Vector4[] colorSlots = new Vector4[MAX_SLOTS];

        static class ShaderIDs
        {
            public static class Lighting
            {
                public static readonly int SpecularColor =          Shader.PropertyToID("_Lighting_SpecularColor");
                public static readonly int SpecularCoverage =       Shader.PropertyToID("_Lighting_SpecularCoverage");
                public static readonly int SpecularSharpness =      Shader.PropertyToID("_Lighting_SpecularSharpness");
                public static readonly int ShadeColor =             Shader.PropertyToID("_Lighting_ShadeColor");
                public static readonly int ShadeCoverage =          Shader.PropertyToID("_Lighting_ShadeCoverage");
                public static readonly int ShadeSharpness =         Shader.PropertyToID("_Lighting_ShadeSharpness");
                public static readonly int ShadowsOpacity =         Shader.PropertyToID("_Lighting_ShadowsOpacity");
                public static readonly int ShadowsTint =            Shader.PropertyToID("_Lighting_ShadowsTint");
                public static readonly int ShadowsReplacementColor =Shader.PropertyToID("_Lighting_ShadowsReplacementColor");
                public static readonly int LitTint =                Shader.PropertyToID("_Lighting_LitTint");
                public static readonly int LitTintAmount =          Shader.PropertyToID("_Lighting_LitTintAmount");
            }
            public static class Fog
            {
                public static readonly int Color =              Shader.PropertyToID("_Fog_Color");
                public static readonly int Density =            Shader.PropertyToID("_Fog_Density");
                public static readonly int HeightFogOpacity =   Shader.PropertyToID("_Fog_HeightFogOpacity");
                public static readonly int HeightFogHeight =    Shader.PropertyToID("_Fog_HeightFogHeight");
            }
            public static class Sky
            {
                public static readonly int GroundColor =    Shader.PropertyToID("_Sky_GroundColor");
                public static readonly int HorizonColor =   Shader.PropertyToID("_Sky_HorizonColor");
                public static readonly int ZenithColor =    Shader.PropertyToID("_Sky_ZenithColor");
                public static readonly int SunColor =       Shader.PropertyToID("_Sky_SunColor");
                public static readonly int SunSize =        Shader.PropertyToID("_Sky_SunSize");
                public static readonly int NighttimeTint =  Shader.PropertyToID("_Sky_NighttimeTint");
            }
            public static class Texturing
            {
                public static readonly int DirtMapAmount =                  Shader.PropertyToID("_Texturing_DirtMap_Amount");
                public static readonly int DirtMapColor =                   Shader.PropertyToID("_Texturing_DirtMap_Color");
                public static readonly int DirtMapEnabled =                 Shader.PropertyToID("_Texturing_DirtMap_Enabled");
                public static readonly int DirtMapTexture =                 Shader.PropertyToID("_Texturing_DirtMap_Texture");
                public static readonly int DirtMapSize =                    Shader.PropertyToID("_Texturing_DirtMap_Size");

            }

        }

        void Update()
        {
            Sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(0f, 180f, TimeOfDay), SunRotation * 360f, 0f);

            ApplyGlobalLightingVars();

            ApplySceneObjectPaletteVars();
        }

        void ApplyGlobalLightingVars()
        {
            if (palette == null)
            {
                return;
            }
            // See PaletteLighting.subgraph
            // Note: SetGlobalColor doesn't convert to linear automatically
            Shader.SetGlobalColor(ShaderIDs.Lighting.SpecularColor, palette.shading.SpecularColor.linear);
            Shader.SetGlobalFloat(ShaderIDs.Lighting.SpecularCoverage, palette.shading.SpecularCoverage);
            Shader.SetGlobalFloat(ShaderIDs.Lighting.SpecularSharpness, palette.shading.SpecularSharpness);
            Shader.SetGlobalColor(ShaderIDs.Lighting.ShadeColor, palette.shading.ShadeColor.linear);
            Shader.SetGlobalFloat(ShaderIDs.Lighting.ShadeCoverage, palette.shading.ShadeCoverage);
            Shader.SetGlobalFloat(ShaderIDs.Lighting.ShadeSharpness, palette.shading.ShadeSharpness);
            Shader.SetGlobalFloat(ShaderIDs.Lighting.ShadowsOpacity, palette.shading.ShadowsOpacity);
            Shader.SetGlobalColor(ShaderIDs.Lighting.ShadowsTint, palette.shading.ShadowsTint.linear);
            Shader.SetGlobalColor(ShaderIDs.Lighting.ShadowsReplacementColor, palette.shading.ShadowsReplacementColor.linear);
            Shader.SetGlobalVector(ShaderIDs.Lighting.LitTint, palette.shading.LitTint.linear);

            Shader.SetGlobalColor(ShaderIDs.Fog.Color, palette.skyAndFog.FogColor.linear);
            Shader.SetGlobalFloat(ShaderIDs.Fog.Density, palette.skyAndFog.FogDensity);
            Shader.SetGlobalFloat(ShaderIDs.Fog.HeightFogOpacity, palette.skyAndFog.HeightFogOpacity);
            Shader.SetGlobalFloat(ShaderIDs.Fog.HeightFogHeight, palette.skyAndFog.HeightFogHeight);

            Shader.SetGlobalColor(ShaderIDs.Sky.GroundColor, palette.skyAndFog.SkyGroundColor.linear);
            Shader.SetGlobalColor(ShaderIDs.Sky.HorizonColor, palette.skyAndFog.SkyHorizonColor.linear);
            Shader.SetGlobalColor(ShaderIDs.Sky.ZenithColor, palette.skyAndFog.SkyZenithColor.linear);
            Shader.SetGlobalColor(ShaderIDs.Sky.SunColor, palette.skyAndFog.SkySunColor.linear);
            Shader.SetGlobalFloat(ShaderIDs.Sky.SunSize, palette.skyAndFog.SkySunSize);
            Shader.SetGlobalColor(ShaderIDs.Sky.NighttimeTint, palette.skyAndFog.NighttimeTint.linear);

            Shader.SetGlobalFloat(ShaderIDs.Texturing.DirtMapEnabled, palette.texturing.UseDirtmap ? 1f : 0f);
            Shader.SetGlobalFloat(ShaderIDs.Texturing.DirtMapAmount, palette.texturing.DirtmapOpacity);
            Shader.SetGlobalColor(ShaderIDs.Texturing.DirtMapColor, palette.texturing.DirtmapColor.linear);
            Shader.SetGlobalTexture(ShaderIDs.Texturing.DirtMapTexture, palette.texturing.DirtmapTexture);
            Shader.SetGlobalFloat(ShaderIDs.Texturing.DirtMapSize, palette.texturing.DirtmapSize);

        }

        const int MAX_MATERIAL_IDS = 256;
        
        GraphicsBuffer _materialSlotBuffer;
        readonly Vector2Int[] _slotPairs = new Vector2Int[MAX_MATERIAL_IDS];

        static readonly int _PaletteId           = Shader.PropertyToID("_Palette");
        static readonly int _MaterialSlotMapId   = Shader.PropertyToID("_MaterialSlotMap");
        static readonly int _MaterialSlotCountId = Shader.PropertyToID("_MaterialSlotCount");
        
        
        // Number of palette colour slots (KeyColor, SecondaryColor, then slots[]).
        public int SlotCount
        {
            get
            {
                if (palette == null || palette.colorSlots == null)
                    return 0;
                int extra = palette.colorSlots.slots != null ? palette.colorSlots.slots.Length : 0;
                return 2 + extra;
            }
        }

        // Authored colour of a palette slot (sRGB, as the artist set it). Source of
        // truth for tools that want to show slot colours.
        public bool TryGetSlotColor(int index, out Color color)
        {
            color = Color.gray;
            if (palette == null || palette.colorSlots == null)
                return false;
            if (palette.colorSlots.TryGetColor(index, out var slot) && slot != null)
            {
                color = slot.color;
                return true;
            }
            return false;
        }
        public bool TryGetSlotLabel(int index, out string label)
        {
            label = "";
            if (palette == null || palette.colorSlots == null)
                return false;
            if (palette.colorSlots.TryGetColor(index, out var slot) && slot != null)
            {
                label = slot.name;
                return true;
            }
            return false;
        }

        void OnDisable()
        {
            // Release the GPU buffer (also runs on domain reload / recompile).
            _materialSlotBuffer?.Dispose();
            _materialSlotBuffer = null;
        }

        void ApplySceneObjectPaletteVars()
        {
            if (palette == null)
                return;

            // Palette: colour slot -> colour.
            if (colorSlots == null)
                colorSlots = new Vector4[MAX_SLOTS];
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                if (!palette.colorSlots.TryGetColor(i, out var slot))
                {
                    colorSlots[i] = Vector4.zero;
                    continue;
                }
                var c = slot.color.linear;
                colorSlots[i] = new Vector4(c.r, c.g, c.b, c.a);
            }
            Shader.SetGlobalVectorArray(_PaletteId, colorSlots);

            // Material id -> colour slot pairs.
            // NOTE: only one model is handled today; supporting several means
            // merging their materialData (and de-duping ids) here.
            int count = 0;
            var applier = FindFirstObjectByType<ArtPipeline.ModelColoringApplier>(FindObjectsInactive.Exclude);
            var coloring = applier != null ? applier.coloring : null;
            if (coloring != null && coloring.materialData != null)
            {
                foreach (var d in coloring.materialData)
                {
                    if (d == null)
                        continue;
                    if (count >= MAX_MATERIAL_IDS)
                    {
                        Debug.LogError($"[Palette] More than {MAX_MATERIAL_IDS} materials in coloring; extras ignored.", this);
                        break;
                    }
                    // colorSlot must index _Palette[0..MAX_SLOTS-1].
                    int slot = Mathf.Clamp(d.properties != null ? d.properties.colorSlot : 0, 0, MAX_SLOTS - 1);
                    _slotPairs[count++] = new Vector2Int(d.materialId, slot);
                }
            }

            EnsureSlotBuffer();
            if (count > 0)
                _materialSlotBuffer.SetData(_slotPairs, 0, 0, count);

            Shader.SetGlobalBuffer(_MaterialSlotMapId, _materialSlotBuffer);
            // The count guards the shader loop, so an empty/unbound buffer is never indexed.
            Shader.SetGlobalInt(_MaterialSlotCountId, count);

            // Coloring tool editor preview vars (off unless the editor tool set them).
            #if UNITY_EDITOR
            ApplyColoringToolGlobals();
            #endif
        }


        void EnsureSlotBuffer()
        {
            if (_materialSlotBuffer == null || !_materialSlotBuffer.IsValid())
                _materialSlotBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, MAX_MATERIAL_IDS, sizeof(int) * 2);
        }

        #region Editor

        public static bool EDITOR_COLORING_TOOL_ACTIVE = false;
        public static int EDITOR_COLORING_TOOL_PREVIEW_MATERIAL_ID = -1;
        public static int EDITOR_COLORING_TOOL_PREVIEW_SLOT = 0;

        // NOTE: names must match PaletteMaterialTag.cginc (incl. the "Colorint" typo).
        static readonly int _ColoringToolActiveId           = Shader.PropertyToID("_ColoringToolActive");
        static readonly int _ColoringToolPreviewMaterialIdId = Shader.PropertyToID("_ColoringToolPreviewMaterialId");
        static readonly int _ColoringToolPreviewSlotId      = Shader.PropertyToID("_ColorintToolPreviewSlot");

        public static void ApplyColoringToolGlobals()
        {
            Shader.SetGlobalInt(_ColoringToolActiveId, EDITOR_COLORING_TOOL_ACTIVE ? 1 : 0);
            Shader.SetGlobalInt(_ColoringToolPreviewMaterialIdId, EDITOR_COLORING_TOOL_PREVIEW_MATERIAL_ID);
            Shader.SetGlobalInt(_ColoringToolPreviewSlotId, EDITOR_COLORING_TOOL_PREVIEW_SLOT);
        }

        #endregion
    }
}