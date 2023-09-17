using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

// ReSharper disable once CheckNamespace
namespace TMPro
{
    [BurstCompile(CompileSynchronously = true)]
    internal static class TextMeshProBurst
    {        
        [BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void BurstCompiled_CalculatePositionUVColor_Multi([NoAlias] in TMP_MeshVertex* verts, in TMP_CacheCalculatedCharacter* calculatedCharacters, int characterStartIndex, int characterCount, ref float4 parameters, 
            in float4 colorSrc, float adjustedScale, float xScale, float normalSpacingCharacterSpacingOffset)
        {
            for(int i = 0; i < characterCount; i++)
            {
                int targetCharacterIndex = characterStartIndex + i;
                TMP_CacheCalculatedCharacter calculatedCharacter = calculatedCharacters[targetCharacterIndex];

                {
                    int bufferIndex = targetCharacterIndex * 4;
                    
                    float4 lrdu = calculatedCharacter.GlyphMetrics4 * parameters.z;
                    lrdu += new float4(parameters.x * adjustedScale, (calculatedCharacter.GlyphMetrics4.x * parameters.z) + (parameters.x * adjustedScale), parameters.y, parameters.y);
                    verts[bufferIndex + 0].PositionColor = new float4(lrdu.x,  lrdu.z, 0, colorSrc.x);
                    verts[bufferIndex + 1].PositionColor = new float4(lrdu.x,  lrdu.w, 0, colorSrc.y);
                    verts[bufferIndex + 2].PositionColor = new float4(lrdu.y, lrdu.w, 0, colorSrc.z);
                    verts[bufferIndex + 3].PositionColor = new float4(lrdu.y, lrdu.z, 0,  colorSrc.w);
            
                    verts[bufferIndex + 0].TextCoord0 = new float4(calculatedCharacter.GlyphBox.xy, 0, xScale);
                    verts[bufferIndex + 1].TextCoord0 = new float4(calculatedCharacter.GlyphBox.xw, 0, xScale);
                    verts[bufferIndex + 2].TextCoord0 = new float4(calculatedCharacter.GlyphBox.zw, 0, xScale);
                    verts[bufferIndex + 3].TextCoord0 = new float4(calculatedCharacter.GlyphBox.zy, 0, xScale);
                }

                // Update the current line with the calculated width of the glyph
                parameters.x += calculatedCharacter.GlyphHorizontalAdvance + normalSpacingCharacterSpacingOffset;
            }
        }
        
        [BurstCompile(CompileSynchronously = true)]
        public static unsafe void BurstCompiled_OffsetQuadPositionFull([NoAlias] in TMP_MeshVertex* positions,[NoAlias] in MattishCaseLineInfo* lines, int lineInfoCount)
        {
            TMP_MeshVertex* pos = positions;
            
            for(int lineIndex = 0; lineIndex < lineInfoCount; lineIndex++)
            {
                ref MattishCaseLineInfo lineInfo = ref lines[lineIndex];
                float3 offset = new(lineInfo.CalculatedAlignmentJustificationOffset.x, -(lineInfo.LineYOffset + lineInfo.CalculatedAlignmentJustificationOffset.y), 0);

                int total = lineInfo.Length * 4;
                for(int i = 0; i < total; i += 4)
                {
                    pos[i].Position     += offset;
                    pos[i + 1].Position += offset;
                    pos[i + 2].Position += offset;
                    pos[i + 3].Position += offset;
                }

                pos += total;
            }
        }
        
    }
}