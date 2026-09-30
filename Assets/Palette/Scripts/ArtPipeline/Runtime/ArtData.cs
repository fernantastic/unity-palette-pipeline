using UnityEngine;
using UnityEditor;
using TriInspector;

namespace Palette.ArtPipeline
{
    // Match art_data.json structure
    [System.Serializable]
    public class ArtData
    {
        [System.Serializable]
        public class Scene
        {
            public string id;
            public bool isMain;
            public SurfaceType[] surface_types;
        }

        [System.Serializable]
        public class SurfaceType
        {
            public string id;
        }

        public Scene[] scenes;

    }
}
