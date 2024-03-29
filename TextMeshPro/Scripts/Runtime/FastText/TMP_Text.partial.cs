﻿using System;
using System.Runtime.CompilerServices;
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
        /// Enables or Disables FastText Optimizations
        /// </summary>
        public bool IsFastTextOptimization
        {
            get { return m_isFastTextOptimization; }
            set { if(m_isFastTextOptimization == value) return; m_isFastTextOptimization = value; m_havePropertiesChanged = true; SetVerticesDirty(); SetLayoutDirty(); }
        }
        [SerializeField]
        protected bool m_isFastTextOptimization = true; // Used to enable or disable FastText Optimizations.

        internal static TMP_CacheCalculatedCharacter[] m_CharacterResolvedCharacters = new TMP_CacheCalculatedCharacter[8];
        internal static FastTextCharacterBatch[] m_CharacterBatches = new FastTextCharacterBatch[4];
        internal static int m_CharacterBatchCount;

        [Flags]
        internal enum FastTextBatchTypeFlag
        {
            None = 0,
            LineBreak = 1 << 0,
            Material = 1 << 1
        }

        internal struct FastTextCharacterBatch
        {
            public FastTextBatchTypeFlag BatchTypeFlag;
            public int StartIndex;
            public int Length;
            public byte AtlasIndex;
        }

        protected void PopulateTextProcessingArrayFastText()
        {
            OperationTimingTarget op = OperationTimingTarget.Start();
            int srcLength = m_text.Length;
            m_CharacterBatchCount = 0;

            if(m_CharacterResolvedCharacters.Length < srcLength)
            {
                Array.Resize(ref m_CharacterResolvedCharacters, (int)(srcLength * 1.5));
            }

            ref FastTextCharacterBatch characterBatch = ref m_CharacterBatches[0];
            characterBatch.BatchTypeFlag = FastTextBatchTypeFlag.Material;
            characterBatch.Length = 0;
            characterBatch.AtlasIndex = 0;

            if(srcLength == 0)
            {
                return;
            }
#if UNITY_EDITOR
            // This is required due to moving from play/edit
            if(m_fontAsset.m_CachedCalculatedCharacterLookup == null)
            {
                m_fontAsset.ReadFontAssetDefinition();
            }
#endif

            //TODO: cache this value elsewhere rather then every call
            uint missingCharacterUnicode = (uint)TMP_Settings.missingGlyphCharacter == 0 ? 9633 : (uint)TMP_Settings.missingGlyphCharacter;
            ref TMP_CacheCalculatedCharacter missingCalculatedCharacter = ref TMP_FontAssetUtilities.TryGetCharacterFromFontAsset_DirectRef(missingCharacterUnicode, m_fontAsset, out _);
            int countResolvedCharacters = 0;
            uint nextCharacter = m_text[0];
            for(int i = 0; i < srcLength - 1; ++i)
            {
                uint unicode = nextCharacter;
                nextCharacter = m_text[i + 1];
                ref TMP_CacheCalculatedCharacter calculatedCharacter = ref TMP_FontAssetUtilities.TryGetCharacterFromFontAsset_DirectRef(unicode, m_fontAsset, out bool found);
                if(!found)
                {
                    //TODO: ?
                    //DoMissingGlyphCallback((int)unicode, textProcessingArray[i].stringIndex, m_currentFontAsset);
                    unicode = missingCharacterUnicode;
                    calculatedCharacter = ref missingCalculatedCharacter;
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

                FastTextBatchTypeFlag resultingFlag = ProcessForBatchBreak(ref characterBatch, unicode, calculatedCharacter);

                // Only populate resolved characters if it's not a linebreak
                if((resultingFlag & FastTextBatchTypeFlag.LineBreak) == 0)
                {
                    ref TMP_CacheCalculatedCharacter target = ref m_CharacterResolvedCharacters[countResolvedCharacters++];
                    target.GlyphMetrics4 = calculatedCharacter.GlyphMetrics4;
                    target.GlyphBox = calculatedCharacter.GlyphBox;
                    target.GlyphHorizontalAdvance = calculatedCharacter.GlyphHorizontalAdvance;
                    target.AtlasIndex = calculatedCharacter.AtlasIndex;
                    target.uvAtlasReciprocal = calculatedCharacter.uvAtlasReciprocal;

                    ++characterBatch.Length;
                }
            }

            // Process now the final character
            {
                uint unicode = m_text[srcLength - 1];
                ref TMP_CacheCalculatedCharacter calculatedCharacter = ref TMP_FontAssetUtilities.TryGetCharacterFromFontAsset_DirectRef(unicode, m_fontAsset, out bool found);
                if(!found)
                {
                    //TODO: ?
                    //DoMissingGlyphCallback((int)unicode, textProcessingArray[i].stringIndex, m_currentFontAsset);
                    unicode = missingCharacterUnicode;
                    calculatedCharacter = ref missingCalculatedCharacter;
                    // If we don't have a missing glyph character here, we're donezo
                }

                FastTextBatchTypeFlag resultingFlag = ProcessForBatchBreak(ref characterBatch, unicode, calculatedCharacter);

                // Only populate resolved characters if it's not a linebreak
                if((resultingFlag & FastTextBatchTypeFlag.LineBreak) == 0)
                {
                    m_CharacterResolvedCharacters[countResolvedCharacters++] = calculatedCharacter;
                    ++characterBatch.Length;
                }
            }

            m_CharacterBatchCount++;

            op.Record(ref TextMeshPro.MattPopulateTextProcessingArrayFastTextCounterValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FastTextBatchTypeFlag ProcessForBatchBreak(ref FastTextCharacterBatch characterBatch, uint unicode, TMP_CacheCalculatedCharacter calculatedCharacter)
        {
            bool atlasIndexChanging = characterBatch.AtlasIndex != calculatedCharacter.AtlasIndex;
            bool isLineBreak = unicode == '\n';

            FastTextBatchTypeFlag resultingFlag = isLineBreak ? FastTextBatchTypeFlag.LineBreak : FastTextBatchTypeFlag.None;

            // If we have any flags, finish up this batch
            if(isLineBreak || atlasIndexChanging)
            {
                int nextStartIndex = characterBatch.StartIndex + characterBatch.Length;
                characterBatch.BatchTypeFlag |= isLineBreak ? FastTextBatchTypeFlag.LineBreak : FastTextBatchTypeFlag.None;

                if(m_CharacterBatches.Length < m_CharacterBatchCount + 2)
                {
                    Array.Resize(ref m_CharacterBatches, m_CharacterBatchCount * 2);
                }

                characterBatch = ref m_CharacterBatches[++m_CharacterBatchCount];
                characterBatch.Length = 0;
                characterBatch.BatchTypeFlag = atlasIndexChanging ? FastTextBatchTypeFlag.Material : FastTextBatchTypeFlag.None;
                characterBatch.StartIndex = nextStartIndex;
                characterBatch.AtlasIndex = (byte)calculatedCharacter.AtlasIndex;
            }

            return resultingFlag;
        }
    }
}
