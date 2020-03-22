#region License

/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2019 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */

#endregion

#region Using Statements

using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using Vulkan;

#endregion

namespace Vulkan
{
    public static class DeviceExtensions
    {
        public static uint[] GetQueryPoolResults2(this Device device, QueryPool queryPool, uint firstQuery,
            uint queryCount,
            UIntPtr dataSize,
            DeviceSize stride,
            QueryResultFlags flags = (QueryResultFlags) 0)
        {
            var deviceHandle = ((IMarshalling)device).Handle;
            var queryPoolHandle = ((INonDispatchableHandleMarshalling) queryPool)?.Handle ?? 0UL;
            var pData = new uint[(uint)dataSize];
            Result queryPoolResults = VulkanDevice.vkGetQueryPoolResults(deviceHandle, queryPoolHandle, firstQuery, queryCount, (UIntPtr)((uint)dataSize * sizeof(int)), pData, stride, flags);
            if ((uint) queryPoolResults > 0U)
                throw new ResultException_Ext(queryPoolResults);
            return pData;
        }
    }

    class ResultException_Ext : Exception
    {
        public ResultException_Ext(Result queryPoolResults)
        {

        }
    }
}

namespace Microsoft.Xna.Framework.Graphics
{

    /*
    extern DECLSPEC SDL_bool SDLCALL SDL_Vulkan_CreateSurface(
    SDL_Window *window,
    VkInstance instance,
    VkSurfaceKHR* surface);
    */

    partial class VulkanDevice
    {
        [DllImport("vulkan-1")]
        internal static extern Result vkGetQueryPoolResults(
            IntPtr device,
            ulong queryPool,
            uint firstQuery,
            uint queryCount,
            UIntPtr dataSize,
            IntPtr pData,
            DeviceSize stride,
            QueryResultFlags flags);

        [DllImport("vulkan-1")]
        internal static extern Result vkGetQueryPoolResults(
            IntPtr device,
            ulong queryPool,
            uint firstQuery,
            uint queryCount,
            UIntPtr dataSize,
            [In, Out] uint[] pData,
            DeviceSize stride,
            QueryResultFlags flags);

        /*
        [DllImport("vulkan-1")]
        internal static extern unsafe Result vkCreateFramebuffer(
            IntPtr device,
            FramebufferCreateInfo* pCreateInfo,
            AllocationCallbacks* pAllocator,
            ulong* pFramebuffer);
            */
    }

    class MyClass
    {
        public static T makeT<T>(ulong m)
        {
            var fieldInfo = typeof(T).GetField("m", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfo == null)
            {
                throw new Exception($"Runtime reflection failed to find field m for {typeof(T)}");
            }

            var constructor = typeof(T).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (constructor == null)
            {
                throw new Exception($"Runtime reflection failed to find default constructor for {typeof(T)}");
            }

            Debug.Assert(fieldInfo != null);
            var result = (T) constructor.Invoke(null);

            object obj = result;
            var value = m;
            fieldInfo.SetValue(obj, value);

            return result;
        }
    }
}
