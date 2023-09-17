using System;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.UI;

// ReSharper disable once CheckNamespace
namespace TMPro
{
    // ReSharper disable once InconsistentNaming
    public abstract partial class TMP_Text : MaskableGraphic
    {
        /// <summary>
        /// Enables or Disables Mattish Optimizations
        /// </summary>
        public bool isMattishOptimization
        {
            get { return m_isMattishOptimization; }
            set { if (m_isMattishOptimization == value) return; m_isMattishOptimization = value; m_havePropertiesChanged = true; SetVerticesDirty(); SetLayoutDirty(); }
        }
        [SerializeField]
        protected bool m_isMattishOptimization = true; // Used to enable or disable Mattish Optimizations.
        
        internal TMP_CacheCalculatedCharacter[] m_CharacterResolvedCharacters = new TMP_CacheCalculatedCharacter[8];
        internal MattishCharacterBatch[] m_CharacterBatches = new MattishCharacterBatch[4];
        internal int m_CharacterBatchCount;
        
        [Flags]
        internal enum MattishBatchTypeFlag
        {
            None,
            LineBreak,
            Material
        }
    
        internal struct MattishCharacterBatch
        {
            public MattishBatchTypeFlag BatchTypeFlag;
            public int StartIndex;
            public int Length;
            public byte AtlasIndex;
        }
        
        protected void PopulateTextProcessingArrayMattish()
        {
            OperationTimingTarget op = OperationTimingTarget.Start();
            int srcLength = m_TextBackingArray.Count;
            m_CharacterBatchCount = 0;

            if(m_CharacterResolvedCharacters.Length < srcLength)
            {
                Array.Resize(ref m_CharacterResolvedCharacters, (int)(srcLength * 1.5));
            }

            ref MattishCharacterBatch characterBatch = ref m_CharacterBatches[0];
            characterBatch.BatchTypeFlag = MattishBatchTypeFlag.Material;
            characterBatch.Length = 0;
            characterBatch.AtlasIndex = 0;

            if(srcLength == 0)
            {
                return;
            }
            
            int countResolvedCharacters = 0;
            uint nextCharacter = m_TextBackingArray[0];
            for(int i = 0; i < srcLength - 1; ++i)
            {
                uint unicode = nextCharacter;
                nextCharacter = m_TextBackingArray[i + 1];
                ref TMP_CacheCalculatedCharacter calculatedCharacter = ref TMP_FontAssetUtilities.TryGetCharacterFromFontAsset_DirectRef(unicode, m_fontAsset, out bool found);
                if(!found)
                {
                    //TODO: ?
                    //DoMissingGlyphCallback((int)unicode, textProcessingArray[i].stringIndex, m_currentFontAsset);
                    unicode = (uint)TMP_Settings.missingGlyphCharacter == 0 ? 9633 : (uint)TMP_Settings.missingGlyphCharacter;
                    calculatedCharacter = ref TMP_FontAssetUtilities.TryGetCharacterFromFontAsset_DirectRef(unicode, m_fontAsset, out found);
                    // If we don't have a missing glyph character here, we're donezo
                }
                unchecked
                {
                    //nextCharacter >= 0xFE00 && nextCharacter <= 0xFE0F
                    if((nextCharacter - 0xFE0F) <= 0x0F)
                    {
                        uint variantGlyphIndex = m_currentFontAsset.GetGlyphVariantIndex((uint)unicode, nextCharacter);

                        if(variantGlyphIndex != 0)
                        {
                            if(m_currentFontAsset.TryAddGlyphInternal(variantGlyphIndex, out Glyph glyph))
                            {
                                calculatedCharacter = TMP_CacheCalculatedCharacter.Calcuate(glyph, m_fontAsset.m_AtlasHeight);
                            }
                        }

                        ++i;
                        // This only handles single glyph variants, I guess?
                        //TODO: Handle changed variations? Are they even a thing? I assume they are with how emojis work
                    }
                }

                bool atlasIndexChanging = characterBatch.AtlasIndex != calculatedCharacter.AtlasIndex;
                bool isLineBreak = unicode == '\n';

                MattishBatchTypeFlag resultingFlag = isLineBreak ? MattishBatchTypeFlag.LineBreak : MattishBatchTypeFlag.None;
                
                // If we have any flags, finish up this batch
                if(isLineBreak || atlasIndexChanging)
                {
                    int nextStartIndex = characterBatch.StartIndex + characterBatch.Length;
                    characterBatch.BatchTypeFlag |= isLineBreak ? MattishBatchTypeFlag.LineBreak : MattishBatchTypeFlag.None;

                    if(m_CharacterBatches.Length < m_CharacterBatchCount + 2)
                    {
                        Array.Resize(ref m_CharacterBatches, m_CharacterBatchCount * 2);
                    }

                    characterBatch = ref m_CharacterBatches[++m_CharacterBatchCount];
                    characterBatch.Length = 0;
                    characterBatch.BatchTypeFlag = atlasIndexChanging ? MattishBatchTypeFlag.Material : MattishBatchTypeFlag.None;
                    characterBatch.StartIndex = nextStartIndex;
                }
                
                // Only populate resolved characters if it's not a linebreak
                if((resultingFlag & MattishBatchTypeFlag.LineBreak) == 0)
                {
                    m_CharacterResolvedCharacters[countResolvedCharacters++] = calculatedCharacter;
                    ++characterBatch.Length;
                }

            }

            // Process now the final character
            {
                uint unicode = m_TextBackingArray[srcLength - 1];
                ref TMP_CacheCalculatedCharacter calculatedCharacter = ref TMP_FontAssetUtilities.TryGetCharacterFromFontAsset_DirectRef(unicode, m_fontAsset, out bool found);
                if(!found)
                {
                    //TODO: ?
                    //DoMissingGlyphCallback((int)unicode, textProcessingArray[i].stringIndex, m_currentFontAsset);
                    unicode = (uint)TMP_Settings.missingGlyphCharacter == 0 ? 9633 : (uint)TMP_Settings.missingGlyphCharacter;
                    calculatedCharacter = ref TMP_FontAssetUtilities.TryGetCharacterFromFontAsset_DirectRef(unicode, m_fontAsset, out found);
                    // If we don't have a missing glyph character here, we're donezo
                }

                bool atlasIndexChanging = characterBatch.AtlasIndex != calculatedCharacter.AtlasIndex;
                bool isLineBreak = unicode == '\n';
                
                MattishBatchTypeFlag resultingFlag = isLineBreak ? MattishBatchTypeFlag.LineBreak : MattishBatchTypeFlag.None;
                
                // If we have any flags, finish up this batch
                if(isLineBreak || atlasIndexChanging)
                {
                    int nextStartIndex = characterBatch.StartIndex + characterBatch.Length;
                    characterBatch.BatchTypeFlag |= isLineBreak ? MattishBatchTypeFlag.LineBreak : MattishBatchTypeFlag.None;

                    if(m_CharacterBatches.Length < m_CharacterBatchCount + 2)
                    {
                        Array.Resize(ref m_CharacterBatches, m_CharacterBatchCount * 2);
                    }

                    characterBatch = ref m_CharacterBatches[++m_CharacterBatchCount];
                    characterBatch.Length = 0;
                    characterBatch.BatchTypeFlag = atlasIndexChanging ? MattishBatchTypeFlag.Material : MattishBatchTypeFlag.None;
                    characterBatch.StartIndex = nextStartIndex;
                }
                
                // Only populate resolved characters if it's not a linebreak
                if((resultingFlag & MattishBatchTypeFlag.LineBreak) == 0)
                {
                    m_CharacterResolvedCharacters[countResolvedCharacters++] = calculatedCharacter;
                    ++characterBatch.Length;
                }
            }

            m_CharacterBatchCount++;
            
            op.Record(ref TextMeshPro.MattProcessCharacterArrayCounterValue);
        }

    }
}