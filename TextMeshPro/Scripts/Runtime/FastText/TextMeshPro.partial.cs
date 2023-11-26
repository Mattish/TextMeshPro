// ReSharper disable once CheckNamespace

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace TMPro
{
    public partial class TextMeshPro : TMP_Text, ILayoutElement
    {
        // TODO: these should be checked against and sized accordingly to stop over running.
        // As we do pointer stuff with these, if we overrun the editor/game is going to blow up randomly and confusingly.
        private static TMP_MeshVertex[] vertsBuffer = new TMP_MeshVertex[1024 * 4];
        private static TMP_MeshVertexStream2[] verts2Buffer = new TMP_MeshVertexStream2[1024 * 4];
        private static int verts2Populated = 0;
        private static FastTextCaseLineInfo[] fastTextCaseLineInfos = new FastTextCaseLineInfo[256];

        public unsafe void DoFastTextCaseGenerateTextMesh()
        {
            var ot1 = OperationTimingTarget.Start();

            // Setup all our required state...
            m_lineJustification = horizontalAlignment;
            m_currentFontAsset = m_fontAsset;
            m_currentMaterial = m_sharedMaterial;
            m_currentMaterialIndex = 0;
            int totalCharacterCount = 0;
            float orthographicAdjustmentFactor = (m_isOrthographic ? 1 : 0.1f);
            float adjustedScale = m_fontSize * (1.0f / m_fontAsset.m_FaceInfo.pointSize) * orthographicAdjustmentFactor;
            float baselineOffset = m_currentFontAsset.m_FaceInfo.baseline * adjustedScale;
            float lineHeight = m_currentFontAsset.m_FaceInfo.lineHeight * adjustedScale;
            float elementAscentLine = m_currentFontAsset.m_FaceInfo.ascentLine * adjustedScale;
            float elementDescentLine = m_currentFontAsset.m_FaceInfo.descentLine * adjustedScale;
            float normalSpacingCharacterSpacingOffset = m_currentFontAsset.normalSpacingOffset + m_characterSpacing;
            float lossyScale = m_previousLossyScaleY = this.transform.lossyScale.y;
            float xScale = adjustedScale * Mathf.Abs(lossyScale);
            float4 calcPosBurstParams = new(0, baselineOffset, adjustedScale, 0);
            Span<int> materialIndexToCharCount = stackalloc int[16];

            // adjustedScale,
            // xScale,
            // normalSpacingCharacterSpacingOffset,
            // m_padding
            float4 sop = new float4(
                adjustedScale,
                xScale,
                normalSpacingCharacterSpacingOffset,
                m_padding
            );

            int lineInfoCount = 0;

            ref FastTextCaseLineInfo currentLine = ref fastTextCaseLineInfos[0];
            currentLine.Length = 0;
            currentLine.LineYOffset = 0;
            currentLine.TotalWidth = 0;

            Span<Color32> quadColors = stackalloc Color32[4];
            {
                GetQuadColors(m_fontColor, quadColors);
            }

            float4 srcColors; // These are actually uint values being reinterpreted for burst code
            fixed(Color32* clrs2 = quadColors)
            {
                srcColors = *(float4*)clrs2;
            }

            m_materialReferenceIndexLookup.Clear();

            // ...State setup finished

            fixed(TMP_MeshVertex* verts = vertsBuffer)
            {
                fixed(TMP_CacheCalculatedCharacter* resolvedChars = m_CharacterResolvedCharacters)
                {
                    for(int batchIndex = 0; batchIndex < m_CharacterBatchCount; ++batchIndex)
                    {
                        ref FastTextCharacterBatch batch = ref m_CharacterBatches[batchIndex];
                        if((batch.BatchTypeFlag & FastTextBatchTypeFlag.Material) != 0 && batch.AtlasIndex != byte.MaxValue)
                        {
                            Material targetMaterial = batch.AtlasIndex > 0
                                ? TMP_MaterialManager.GetFallbackMaterial(m_currentFontAsset, m_currentMaterial, batch.AtlasIndex)
                                : m_currentMaterial;
                            m_currentMaterialIndex = MaterialReference.AddMaterialReference(targetMaterial, m_currentFontAsset, ref m_materialReferences, m_materialReferenceIndexLookup);
                        }

                        TextMeshProBurst.BurstCompiled_CalculatePositionUVColor_Multi(
                            verts,
                            resolvedChars + batch.StartIndex,
                            batch.Length,
                            ref calcPosBurstParams,
                            in srcColors,
                            in sop
                        );

                        materialIndexToCharCount[m_currentMaterialIndex] += batch.Length;
                        currentLine.Length += batch.Length;
                        totalCharacterCount += batch.Length;
                        // If this batch is a line, then commit the calculated line details
                        if((batch.BatchTypeFlag & FastTextBatchTypeFlag.LineBreak) != 0)
                        {
                            currentLine.TotalWidth = calcPosBurstParams.x * adjustedScale;
                            calcPosBurstParams.x = 0;
                            currentLine = ref fastTextCaseLineInfos[lineInfoCount + 1];
                            currentLine.LineYOffset = lineHeight * (lineInfoCount + 1);
                            currentLine.Length = 0;
                            currentLine.TotalWidth = 0;
                            ++lineInfoCount;
                        }
                    }

                    m_characterCount = totalCharacterCount;

                    // Finalise this line
                    currentLine.TotalWidth = calcPosBurstParams.x * adjustedScale;
                    fastTextCaseLineInfos[lineInfoCount++] = currentLine;

                    if(m_characterCount == 0)
                    {
                        ClearMesh(true);
                        TMPro_EventManager.ON_TEXT_CHANGED(this);
                        m_IsAutoSizePointSizeSet = true;
                        return;
                    }

                    // The default positions of character lines places the first character in each line in the middle of the `m_rectTransform.rect`
                    // The origin position of characters is the bottom left corner at ascent y=0
                    Rect rect = m_rectTransform.rect;
                    switch(m_lineJustification)
                    {
                        case HorizontalAlignmentOptions.Left:
                            for(var i = 0; i < lineInfoCount; i++)
                            {
                                fastTextCaseLineInfos[i].CalculatedAlignmentJustificationOffset.x = rect.xMin;
                            }
                            break;
                        case HorizontalAlignmentOptions.Center:
                            // Center alignment depends on the total length of each line to center based on content and the size of the text rect
                            for(var i = 0; i < lineInfoCount; i++)
                            {
                                fastTextCaseLineInfos[i].CalculatedAlignmentJustificationOffset.x = -fastTextCaseLineInfos[i].TotalWidth * 0.5f;
                            }
                            break;
                        case HorizontalAlignmentOptions.Right:
                            // Right alignment depends on the total length of each line, and may require negative offset if the line overflows
                            for(var i = 0; i < lineInfoCount; i++)
                            {
                                fastTextCaseLineInfos[i].CalculatedAlignmentJustificationOffset.x = rect.xMax - fastTextCaseLineInfos[i].TotalWidth;
                            }
                            break;
                        case HorizontalAlignmentOptions.Justified:
                        case HorizontalAlignmentOptions.Flush:
                        case HorizontalAlignmentOptions.Geometry:
                        default:
                            // ¯\_(ツ)_/¯
                            break;
                    }

                    switch(m_VerticalAlignment)
                    {
                        case VerticalAlignmentOptions.Top:
                            // Shift down from first line BL, then shift up based on rect height
                            for(var i = 0; i < lineInfoCount; i++)
                            {
                                fastTextCaseLineInfos[i].CalculatedAlignmentJustificationOffset.y = (elementAscentLine - rect.yMax);
                            }
                            break;
                        case VerticalAlignmentOptions.Middle:
                            // We always have one line, even if we have zero characters
                            // The first line initial position is at BL...
                            float totalHeightOfAllLines = ((lineInfoCount * -lineHeight) * 0.5f) + elementAscentLine;
                            for(var i = 0; i < lineInfoCount; i++)
                            {
                                fastTextCaseLineInfos[i].CalculatedAlignmentJustificationOffset.y = totalHeightOfAllLines;
                            }

                            break;
                        case VerticalAlignmentOptions.Bottom:
                            // Shift down from last line BL
                            ref FastTextCaseLineInfo lastLineInfo = ref fastTextCaseLineInfos[lineInfoCount - 1];

                            for(var i = 0; i < lineInfoCount; i++)
                            {
                                fastTextCaseLineInfos[i].CalculatedAlignmentJustificationOffset.y = -rect.yMin - lastLineInfo.LineYOffset + elementDescentLine;
                            }
                            break;
                        case VerticalAlignmentOptions.Baseline:
                        case VerticalAlignmentOptions.Capline:
                        case VerticalAlignmentOptions.Geometry:
                        default:
                            // ¯\_(ツ)_/¯
                            break;
                    }

                    // If we are all the same material, we can blit much faster!
                    // This is likely going to be the case for roman alphabets and numbers
                    if(m_currentMaterialIndex == 0)
                    {
                        fixed(FastTextCaseLineInfo* lines = fastTextCaseLineInfos)
                        {
                            TextMeshProBurst.BurstCompiled_OffsetQuadPositionFull(in verts, in lines, lineInfoCount);
                        }
                    }
                    else
                    {
                        //TODO: Handle cases of multiple materials properly

                        // for(int i = 0; i < m_characterCount; i++)
                        // {
                        //     int bufferIndex = i * 4;
                        //
                        //     characterProgress = ref CalculatedCharacterDetails[i];
                        //
                        //     FastTextCaseLineInfo lineInfo = fastTextCaseLineInfos[characterProgress.CalculatedLineNumber];
                        //     Vector3 offset = new(lineInfo.CalculatedAlignmentJustificationOffset.x, -(lineInfo.LineYOffset + lineInfo.CalculatedAlignmentJustificationOffset.y), 0);
                        //
                        //     ref var meshInfo = ref m_textInfo.meshInfo[characterProgress.MaterialReferenceIndex];
                        //
                        //     int targetIndex = meshInfo.vertexCount;
                        //
                        //     meshInfo.vertices[targetIndex] = vertices[bufferIndex] + offset;
                        //     meshInfo.vertices[targetIndex + 1] = vertices[bufferIndex + 1] + offset;
                        //     meshInfo.vertices[targetIndex + 2] = vertices[bufferIndex + 2] + offset;
                        //     meshInfo.vertices[targetIndex + 3] = vertices[bufferIndex + 3] + offset;
                        //
                        //     meshInfo.colors32[targetIndex] = colors[bufferIndex];
                        //     meshInfo.colors32[targetIndex + 1] = colors[bufferIndex + 1];
                        //     meshInfo.colors32[targetIndex + 2] = colors[bufferIndex + 2];
                        //     meshInfo.colors32[targetIndex + 3] = colors[bufferIndex + 3];
                        //
                        //     meshInfo.uvs0[targetIndex] = uvs[bufferIndex];
                        //     meshInfo.uvs0[targetIndex + 1] = uvs[bufferIndex + 1];
                        //     meshInfo.uvs0[targetIndex + 2] = uvs[bufferIndex + 2];
                        //     meshInfo.uvs0[targetIndex + 3] = uvs[bufferIndex + 3];
                        //
                        //     meshInfo.vertexCount += 4;
                        // }
                    }

                    if(m_renderMode == TextRenderFlags.Render && IsActive())
                    {
                        OnPreRenderText?.Invoke(m_textInfo);

                        // Degenerate the remaining vertex
                        int vCount = m_characterCount * 4;

                        // Ensure verts2 is populated
                        for(int i = verts2Populated; i < vCount; ++i)
                        {
                            verts2Buffer[i].Normal = TMP_MeshInfo.s_DefaultNormal;
                            verts2Buffer[i].Tangent = TMP_MeshInfo.s_DefaultTangent;
                        }

                        if(m_mesh.vertexCount < vCount)
                        {
                            m_mesh.SetVertexBufferParams(vCount, TMP_MeshInfo.DefaultMeshDescriptors);
                            UpdateMeshInfoInit(m_mesh, vertsBuffer.AsSpan(0, vCount), verts2Buffer.AsSpan(0, vCount));
                        }
                        else
                        {
                            long bytesLength = (m_mesh.vertexCount - vCount) * sizeof(TMP_MeshVertex);
                            bytesLength = Math.Clamp(bytesLength, 0, bytesLength);
                            UnsafeUtility.MemClear(&verts[vCount], bytesLength);

                            UpdateMeshInfo(m_mesh, vertsBuffer, verts2Buffer);
                        }
                        verts2Populated = vCount;

                        // for (int i = 1; i < m_textInfo.materialCount; i++)
                        // {
                        //     m_textInfo.meshInfo[i].ClearUnusedVertices();

                        //     if(m_subTextObjects[i] == null)
                        //     {
                        //         continue;
                        //     }

                        //     if(m_geometrySortingOrder != VertexSortingOrder.Normal)
                        //     {
                        //         m_textInfo.meshInfo[i].SortGeometry(VertexSortingOrder.Reverse);
                        //     }

                        //     UpdateMeshInfo(m_subTextObjects[i].mesh, ref m_textInfo.meshInfo[i]);
                        // }
                    }

                    TMPro_EventManager.ON_TEXT_CHANGED(this);
                    m_IsAutoSizePointSizeSet = true;
                }
            }

            ot1.Record(ref MattDoFastTextCaseGenerateTextMeshCounterValue);
        }

        private unsafe void GetQuadColors(Color vertexColor, Span<Color32> returnColors)
        {
            Span<Color> tmpColors = stackalloc Color[4];
            vertexColor.a = m_fontColor32.a < vertexColor.a ? m_fontColor32.a : vertexColor.a;

            if(!m_enableVertexGradient)
            {
                tmpColors[0] = vertexColor;
                tmpColors[1] = vertexColor;
                tmpColors[2] = vertexColor;
                tmpColors[3] = vertexColor;
            }
            else
            {
                // Use Vertex Color Gradient Preset (if one is assigned)
                if(!ReferenceEquals(m_fontColorGradientPreset, null))
                {
                    tmpColors[0] = m_fontColorGradientPreset.bottomLeft * vertexColor;
                    tmpColors[1] = m_fontColorGradientPreset.topLeft * vertexColor;
                    tmpColors[2] = m_fontColorGradientPreset.topRight * vertexColor;
                    tmpColors[3] = m_fontColorGradientPreset.bottomRight * vertexColor;
                }
                else
                {
                    tmpColors[0] = m_fontColorGradient.bottomLeft * vertexColor;
                    tmpColors[1] = m_fontColorGradient.topLeft * vertexColor;
                    tmpColors[2] = m_fontColorGradient.topRight * vertexColor;
                    tmpColors[3] = m_fontColorGradient.bottomRight * vertexColor;
                }
            }

            if(!ReferenceEquals(m_colorGradientPreset, null))
            {
                if(m_colorGradientPresetIsTinted)
                {
                    tmpColors[0] *= m_colorGradientPreset.bottomLeft;
                    tmpColors[1] *= m_colorGradientPreset.topLeft;
                    tmpColors[2] *= m_colorGradientPreset.topRight;
                    tmpColors[3] *= m_colorGradientPreset.bottomRight;
                }
                else
                {
                    tmpColors[0] = m_colorGradientPreset.bottomLeft.MinAlpha(vertexColor);
                    tmpColors[1] = m_colorGradientPreset.topLeft.MinAlpha(vertexColor);
                    tmpColors[2] = m_colorGradientPreset.topRight.MinAlpha(vertexColor);
                    tmpColors[3] = m_colorGradientPreset.bottomRight.MinAlpha(vertexColor);
                }
            }

            if(m_ConvertToLinearSpace)
            {
                returnColors[0] = ((Color32)tmpColors[0]).GammaToLinear();
                returnColors[1] = ((Color32)tmpColors[1]).GammaToLinear();
                returnColors[2] = ((Color32)tmpColors[2]).GammaToLinear();
                returnColors[3] = ((Color32)tmpColors[3]).GammaToLinear();
            }
            else
            {
                fixed(Color* tmpColorsPtr = tmpColors)
                {
                    fixed(Color32* returnColorsPtr = returnColors)
                    {
                        TextMeshProBurst.BurstCompiled_ColorToColor32((float4*)tmpColorsPtr, returnColorsPtr);
                    }
                }
            }
        }

        private static unsafe void UpdateMeshInfo(Mesh instanceMesh, Span<TMP_MeshVertex> data, Span<TMP_MeshVertexStream2> dataStream2)
        {
            int verticesSize = instanceMesh.vertexCount;
            Debug.Assert(data.Length >= verticesSize);

            fixed(TMP_MeshVertex* verts = data)
            {
                NativeArray<TMP_MeshVertex> srcArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<TMP_MeshVertex>(verts, verticesSize, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Unity is cringe and requires safety handles when using native arrays in editor builds. Release builds don't even have the class defined and will cause a build compile failure
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref srcArray, AtomicSafetyHandle.GetTempUnsafePtrSliceHandle());
#endif

                instanceMesh.SetVertexBufferData(srcArray, 0, 0, verticesSize, flags: MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds);
                srcArray.Dispose();
            }

            fixed(TMP_MeshVertexStream2* verts2 = dataStream2)
            {
                NativeArray<TMP_MeshVertexStream2> srcArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<TMP_MeshVertexStream2>(verts2, verticesSize, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Unity is cringe and requires safety handles when using native arrays in editor builds. Release builds don't even have the class defined and will cause a build compile failure
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref srcArray, AtomicSafetyHandle.GetTempUnsafePtrSliceHandle());
#endif

                instanceMesh.SetVertexBufferData(srcArray, 0, 0, verticesSize, 1, flags: MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds);
                srcArray.Dispose();
            }
        }

        private static int[] trianglesBuffer = Array.Empty<int>();
        private static unsafe void UpdateMeshInfoInit(Mesh instanceMesh, Span<TMP_MeshVertex> data, Span<TMP_MeshVertexStream2> dataStream2)
        {
            fixed(TMP_MeshVertex* verts = data)
            {
                NativeArray<TMP_MeshVertex> srcArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<TMP_MeshVertex>(verts, data.Length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Unity is cringe and requires safety handles when using native arrays in editor builds. Release builds don't even have the class defined and will cause a build compile failure
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref srcArray, AtomicSafetyHandle.GetTempUnsafePtrSliceHandle());
#endif

                instanceMesh.SetVertexBufferData(srcArray, 0, 0, data.Length, flags: MeshUpdateFlags.Default);
                srcArray.Dispose();
            }

            fixed(TMP_MeshVertexStream2* verts2 = dataStream2)
            {
                NativeArray<TMP_MeshVertexStream2> srcArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<TMP_MeshVertexStream2>(verts2, dataStream2.Length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Unity is cringe and requires safety handles when using native arrays in editor builds. Release builds don't even have the class defined and will cause a build compile failure
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref srcArray, AtomicSafetyHandle.GetTempUnsafePtrSliceHandle());
#endif

                instanceMesh.SetVertexBufferData(srcArray, 0, 0, dataStream2.Length, 1, flags: MeshUpdateFlags.Default);
                srcArray.Dispose();
            }

            int quadCount = (data.Length / 4);
            if(trianglesBuffer.Length == 0)
            {
                Array.Resize(ref trianglesBuffer, ushort.MaxValue);
                const int quadIndicesCount = ushort.MaxValue / 6;
                for(int i = 0; i < quadIndicesCount; ++i)
                {
                    int index_X4 = i * 4;
                    int index_X6 = i * 6;

                    // Setup Triangles
                    trianglesBuffer[0 + index_X6] = 0 + index_X4;
                    trianglesBuffer[1 + index_X6] = 1 + index_X4;
                    trianglesBuffer[2 + index_X6] = 2 + index_X4;
                    trianglesBuffer[3 + index_X6] = 2 + index_X4;
                    trianglesBuffer[4 + index_X6] = 3 + index_X4;
                    trianglesBuffer[5 + index_X6] = 0 + index_X4;
                }
            }

            instanceMesh.SetTriangles(trianglesBuffer, 0, quadCount * 6, 0, false);
        }
    }
}
