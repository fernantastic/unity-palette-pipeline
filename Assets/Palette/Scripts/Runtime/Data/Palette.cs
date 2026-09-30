using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TriInspector;

namespace Palette
{
    [CreateAssetMenu(menuName = "Palette/Palette")]
    public class Palette : ScriptableObject
    {
        [System.Serializable]
        public class Shading
        {
            public Color SpecularColor = new Color(1f, 0.5f, 1f, 1f);
            [Range(0, 1)]
            public float SpecularSharpness = .92f;
            [Range(0, 1)]
            public float SpecularCoverage = .16f;
            public Color ShadeColor = new Color(0f, 0.5f, 0f, 1f);
            [Range(0, 1)]
            public float ShadeCoverage = .63f;  
            [Range(0, 1)]
            public float ShadeSharpness = .61f;
            [Range(-1, 1)]
            public float ShadowsOpacity = 1f;
            public Color ShadowsTint = new Color(1f, 1f, 1f, 1f);
            public Color ShadowsReplacementColor = new Color(0f, 0f, 0f, 1f);
            [Range(0, 1)]            
            public float LitTintAmount = 0f;
            public Color LitTint = new Color(1f, 1f, 1f, 0f);
            
        }
        [System.Serializable]
        public class SkyAndFog
        {
            public Color FogColor = new Color(1f, 1f, 1f, 1f);
            [Range(0,1)] public float FogDensity = 0f;
            [Range(0,1)] public float HeightFogOpacity = 0f;
            public float HeightFogHeight = 0f;

            public Color SkyGroundColor = new Color(0.35f, 0.32f, 0.28f, 1f);
            public Color SkyHorizonColor = new Color(0.85f, 0.75f, 0.65f, 1f);
            public Color SkyZenithColor = new Color(0.25f, 0.45f, 0.75f, 1f);
            public Color SkySunColor = new Color(1f, 0.95f, 0.85f, 1f);
            [Range(0.0001f, 0.05f)] public float SkySunSize = 0.002f;
            public Color NighttimeTint = new Color(0.1f, 0.15f, 0.35f, 1f);
        }
        [System.Serializable]
        public class Texturing
        {
            public Color DirtmapColor = new Color(1f, 1f, 1f, 1f);
            [Range(-1,1)] public float DirtmapOpacity = 0f;
            public bool UseDirtmap = false;
            [Range(0,1)] public float DirtmapSize = 10.0f;
            public Texture2D DirtmapTexture;
        }

        [System.Serializable]
        public class ColorSlots
        {
            [System.Serializable]
            public class ColorSlot
            {
                public string name = "";
                public Color color = Color.white;

                public bool overrideDirtmap = false;
                [ShowIf(nameof(overrideDirtmap))]
                public Texture2D DirtmapTexture;
                [ShowIf(nameof(overrideDirtmap))]
                [Range(-1,1)] public float DirtmapOpacity = 0;
                [ShowIf(nameof(overrideDirtmap))]
                [Range(-1,1)] public Color DirtmapColor = Color.white;
                [ShowIf(nameof(overrideDirtmap))]
                public float DirtmapSize = 20.0f;                
            }
            public ColorSlot KeyColor = new ColorSlot();
            public ColorSlot SecondaryColor = new ColorSlot();

            public ColorSlot[] slots;

            public bool TryGetColor(int index, out ColorSlot slot)
            {
                slot = null;
                if (index == 0)
                {
                    slot = KeyColor;
                    return slot != null;
                }
                if (index == 1)
                {
                    slot = SecondaryColor;
                    return slot != null;
                }
                if (slots != null && slots.Length > 0 && (index - 2) < slots.Length)
                {
                    slot = slots[index - 2];
                    return slot != null;
                }
                return false;
            }
        }

        [System.Serializable]
        public class ScenePaletteData
        {
            public string id;
            public SurfaceTypePaletteData[] surfaceTypes;
        }

        [System.Serializable]
        public class SurfaceTypePaletteData
        {
            public Color color;
        }

        public Shading shading;
        public SkyAndFog skyAndFog;
        public Texturing texturing;
        public ColorSlots colorSlots;

        
    }
}
