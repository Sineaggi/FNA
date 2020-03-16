using System;

namespace Microsoft.Xna.Framework.Graphics
{
    internal partial class VulkanDevice : IGLDevice
    {
        public void DrawIndexedPrimitives(PrimitiveType primitiveType, int baseVertex, int minVertexIndex,
            int numVertices,
            int startIndex, int primitiveCount, IndexBuffer indices)
        {
            VulkanBuffer indexBuffer = indices.buffer as VulkanBuffer;
            //indexBuffer.;
            indexBuffer.Bound();
            int totalIndexOffset = (
            (startIndex * XNAToVK.IndexSize[(int) indices.IndexElementSize]) +
            indexBuffer.InternalOffset
            );

            _commandBuffer.CmdBindIndexBuffer(indexBuffer.Buffer, 0, Vulkan.IndexType.Uint16); // todo: fix this hard-coded value
            _commandBuffer.CmdDrawIndexed((uint)XNAToVK.PrimitiveVerts(primitiveType, primitiveCount), 1, 0, 0, 0);
        }

        public void DrawInstancedPrimitives(PrimitiveType primitiveType, int baseVertex, int minVertexIndex,
            int numVertices,
            int startIndex, int primitiveCount, int instanceCount, IndexBuffer indices)
        {
            throw new NotImplementedException();
        }

        public void DrawPrimitives(PrimitiveType primitiveType, int vertexStart, int primitiveCount)
        {
            _commandBuffer.CmdDraw((uint)XNAToVK.PrimitiveVerts(primitiveType, primitiveCount), 1, 0, 0);
        }
    }
}
