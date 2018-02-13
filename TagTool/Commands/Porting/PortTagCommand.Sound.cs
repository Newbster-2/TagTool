using TagTool.Cache;
using TagTool.Common;
using TagTool.IO;
using TagTool.Legacy.Base;
using TagTool.Serialization;
using TagTool.Tags.Definitions;
using TagTool.Tags.Resources;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TagTool.Tags;

namespace TagTool.Commands.Porting
{
    partial class PortTagCommand
    {
        private SoundCacheFileGestalt BlamSoundGestalt { get; set; } = null;

        private static byte[] CreateXMAHeader(int fileSize, byte channelCount, int sampleRate)
        {
            // Generates a XMA header, adapted from Adjutant

            byte[] header = new byte[60];
            using (var output = new EndianWriter(new MemoryStream(header), EndianFormat.BigEndian))
            {
                output.Write(0x52494646);                   // RIFF
                output.Format = EndianFormat.LittleEndian;
                output.Write(fileSize);
                output.Format = EndianFormat.BigEndian;
                output.Write(0x57415645);                   // WAVE

                // Generate the 'fmt ' chunk
                output.Write(0x666D7420);                   // 'fmt '
                output.Format = EndianFormat.LittleEndian;
                output.Write(0x20);
                output.Write((short)0x165);                 // WAVE_FORMAT_XMA
                output.Write((short)16);                    // 16 bits per sample
                output.Write((short)0);                     // encode options **
                output.Write((short)0);                     // largest skip
                output.Write((short)1);                     // # streams
                output.Write((byte)0);                      // loops
                output.Write((byte)3);                      // encoder version
                output.Write(0);                            // bytes per second **
                output.Write(sampleRate);                   // sample rate
                output.Write(0);                            // loop start
                output.Write(0);                            // loop end
                output.Write((byte)0);                      // subframe loop data
                output.Write(channelCount);                 // channels
                output.Write((short)0x0002);                // channel mask

                // 'data' chunk
                output.Format = EndianFormat.BigEndian;
                output.Write(0x64617461);                   // 'data'
                output.Format = EndianFormat.LittleEndian;
                output.Write((fileSize - 52));              //File offset raw

            }
            return header;
        }

        private static byte[] CreateWAVHeader(int fileSize, short channelCount, int sampleRate)
        {
            byte[] header = new byte[0x2C];
            using (var output = new EndianWriter(new MemoryStream(header), EndianFormat.BigEndian))
            {
                //RIFF header
                output.Write(0x52494646);                   // RIFF
                output.Format = EndianFormat.LittleEndian;
                output.Write(fileSize+0x24);
                output.Format = EndianFormat.BigEndian;
                output.Write(0x57415645);                   // WAVE
                 
                // fmt chunk
                output.Write(0x666D7420);                   // 'fmt '
                output.Format = EndianFormat.LittleEndian;
                output.Write(0x10);                         // Subchunk size (PCM)
                output.Write((short)0x1);                   // PCM Linear quantization
                output.Write(channelCount);                 // Number of channels
                output.Write(sampleRate);                   // Sample rate
                output.Write(sampleRate*channelCount*2);    // Byte rate
                output.Write((short)(channelCount*2));               // Block align
                output.Write((short)0x10);                  // bits per second

                // data chunk
                output.Format = EndianFormat.BigEndian;
                output.Write(0x64617461);                   // 'data'
                output.Format = EndianFormat.LittleEndian;
                output.Write(fileSize);                     // File size

            }

            return header;
        }

        public static string ConvertSoundPermutation(byte[] buffer, int index, int count, int fileSize, byte channelCount, int sampleRate, bool loop)
        {
            Directory.CreateDirectory(@"Temp");

            if(!File.Exists(@"Tools\ffmpeg.exe") || !File.Exists(@"Tools\towav.exe") || !File.Exists(@"Tools\mp3loop.exe"))
            {
                Console.WriteLine("Missing tools, please install all the required tools before porting sounds.");
                return null;
            }

            string tempXMA = @"Temp\permutation.xma";
            string tempWAV = @"Temp\permutation.wav";
            string fixedWAV = @"Temp\permutationTruncated.wav";
            string loopMP3 = @"Temp\permutationTruncated.mp3";
            string tempMP3 = @"Temp\permutation.mp3";

            //If the files are still present, somehow, before the conversion happens, it will stall because ffmpeg doesn't override existing sounds.
            if (File.Exists(tempXMA))
                File.Delete(tempXMA);
            if (File.Exists(tempWAV))
                File.Delete(tempWAV);
            if (File.Exists(fixedWAV))
                File.Delete(fixedWAV);
            if(File.Exists(loopMP3))
                File.Delete(loopMP3);
            if (File.Exists(tempMP3))
                File.Delete(tempMP3);

            try
            {
                using (EndianWriter output = new EndianWriter(File.OpenWrite(tempXMA), EndianFormat.BigEndian))
                {
                    output.Write(CreateXMAHeader(fileSize, channelCount, sampleRate));
                    output.Format = EndianFormat.LittleEndian;
                    output.Write(buffer, index, count);           
                }

                if (channelCount == 1 || channelCount == 2)
                {
                    //Use towav as the conversion is better
                    ProcessStartInfo info = new ProcessStartInfo(@"Tools\towav.exe")
                    {
                        Arguments = tempXMA,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = false,
                        RedirectStandardError = false,
                        RedirectStandardOutput = false,
                        RedirectStandardInput = false
                    };
                    Process towav = Process.Start(info);
                    towav.WaitForExit();
                    File.Move(@"permutation.wav", tempWAV);

                    //towav wav header requires a modification to work with mp3loop

                    byte[] WAVstream = File.ReadAllBytes(tempWAV);
                    int removeBeginning = 1152 * channelCount * (sampleRate / 44100);

                    //Loop will require further testing without the removed bits.

                    if (!loop)
                    {
                        var WAVFileSize = WAVstream.Length - 0x2C - removeBeginning;
                        using (EndianWriter output = new EndianWriter(File.OpenWrite(fixedWAV), EndianFormat.BigEndian))
                        {
                            output.WriteBlock(CreateWAVHeader(WAVFileSize, channelCount, sampleRate));
                            output.Format = EndianFormat.LittleEndian;
                            output.WriteBlock(WAVstream, 0x2C + removeBeginning, WAVFileSize);
                        }
                    }
                    else
                    {
                        var WAVFileSize = WAVstream.Length - 0x2C;
                        using (EndianWriter output = new EndianWriter(File.OpenWrite(fixedWAV), EndianFormat.BigEndian))
                        {
                            output.WriteBlock(CreateWAVHeader(WAVFileSize, channelCount, sampleRate));
                            output.Format = EndianFormat.LittleEndian;
                            output.WriteBlock(WAVstream, 0x2C, WAVFileSize);
                        }
                    }
                }
                else
                {
                    ProcessStartInfo info = new ProcessStartInfo(@"Tools\ffmpeg.exe")
                    {
                        Arguments = "-i " + tempXMA + " " + tempWAV,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = false,
                        RedirectStandardError = false,
                        RedirectStandardOutput = false,
                        RedirectStandardInput = false
                    };
                    Process ffmpeg = Process.Start(info);
                    ffmpeg.WaitForExit();

                    int removeBeginning = 1152 * channelCount * (sampleRate / 44100);
                    uint size = (uint)((new FileInfo(tempWAV).Length) - removeBeginning - 78);       //header size is 78 bytes.
                    byte[] WAVstream = File.ReadAllBytes(tempWAV);
                    var WAVFileSize = WAVstream.Length - 0x4E;
                    using (EndianWriter output = new EndianWriter(File.OpenWrite(fixedWAV), EndianFormat.BigEndian))
                    {
                        output.WriteBlock(CreateWAVHeader(WAVFileSize, channelCount, sampleRate));
                        output.WriteBlock(WAVstream, 0x4E + removeBeginning, (int)size);
                    }
                }

                //Convert to MP3 using ffmpeg or mp3loop

                if (loop)
                {
                    if (channelCount >= 3)
                    {
                        //MP3Loop doesn't handle WAV files with more than 2 channels
                        //fixedWAV now becomes the main audio file, headerless.
                        tempMP3 = fixedWAV;
                        fixedWAV = "~";
                    }
                    else
                    {
                        ProcessStartInfo info = new ProcessStartInfo(@"Tools\mp3loop.exe")
                        {
                            Arguments = fixedWAV,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            UseShellExecute = false,
                            RedirectStandardError = false,
                            RedirectStandardOutput = false,
                            RedirectStandardInput = false
                        };
                        Process mp3loop = Process.Start(info);
                        mp3loop.WaitForExit();
                        tempMP3 = loopMP3;
                    }
                }
                else
                {
                    ProcessStartInfo info = new ProcessStartInfo(@"Tools\ffmpeg.exe")
                    {
                        Arguments = "-i " + fixedWAV + " "+ tempMP3, //No imposed bitrate for now
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = false,
                        RedirectStandardError = false,
                        RedirectStandardOutput = false,
                        RedirectStandardInput = false
                    };
                    Process ffmpeg = Process.Start(info);
                    ffmpeg.WaitForExit();

                    //Remove MP3 header

                    uint size = (uint)new FileInfo(tempMP3).Length - 0x2D;
                    byte[] MP3stream = File.ReadAllBytes(tempMP3);

                    using (Stream output = new FileStream(tempMP3, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        output.Write(MP3stream, 0x2D, (int)size);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("{0} Exception caught. Failed to convert sound.", e);
            }
            finally
            {
                if (File.Exists(tempXMA))
                    File.Delete(tempXMA);
                if (File.Exists(tempWAV))
                    File.Delete(tempWAV);
                if (File.Exists(fixedWAV))
                    File.Delete(fixedWAV);
            }
            return tempMP3;
        }

        private Sound ConvertSound(Sound sound)
        {
            if (BlamSoundGestalt == null)
                BlamSoundGestalt = PortingContextFactory.LoadSoundGestalt(CacheContext, BlamCache);

            if (!File.Exists(@"Tools\ffmpeg.exe") || !File.Exists(@"Tools\mp3loop.exe"))
            {
                Console.WriteLine("Failed to convert sounds, please install ffmpeg and mp3loop in the Helpers folder");
                return null;
            }

            //
            // Convert Blam data to ElDorado data
            //
            
            Console.Write("Converting Halo 3/Halo 3 ODST Sound Data...");

            sound.SoundClass = ((int)sound.SoundClass < 50) ? sound.SoundClass : (sound.SoundClass + 1);
            if (sound.SoundClass == Sound.SoundClassValue.FirstPersonInside)
                sound.SoundClass = Sound.SoundClassValue.InsideSurroundTail;
            if (sound.SoundClass == Sound.SoundClassValue.FirstPersonOutside)
                sound.SoundClass = Sound.SoundClassValue.OutsideSurroundTail;

            sound.SampleRate = BlamSoundGestalt.PlatformCodecs[sound.PlatformCodecIndex].SampleRate;

            var sampleRate = sound.SampleRate == Sound.SampleRateValue._22khz ? 22050 : (sound.SampleRate == Sound.SampleRateValue._44khz ? 44100 : 32000);


            // Testing import type with SingleLayer
            sound.ImportType = Sound.ImportTypeValue.SingleLayer;

            // Assuming these all exists, which should happen in most if not all cases.
            sound.PlaybackParameters = BlamSoundGestalt.PlaybackParameters[sound.PlaybackParameterIndex];

            //
            // Trial for gain leveling (compared to HO)
            //

            sound.PlaybackParameters.GainBase += 1.3f;             

            sound.Scale = BlamSoundGestalt.Scales[sound.ScaleIndex];

            sound.PlatformCodec = BlamSoundGestalt.PlatformCodecs[sound.PlatformCodecIndex];

            
            sound.PlatformCodec.Unknown = 0;

            //
            // Convert Blam promotion to ElDorado format
            //

            sound.Promotion = (sound.PromotionIndex != -1) ?
                BlamSoundGestalt.Promotions[sound.PromotionIndex] :
                new SoundCacheFileGestalt.Promotion();

            //
            // Process all the unique sounds first:
            //

            sound.PitchRanges = new List<SoundCacheFileGestalt.PitchRange> (sound.UniqueSoundCount);

            Directory.CreateDirectory(@"Temp");
            string soundMP3 = @"Temp\soundMP3.mp3";
            if (File.Exists(soundMP3))
                File.Delete(soundMP3);

            uint LargestSampleCount = 0;

            for (int u = 0; u < sound.UniqueSoundCount; u++)
            {
                //Need to get permlist, MaxChunkIndex,MaxIndex etc...

                int firstPermutationIndex = BlamSoundGestalt.PitchRanges[sound.PitchRangeIndex+u].FirstPermutationIndex;

                //Index of the permutation that contains the largest offset
                int maxIndex = 0;

                //Largest offset of a permutation
                uint maxOffset = 0;

                //Get first samplesize

                if(firstPermutationIndex < 0 || firstPermutationIndex >= BlamSoundGestalt.Permutations.Count) 
                    return null;
                uint SumSamples = BlamSoundGestalt.Permutations[firstPermutationIndex].SampleSize;

                //Number of permutation
                int permutationCount = (BlamSoundGestalt.PitchRanges[sound.PitchRangeIndex+u].EncodedPermutationCount >> 4) & 63;

                //Next permutation, if it exists.
                int permutationIndex = firstPermutationIndex + 1;

                int i = 0;

                for (i = 0; i < permutationCount; i++)
                {
                    if (maxOffset <= (BlamSoundGestalt.PermutationChunks[BlamSoundGestalt.Permutations[firstPermutationIndex + i].FirstPermutationChunkIndex].Offset))
                    {
                        maxOffset = (BlamSoundGestalt.PermutationChunks[BlamSoundGestalt.Permutations[firstPermutationIndex + i].FirstPermutationChunkIndex].Offset);
                        maxIndex = firstPermutationIndex + i;
                    }

                    // Add the next samplesize to the total
                    SumSamples = SumSamples + BlamSoundGestalt.Permutations[firstPermutationIndex + i].SampleSize;

                    //Find largest sample count
                    if (LargestSampleCount < BlamSoundGestalt.Permutations[firstPermutationIndex + i].SampleSize)
                        LargestSampleCount = BlamSoundGestalt.Permutations[firstPermutationIndex + i].SampleSize;
                }

                //create an array that contains the ordering of the permutation, sorted by appearance in the ugh!.
                int[] permutationList = new int[permutationCount];

                for (i = 0; i < permutationCount; i++)
                {
                    permutationList[i] = BlamSoundGestalt.Permutations[firstPermutationIndex + i].OverallPermutationIndex;
                }

                sound.Promotion.TotalSampleSize = SumSamples;

                //
                // Convert Blam pitch range to ElDorado format
                //

                var pitchRange = BlamSoundGestalt.PitchRanges[sound.PitchRangeIndex+u];
                pitchRange.ImportName = ConvertStringId(BlamSoundGestalt.ImportNames[pitchRange.ImportNameIndex].Name);
                pitchRange.PitchRangeParameters = BlamSoundGestalt.PitchRangeParameters[pitchRange.PitchRangeParametersIndex];
                pitchRange.Unknown1 = 0;
                pitchRange.Unknown2 = 0;
                pitchRange.Unknown3 = 0;
                pitchRange.Unknown4 = 0;
                pitchRange.Unknown5 = -1;
                pitchRange.Unknown6 = -1;
                //I suspect this unknown7 to be a flag to tell if there is a Unknownblock in extrainfo. (See a sound in udlg for example)
                pitchRange.Unknown7 = 0;
                pitchRange.PermutationCount = (byte)permutationCount;
                pitchRange.Unknown8 = -1;
                sound.PitchRanges.Add(pitchRange);
                sound.PitchRanges[u].Permutations = new List<SoundCacheFileGestalt.Permutation>();

                //
                // Determine the audio channel count
                //

                var channelCount = 0;

                switch (sound.PlatformCodec.Encoding)
                {
                    case Sound.EncodingValue.Mono:
                        channelCount = 1;
                        break;

                    case Sound.EncodingValue.Stereo:
                        channelCount = 2;
                        break;

                    case Sound.EncodingValue.Surround:
                        channelCount = 4;
                        break;

                    case Sound.EncodingValue._51Surround:
                        channelCount = 6;
                        break;
                }

                //
                // Set compression format
                //

                if (((ushort)sound.Flags & (ushort)Sound.FlagsValue.FitToAdpcmBlockSize) != 0 && channelCount >=3)
                    sound.PlatformCodec.Compression = 3;
                else
                    sound.PlatformCodec.Compression = 8;

                //
                // Convert Blam resource data to ElDorado resource data
                //

                int chunkIndex = BlamSoundGestalt.Permutations[maxIndex].FirstPermutationChunkIndex + BlamSoundGestalt.Permutations[maxIndex].PermutationChunkCount - 1;
                int xmaFileSize = (int)(BlamSoundGestalt.PermutationChunks[chunkIndex].Offset + BlamSoundGestalt.PermutationChunks[chunkIndex].Size + 65536 * BlamSoundGestalt.PermutationChunks[chunkIndex].Unknown1);

                //No audio data present

                if (xmaFileSize == -1) 
                    return null;

                var resourceData = BlamCache.GetSoundRaw(sound.ZoneAssetHandle, xmaFileSize);

                if (resourceData == null)
                    return null;

                //
                // Convert Blam permutations to ElDorado format
                //

                permutationIndex = firstPermutationIndex;

                for (i = 0; i < permutationCount; i++)
                {
                    // For the permutation conversion to work properly, we must go through the permutation in chunk order.
                    var permutation = BlamSoundGestalt.Permutations[pitchRange.FirstPermutationIndex + i];

                    permutation.ImportName = ConvertStringId(BlamSoundGestalt.ImportNames[permutation.ImportNameIndex].Name);
                    permutation.SkipFraction = new Bounds<float>(0.0f, permutation.Gain);
                    permutation.PermutationChunks = new List<SoundCacheFileGestalt.PermutationChunk>();
                    permutation.PermutationNumber = (uint)permutationList[i];
                    permutation.IsNotFirstPermutation = (uint)(permutation.PermutationNumber == 0 ? 0 : 1);

                    //
                    // Get size and append MP3
                    //

                    chunkIndex = BlamSoundGestalt.Permutations[permutationIndex].FirstPermutationChunkIndex;
                    int chunkCount = BlamSoundGestalt.Permutations[permutationIndex].PermutationChunkCount;

                    int begin = (int)BlamSoundGestalt.PermutationChunks[chunkIndex].Offset;
                    int count = (int)(BlamSoundGestalt.PermutationChunks[chunkIndex + chunkCount - 1].Offset + BlamSoundGestalt.PermutationChunks[chunkIndex + chunkCount - 1].Size + 65536 * BlamSoundGestalt.PermutationChunks[chunkIndex + chunkCount - 1].Unknown1) - begin;

                    //
                    // Convert XMA permutation to MP3 headerless
                    //

                    var loop = false;
                    if (((ushort)sound.Flags & (ushort)Sound.FlagsValue.FitToAdpcmBlockSize) != 0)
                    {
                        loop = true;
                    }

                    var permutationMP3 = ConvertSoundPermutation(resourceData, begin, count, count + 52, (byte)channelCount, sampleRate, loop);

                    uint permutationChunkSize = 0;

                    //
                    // Copy the permutation mp3 to the overall mp3
                    //

                    byte[] permBuffer = File.ReadAllBytes(permutationMP3);
                    try
                    {
                        using (Stream output = new FileStream(soundMP3, FileMode.Append, FileAccess.Write, FileShare.None))
                        {
                            output.Write(permBuffer, 0, permBuffer.Count());
                            permutationChunkSize = (uint)permBuffer.Count();
                            output.Dispose();
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("{0} Exception caught. Failed to write mp3 to file", e);
                    }

                    if (File.Exists(permutationMP3))
                        File.Delete(permutationMP3);

                    var chunkSize = (ushort)(permutationChunkSize & ushort.MaxValue);

                    var permutationChunk = new SoundCacheFileGestalt.PermutationChunk
                    {
                        Offset = (uint)new FileInfo(soundMP3).Length - permutationChunkSize,
                        Size = chunkSize,
                        Unknown2 = (byte)((permutationChunkSize - chunkSize) / 65536),
                        Unknown3 = 4,
                        RuntimeIndex = -1,
                        UnknownA = 0,
                        UnknownSize = 0
                    };

                    permutation.PermutationChunks.Add(permutationChunk);

                    pitchRange.Permutations.Add(permutation);

                    permutationIndex++;
                }
            }

            sound.Promotion.LongestPermutationDuration = (uint) (1000*((float)LargestSampleCount)/sampleRate);

            //
            // Convert Blam custom playbacks to ElDorado format
            //

            if (sound.CustomPlaybackIndex != -1)
                sound.CustomPlayBacks = new List<SoundCacheFileGestalt.CustomPlayback> { BlamSoundGestalt.CustomPlaybacks[sound.CustomPlaybackIndex] };

            //
            // Convert Blam extra info to ElDorado format
            //

            var extraInfo = new SoundCacheFileGestalt.ExtraInfoBlock()
            {
                LanguagePermutations = new List<SoundCacheFileGestalt.ExtraInfoBlock.LanguagePermutation>()
            };
            
            for(int u = 0; u < sound.UniqueSoundCount; u++)
            {
                var pitchRange = BlamSoundGestalt.PitchRanges[sound.PitchRangeIndex + u];

                var extraInfoBlock = new SoundCacheFileGestalt.ExtraInfoBlock.LanguagePermutation
                {
                    RawInfo = new List<SoundCacheFileGestalt.ExtraInfoBlock.LanguagePermutation.RawInfoBlock>()
                };

                
                for(int i = 0; i < sound.PitchRanges[u].PermutationCount; i++)
                {
                    var rawInfoBlock = new SoundCacheFileGestalt.ExtraInfoBlock.LanguagePermutation.RawInfoBlock
                    {
                        SkipFractionName = StringId.Null,
                        Unknown24 = 480,
                        UnknownList = new List<SoundCacheFileGestalt.ExtraInfoBlock.LanguagePermutation.RawInfoBlock.Unknown>(),
                        Compression = 8,
                        SampleCount = (uint)Math.Floor(pitchRange.Permutations[i].SampleSize * 128000.0 / (8 * sampleRate)),
                        ResourceSampleSize = pitchRange.Permutations[i].SampleSize,
                        ResourceSampleOffset = pitchRange.Permutations[i].PermutationChunks[0].Offset
                    };
                    extraInfoBlock.RawInfo.Add(rawInfoBlock);
                }
                extraInfo.LanguagePermutations.Add(extraInfoBlock);
            }


            extraInfo.Unknown1 = 0;
            extraInfo.Unknown2 = 0;
            extraInfo.Unknown3 = 0;
            extraInfo.Unknown4 = 0;

            //Data ref needs endian swapping

            if (sound.ExtraInfoIndex != -1)
            {
                if (BlamSoundGestalt.ExtraInfo[sound.ExtraInfoIndex].EncodedPermutationSections.Count != 0)
                {
                    extraInfo.EncodedPermutationSections = BlamSoundGestalt.ExtraInfo[sound.ExtraInfoIndex].EncodedPermutationSections;
                }
            }

            sound.ExtraInfo = new List<SoundCacheFileGestalt.ExtraInfoBlock> { extraInfo };

            //
            // Convert Blam languages to ElDorado format
            //

            if (sound.LanguageBIndex != -1)
            {
                sound.Languages = new List<SoundCacheFileGestalt.LanguageBlock>();

                foreach (var language in BlamSoundGestalt.Languages)
                {
                    sound.Languages.Add(new SoundCacheFileGestalt.LanguageBlock
                    {
                        Language = language.Language,
                        PermutationDurations = new List<SoundCacheFileGestalt.LanguageBlock.PermutationDurationBlock>(),
                        PitchRangeDurations = new List<SoundCacheFileGestalt.LanguageBlock.PitchRangeDurationBlock>(),
                    });

                    //Add all the  Pitch Range Duration block (pitch range count dependent)

                    var curLanguage = sound.Languages.Last();

                    for(int i =0; i< sound.UniqueSoundCount; i++)
                    {
                        curLanguage.PitchRangeDurations.Add(language.PitchRangeDurations[sound.LanguageBIndex + i]);
                    }

                    //Add all the Permutation Duration Block and adjust offsets

                    for(int i =0; i < curLanguage.PitchRangeDurations.Count; i++)
                    {
                        var curPRD = curLanguage.PitchRangeDurations[i];

                        //Get current block count for new index
                        short newPermutationIndex = (short)curLanguage.PermutationDurations.Count();

                        for(int j = curPRD.PermutationStartIndex; j< curPRD.PermutationStartIndex + curPRD.PermutationCount; j++)
                        {
                            curLanguage.PermutationDurations.Add(language.PermutationDurations[j]);
                        }

                        //apply new index
                        curPRD.PermutationStartIndex = newPermutationIndex;
                    }

                }
            }

            //
            // Prepare resource
            //
            
            sound.Unused = new byte[] { 0, 0, 0, 0 };
            sound.Unknown12 = 0;

            sound.Resource = new PageableResource
            {
                Page = new RawPage
                {
                    Index = -1,
                },
                Resource = new TagResource
                {
                    Type = TagResourceType.Sound,
                    DefinitionData = new byte[20],
                    DefinitionAddress = new CacheAddress(CacheAddressType.Definition, 536870912),
                    ResourceFixups = new List<TagResource.ResourceFixup>(),
                    ResourceDefinitionFixups = new List<TagResource.ResourceDefinitionFixup>(),
                    Unknown2 = 1
                }
            };

            using (var dataStream = File.OpenRead(soundMP3))
            {
                //Console.Write($"Serializing \"{blamTagName}.sound\" tag data...");

                var resourceContext = new ResourceSerializationContext(sound.Resource);
                CacheContext.Serializer.Serialize(resourceContext,
                    new SoundResourceDefinition
                    {
                        Data = new TagData(soundMP3.Length, new CacheAddress(CacheAddressType.Resource, 0))
                    });

                var definitionFixup = new TagResource.ResourceFixup()
                {
                    BlockOffset = 12,
                    Address = new CacheAddress(CacheAddressType.Resource, 1073741824)
                };
                sound.Resource.Resource.ResourceFixups.Add(definitionFixup);

                CacheContext.AddResource(sound.Resource, ResourceLocation.ResourcesB, dataStream);

                for (int i = 0; i < 4; i++)
                {
                    sound.Resource.Resource.DefinitionData[i] = (byte)(sound.Resource.Page.UncompressedBlockSize >> (i * 8));
                }

                Console.WriteLine("done.");
            }
            
            if (File.Exists(soundMP3))
                File.Delete(soundMP3);
            
            return sound;
        }

        private SoundLooping ConvertSoundLooping(SoundLooping soundLooping, CacheFile blamCache)
        {
            if (BlamSoundGestalt == null)
                BlamSoundGestalt = PortingContextFactory.LoadSoundGestalt(CacheContext, BlamCache);

            soundLooping.Unused = null;

            soundLooping.SoundClass = ((int)soundLooping.SoundClass < 50) ? soundLooping.SoundClass : (soundLooping.SoundClass + 1);

            if (soundLooping.SoundClass == SoundLooping.SoundClassValue.FirstPersonInside)
                soundLooping.SoundClass = SoundLooping.SoundClassValue.InsideSurroundTail;

            if (soundLooping.SoundClass == SoundLooping.SoundClassValue.FirstPersonOutside)
                soundLooping.SoundClass = SoundLooping.SoundClassValue.OutsideSurroundTail;

            // Seems to be an option to make it loop better

            soundLooping.Unknown4 = 1;

            foreach (var track in soundLooping.Tracks)
            {
                if (blamCache.Version == CacheVersion.Halo3Retail)
                {
                    track.Unknown1 = 0;
                    track.Unknown2 = 0;
                    track.Unknown3 = 0;
                    track.Unknown5 = 0;
                    track.Unknown6 = 0;
                }
            }
            
            return soundLooping;
        }
        
        private Dialogue ConvertDialogue(Stream cacheStream, GameCacheContext cacheContext,Dialogue dialogue)
        {
            if (BlamSoundGestalt == null)
                BlamSoundGestalt = PortingContextFactory.LoadSoundGestalt(CacheContext, BlamCache);

            CachedTagInstance edAdlg = null;
            AiDialogueGlobals adlg = null;
            foreach (var tag in cacheContext.TagCache.Index.FindAllInGroup("adlg"))
            {
                edAdlg = tag;
            }
            var context = new TagSerializationContext(cacheStream, cacheContext, edAdlg);
            adlg = cacheContext.Deserializer.Deserialize<AiDialogueGlobals>(context);

            //Create empty udlg vocalization block and fill it with empty blocks matching adlg

            List<Dialogue.Vocalization> newVocalization = new List<Dialogue.Vocalization>();
            foreach(var vocalization in adlg.Vocalizations)
            {
                Dialogue.Vocalization block = new Dialogue.Vocalization
                {
                    Sound = null,
                    Flags = 0,
                    Unknown = 0,
                    Name = vocalization.Name,
                };
                newVocalization.Add(block);
            }

            //Match the tags with the proper stringId

            for(int i = 0; i < 304; i++)
            {
                var vocalization = newVocalization[i];
                for(int j = 0; j < dialogue.Vocalizations.Count; j++)
                {
                    var vocalizationH3 = dialogue.Vocalizations[j];
                    if (cacheContext.StringIdCache.GetString(vocalization.Name).Equals(cacheContext.StringIdCache.GetString(vocalizationH3.Name)))
                    {
                        vocalization.Flags = vocalizationH3.Flags;
                        vocalization.Unknown = vocalizationH3.Unknown;
                        vocalization.Sound = vocalizationH3.Sound;
                        break;
                    }
                }
            }
            
            dialogue.Vocalizations = newVocalization;
            
            return dialogue;
        }

        private SoundMix ConvertSoundMix(CacheFile blamCache,SoundMix soundMix)
        {
            if(blamCache.Version == CacheVersion.Halo3Retail)
                soundMix.Unknown1 = 0;

            return soundMix;
        }
    }
}

