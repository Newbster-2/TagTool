using System;
using System.Collections.Generic;
using System.IO;
using TagTool.Cache;
using TagTool.Common;
using TagTool.Geometry;
using TagTool.IO;
using TagTool.Serialization;
using TagTool.Tags.Definitions;
using TagTool.Tags.Resources;

namespace TagTool.Commands.Porting
{
    partial class PortTagCommand
    {
        public ScenarioStructureBsp ConvertScenarioStructureBsp(ScenarioStructureBsp sbsp, CachedTag instance, Dictionary<ResourceLocation, Stream> resourceStreams)
        {
            var converter = new RenderGeometryConverter(CacheContext, BlamCache);   // should be made static

            var blamDecoratorResourceDefinition = BlamCache.ResourceCache.GetRenderGeometryApiResourceDefinition(sbsp.DecoratorGeometry.Resource);
            var blamGeometryResourceDefinition = BlamCache.ResourceCache.GetRenderGeometryApiResourceDefinition(sbsp.Geometry.Resource);

            var decoratorGeometry = converter.Convert(sbsp.DecoratorGeometry, blamDecoratorResourceDefinition);
            var geometry = converter.Convert(sbsp.Geometry, blamGeometryResourceDefinition);

            foreach (var cluster in sbsp.Clusters)
            {
                List<ScenarioStructureBsp.Cluster.DecoratorGrid> newDecoratorGrids = new List<ScenarioStructureBsp.Cluster.DecoratorGrid>();

                foreach (var grid in cluster.DecoratorGrids)
                {
                    var buffer = blamDecoratorResourceDefinition.VertexBuffers[grid.Gen3Info.VertexBufferIndex].Definition;
                    var offset = grid.VertexBufferOffset;              

                    grid.Vertices = new List<TinyPositionVertex>();
                    using (var stream = new MemoryStream(buffer.Data.Data))
                    {
                        var vertexStream = VertexStreamFactory.Create(BlamCache.Version, stream);
                        stream.Position = offset;

                        for(int i = 0; i < grid.Amount; i++)
                            grid.Vertices.Add(vertexStream.ReadTinyPositionVertex());
                    }

                    if (grid.Amount == 0)
                        newDecoratorGrids.Add(grid);
                    else
                    {
                        // Get the new grids
                        var newGrids = ConvertDecoratorGrid(grid.Vertices, grid);

                        // Add all to list
                        foreach (var newGrid in newGrids)
                            newDecoratorGrids.Add(newGrid);
                    }
                }
                cluster.DecoratorGrids = newDecoratorGrids;
            }

            // convert all the decorator vertex buffers
            foreach(var d3dBuffer in blamDecoratorResourceDefinition.VertexBuffers)
            {
                VertexBufferConverter.ConvertVertexBuffer(BlamCache.Version, CacheContext.Version, d3dBuffer.Definition);
                decoratorGeometry.VertexBuffers.Add(d3dBuffer);
            }

            sbsp.DecoratorGeometry.Resource = CacheContext.ResourceCache.CreateRenderGeometryApiResource(decoratorGeometry);
            sbsp.Geometry.Resource = CacheContext.ResourceCache.CreateRenderGeometryApiResource(geometry);

            sbsp.CollisionBspResource = ConvertStructureBspTagResources(sbsp);
            sbsp.PathfindingResource = ConvertStructureBspCacheFileTagResources(sbsp);

            sbsp.Unknown86 = 1;

            //
            // Set compatibility flag for H3 mopps for the engine to perform some fixups just in time
            //

            if (BlamCache.Version == CacheVersion.Halo3Retail || BlamCache.Version == CacheVersion.Halo3Beta)
                sbsp.CompatibilityFlags |= ScenarioStructureBsp.StructureBspCompatibilityValue.UseMoppIndexPatch;

            //
            // Temporary Fixes:
            //

            // Without this 005_intro crash on cortana sbsp       
            sbsp.Geometry.MeshClusterVisibility = new List<RenderGeometry.MoppClusterVisiblity>();
            
            return sbsp;
        }

        List<ScenarioStructureBsp.Cluster.DecoratorGrid> ConvertDecoratorGrid(List<TinyPositionVertex> vertices, ScenarioStructureBsp.Cluster.DecoratorGrid grid)
        {
            List<ScenarioStructureBsp.Cluster.DecoratorGrid> decoratorGrids = new List<ScenarioStructureBsp.Cluster.DecoratorGrid>();

            List<DecoratorData> decoratorData = ParseVertices(vertices);

            foreach(var data in decoratorData)
            {
                var newGrid = grid.DeepClone();

                newGrid.HaloOnlineInfo = new ScenarioStructureBsp.Cluster.DecoratorGrid.HaloOnlineDecoratorInfo();
                newGrid.Amount = data.Amount;
                newGrid.VertexBufferOffset = grid.VertexBufferOffset + data.GeometryOffset;

                newGrid.HaloOnlineInfo.Variant = data.Variant;
                newGrid.HaloOnlineInfo.PaletteIndex = grid.Gen3Info.PaletteIndex;
                newGrid.HaloOnlineInfo.VertexBufferIndex = grid.Gen3Info.VertexBufferIndex; // this doesn't change as each vertex buffer corresponds to the palette index

                decoratorGrids.Add(newGrid);
            }
            return decoratorGrids;
        }

        List<DecoratorData> ParseVertices(List<TinyPositionVertex> vertices)
        {
            List<DecoratorData> decoratorData = new List<DecoratorData>();
            var currentIndex = 0;
            while(currentIndex < vertices.Count)
            {
                var currentVertex = vertices[currentIndex];
                var currentVariant = (currentVertex.Variant >> 8) & 0xFF;

                DecoratorData data = new DecoratorData(0,(short)currentVariant,currentIndex*16);

                while(currentIndex < vertices.Count && currentVariant == ((currentVertex.Variant >> 8) & 0xFF))
                {
                    currentVertex = vertices[currentIndex];
                    data.Amount++;
                    currentIndex++;
                }

                decoratorData.Add(data);
            }

            return decoratorData;
        }
    }

    class DecoratorData
    {
        public short Amount;
        public short Variant;
        public int GeometryOffset;

        //Add position data if needed

        public DecoratorData(short count, short variant, int offset)
        {
            Amount = count;
            Variant = variant;
            GeometryOffset = offset;
        }
    }
}