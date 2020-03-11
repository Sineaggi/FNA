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
using Vulkan;

#endregion

namespace Microsoft.Xna.Framework.Graphics
{

    /*
    extern DECLSPEC SDL_bool SDLCALL SDL_Vulkan_CreateSurface(
    SDL_Window *window,
    VkInstance instance,
    VkSurfaceKHR* surface);
    */

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
