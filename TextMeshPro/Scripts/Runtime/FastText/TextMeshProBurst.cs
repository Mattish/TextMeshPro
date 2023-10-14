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
            in float4 colorSrc, float adjustedScale, float xScale, float normalSpacingCharacterSpacingOffset, float padding)
        {
            for(int i = 0; i < characterCount; i++)
            {
                int targetCharacterIndex = characterStartIndex + i;
                TMP_CacheCalculatedCharacter calculatedCharacter = calculatedCharacters[targetCharacterIndex];

                float paddingAdjusted = padding * calculatedCharacter.uvAtlasReciprocal;
                float paddingAdjustedDoubled = padding * adjustedScale;
                {
                    int bufferIndex = targetCharacterIndex * 4;
                    
                    float4 lrdu = calculatedCharacter.GlyphMetrics4 * parameters.z;
                    lrdu += new float4(
                        parameters.x * adjustedScale, 
                        (calculatedCharacter.GlyphMetrics4.x * parameters.z) + (parameters.x * adjustedScale), 
                        parameters.y, 
                        parameters.y
                    );
                    verts[bufferIndex + 0].PositionColor = new float4(lrdu.x - paddingAdjustedDoubled,  lrdu.z - paddingAdjustedDoubled, 0, colorSrc.x);
                    verts[bufferIndex + 1].PositionColor = new float4(lrdu.x - paddingAdjustedDoubled,  lrdu.w + paddingAdjustedDoubled, 0, colorSrc.y);
                    verts[bufferIndex + 2].PositionColor = new float4(lrdu.y + paddingAdjustedDoubled,  lrdu.w + paddingAdjustedDoubled, 0, colorSrc.z);
                    verts[bufferIndex + 3].PositionColor = new float4(lrdu.y + paddingAdjustedDoubled,  lrdu.z - paddingAdjustedDoubled, 0,  colorSrc.w);
            
                    verts[bufferIndex + 0].TextCoord0 = new float4(calculatedCharacter.GlyphBox.xy + new float2(-paddingAdjusted, -paddingAdjusted), 0, xScale);
                    verts[bufferIndex + 1].TextCoord0 = new float4(calculatedCharacter.GlyphBox.xw + new float2(-paddingAdjusted, paddingAdjusted), 0, xScale);
                    verts[bufferIndex + 2].TextCoord0 = new float4(calculatedCharacter.GlyphBox.zw + new float2(paddingAdjusted, paddingAdjusted), 0, xScale);
                    verts[bufferIndex + 3].TextCoord0 = new float4(calculatedCharacter.GlyphBox.zy + new float2(paddingAdjusted, -paddingAdjusted), 0, xScale);
                }

                // Update the current line with the calculated width of the glyph
                parameters.x += calculatedCharacter.GlyphHorizontalAdvance + normalSpacingCharacterSpacingOffset;
            }
        }
        
        [BurstCompile(CompileSynchronously = true)]
        public static unsafe void BurstCompiled_OffsetQuadPositionFull([NoAlias] in TMP_MeshVertex* positions,[NoAlias] in FastTextCaseLineInfo* lines, int lineInfoCount)
        {
            TMP_MeshVertex* pos = positions;
            
            for(int lineIndex = 0; lineIndex < lineInfoCount; lineIndex++)
            {
                ref FastTextCaseLineInfo lineInfo = ref lines[lineIndex];
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