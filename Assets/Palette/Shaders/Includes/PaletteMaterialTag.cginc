// HSV ↔ RGB | Sam Hocevar (RGB→HSV)
//[SNIPPET] https://fragcoord.xyz/snippet/89a962c1-8000-4800-8000-1ae8e2c12a3a0000
float3 hsv_to_rgb(float3 c)
{
    float3 p = abs(frac(c.xxx + float3(1.0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0);
    return c.z * lerp(float3(1.0, 1.0, 1.0), clamp(p - 1.0, 0.0, 1.0), c.y);
}

// PCG integer hash - scrambles an id so neighbouring ids get very different hues.
uint palette_hash(uint v)
{
    uint h = v * 747796405u + 2891336453u;
    h = ((h >> ((h >> 28) + 4u)) ^ h) * 277803737u;
    return (h >> 22) ^ h;
}

void ReadMaterialTag_float(in float materialId, out float3 id)
{
    uint h = palette_hash((uint)(materialId + 0.5));
    float hue = (h & 0xFFFFu) / 65535.0;
    id = hsv_to_rgb(float3(hue, 1.0, 1.0));
}
void ReadMaterialTag_half(in float materialId, out half3 id)
{
    uint h = palette_hash((uint)(materialId + 0.5));
    float hue = (h & 0xFFFFu) / 65535.0;
    id = (half3)hsv_to_rgb(float3(hue, 1.0, 1.0));
}


// Keyed (id, slot) pairs uploaded by Environment.cs. Material ids are sparse
// 24-bit hashes, so we can't index a small array by id - we linear-search this
// buffer instead. _MaterialSlotCount is 0 when unbound, so the loop is skipped
// and the (possibly unbound) buffer is never indexed.
StructuredBuffer<int2> _MaterialSlotMap;
int    _MaterialSlotCount;
float4 _Palette[32];

bool _ColoringToolActive;
int _ColoringToolPreviewMaterialId;
int _ColorintToolPreviewSlot;

int PaletteSlotForMaterial(int id)
{
    [loop]
    for (int i = 0; i < _MaterialSlotCount; i++)
    {
        if (_MaterialSlotMap[i].x == id)
            return _MaterialSlotMap[i].y;
    }
    return 0; // unmapped -> slot 0
}

void GetPaletteData_float(in float materialId, out float4 paletteColor)
{
    if (_ColoringToolActive && materialId == _ColoringToolPreviewMaterialId)
    {
        int slot = PaletteSlotForMaterial((int)(_ColoringToolPreviewMaterialId + 0.5));
        paletteColor = _Palette[clamp(_ColorintToolPreviewSlot, 0, 31)];
        return;
    }
    // round() not (+0.5): the id is an exact integer, but +0.5 ties-to-even at
    // single-precision for ids >= 2^23, decoding odd ids to id+1 (no match).
    int slot = PaletteSlotForMaterial((int)round(materialId));
    paletteColor = _Palette[clamp(slot, 0, 31)];
}
void GetPaletteData_half(in float materialId, out float4 paletteColor)
{
    if (_ColoringToolActive && materialId == _ColoringToolPreviewMaterialId)
    {
        paletteColor = _Palette[clamp(_ColorintToolPreviewSlot, 0, 31)];
        return;
    }
    // round() not (+0.5): the id is an exact integer, but +0.5 ties-to-even at
    // single-precision for ids >= 2^23, decoding odd ids to id+1 (no match).
    int slot = PaletteSlotForMaterial((int)round(materialId));
    paletteColor = _Palette[clamp(slot, 0, 31)];
}