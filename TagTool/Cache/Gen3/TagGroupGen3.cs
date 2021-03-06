﻿using TagTool.Common;
using TagTool.Tags;

namespace TagTool.Cache.Gen3
{
    public class TagGroupGen3 : TagGroup
    {
        public string Name;
        public TagGroupGen3() : base() { Name = ""; }
        public TagGroupGen3(Tag tag, string name) : base(tag) { Name = name; }
        public TagGroupGen3(Tag tag, Tag parentTag, string name) : base(tag, parentTag) { Name = name; }
        public TagGroupGen3(Tag tag, Tag parentTag, Tag grandparentTag, string name) : base(tag, parentTag, grandparentTag) { Name = name; }
        public override string ToString() => Name == "" ? Tag.ToString() : Name;
    }
}
