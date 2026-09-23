using WaW.UiLib.Core;

namespace WaW.UiLib.Data;

public record struct TextureInfo(AtlasPosition AtlasPosition, TextureType TextureType);

public record struct AtlasPosition(float U, float V, float W, float H);