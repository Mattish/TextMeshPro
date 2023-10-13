// ReSharper disable once CheckNamespace

using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace TMPro
{
    public partial class TextMeshPro : TMP_Text, ILayoutElement
    {
        
        // TODO: these should be checked against and sized accordingly to stop over running.
        // As we do pointer stuff with these, if we overrun the editor/game is going to blow up randomly and confusingly.
        private static TMP_MeshVertex[] vertsBuffer = new TMP_MeshVertex[1024 * 4];
        private static FastTextCaseLineInfo[] fastTextCaseLineInfos = new FastTextCaseLineInfo[256];
        
        public unsafe void DoFastTextCaseGenerateTextMesh()
        {
            var ot1 = OperationTimingTarget.Start();
            
            // Setup all our required state...
            m_xAdvance = 0;
            m_lineOffset = 0;
            m_fontColor32 = m_fontColor;
            m_htmlColor = m_fontColor32;
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
            Span<Color32> quadColors = stackalloc Color32[4];
            
            int lineInfoCount = 0;
            
            ref FastTextCaseLineInfo currentLine = ref fastTextCaseLineInfos[0];
            currentLine.Length = 0;
            currentLine.LineYOffset = 0;
            currentLine.TotalWidth = 0;
            
            GetQuadColors(m_overrideHtmlColors ? m_fontColor32 : m_htmlColor, quadColors);
            float4 srcColors; // These are actually uint values being reinterpreted for burst code
            fixed(Color32* clrs2 = quadColors)
            {
                srcColors = *(float4*)clrs2;
            }
            
            m_materialReferenceIndexLookup.Clear();
            
            // ...State setup finished
            
            ot1.Record(ref MattCounter7Value);
            fixed(TMP_MeshVertex* verts = vertsBuffer)
            fixed(TMP_CacheCalculatedCharacter* resolvedChars = m_CharacterResolvedCharacters)
            {                
                var ot = OperationTimingTarget.Start();

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
                        resolvedChars,
                        batch.StartIndex,
                        batch.Length,
                        ref calcPosBurstParams,
                        in srcColors,
                        adjustedScale,
                        xScale,
                        normalSpacingCharacterSpacingOffset
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
                
                ot.Record(ref MattCounter5Value);

                var ot8 = OperationTimingTarget.Start();
                m_characterCount = totalCharacterCount;
                MattCountValue.Value += totalCharacterCount;

                // Finalise this line
                currentLine.TotalWidth = calcPosBurstParams.x * adjustedScale;
                fastTextCaseLineInfos[lineInfoCount++] = currentLine;

                //TODO:
                if(m_characterCount == 0)
                {
                    ClearMesh(true);
                    TMPro_EventManager.ON_TEXT_CHANGED(this);
                    m_IsAutoSizePointSizeSet = true;
                    return;
                }

                // TODO: Tidy this up, it's a branchy mess
                {
                    int materialCount = m_textInfo.materialCount = (int)m_materialReferenceIndexLookup.Length();

                    if(materialCount > m_textInfo.meshInfo.Length)
                    {
                        TMP_TextInfo.Resize(ref m_textInfo.meshInfo, materialCount, false);
                    }

                    if(materialCount > m_subTextObjects.Length)
                    {
                        TMP_TextInfo.Resize(ref m_subTextObjects, Mathf.NextPowerOfTwo(materialCount + 1));
                    }

                    for(int i = 0; i < materialCount; i++)
                    {
                        if(i > 0)
                        {
                            if(m_subTextObjects[i] == null)
                            {
                                m_subTextObjects[i] = TMP_SubMesh.AddSubTextObject(this, m_materialReferences[i]);

                                // Not sure this is necessary
                                m_textInfo.meshInfo[i].vertices = null;
                            }

                            // Check if the material has changed.
                            if(m_subTextObjects[i].sharedMaterial == null || m_subTextObjects[i].sharedMaterial.GetInstanceID() != m_materialReferences[i].material.GetInstanceID())
                            {
                                m_subTextObjects[i].sharedMaterial = m_materialReferences[i].material;
                                m_subTextObjects[i].fontAsset = m_materialReferences[i].fontAsset;
                                m_subTextObjects[i].spriteAsset = m_materialReferences[i].spriteAsset;
                            }

                            // Check if we need to use a Fallback Material
                            if(m_materialReferences[i].isFallbackMaterial)
                            {
                                m_subTextObjects[i].fallbackMaterial = m_materialReferences[i].material;
                                m_subTextObjects[i].fallbackSourceMaterial = m_materialReferences[i].fallbackMaterial;
                            }
                        }

                        int referenceCount = m_materialReferences[i].referenceCount;

                        if(m_textInfo.meshInfo[i].vertices == null || m_textInfo.meshInfo[i].vertices.Length < referenceCount * 4)
                        {
                            if(m_textInfo.meshInfo[i].vertices == null)
                            {
                                if(i == 0)
                                {
                                    m_textInfo.meshInfo[i] = new TMP_MeshInfo(m_mesh, referenceCount + 1);
                                }
                                else
                                {
                                    m_textInfo.meshInfo[i] = new TMP_MeshInfo(m_subTextObjects[i].mesh, referenceCount + 1);
                                }
                            }
                            else
                            {
                                m_textInfo.meshInfo[i].ResizeMeshInfo(referenceCount > 1024 ? referenceCount + 256 : Mathf.NextPowerOfTwo(referenceCount + 1));
                            }
                        }
                        else if(m_VertexBufferAutoSizeReduction && referenceCount > 0 && m_textInfo.meshInfo[i].vertices.Length / 4 - referenceCount > 256)
                        {
                            m_textInfo.meshInfo[i].ResizeMeshInfo(referenceCount > 1024 ? referenceCount + 256 : Mathf.NextPowerOfTwo(referenceCount + 1));
                        }

                        m_textInfo.meshInfo[i].material = m_materialReferences[i].material;
                    }

                    for(int i = materialCount; i < m_subTextObjects.Length && m_subTextObjects[i] != null; i++)
                    {
                        if(i < m_textInfo.meshInfo.Length)
                        {
                            m_textInfo.meshInfo[i].ClearUnusedVertices(0, true);
                        }
                    }

                    for(int i = 0; i < materialIndexToCharCount.Length && i < m_textInfo.meshInfo.Length; i++)
                    {
                        if(materialIndexToCharCount[i] > 0)
                        {
                            int targetVertexCount = materialIndexToCharCount[i] * 4;
                            if(targetVertexCount >= m_textInfo.meshInfo[i].vertices.Length)
                            {
                                m_textInfo.meshInfo[i].ResizeMeshInfo(Mathf.NextPowerOfTwo((targetVertexCount + 4) / 4));
                            }
                        }
                    }
                }
                ot8.Record(ref MattCounter8Value);
                ot1.Record(ref MattCalculateQuadsCounterValue);
                var ot2 = OperationTimingTarget.Start();
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

                // We always have one line
                
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
                            fastTextCaseLineInfos[i].CalculatedAlignmentJustificationOffset.y = (totalHeightOfAllLines);
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
                
                ot2.Record(ref MattCalculateAlignmentCounterValue);
                var ot3 = OperationTimingTarget.Start();
                
                // If we are all the same material, we can blit much faster!
                // This is likely going to be the case for roman alphabets and numbers
                if(m_currentMaterialIndex == 0)
                {
                    fixed(FastTextCaseLineInfo* lines = fastTextCaseLineInfos)
                    {
                        TextMeshProBurst.BurstCompiled_OffsetQuadPositionFull(in verts,in lines, lineInfoCount);
                    }
                }
                else
                {
                    //TODO: Handle cases of multiple matterials properly
                    
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
                
                ot3.Record(ref MattJustificationCounterValue);
                var ot4 = OperationTimingTarget.Start();
                if(m_renderMode == TextRenderFlags.Render && IsActive())
                {
                    OnPreRenderText?.Invoke(m_textInfo);

                    if(m_geometrySortingOrder != VertexSortingOrder.Normal)
                    {
                        m_textInfo.meshInfo[0].SortGeometry(VertexSortingOrder.Reverse);
                    }

                    // Degenerate the remaining vertex
                    int vCount = m_characterCount * 4;
                    long bytesLength = (m_mesh.vertexCount - vCount) * sizeof(TMP_MeshVertex);
                    bytesLength = Math.Clamp(bytesLength, 0, bytesLength);
                    UnsafeUtility.MemClear(&verts[vCount], bytesLength);

                    var ot6 = OperationTimingTarget.Start();
                    UpdateMeshInfo2(m_mesh, vertsBuffer);
                    ot6.Record(ref MattCounter6Value);

                    for (int i = 1; i < m_textInfo.materialCount; i++)
                    {
                        m_textInfo.meshInfo[i].ClearUnusedVertices();

                        if(m_subTextObjects[i] == null)
                        {
                            continue;
                        }

                        if(m_geometrySortingOrder != VertexSortingOrder.Normal)
                        {
                            m_textInfo.meshInfo[i].SortGeometry(VertexSortingOrder.Reverse);
                        }

                        UpdateMeshInfo(m_subTextObjects[i].mesh, ref m_textInfo.meshInfo[i]);
                    }
                }
                    
                TMPro_EventManager.ON_TEXT_CHANGED(this);
                m_IsAutoSizePointSizeSet = true;
                ot4.Record(ref MattUploadMeshDataCounterValue);
            }
        }
        
    }
}