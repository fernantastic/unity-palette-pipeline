# Keeping the surface-type tag intact on Unity import

## The problem

The assignment is **per material**: one material → one surface type. In Blender
each material stores its assignment as a string custom property
`palette_surface_tag = "{sceneID}.{surfaceTypeID}"` (e.g. `"beach.water"`).
The trouble is the export/import boundary:

- **FBX does not carry arbitrary string custom properties** on materials in a
  way Unity's `ModelImporter` exposes. Unity's importer pipeline
  (`AssetPostprocessor`) gives you `GameObject`/`Renderer`/`Mesh`/`Material` —
  not the raw FBX material user-property strings.
- We need `MaterialPostProcessor` to recover the tag per material/submesh at
  import time.

What **does** reliably survive FBX → Unity is **vertex colors**. So we move the
identity through a vertex-color channel. Because a mesh can have several material
slots (submeshes), the id is baked **per polygon**: every face inherits the int
id of the material in its slot. A submesh is therefore uniform in vertex-color R,
and Unity can read one vertex per submesh to recover that material's tag.

## The scheme (recommended)

### 1. A deterministic integer id per surface type

`art_data.json` is the single source of truth and is **read-only** for both
Blender and Unity. We never add `int` ids to the JSON. Instead both sides
**derive the same id deterministically** by enumerating the file in order:

```
id = 0                      -> Unassigned
then, walking scenes top-to-bottom, surface_types top-to-bottom:
  game.generic.metal        -> 1
  game.generic.concrete     -> 2
  ...
  corner.building.fancierwall -> 10
  beach.sand                -> 11
  beach.water               -> 12
  ...
```

Because Blender (`_DataCache` in `__init__.py`) and Unity build the map from the
**same file with the same ordering**, they agree on the integer without ever
storing it. Add new surface types **at the end of a scene's list** to keep
existing ids stable; reordering renumbers everything (acceptable, since a
re-import re-bakes from the current JSON anyway).

> Range note: the R byte channel holds 0–255, so this supports 255 surface
> types total. We currently have ~15. If we ever exceed 255, spill into the G
> channel (see "Scaling" below).

### 2. Bake the id into vertex color R (already implemented)

`bake_mesh_tag()` writes a `BYTE_COLOR` attribute named `PaletteTag` on the
`CORNER` domain, with `R = id / 255.0`, `G = B = 0`, `A = 1`. Every loop gets
the same value, so the whole mesh reads back one id.

### 3. Read it back in Unity

`MaterialPostProcessor.OnPostprocessModel` already has the `Renderer`. Add a
read of the baked channel and a JSON-derived lookup:

```csharp
// Build once from Assets/Palette/Data/art_data.json (cache it).
// surfaceTypeById[id] -> "scene.surface"
static Dictionary<int,string> BuildIdMap() {
    var map = new Dictionary<int,string> { [0] = "" };
    var data = JsonUtility.From(... art_data.json ...);
    int next = 1;
    foreach (var scene in data.scenes)
        foreach (var st in scene.surface_types)
            map[next++] = scene.id + "." + st.id;
    return map;
}

void OnPostprocessModel(GameObject g) {
    var mf = g.GetComponent<MeshFilter>();
    if (!mf || !mf.sharedMesh) return;
    var mesh = mf.sharedMesh;
    var colors = mesh.colors32;                 // raw bytes, see gotcha 1
    if (colors.Length == 0) return;

    // One id per submesh (= per material slot). Read the first vertex of each
    // submesh; the bake makes every face in a slot uniform in R.
    for (int sm = 0; sm < mesh.subMeshCount; sm++) {
        int firstVert = (int)mesh.GetIndices(sm)[0];
        byte r = colors[firstVert].r;           // 0..255
        string tag = idMap.TryGetValue(r, out var t) ? t : "";
        // -> route submesh `sm` / its material to the right surface type
    }
}
```

### 4. Two import gotchas to lock down

1. **Vertex-color color space.** Unity's FBX importer can treat vertex colors as
   sRGB and gamma-convert them, which corrupts an integer payload. Read from
   `Mesh.colors32` (raw bytes, no gamma) rather than `Mesh.colors` (float,
   possibly converted), and verify `r` round-trips for a known tag. If the
   importer still shifts values, set the model importer's vertex-color import to
   linear/none, or do the read in `OnPostprocessMesh` where the raw mesh is
   available.

2. **Use `colors32` + exact byte compare.** Because we store integers in a byte
   channel and read them back as bytes, no float epsilon is involved — `r` is
   the id directly. Avoid `Mathf.RoundToInt(color.r * 255)` on a gamma-converted
   float.

## Why not just a Renderer/GameObject custom property?

Unity can read some FBX user properties via the model importer, but it's brittle
across DCC/FBX versions and doesn't map cleanly to submeshes. Vertex color is
geometry-bound, survives instancing, and is trivially readable in both
`AssetPostprocessor` and a shader.

## Reading it in the shader (optional, already half-wired)

If a material needs the id at runtime (not just at import), the R channel is
already on the mesh. Sample vertex color in the shader graph and multiply by 255
to recover the id, or branch on it for per-surface effects. Keep the
authoritative string mapping on the C# side; the shader only needs the integer.

## Scaling past 255 types

Pack a 16-bit id as `R = id & 0xFF`, `G = (id >> 8) & 0xFF`. Blender bake and
Unity read both change in one place. Not needed at current data size.

## Summary

| Stage    | Carrier                                          | Writes JSON? |
|----------|--------------------------------------------------|--------------|
| Blender  | material `palette_surface_tag` → per-poly R-byte | No (read-only) |
| FBX      | `PaletteTag` vertex color (R = id/255)        | —            |
| Unity    | `Mesh.colors32[submeshFirstVert].r` → JSON map   | No (read-only) |

Single source of truth stays `art_data.json`; the integer is a derived index, so
nothing has to be written back to keep the two engines in agreement.
