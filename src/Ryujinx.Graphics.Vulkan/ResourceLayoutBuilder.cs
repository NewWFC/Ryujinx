using Ryujinx.Graphics.GAL;
using System;
using System.Collections.Generic;

namespace Ryujinx.Graphics.Vulkan
{
    class ResourceLayoutBuilder
    {
        private const int TotalSets = PipelineBase.DescriptorSetLayouts;

        private readonly List<ResourceDescriptor>[] _resourceDescriptors;
        private readonly List<ResourceUsage>[] _resourceUsages;

        public ResourceLayoutBuilder()
        {
            _resourceDescriptors = new List<ResourceDescriptor>[TotalSets];
            _resourceUsages = new List<ResourceUsage>[TotalSets];

            for (int index = 0; index < TotalSets; index++)
            {
                _resourceDescriptors[index] = new();
                _resourceUsages[index] = new();
            }
        }

        public ResourceLayoutBuilder Add(ResourceStages stages, ResourceType type, int binding)
        {
            int setIndex = type switch
            {
                ResourceType.UniformBuffer => PipelineBase.UniformSetIndex,
                ResourceType.StorageBuffer => PipelineBase.StorageSetIndex,
                ResourceType.TextureAndSampler or ResourceType.BufferTexture => PipelineBase.TextureSetIndex,
                ResourceType.Image or ResourceType.BufferImage => PipelineBase.ImageSetIndex,
                _ => throw new ArgumentException($"Invalid resource type \"{type}\".")
            };

            ResourceAccess access = IsReadOnlyType(type) ? ResourceAccess.Read : ResourceAccess.ReadWrite;

            _resourceDescriptors[setIndex].Add(new ResourceDescriptor(binding, 1, type, stages));
            _resourceUsages[setIndex].Add(new ResourceUsage(binding, type, stages, access));

            return this;
        }

        private static bool IsReadOnlyType(ResourceType type)
        {
            return type == ResourceType.UniformBuffer ||
                   type == ResourceType.Sampler ||
                   type == ResourceType.TextureAndSampler ||
                   type == ResourceType.BufferTexture;
        }

        public ResourceLayout Build()
        {
            var descriptors = new ResourceDescriptorCollection[TotalSets];
            var usages = new ResourceUsageCollection[TotalSets];

            for (int index = 0; index < TotalSets; index++)
            {
                descriptors[index] = new ResourceDescriptorCollection(_resourceDescriptors[index].ToArray().AsReadOnly());
                usages[index] = new ResourceUsageCollection(_resourceUsages[index].ToArray().AsReadOnly());
            }

            return new ResourceLayout(descriptors.AsReadOnly(), usages.AsReadOnly());
        }
    }
}