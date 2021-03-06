﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using TagTool.IO;
using TagTool.Serialization;
using TagTool.Cache.HaloOnline;
using TagTool.BlamFile;
using TagTool.Tags;
using System.Runtime.InteropServices;
using TagTool.Common;
using System.Text.RegularExpressions;

namespace TagTool.Cache
{
    public class ModPackage
    {
        public ModPackageHeader Header { get; set; } = new ModPackageHeader();

        public ModPackageMetadata Metadata { get; set; } = new ModPackageMetadata();

        public List<ModPackageStream> TagCachesStreams { get; set; } = new List<ModPackageStream>();

        public List<Dictionary<int, string>> TagCacheNames { get; set; } = new List<Dictionary<int, string>>();

        public ModPackageStream ResourcesStream { get; set; }

        public List<Stream> MapFileStreams { get; set; } = new List<Stream>();

        public Stream CampaignFileStream { get; set; } = new MemoryStream();

        public Dictionary<int, int> MapToCacheMapping { get; set; } = new Dictionary<int, int>();

        public List<int> MapIds = new List<int>();

        public List<string> CacheNames { get; set; } = new List<string>();

        public StringTableHaloOnline StringTable { get; set; }

        public Stream FontPackage;

        public Dictionary<string, Stream> Files { get; set; }

        private int TagCacheCount => CacheNames.Count; // find safer solution?

        public int GetTagCacheCount() => TagCacheCount;

        ~ModPackage()
        {
            foreach(var stream in TagCachesStreams)
            {
                stream.SetDisposable(true);
                stream.Dispose();
            }
            ResourcesStream.SetDisposable(true);
            ResourcesStream.Dispose();
        }

        public ModPackage(FileInfo file = null, bool unmanagedResourceStream=false)
        {
            if (file != null)
                Load(file);
            else
            {
                // init a single cache
                var tagStream = new MemoryStream();
                TagCachesStreams.Add(new ModPackageStream(tagStream));

                FontPackage = new MemoryStream();

                var names = new Dictionary<int, string>();
                TagCacheNames.Add(names);
                CacheNames.Add("default");
                Files = new Dictionary<string, Stream>();
                StringTable = new StringTableHaloOnline(CacheVersion.HaloOnline106708, null);
                Header.SectionTable = new ModPackageSectionTable();
                if (!unmanagedResourceStream)
                {
                    ResourcesStream = new ModPackageStream(new MemoryStream());
                }
                else
                {
                    unsafe
                    {
                        long bufferSize = 4L * 1024 * 1024 * 1024; // 4 GB max
                        IntPtr data = Marshal.AllocHGlobal((IntPtr)bufferSize);
                        ResourcesStream = new ModPackageStream(new UnmanagedMemoryStream((byte*)data.ToPointer(), 0, bufferSize, FileAccess.ReadWrite));
                    }
                }
                
            }
        }

        public void AddMap(Stream mapStream, int mapId, int cacheIndex)
        {
            var mapFileIndex = MapIds.IndexOf(mapId);
            if (mapFileIndex != -1)
            {
                mapStream.Position = 0;
                MapFileStreams[mapFileIndex] = mapStream;
            }
            else
            {
                MapFileStreams.Add(mapStream);
                MapToCacheMapping.Add(MapFileStreams.Count - 1, cacheIndex);
                MapIds.Add(mapId);
            }
        }

        public void Load(FileInfo file)
        {
            if (!file.Exists)
                throw new FileNotFoundException(file.FullName);

            if (file.Length < typeof(ModPackageHeader).GetSize())
                throw new FormatException(file.FullName);

            using (var stream = file.OpenRead())
            using (var reader = new EndianReader(stream, leaveOpen: true))
            {
                var dataContext = new DataSerializationContext(reader);
                var deserializer = new TagDeserializer(CacheVersion.HaloOnline106708);

                Header = deserializer.Deserialize<ModPackageHeader>(dataContext);

                ReadMetadataSection(reader, dataContext, deserializer);
                ReadTagsSection(reader, dataContext, deserializer);
                ReadTagNamesSection(reader, dataContext, deserializer);
                ReadResourcesSection(reader);
                ReadMapFilesSection(reader);
                ReadCampaignFileSection(reader);
                ReadStringIdSection(reader);
                ReadFontSection(reader);
                ReadFileEntries(reader, dataContext, deserializer);
            }
        }

        public void Save(FileInfo file)
        {
            if (!file.Directory.Exists)
                file.Directory.Create();

            using (var packageStream = file.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite))
            using (var writer = new EndianWriter(packageStream, leaveOpen: true))
            {
                var serializer = new TagSerializer(CacheVersion.HaloOnline106708);
                var dataContext = new DataSerializationContext(writer);
                
                packageStream.SetLength(0);

                //
                // reserve header space
                //

                writer.Write(new byte[typeof(ModPackageHeader).GetSize()]);

                //
                // build section table
                //

                Header.SectionTable.Count = (int)ModPackageSection.SectionCount;
                Header.SectionTable.Offset = (uint)writer.BaseStream.Position;

                writer.Write(new byte[typeof(ModPackageSectionHeader).GetSize() * (int)ModPackageSection.SectionCount]);


                uint size;
                uint offset;

                //
                // Write metadata
                //

                offset = (uint)writer.BaseStream.Position;
                WriteMetadataSection(dataContext, serializer);
                size = (uint)(writer.BaseStream.Position - offset);
                WriteSectionEntry((int)ModPackageSection.Metadata, writer, size, offset);

                //
                // Write tag cache
                //

                offset = (uint)writer.BaseStream.Position;
                WriteTagsSection(writer, dataContext, serializer);
                size = (uint)(writer.BaseStream.Position - offset);
                WriteSectionEntry((int)ModPackageSection.Tags, writer, size, offset);

                // 
                // Write tag names table
                //

                offset = (uint)writer.BaseStream.Position;
                WriteTagNamesSection(writer, dataContext, serializer);
                size = (uint)(writer.BaseStream.Position - offset);
                WriteSectionEntry((int)ModPackageSection.TagNames, writer, size, offset);

                //
                // write resource cache
                //

                offset = (uint)writer.BaseStream.Position;
                WriteResourcesSection(writer);
                size = (uint)(writer.BaseStream.Position - offset);
                StreamUtil.Align(writer.BaseStream, 4);
                WriteSectionEntry((int)ModPackageSection.Resources, writer, size, offset);

                //
                // Write map file section
                //

                if(MapFileStreams.Count > 0)
                {
                    offset = (uint)writer.BaseStream.Position;
                    WriteMapsSection(writer);
                    size = (uint)(writer.BaseStream.Position - offset);
                    WriteSectionEntry((int)ModPackageSection.MapFiles, writer, size, offset);

                    DetermineMapFlags();
                }

                //
                // Write campaign file section
                // 
                if(CampaignFileStream != null && CampaignFileStream.Length > 0)
                {
                    offset = (uint)writer.BaseStream.Position;
                    WriteCampaignFileSection(writer);
                    size = (uint)(writer.BaseStream.Position - offset);
                    StreamUtil.Align(writer.BaseStream, 4);
                    WriteSectionEntry((int)ModPackageSection.CampaignFiles, writer, size, offset);
                }

                //
                // Write font file section
                // 
                if (FontPackage != null && FontPackage.Length > 0)
                {
                    offset = (uint)writer.BaseStream.Position;
                    WriteFontFileSection(writer);
                    size = (uint)(writer.BaseStream.Position - offset);
                    StreamUtil.Align(writer.BaseStream, 4);
                    WriteSectionEntry((int)ModPackageSection.Fonts, writer, size, offset);
                }

                //
                // Write stringid file section
                // 
                if (StringTable != null)
                {
                    offset = (uint)writer.BaseStream.Position;
                    WriteStringIdsSection(writer);
                    size = (uint)(writer.BaseStream.Position - offset);
                    WriteSectionEntry((int)ModPackageSection.StringIds, writer, size, offset);
                }

                //
                // Write files section
                // 
                if (Files != null && Files.Count > 0)
                {
                    offset = (uint)writer.BaseStream.Position;
                    WriteFileEntries(writer, dataContext, serializer);
                    size = (uint)(writer.BaseStream.Position - offset);
                    WriteSectionEntry((int)ModPackageSection.Files, writer, size, offset);
                }

                //
                // calculate package sha1
                //

                packageStream.Position = typeof(ModPackageHeader).GetSize();
                Header.SHA1 = new SHA1Managed().ComputeHash(packageStream);

                //
                // Sign the package using the ED profile keys
                //

                byte[] privateKey, publicKey;
                if(TryReadElDewritoProfileKeys(out privateKey, out publicKey))
                {
                    SignPackage(Header, privateKey, publicKey);
                    Header.ModifierFlags |= ModifierFlags.SignedBit;
                }

                //
                // update package header
                //

                Header.FileSize = (uint)packageStream.Length;

                packageStream.Position = 0;
                serializer.Serialize(dataContext, Header);

                if (packageStream.Length > uint.MaxValue)
                    Console.WriteLine($"WARNING: Mod package size exceeded 0x{uint.MaxValue.ToString("X8")} bytes, it will fail to load.");

            }
        }

        private bool TryReadElDewritoProfileKeys(out byte[] privateKey, out byte[] publicKey)
        {
            privateKey = publicKey = null;

            var keysPath = Path.Combine(Environment.GetEnvironmentVariable("LocalAppData"), "ElDewrito\\keys.cfg");
            if (!File.Exists(keysPath))
                return false;

            var cfg = File.ReadAllText(keysPath);
            privateKey = Convert.FromBase64String(Regex.Match(cfg, "Player.PrivKey\\s+\"([^\"]+)").Groups[1].Value);
            publicKey = Convert.FromBase64String(Regex.Match(cfg, "Player.PubKey\\s+\"([^\"]+)").Groups[1].Value);

            return true;
        }

        private void SignPackage(ModPackageHeader header, byte[] privateKey, byte[] publicKey)
        {
            var rsa = RSAUtil.DecodeRSAPrivateKey(privateKey);
            header.RSAPublicKey = publicKey;
            header.RSASignature = rsa.SignHash(header.SHA1, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }

        private static void CopyStreamChunk(EndianReader reader, ModPackageStream stream, uint size)
        {
            var bufferSize = 0x14000;
            var remaining = size;
            while(remaining > 0)
            {
                int readSize = remaining > bufferSize ? bufferSize : (int)remaining;
                var tempData = new byte[readSize];
                reader.ReadBlock(tempData, 0, readSize);
                stream.Write(tempData, 0, readSize);
                remaining -= (uint)readSize;
            }
        }

        private void WriteSectionEntry(int index, EndianWriter writer, uint size, uint offset)
        {
            // seek to the table and update size and offset
            writer.BaseStream.Seek(Header.SectionTable.Offset + typeof(ModPackageSectionHeader).GetSize() * index, SeekOrigin.Begin);
            var tableEntry = new ModPackageSectionHeader(size, offset);
            tableEntry.Write(writer);
            writer.BaseStream.Seek(0, SeekOrigin.End);
        }

        private void WriteMapsSection(EndianWriter writer)
        {
            uint sectionOffset = (uint)writer.BaseStream.Position;
            uint sectionEntrySize = TagStructure.GetStructureSize(typeof(GenericSectionEntry));
            int cacheMapEntrySize = (int)TagStructure.GetStructureSize(typeof(CacheMapTableEntry));
            GenericSectionEntry mapEntry = new GenericSectionEntry(MapFileStreams.Count, sectionEntrySize);
            mapEntry.Write(writer);
            // make room for table

            writer.Write(new byte[cacheMapEntrySize * mapEntry.Count]);

            for(int i = 0; i < MapFileStreams.Count; i++)
            {
                var mapFileStream = MapFileStreams[i];
                uint offset = (uint)writer.BaseStream.Position;
                int size = (int)mapFileStream.Length;

                mapFileStream.Position = 0;
                StreamUtil.Copy(mapFileStream, writer.BaseStream, (int)mapFileStream.Length);
                StreamUtil.Align(writer.BaseStream, 4);

                // seek to the table and update size and offset
                long originalPos = writer.BaseStream.Position;
                writer.BaseStream.Seek(mapEntry.TableOffset + cacheMapEntrySize * i + sectionOffset, SeekOrigin.Begin);
                var tableEntry = new CacheMapTableEntry(size, offset - sectionOffset, MapToCacheMapping[i], MapIds[i]);
                tableEntry.Write(writer);
                writer.BaseStream.Seek(originalPos, SeekOrigin.Begin);
            }
        }

        private void WriteTagsSection(EndianWriter writer, DataSerializationContext context, TagSerializer serializer)
        {
            uint sectionOffset = (uint)writer.BaseStream.Position;
            uint sectionEntrySize = TagStructure.GetStructureSize(typeof(GenericSectionEntry));
            int tagCacheEntrySize = (int)TagStructure.GetStructureSize(typeof(CacheTableEntry));
            GenericSectionEntry tagCachesEntry = new GenericSectionEntry(TagCacheCount, sectionEntrySize);
            tagCachesEntry.Write(writer);
            // make room for table

            writer.Write(new byte[tagCacheEntrySize * TagCacheCount]);

            for(int i = 0; i < TagCacheCount; i++)
            {

                uint offset = (uint)writer.BaseStream.Position;

                TagCachesStreams[i].Position = 0;
                StreamUtil.Copy(TagCachesStreams[i], writer.BaseStream, (int)TagCachesStreams[i].Length);
                StreamUtil.Align(writer.BaseStream, 4);

                uint size = (uint)(writer.BaseStream.Position - offset);

                long originalPos = writer.BaseStream.Position;
                writer.BaseStream.Seek(tagCachesEntry.TableOffset + tagCacheEntrySize * i + sectionOffset, SeekOrigin.Begin);
                var tableEntry = new CacheTableEntry(size, offset - sectionOffset, CacheNames[i]);
                serializer.Serialize(context, tableEntry);
                writer.BaseStream.Seek(originalPos, SeekOrigin.Begin);
            }
        }

        private void WriteTagNamesSection(EndianWriter writer, DataSerializationContext context, TagSerializer serializer)
        {
            uint sectionOffset = (uint)writer.BaseStream.Position;
            uint sectionEntrySize = TagStructure.GetStructureSize(typeof(GenericSectionEntry));
            uint tableEntrySize = TagStructure.GetStructureSize(typeof(GenericTableEntry));
            GenericSectionEntry tagNameFileEntry = new GenericSectionEntry(TagCacheNames.Count, sectionEntrySize);
            tagNameFileEntry.Write(writer);
            // make room for table

            writer.Write(new byte[tableEntrySize * TagCacheNames.Count]);

            for(int i = 0; i < TagCacheNames.Count; i++)
            {
                var names = TagCacheNames[i];

                uint offset = (uint)writer.BaseStream.Position;

                GenericSectionEntry tagNameTable = new GenericSectionEntry(names.Count, offset - sectionOffset + sectionEntrySize);
                tagNameTable.Write(writer);

                foreach (var entry in names)
                {
                    var tagNameEntry = new ModPackageTagNamesEntry(entry.Key, entry.Value);
                    serializer.Serialize(context, tagNameEntry);
                }

                uint size = (uint)(writer.BaseStream.Position - offset);

                long originalPos = writer.BaseStream.Position;
                writer.BaseStream.Seek(tagNameFileEntry.TableOffset + tableEntrySize * i + sectionOffset, SeekOrigin.Begin);
                var tableEntry = new GenericTableEntry(size, offset - sectionOffset);
                tableEntry.Write(writer);
                writer.BaseStream.Position = originalPos;
            }
            
        }

        private void WriteResourcesSection(EndianWriter writer)
        {
            ResourcesStream.Position = 0;
            StreamUtil.Copy(ResourcesStream, writer.BaseStream, ResourcesStream.Length);
        }

        private void WriteCampaignFileSection(EndianWriter writer)
        {
            CampaignFileStream.Position = 0;
            StreamUtil.Copy(CampaignFileStream, writer.BaseStream, (int)CampaignFileStream.Length);
        }

        private void WriteFontFileSection(EndianWriter writer)
        {
            FontPackage.Position = 0;
            StreamUtil.Copy(FontPackage, writer.BaseStream, (int)FontPackage.Length);
        }

        private void WriteStringIdsSection(EndianWriter writer)
        {
            var stringIdStream = new MemoryStream();
            StringTable.Save(stringIdStream);
            stringIdStream.Position = 0;
            StreamUtil.Copy(stringIdStream, writer.BaseStream, (int)stringIdStream.Length);
            StreamUtil.Align(writer.BaseStream, 4);
        }

        private void WriteMetadataSection(DataSerializationContext context, TagSerializer serializer)
        {
            serializer.Serialize(context, Metadata);
        }

        private ModPackageSectionHeader GetSectionHeader(EndianReader reader, ModPackageSection section)
        {
            uint sectionHeaderSize = TagStructure.GetStructureSize(typeof(ModPackageSectionHeader));
            reader.SeekTo(Header.SectionTable.Offset + sectionHeaderSize * (int)section);
            return new ModPackageSectionHeader(reader);
        }

        private void ReadMetadataSection(EndianReader reader, DataSerializationContext context, TagDeserializer deserializer)
        {
            var section = GetSectionHeader(reader, ModPackageSection.Metadata);
            if (!GoToSectionHeaderOffset(reader, section))
                return;

            Metadata = deserializer.Deserialize<ModPackageMetadata>(context);
        }

        private void ReadTagsSection(EndianReader reader, DataSerializationContext context, TagDeserializer deserializer)
        {
            var section = GetSectionHeader(reader, ModPackageSection.Tags);
            if (!GoToSectionHeaderOffset(reader, section))
                return;
            int tagCacheEntrySize = (int)TagStructure.GetStructureSize(typeof(CacheTableEntry));
            var entry = new GenericSectionEntry(reader);
            var cacheCount = entry.Count;

            TagCachesStreams = new List<ModPackageStream>();
            CacheNames = new List<string>();

            for(int i = 0; i < cacheCount; i++)
            {
                reader.BaseStream.Position = entry.TableOffset + tagCacheEntrySize * i + section.Offset;
                var tableEntry = deserializer.Deserialize<CacheTableEntry>(context);
                CacheNames.Add(tableEntry.CacheName);
                reader.BaseStream.Position = tableEntry.Offset + section.Offset;

                if (tableEntry.Size > int.MaxValue)
                    throw new Exception("Tag cache size not supported");

                var tagStream = new MemoryStream();
                StreamUtil.Copy(reader.BaseStream, tagStream, (int)tableEntry.Size);
                tagStream.Position = 0;
                TagCachesStreams.Add(new ModPackageStream(tagStream));
            }
        }

        private void ReadResourcesSection(EndianReader reader)
        {
            var section = GetSectionHeader(reader, ModPackageSection.Resources);
            if (!GoToSectionHeaderOffset(reader, section))
                return;
            Stream newResourceStream;
            if(section.Size <= 0x7FFFFFFF)
            {
                newResourceStream = new MemoryStream();
                ResourcesStream = new ModPackageStream(newResourceStream);
                CopyStreamChunk(reader, ResourcesStream, section.Size);
            }
            else
            {
                unsafe
                {
                    long bufferSize = 4L * 1024 * 1024 * 1024; // 4 GB max
                    IntPtr data = Marshal.AllocHGlobal((IntPtr)bufferSize);
                    newResourceStream = new UnmanagedMemoryStream((byte*)data.ToPointer(), 0, bufferSize, FileAccess.ReadWrite);
                    ResourcesStream = new ModPackageStream(newResourceStream);
                    CopyStreamChunk(reader, ResourcesStream, section.Size);
                }
            }
            
            ResourcesStream.Position = 0;
        }

        private void ReadTagNamesSection(EndianReader reader, DataSerializationContext context, TagDeserializer deserializer)
        {
            var section = GetSectionHeader(reader, ModPackageSection.TagNames);
            if (!GoToSectionHeaderOffset(reader, section))
                return;
            int tableEntrySize = (int)TagStructure.GetStructureSize(typeof(GenericTableEntry));
            var entry = new GenericSectionEntry(reader);
            var cacheCount = entry.Count;

            TagCacheNames = new List<Dictionary<int, string>>();

            for(int i = 0; i < cacheCount; i++)
            {
                var nameDict = new Dictionary<int, string>();
                reader.BaseStream.Position = entry.TableOffset + tableEntrySize * i + section.Offset;

                var tagNamesTableEntry = new GenericTableEntry(reader);
                if (tagNamesTableEntry.Size == 0)
                    throw new Exception("invalid tag name table entry size!");

                reader.BaseStream.Position = tagNamesTableEntry.Offset + section.Offset;
                var tagNamesHeader = new GenericSectionEntry(reader);
                reader.BaseStream.Position = tagNamesHeader.TableOffset + section.Offset;

                for (int j = 0; j < tagNamesHeader.Count; j++)
                {
                    var tagNamesEntry = deserializer.Deserialize<ModPackageTagNamesEntry>(context);
                    nameDict.Add(tagNamesEntry.TagIndex, tagNamesEntry.Name);
                }

                TagCacheNames.Add(nameDict);
            }
        }

        private void ReadStringIdSection(EndianReader reader)
        {
            var section = GetSectionHeader(reader, ModPackageSection.StringIds);
            if (!GoToSectionHeaderOffset(reader, section))
                return;

            int size = (int)section.Size;   // string_ids.dat will never exceed the int limits.

            var stringIdCacheStream = new MemoryStream();
            byte[] data = new byte[size];
            reader.Read(data, 0, size);
            stringIdCacheStream.Write(data, 0, size);
            stringIdCacheStream.Position = 0;
            StringTable = new StringTableHaloOnline(CacheVersion.HaloOnline106708, stringIdCacheStream);
        }

        private void ReadFontSection(EndianReader reader)
        {
            var section = GetSectionHeader(reader, ModPackageSection.Fonts);
            if (!GoToSectionHeaderOffset(reader, section))
                return;

            int size = (int)section.Size;   // font_package.bin will never exceed the int limits.

            FontPackage = new MemoryStream();
            byte[] data = new byte[size];
            reader.Read(data, 0, size);
            FontPackage.Write(data, 0, size);
            FontPackage.Position = 0;
        }

        private void ReadCampaignFileSection(EndianReader reader)
        {
            var section = GetSectionHeader(reader, ModPackageSection.CampaignFiles);
            if (!GoToSectionHeaderOffset(reader, section))
                return;

            int size = (int)section.Size;   // Campaign files will never exceed the int limits.

            CampaignFileStream = new MemoryStream();
            byte[] data = new byte[size];
            reader.Read(data, 0, size);
            CampaignFileStream.Write(data, 0, size);
            CampaignFileStream.Position = 0;
        }

        private void ReadMapFilesSection(EndianReader reader)
        {
            var section = GetSectionHeader(reader, ModPackageSection.MapFiles);
            if (!GoToSectionHeaderOffset(reader, section))
                return;

            var entry = new GenericSectionEntry(reader);
            var mapCount = entry.Count;

            MapFileStreams = new List<Stream>();
            MapToCacheMapping = new Dictionary<int, int>();
            MapIds = new List<int>();
            // TODO: add map ids on load
            for(int i = 0; i < mapCount; i++)
            {
                var structureSize = TagStructure.GetStructureSize(typeof(CacheMapTableEntry));
                reader.BaseStream.Position = entry.TableOffset + structureSize * i + section.Offset;
                var tableEntry = new CacheMapTableEntry(reader);

                reader.BaseStream.Position = tableEntry.Offset + section.Offset;

                MapToCacheMapping.Add(i, tableEntry.CacheIndex);
                int size = tableEntry.Size;
                var stream = new MemoryStream();
                byte[] data = new byte[size];
                reader.Read(data, 0, size);
                stream.Write(data, 0, size);
                MapFileStreams.Add(stream);

                var mapReader = new EndianReader(stream, true, EndianFormat.LittleEndian);
                MapFile mapFile = new MapFile();
                mapReader.BaseStream.Position = 0;
                mapFile.Read(mapReader);
                MapIds.Add(((CacheFileHeaderGenHaloOnline)mapFile.Header).MapId);
            }
        }

        private void ReadFileEntries(EndianReader reader, ISerializationContext context, TagDeserializer deserializer)
        {
            Files = new Dictionary<string, Stream>();

            var section = GetSectionHeader(reader, ModPackageSection.Files);
            if (!GoToSectionHeaderOffset(reader, section))
                return;

            var fileTable = new GenericSectionEntry(reader);

            
            for(int i = 0; i < fileTable.Count; i++)
            {
                var entryStructureSize = TagStructure.GetStructureSize(typeof(FileTableEntry));
                reader.BaseStream.Position = fileTable.TableOffset + section.Offset + entryStructureSize * i;
                
                var tableEntry = deserializer.Deserialize<FileTableEntry>(context);

                var stream = new MemoryStream();
                reader.BaseStream.Position = section.Offset + tableEntry.Offset;
                StreamUtil.Copy(reader.BaseStream, stream, tableEntry.Size);
                stream.Position = 0;
                Files.Add(tableEntry.Path, stream);
            }
        }

        private void WriteFileEntries(EndianWriter writer, ISerializationContext context, TagSerializer serializer)
        {
            int kFileTableEntrySize = (int)TagStructure.GetStructureSize(typeof(FileTableEntry));
            uint sectionEntrySize = TagStructure.GetStructureSize(typeof(GenericSectionEntry));

            uint sectionOffset = (uint)writer.BaseStream.Position;
            GenericSectionEntry table = new GenericSectionEntry(Files.Count, sectionEntrySize);
            table.Write(writer);

            // make room for table
            writer.BaseStream.Position = sectionOffset + table.TableOffset + Files.Count * kFileTableEntrySize;
            var index = 0;
            foreach (var fileEntry in Files)
            {
                StreamUtil.Align(writer.BaseStream, 0x10);
                uint offset = (uint)(writer.BaseStream.Position - sectionOffset);
                // write the contents
                fileEntry.Value.CopyTo(writer.BaseStream);

                long endPos = writer.BaseStream.Position;

                // seek to the file table entry
                writer.BaseStream.Position = sectionOffset + table.TableOffset + index * kFileTableEntrySize;
                index++;

                // write the table entry
                var tableEntry = new FileTableEntry();
                tableEntry.Path = fileEntry.Key;
                tableEntry.Size = (uint)fileEntry.Value.Length;
                tableEntry.Offset = offset;
                serializer.Serialize(context, tableEntry);

                writer.BaseStream.Position = endPos;
            }
        }

        private bool GoToSectionHeaderOffset(EndianReader reader, ModPackageSectionHeader header)
        {
            if (header.Size == 0 || header.Size == 0)
                return false;
            else
            {
                reader.BaseStream.Position = header.Offset;
                return true;
            }  
        }

        public void DetermineMapFlags()
        {
            foreach (var mapFile in MapFileStreams)
            {
                var reader = new EndianReader(mapFile);
                MapFile map = new MapFile();
                map.Read(reader);

                var type = ((CacheFileHeaderGenHaloOnline)map.Header).CacheType;

                if (type == CacheFileType.Campaign)
                    Header.MapFlags |= MapFlags.CampaignMaps;
                else if (type == CacheFileType.MainMenu)
                    Header.MapFlags |= MapFlags.MainmenuMaps;
                else if (type == CacheFileType.Multiplayer)
                    Header.MapFlags |= MapFlags.MultiplayerMaps;
            }
        }

        // IO stuff

        public void CreateDescription()
        {
            Metadata = new ModPackageMetadata();

            Console.WriteLine("Enter the display name of the mod package (32 chars max):");
            Metadata.Name = Console.ReadLine().Trim();

            Console.WriteLine();
            Console.WriteLine("Enter the description of the mod package (512 chars max):");
            Metadata.Description = Console.ReadLine().Trim();

            Console.WriteLine();
            Console.WriteLine("Enter the author of the mod package (32 chars max):");
            Metadata.Author = Console.ReadLine().Trim();

            Console.WriteLine();
            Console.WriteLine("Enter the version of the mod package: (major.minor)");

            try
            {
                var version = Version.Parse(Console.ReadLine());
                Metadata.VersionMajor = (short)version.Major;
                Metadata.VersionMinor = (short)version.Minor;
                if (version.Major == 0 && version.Minor == 0)
                    throw new ArgumentException(nameof(version));
            }
            catch(ArgumentException e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine("ERROR: Failed to parse version number, setting it to default");
                Metadata.VersionMajor = 1;
                Metadata.VersionMinor = 0;
            }

            Console.WriteLine();
            Console.WriteLine("Please enter the types of the mod package. Separated by a space [MainMenu Multiplayer Campaign Firefight]");
            string response = Console.ReadLine().Trim();

            Header.ModifierFlags = Header.ModifierFlags & ModifierFlags.SignedBit;

            var args = response.Split(' ');
            for (int x = 0; x < args.Length; x++)
            {
                if (Enum.TryParse<ModifierFlags>(args[x].ToLower(), out var value) && args[x] != "SignedBit")
                {
                    Header.ModifierFlags |= value;
                }
                else
                    Console.WriteLine($"Could not parse flag \"{response}\"");
            }
            Console.WriteLine();

            Metadata.BuildDateLow = (int)DateTime.Now.ToFileTime() & 0x7FFFFFFF;
            Metadata.BuildDateHigh = (int)((DateTime.Now.ToFileTime() & 0x7FFFFFFF00000000) >> 32);
        }

    }
}