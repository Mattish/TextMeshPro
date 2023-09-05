using Unity.Mathematics;
using UnityEngine.TextCore;

namespace TMPro
{
    public struct TMP_CacheCalculatedCharacter
    {
        public static TMP_CacheCalculatedCharacter DefaultRef = new(); 
        public float4 GlyphMetrics4;
        
        // X,Y, X+Width, Y+Height
        public float4 GlyphBox;
        public float GlyphHorizontalAdvance;
        public int AtlasIndex;

        public static TMP_CacheCalculatedCharacter Calcuate(Glyph glyph)
        {
            GlyphMetrics glyphMetrics = glyph.metrics;
            GlyphRect glyphGlyphRect = glyph.glyphRect;
            return new TMP_CacheCalculatedCharacter
            {
                GlyphMetrics4 = new(glyphMetrics.horizontalBearingX, glyphMetrics.width, 0, glyphMetrics.horizontalBearingY),
                GlyphHorizontalAdvance = glyphMetrics.horizontalAdvance,
                GlyphBox = new (glyphGlyphRect.x, glyphGlyphRect.y, glyphGlyphRect.x + glyphGlyphRect.width, glyphGlyphRect.y + glyphGlyphRect.height),
                AtlasIndex = glyph.atlasIndex
            };
        }
    }
}