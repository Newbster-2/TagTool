﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TagTool.Cache.HaloOnline;
using TagTool.Cache.Resources;
using TagTool.Common;

namespace TagTool.Cache.ModPackages
{
    public class ResourceCachesModPackage : ResourceCachesHaloOnlineBase
    {
        private ModPackage Package;

        private Dictionary<string, ResourcePage> ExistingResources;

        private ResourceCacheHaloOnline ResourceCache;

        public ResourceCachesModPackage(GameCacheModPackage cache, ModPackage package)
        {
            Package = package;
            Cache = cache;
            ExistingResources = new Dictionary<string, ResourcePage>();
            ResourceCache = new ResourceCacheHaloOnline(CacheVersion.HaloOnline106708, package.ResourcesStream);
        }

        public override ResourceCacheHaloOnline GetResourceCache(ResourceLocation location) => ResourceCache;

        public override Stream OpenCacheRead(ResourceLocation location) =>  Package.ResourcesStream;

        public override Stream OpenCacheReadWrite(ResourceLocation location) => Package.ResourcesStream;

        public override Stream OpenCacheWrite(ResourceLocation location) => Package.ResourcesStream;

        public void RebuildResourceDictionary()
        {
            throw new Exception("Not implemented");
        }
        
        public override void AddResource(PageableResource resource, Stream dataStream)
        {
            // check hash of existing resources 
            if (resource == null)
                throw new ArgumentNullException("resource");
            if (!dataStream.CanRead)
                throw new ArgumentException("The input stream is not open for reading", "dataStream");

            // change resource location
            resource.ChangeLocation(ResourceLocation.Mods);

            var dataSize = (int)(dataStream.Length - dataStream.Position);
            var data = new byte[dataSize];
            dataStream.Read(data, 0, dataSize);

            string hash;
            using (SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider())
            {
                hash = Convert.ToBase64String(sha1.ComputeHash(data));
            }
            // check if a perfect resource match exists, if yes reuse it to save memory in multicache packages
            if (ExistingResources.ContainsKey(hash) && ExistingResources[hash].UncompressedBlockSize == dataSize)
            {
                var existingPage = ExistingResources[hash];
                resource.Page = existingPage;
                resource.DisableChecksum();
                Debug.WriteLine("Found perfect resource match, reusing resource!");
            }
            else
            {
                ExistingResources[hash] = resource.Page;
                var cache = GetResourceCache(ResourceLocation.Mods);
                var stream = OpenCacheReadWrite(ResourceLocation.Mods);

                resource.Page.Index = cache.Add(stream, data, out uint compressedSize);
                resource.Page.CompressedBlockSize = compressedSize;
                resource.Page.UncompressedBlockSize = (uint)dataSize;
                resource.DisableChecksum();
            }
        }
        
    }
}
