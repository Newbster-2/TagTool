﻿using System.Collections.Generic;
using TagTool.Cache;
using TagTool.Common;
using TagTool.Tags;
using static TagTool.Tags.TagFieldFlags;

namespace TagTool.Audio
{
    [TagStructure(Size = 0xC, MinVersion = CacheVersion.Halo3Beta, MaxVersion = CacheVersion.Halo3ODST)]
    [TagStructure(Size = 0x28, MinVersion = CacheVersion.HaloOnline106708, MaxVersion = CacheVersion.HaloOnline700123)]
    [TagStructure(Size = 0x8, MinVersion = CacheVersion.HaloReach)]
    public class ExtraInfo : TagStructure
	{
        [TagField(Gen = CacheGeneration.HaloOnline)]
        public List<LanguagePermutation> LanguagePermutations;

        [TagField(MinVersion = CacheVersion.Halo3Beta, MaxVersion = CacheVersion.HaloOnline700123)]
        public List<EncodedPermutationSection> EncodedPermutationSections;

        [TagField(Gen = CacheGeneration.HaloOnline)]
        public uint Unknown1;
        [TagField(Gen = CacheGeneration.HaloOnline)]
        public uint Unknown2;
        [TagField(Gen = CacheGeneration.HaloOnline)]
        public uint Unknown3;
        [TagField(Gen = CacheGeneration.HaloOnline)]
        public uint Unknown4;

        [TagField(MinVersion = CacheVersion.HaloReach)]
        public TagResourceReference FacialAnimationResource;


        [TagStructure(Size = 0xC)]
        public class LanguagePermutation : TagStructure
		{
            public List<RawInfoBlock> RawInfo;

            [TagStructure(Size = 0x7C)]
            public class RawInfoBlock : TagStructure
			{
                public StringId SkipFractionName;
                public uint Unknown1;
                public uint Unknown2;
                public uint Unknown3;
                public uint Unknown4;
                public uint Unknown5;
                public uint Unknown6;
                public uint Unknown7;
                public uint Unknown8;
                public uint Unknown9;
                public uint Unknown10;
                public uint Unknown11;
                public uint Unknown12;
                public uint Unknown13;
                public uint Unknown14;
                public uint Unknown15;
                public uint Unknown16;
                public uint Unknown17;
                public uint Unknown18;
                public List<SeekTableBlock> SeekTable;
                public short Compression;
                public byte Language;
                public byte Unknown19;
                public uint ResourceSampleSize;
                public uint ResourceSampleOffset;
                public uint SampleCount;
                public uint Unknown20;
                public uint Unknown21;
                public uint Unknown22;
                public uint Unknown23;
                public int Unknown24;

                [TagStructure(Size = 0x18)]
                public class SeekTableBlock : TagStructure
				{
                    public uint BlockRelativeSampleStart;
                    public uint BlockRelativeSampleEnd;
                    public uint StartingSampleIndex;
                    public uint EndingSampleIndex;
                    public uint StartingOffset;
                    public uint EndingOffset;
                }
            }
        }

        [TagStructure(Size = 0x2C)]
        public class EncodedPermutationSection : TagStructure
		{
            public byte[] EncodedData;
            public List<SoundDialogueInfoBlock> SoundDialogueInfo;
            public List<FacialAnimationDataBlock> FacialAnimationData;

            [TagStructure(Size = 0x10)]
            public class SoundDialogueInfoBlock : TagStructure
			{
                public uint MouthDataOffset;
                public uint MouthDataLength;
                public uint LipsyncDataOffset;
                public uint LipsyncDataLength;
            }

            [TagStructure(Size = 0xC)]
            public class FacialAnimationDataBlock : TagStructure
			{
                public List<FacialAnimationPermutation> FacialAnimationPermutations;

                [TagStructure(Size = 0x28)]
                public class FacialAnimationPermutation : TagStructure
				{
                    public float StartTime;
                    public float EndTime;
                    public float BlendIn;
                    public float BlendOut;
                    [TagField(Flags = TagFieldFlags.Padding, Length = 0xC)]
                    public byte[] Pad = new byte[0xC];
                    public List<FacialAnimationCurve> FacialAnimationCurves;

                    [TagStructure(Size = 0x8)]
                    public class FacialAnimationCurve : TagStructure
					{
                        public short Time;
                        public FacialAnimationTrack Track1;
                        public FacialAnimationTrack Track2;
                        public FacialAnimationTrack Track3;
                        public sbyte Track1Weight;
                        public sbyte Track2Weight;
                        public sbyte Track3Weight;

                        public enum FacialAnimationTrack : byte
                        {
                            Silence,
                            Eat,
                            Earth,
                            If,
                            Ox,
                            Oat,
                            Wet,
                            Size,
                            Church,
                            Fave,
                            Though,
                            Told,
                            Bump,
                            New,
                            Roar,
                            Cage,
                            Eyebrow_Raise,
                            Blink,
                            Orientation_Head_Pitch,
                            Orientation_Head_Roll,
                            Orientation_Head_Yaw,
                            Emphasis_Head_Pitch,
                            Emphasis_Head_Roll,
                            Emphasis_Head_Yaw,
                            Gaze_Eye_Pitch,
                            Gaze_Eye_Yaw,
                            happy,
                            sad,
                            angry,
                            disgusted,
                            scared,
                            surprised,
                            pain,
                            shout
                        }
                    }
                }
            }
        }
    }
}