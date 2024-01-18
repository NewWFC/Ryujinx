using Ryujinx.Common.Utilities;
using Ryujinx.Graphics.GAL;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;

namespace Ryujinx.Graphics.Vulkan
{
    readonly struct VulkanPhysicalDevice
    {
        public readonly PhysicalDevice PhysicalDevice;
        public readonly PhysicalDeviceFeatures PhysicalDeviceFeatures;
        public readonly PhysicalDeviceProperties PhysicalDeviceProperties;
        public readonly PhysicalDeviceVulkan12Properties PhysicalDeviceVulkan12Properties;
        public readonly PhysicalDeviceMemoryProperties PhysicalDeviceMemoryProperties;
        public readonly QueueFamilyProperties[] QueueFamilyProperties;
        public readonly string DeviceName;
        public readonly IReadOnlySet<string> DeviceExtensions;

        public VulkanPhysicalDevice(Vk api, PhysicalDevice physicalDevice)
        {
            PhysicalDevice = physicalDevice;
            PhysicalDeviceFeatures = api.GetPhysicalDeviceFeature(PhysicalDevice);

            PhysicalDeviceProperties physicalDeviceProperties = default;
            PhysicalDeviceVulkan12Properties physicalDeviceVulkan12Properties = new()
            {
                SType = StructureType.PhysicalDeviceVulkan12Properties
            };

            unsafe
            {
                PhysicalDeviceProperties2 properties2 = new()
                {
                    SType = StructureType.PhysicalDeviceProperties2,
                    PNext = &physicalDeviceVulkan12Properties
                };

                api.GetPhysicalDeviceProperties2(physicalDevice, &properties2);

                physicalDeviceProperties = properties2.Properties;
            }

            PhysicalDeviceProperties = physicalDeviceProperties;
            PhysicalDeviceVulkan12Properties = physicalDeviceVulkan12Properties;

            api.GetPhysicalDeviceMemoryProperties(PhysicalDevice, out PhysicalDeviceMemoryProperties);

            unsafe
            {
                DeviceName = Marshal.PtrToStringAnsi((IntPtr)physicalDeviceProperties.DeviceName);
            }

            uint propertiesCount = 0;

            api.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, SpanHelpers.AsSpan(ref propertiesCount), Span<QueueFamilyProperties>.Empty);

            QueueFamilyProperties = new QueueFamilyProperties[propertiesCount];

            api.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, SpanHelpers.AsSpan(ref propertiesCount), QueueFamilyProperties);

            api.EnumerateDeviceExtensionProperties(PhysicalDevice, Span<byte>.Empty, SpanHelpers.AsSpan(ref propertiesCount), Span<ExtensionProperties>.Empty).ThrowOnError();

            ExtensionProperties[] extensionProperties = new ExtensionProperties[propertiesCount];

            api.EnumerateDeviceExtensionProperties(PhysicalDevice, Span<byte>.Empty, SpanHelpers.AsSpan(ref propertiesCount), extensionProperties).ThrowOnError();

            unsafe
            {
                DeviceExtensions = extensionProperties.Select(x => Marshal.PtrToStringAnsi((IntPtr)x.ExtensionName)).ToImmutableHashSet();
            }
        }

        public string Id => $"0x{PhysicalDeviceProperties.VendorID:X}_0x{PhysicalDeviceProperties.DeviceID:X}";

        public bool IsDeviceExtensionPresent(string extension) => DeviceExtensions.Contains(extension);

        public DeviceInfo ToDeviceInfo()
        {
            return new DeviceInfo(
                Id,
                VendorUtils.GetNameFromId(PhysicalDeviceProperties.VendorID),
                DeviceName,
                PhysicalDeviceProperties.DeviceType == PhysicalDeviceType.DiscreteGpu);
        }
    }
}
