using System;

namespace Microsoft.Xna.Framework.Graphics
{
	internal partial class VulkanDevice : IGLDevice
	{
		public void DrawIndexedPrimitives(
			PrimitiveType primitiveType,
			int baseVertex,
			int minVertexIndex,
			int numVertices,
			int startIndex,
			int primitiveCount,
			IndexBuffer indices)
		{
			VulkanBuffer indexBuffer = indices.buffer as VulkanBuffer;
			indexBuffer.Bound();
			int totalIndexOffset = (
				(startIndex * XNAToVK.IndexSize[(int) indices.IndexElementSize]) +
				indexBuffer.InternalOffset
			);

			_commandBuffer.CmdBindIndexBuffer(
				indexBuffer.Buffer,
				totalIndexOffset,
				XNAToVK.IndexType[(int) indices.IndexElementSize]
			);

			// Bind the pipeline
			BindDrawPipeline(XNAToVK.Primitive[(int) primitiveType]);

			_commandBuffer.CmdDrawIndexed(
				(uint) XNAToVK.PrimitiveVerts(primitiveType, primitiveCount),
				1,
				0,
				0,
				0
			);
		}

		public void DrawInstancedPrimitives(
			PrimitiveType primitiveType,
			int baseVertex,
			int minVertexIndex,
			int numVertices,
			int startIndex,
			int primitiveCount,
			int instanceCount,
			IndexBuffer indices)
		{
			throw new NotImplementedException();
		}

		public void DrawPrimitives(
			PrimitiveType primitiveType,
			int vertexStart,
			int primitiveCount)
		{
			// Bind the pipeline
			BindDrawPipeline(XNAToVK.Primitive[(int) primitiveType]);

			_commandBuffer.CmdDraw((uint) XNAToVK.PrimitiveVerts(primitiveType, primitiveCount), 1, 0, 0);
		}
	}
}
