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
		)
		{
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

		private void BindDrawPipeline(PrimitiveTopology primitiveTopology)
		{
			// only for changes that can happen in-between normal draw calls
			// and not for wider changes.

			var pipeline = device.CreateGraphicsPipelines(null, new[]
			{
				new GraphicsPipelineCreateInfo
				{
					Stages = currentStages,
					VertexInputState = new PipelineVertexInputStateCreateInfo
					{
						VertexAttributeDescriptions = _descriptions, // todo: how to always make sure this is valid?
						VertexBindingDescriptions = new[] {_bindingDescription},
					},
					InputAssemblyState = new PipelineInputAssemblyStateCreateInfo
					{
						Topology = primitiveTopology, // todo, case off of primitive type
					},
					ViewportState = new PipelineViewportStateCreateInfo
					{
						ViewportCount = 1,
						ScissorCount = 1, // todo: unset causes wrongness, test this.
					},
					RasterizationState = new PipelineRasterizationStateCreateInfo
					{
						/*
						 * PolygonMode = PolygonMode.Fill,
				CullMode = (uint)CullModeFlags.None,
				FrontFace = FrontFace.Clockwise,
				LineWidth = 1.0f
						 */
						LineWidth = 1.0f,
					},
					MultisampleState = new PipelineMultisampleStateCreateInfo
					{
						RasterizationSamples = SampleCountFlags.Count1,
					},
					DepthStencilState = new PipelineDepthStencilStateCreateInfo
					{
						DepthTestEnable = true,
						DepthWriteEnable = true,
						DepthCompareOp = CompareOp.Less,
						DepthBoundsTestEnable = false,
						StencilTestEnable = false,
					},
					ColorBlendState = new PipelineColorBlendStateCreateInfo
					{
						Attachments = new[]
						{
							new PipelineColorBlendAttachmentState
							{
								ColorWriteMask = ColorComponentFlags.R | ColorComponentFlags.G | ColorComponentFlags.B |
								                 ColorComponentFlags.A,
								// todo: use blend-state to let this be set dynamically.
								BlendEnable = true,
								SrcColorBlendFactor = Vulkan.BlendFactor.SrcAlpha,
								DstColorBlendFactor = Vulkan.BlendFactor.OneMinusSrcAlpha,
								ColorBlendOp = BlendOp.Add,
								SrcAlphaBlendFactor = Vulkan.BlendFactor.One,
								DstAlphaBlendFactor = Vulkan.BlendFactor.Zero,
								AlphaBlendOp = BlendOp.Add,
							}
						}
					},
					DynamicState = new PipelineDynamicStateCreateInfo
					{
						DynamicStates = new[] {DynamicState.Viewport, DynamicState.Scissor}
					},
					Layout = currentLayout,
					RenderPass = renderPass,
				}
			})[0];

			_commandBuffer.CmdBindPipeline(PipelineBindPoint.Graphics, pipeline);
		}

		public void DrawUserPrimitives(
			PrimitiveType primitiveType,
			IntPtr vertexData,
			int vertexOffset,
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

			// Bind the pipeline
			BindDrawPipeline(XNAToVK.Primitive[(int) primitiveType]);

			// Draw!
			_commandBuffer.CmdDraw((uint) numVerts, 1, 0, 0);
		}

		public void DrawUserIndexedPrimitives(
			PrimitiveType primitiveType,
			IntPtr vertexData,
			int vertexOffset,
			int numVertices,
			IntPtr indexData,
			int indexOffset,
			IndexElementSize indexElementSize,
			int primitiveCount)
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

			// Bind the pipeline
			BindDrawPipeline(XNAToVK.Primitive[(int) primitiveType]);

			// Draw!
			_commandBuffer.CmdDrawIndexed(indexCount, 1, 0, 0, 0);
		}
	}
}
