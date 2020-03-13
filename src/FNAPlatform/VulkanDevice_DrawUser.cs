using System;
using Microsoft.Xna.Framework.Graphics;
using SDL2;
using Vulkan;
using Buffer = Vulkan.Buffer;

namespace Microsoft.Xna.Framework.Graphics
{
    internal partial class VulkanDevice : IGLDevice
    {
        // todo: use metal code to figure how to copy, store, delete, and buffer data
        private void BindUserVertexBuffer(
            IntPtr vertexData,
            int vertexCount,
            int vertexOffset
        ) {
            if (vertexOffset != 0)
            {
                throw new Exception("Failed to implement this properly. Check metal device.");
            }

            // Update the buffer contents
            int len = vertexCount * userVertexStride;

            DeviceSize vertexBufferSize = len;
            createBuffer(vertexBufferSize, BufferUsageFlags.TransferSrc,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out var stagingBuffer,
                out var stagingBufferMemory);
            var dst = device.MapMemory(stagingBufferMemory, 0, vertexBufferSize, 0);
            SDL.SDL_memcpy(dst, vertexData, (IntPtr) len);
            device.UnmapMemory(stagingBufferMemory);

            createBuffer(vertexBufferSize,
                BufferUsageFlags.TransferDst | BufferUsageFlags.VertexBuffer | BufferUsageFlags.UniformBuffer,
                MemoryPropertyFlags.DeviceLocal, out var vertexBuffer, out var vertexBufferMemory);

            copyBuffer(stagingBuffer, vertexBuffer, vertexBufferSize);
            device.FreeMemory(stagingBufferMemory);
            device.DestroyBuffer(stagingBuffer);

            // Bind the buffer
            _commandBuffer.CmdBindVertexBuffer(0, vertexBuffer, 0);
        }

        public void DrawUserPrimitives(PrimitiveType primitiveType, IntPtr vertexData, int vertexOffset,
            int primitiveCount)
        {
            // Bind the vertex buffer
            int numVerts = XNAToVK.PrimitiveVerts(
                primitiveType,
                primitiveCount
            );
            BindUserVertexBuffer(
                vertexData,
                numVerts,
                vertexOffset
            );

            // Draw!
            _commandBuffer.CmdDraw((uint)numVerts, 1, 0, 0);
        }

        public void DrawUserIndexedPrimitives(PrimitiveType primitiveType, IntPtr vertexData, int vertexOffset,
            int numVertices,
            IntPtr indexData, int indexOffset, IndexElementSize indexElementSize, int primitiveCount)
        {
            // Bind the vertex buffer
            BindUserVertexBuffer(
                vertexData,
                numVertices,
                vertexOffset
            );

            // Prepare the index buffer
            var indexCount = (uint) XNAToVK.PrimitiveVerts(primitiveType, primitiveCount);

            var indexBufferLength = indexCount * (uint) XNAToVK.IndexSize[(int) indexElementSize];
            DeviceSize indexBufferSize = indexBufferLength;
            createBuffer(indexBufferSize, BufferUsageFlags.TransferSrc,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out var stagingBuffer2,
                out var stagingBufferMemory2);
            var dst2 = device.MapMemory(stagingBufferMemory2, 0, indexBufferSize, 0);
            SDL.SDL_memcpy(dst2, indexData, (IntPtr) indexBufferLength);
            device.UnmapMemory(stagingBufferMemory2);

            createBuffer(indexBufferSize,
                BufferUsageFlags.TransferDst | BufferUsageFlags.IndexBuffer | BufferUsageFlags.UniformBuffer,
                MemoryPropertyFlags.DeviceLocal, out var indexBuffer, out var indexMemory);

            copyBuffer(stagingBuffer2, indexBuffer, indexBufferSize);
            device.FreeMemory(stagingBufferMemory2);
            device.DestroyBuffer(stagingBuffer2);

            _commandBuffer.CmdBindIndexBuffer(indexBuffer, 0, XNAToVK.IndexType[(int) indexElementSize]);

            // Draw!
            _commandBuffer.CmdDrawIndexed(indexCount, 1, 0, 0, 0);
        }
    }
}
