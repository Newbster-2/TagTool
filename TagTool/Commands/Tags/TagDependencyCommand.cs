﻿using System;
using System.Collections.Generic;
using System.Linq;
using TagTool.Cache;
using TagTool.Commands.Common;
using TagTool.Cache.HaloOnline;

namespace TagTool.Commands.Tags
{
    /// <summary>
    /// Command for managing tag dependencies.
    /// </summary>
    class TagDependencyCommand : Command
    {
        public GameCacheHaloOnlineBase Cache { get; }

        public TagDependencyCommand(GameCacheHaloOnlineBase cache) : base(
            true,

            "TagDependency",
            "Manage tag dependencies.",

            "TagDependency Add <tag> {... dependencies ...}\n" +
            "TagDependency Remove <tag> {... dependencies ...}\n" +
            "TagDependency List <tag>\n" +
            "TagDependency ListAll <tag>\n" +
            "TagDependency ListOn <tag>",

            "\"TagDependency Add\" will cause the first tag to load the other tags.\n" +
            "\"TagDependency Remove\" will prevent the first tag from loading the other tags.\n" +
            "\"TagDependency List\" will list all immediate dependencies of a tag.\n" +
            "\"TagDependency ListAll\" will recursively list all dependencies of a tag.\n" +
            "\"TagDependency ListOn\" will list all tags that depend on a tag.\n" +
            "\n" +
            "To add dependencies to a map, use the \"GetMapInfo\" command to get its scenario tag\n" +
            "index and then add dependencies to the scenario tag.")
        {
            Cache = cache;
        }

        public override object Execute(List<string> args)
        {
            if (args.Count < 2)
                return new TagToolError(CommandError.ArgCount);
            if (!Cache.TagCache.TryGetCachedTag(args[1], out var tag))
                return new TagToolError(CommandError.TagInvalid);

            switch (args[0].ToLower())
            {
                case "add":
                case "remove":
                    return ExecuteAddRemove((CachedTagHaloOnline)tag, args);

                case "list":
                case "listall":
                    return ExecuteList((CachedTagHaloOnline)tag, (args[0] == "listall"), args.Skip(2).ToArray());

                case "liston":
                    return ExecuteListDependsOn((CachedTagHaloOnline)tag);

                default:
                    return new TagToolError(CommandError.ArgInvalid, $"\"{args[0]}\"");
            }
        }

        private object ExecuteAddRemove(CachedTagHaloOnline tag, List<string> args)
        {
            if (args.Count < 3)
                return new TagToolError(CommandError.ArgCount);

            var dependencies = args.Skip(2).Select(name => Cache.TagCache.GetTag(name)).ToList();
            
            if (dependencies.Count == 0 || dependencies.Any(d => d == null))
                return new TagToolError(CommandError.CustomError, "No dependencies were listed");

            using (var stream = Cache.OpenCacheReadWrite())
            {
                var data = Cache.TagCacheGenHO.ExtractTag(stream, tag);

                if (args[0].ToLower() == "add")
                {
                    foreach (var dependency in dependencies)
                    {
                        if (data.Dependencies.Add(dependency.Index))
                            Console.WriteLine("Added dependency on tag {0:X8}.", dependency.Index);
                        else
                            Console.WriteLine("Tag {0:X8} already depends on tag {1:X8}.", tag.Index, dependency.Index);
                    }
                }
                else
                {
                    foreach (var dependency in dependencies)
                    {
                        if (data.Dependencies.Remove(dependency.Index))
                            Console.WriteLine("Removed dependency on tag {0:X8}.", dependency.Index);
                        else
                            Console.WriteLine("Tag {0:X8} does not depend on tag {1:X8}.", tag.Index, dependency.Index);
                    }
                }

                Cache.TagCacheGenHO.SetTagData(stream, tag, data);
            }

            return true;
        }

        private object ExecuteList(CachedTagHaloOnline tag, bool all, params string[] groups)
        {
            if (tag.Dependencies.Count == 0)
            {
                Console.WriteLine("Tag {0:X8} has no dependencies.", tag.Index);
                return true;
            }

            IEnumerable<CachedTagHaloOnline> dependencies;

            if (all)
                dependencies = Cache.TagCacheGenHO.FindDependencies(tag);
            else
                dependencies = tag.Dependencies.Where(i => i >= 0 && i <= Cache.TagCache.Count).Select(i => Cache.TagCacheGenHO.Tags[i]);

            var groupTags = groups.Select(group => Cache.TagCache.ParseGroupTag(group)).ToArray();

            foreach (var dependency in dependencies)
            {
                if (groupTags.Length != 0 && !dependency.IsInGroup(groupTags))
                    continue;

                var tagName = dependency?.Name ?? $"0x{dependency.Index:X4}";

                Console.WriteLine($"[Index: 0x{dependency.Index:X4}, Offset: 0x{dependency.HeaderOffset:X8}, Size: 0x{dependency.TotalSize:X4}] {tagName}.{dependency.Group}");
            }
            
            foreach (var instance in tag.Dependencies)
                if (instance < 0 || instance >= Cache.TagCache.Count)
                    Console.WriteLine($"WARNING: dependency is a non-existant tag: 0x{instance:X4}");
                    
            return true;
        }

        private object ExecuteListDependsOn(CachedTagHaloOnline tag)
        {
            var dependsOn = Cache.TagCacheGenHO.NonNull().Where(t => ((CachedTagHaloOnline)t).Dependencies.Contains(tag.Index));

            foreach (var dependency in dependsOn)
            {
                var tagName = dependency?.Name ?? $"0x{dependency.Index:X4}";

                Console.WriteLine($"[Index: 0x{dependency.Index:X4}, Offset: 0x{dependency.DefinitionOffset:X8}] {tagName}.{dependency.Group}");
            }

            return true;
        }
    }
}
