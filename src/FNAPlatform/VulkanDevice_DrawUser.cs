using System;
using Microsoft.Xna.Framework.Graphics;
using SDL2;
using Vulkan;
using Buffer = Vulkan.Buffer;

namespace Microsoft.Xna.Framework.Graphics
{
    internal partial class VulkanDevice : IGLDevice
    {
        public void DrawUserIndexedPrimitives(PrimitiveType primitiveType, IntPtr vertexData, int vertexOffset,
            int numVertices,
            IntPtr indexData, int indexOffset, IndexElementSize indexElementSize, int primitiveCount)
        {
            var vertexBufferLength = (uint) numVertices * (uint) userVertexStride;
            DeviceSize vertexBufferSize = vertexBufferLength;
            createBuffer(vertexBufferSize, BufferUsageFlags.TransferSrc,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out var stagingBuffer,
                out var stagingBufferMemory);
            var dst = device.MapMemory(stagingBufferMemory, 0, vertexBufferSize, 0);
            SDL.SDL_memcpy(dst, vertexData, (IntPtr) vertexBufferLength);
            device.UnmapMemory(stagingBufferMemory);

            createBuffer(vertexBufferSize,
                BufferUsageFlags.TransferDst | BufferUsageFlags.VertexBuffer | BufferUsageFlags.UniformBuffer,
                MemoryPropertyFlags.DeviceLocal, out var vertexBuffer, out var vertexBufferMemory);

            copyBuffer(stagingBuffer, vertexBuffer, vertexBufferSize);

            var indexCount = (uint)XNAToVK.PrimitiveVerts(primitiveType, primitiveCount);

            var indexBufferLength = indexCount * (uint)XNAToVK.IndexSize[(int) indexElementSize];
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

            Buffer buffer;
            DeviceSize bufferSizeii;
            unsafe
            {
                var fragmentUniformBuffer = (MojoShader.MOJOSHADER_vkBuffer*) shaderState.fragmentUniformBuffer;
                var vertexUniformBuffer = (MojoShader.MOJOSHADER_vkBuffer*) shaderState.vertexUniformBuffer;

                if (vertexUniformBuffer != null)
                {
                    var size = vertexUniformBuffer->size;
                    var bufferPtr = vertexUniformBuffer->buffer;
                    buffer = MyClass.makeT<Buffer>(bufferPtr);
                    bufferSizeii = size;
                }
                else
                {
                    throw new Exception("Unset vertex buffer");
                }

                //var fragmentBufferSize = fragmentUniformBuffer->size;
                //var vertexBufferSize = vertexUniformBuffer->size;
            }

            uniformBufferV = buffer;
            uniformBufferSizeV = bufferSizeii;

            UpdateDescriptorSets();
            //_commandBuffer.CmdBindDescriptorSet(PipelineBindPoint.Graphics, layout, 0, descriptorSets[1], null);
            //buffers [i].CmdBindVertexBuffer (0, vertexBuffer, 0);
            _commandBuffer.CmdBindVertexBuffer(0, vertexBuffer, 0);
            _commandBuffer.CmdBindIndexBuffer(indexBuffer, 0, XNAToVK.IndexType[(int) indexElementSize]);
            _commandBuffer.CmdDrawIndexed(indexCount, 1, 0, 0, 0);
        }
    }
}
