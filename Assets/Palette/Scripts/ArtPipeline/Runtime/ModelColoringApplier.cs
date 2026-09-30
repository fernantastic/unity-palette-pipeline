using UnityEngine;

namespace Palette.ArtPipeline
{
    /// <summary>
    /// Holds the <see cref="ModelColoring"/> that art-directs this model instance.
    ///
    /// The material-id tag is now stamped per renderer by
    /// <see cref="PaletteMaterialId.Apply"/>; this component only carries the
    /// coloring reference (color application will live here when it's wired up).
    /// </summary>
    [DisallowMultipleComponent]
    public class ModelColoringApplier : MonoBehaviour
    {
        public ModelColoring coloring;
    }
}
