using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace TMPro
{
    [BurstCompile(CompileSynchronously = true)]
    internal static class TextMeshProBurst
    {
        [BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void BurstCompiled_CalculatePositionUVColor_Multi([NoAlias] in TMP_MeshVertex* verts, in TMP_CacheCalculatedCharacter* calculatedCharacters, int characterCount, ref float4 parameters,
            in float4 colorSrc, in float4 sop)
        {
            // sop
            //adjustedScale
            //xScale
            //normalSpacingCharacterSpacingOffset
            //padding

            for(int i = 0; i < characterCount; i++)
            {
                TMP_CacheCalculatedCharacter calculatedCharacter = calculatedCharacters[i];

                float paddingAdjusted = sop.w * calculatedCharacter.uvAtlasReciprocal;
                float paddingAdjustedDoubled = sop.w * sop.x;
                {
                    int bufferIndex = i * 4;

                    float4 lrdu = (calculatedCharacter.GlyphMetrics4 * parameters.z) + new float4(
                        (parameters.x * sop.x) - paddingAdjustedDoubled,
                        ((calculatedCharacter.GlyphMetrics4.x * parameters.z) + (parameters.x * sop.x)) + paddingAdjustedDoubled,
                        parameters.y - paddingAdjustedDoubled,
                        parameters.y + paddingAdjustedDoubled
                    );

                    calculatedCharacter.GlyphBox += new float4(
                        -paddingAdjusted,
                        -paddingAdjusted,
                        paddingAdjusted,
                        paddingAdjusted
                    );

                    verts[bufferIndex + 0].PositionColor = new float4(lrdu.xz, 0, colorSrc.x);
                    verts[bufferIndex + 1].PositionColor = new float4(lrdu.xw, 0, colorSrc.y);
                    verts[bufferIndex + 2].PositionColor = new float4(lrdu.yw, 0, colorSrc.z);
                    verts[bufferIndex + 3].PositionColor = new float4(lrdu.yz, 0, colorSrc.w);

                    verts[bufferIndex + 0].TextCoord0 = new float4(calculatedCharacter.GlyphBox.xy, 0, sop.y);
                    verts[bufferIndex + 1].TextCoord0 = new float4(calculatedCharacter.GlyphBox.xw, 0, sop.y);
                    verts[bufferIndex + 2].TextCoord0 = new float4(calculatedCharacter.GlyphBox.zw, 0, sop.y);
                    verts[bufferIndex + 3].TextCoord0 = new float4(calculatedCharacter.GlyphBox.zy, 0, sop.y);
                }

                // Update the current line with the calculated width of the glyph
                parameters.x += calculatedCharacter.GlyphHorizontalAdvance + sop.z;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        public static unsafe void BurstCompiled_OffsetQuadPositionFull([NoAlias] in TMP_MeshVertex* positions, [NoAlias] in FastTextCaseLineInfo* lines, int lineInfoCount)
        {
            TMP_MeshVertex* pos = positions;

            for(int lineIndex = 0; lineIndex < lineInfoCount; lineIndex++)
            {
                ref FastTextCaseLineInfo lineInfo = ref lines[lineIndex];
                float3 offset = new(lineInfo.CalculatedAlignmentJustificationOffset.x, -(lineInfo.LineYOffset + lineInfo.CalculatedAlignmentJustificationOffset.y), 0);

                int total = lineInfo.Length * 4;
                for(int i = 0; i < total; i += 4)
                {
                    pos[i].Position += offset;
                    pos[i + 1].Position += offset;
                    pos[i + 2].Position += offset;
                    pos[i + 3].Position += offset;
                }

                pos += total;
            }
        }

        [BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        public static unsafe void BurstCompiled_ColorToColor32([NoAlias] in float4* c, [NoAlias] in Color32* c32)
        {
            for(int i = 0; i < 4; i++)
            {
                float4 result = c[i] * 255.001f;
                c32[i] = new Color32(
                    (byte)result.x,
                    (byte)result.y,
                    (byte)result.z,
                    (byte)result.w
                );
            }
        }
    }
}
