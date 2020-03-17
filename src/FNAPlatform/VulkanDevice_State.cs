
using System;
using System.Collections.Generic;
using System.Net.Configuration;
using Vulkan;

namespace Microsoft.Xna.Framework.Graphics
{
	internal partial class VulkanDevice
	{
		#region State Creation/Retrieval Methods

#if false
		private struct PipelineHash : IEquatable<PipelineHash>
		{
			readonly ulong a;
			readonly ulong b;
			readonly ulong c;
			readonly ulong d;

			public PipelineHash(
				ulong vertexShader,
				ulong fragmentShader,
				ulong vertexDescriptor,
				MTLPixelFormat[] formats,
				DepthFormat depthFormat,
				int sampleCount,
				BlendState blendState
			)
			{
				this.a = vertexShader;
				this.b = fragmentShader;
				this.c = vertexDescriptor;

				unchecked
				{
					this.d = (
						((ulong) blendState.GetHashCode() << 32) |
						((ulong) sampleCount << 22) |
						((ulong) depthFormat << 20) |
						((ulong) HashFormat(formats[3]) << 15) |
						((ulong) HashFormat(formats[2]) << 10) |
						((ulong) HashFormat(formats[1]) << 5) |
						((ulong) HashFormat(formats[0]))
					);
				}
			}

			private static uint HashFormat(MTLPixelFormat format)
			{
				switch (format)
				{
					case MTLPixelFormat.Invalid:
						return 0;
					case MTLPixelFormat.R16Float:
						return 1;
					case MTLPixelFormat.R32Float:
						return 2;
					case MTLPixelFormat.RG16Float:
						return 3;
					case MTLPixelFormat.RG16Snorm:
						return 4;
					case MTLPixelFormat.RG16Unorm:
						return 5;
					case MTLPixelFormat.RG32Float:
						return 6;
					case MTLPixelFormat.RG8Snorm:
						return 7;
					case MTLPixelFormat.RGB10A2Unorm:
						return 8;
					case MTLPixelFormat.RGBA16Float:
						return 9;
					case MTLPixelFormat.RGBA16Unorm:
						return 10;
					case MTLPixelFormat.RGBA32Float:
						return 11;
					case MTLPixelFormat.RGBA8Unorm:
						return 12;
					case MTLPixelFormat.A8Unorm:
						return 13;
					case MTLPixelFormat.ABGR4Unorm:
						return 14;
					case MTLPixelFormat.B5G6R5Unorm:
						return 15;
					case MTLPixelFormat.BC1_RGBA:
						return 16;
					case MTLPixelFormat.BC2_RGBA:
						return 17;
					case MTLPixelFormat.BC3_RGBA:
						return 18;
					case MTLPixelFormat.BGR5A1Unorm:
						return 19;
					case MTLPixelFormat.BGRA8Unorm:
						return 20;
				}

				throw new NotSupportedException();
			}

			public override int GetHashCode()
			{
				unchecked
				{
					int i1 = (int) (a ^ (a >> 32));
					int i2 = (int) (b ^ (b >> 32));
					int i3 = (int) (c ^ (c >> 32));
					int i4 = (int) (d ^ (d >> 32));
					return i1 + i2 + i3 + i4;
				}
			}

			public bool Equals(PipelineHash other)
			{
				return (
					a == other.a &&
					b == other.b &&
					c == other.c &&
					d == other.d
				);
			}

			public override bool Equals(object obj)
			{
				if (obj == null || obj.GetType() != GetType())
				{
					return false;
				}

				PipelineHash hash = (PipelineHash) obj;
				return (
					a == hash.a &&
					b == hash.b &&
					c == hash.c &&
					d == hash.d
				);
			}
		}

		private IntPtr FetchRenderPipeline()
		{
			// Can we just reuse an existing pipeline?
			PipelineHash hash = new PipelineHash(
				(ulong) shaderState.vertexShader,
				(ulong) shaderState.fragmentShader,
				(ulong) currentVertexDescriptor,
				currentColorFormats,
				currentDepthFormat,
				currentSampleCount,
				blendState
			);
			IntPtr pipeline = IntPtr.Zero;
			if (PipelineStateCache.TryGetValue(hash, out pipeline))
			{
				// We have this state already cached!
				return pipeline;
			}

			// We have to make a new pipeline...
			IntPtr pipelineDesc = mtlNewRenderPipelineDescriptor();
			IntPtr vertHandle = MojoShader.MOJOSHADER_mtlGetFunctionHandle(
				shaderState.vertexShader
			);
			IntPtr fragHandle = MojoShader.MOJOSHADER_mtlGetFunctionHandle(
				shaderState.fragmentShader
			);
			mtlSetPipelineVertexFunction(
				pipelineDesc,
				vertHandle
			);
			mtlSetPipelineFragmentFunction(
				pipelineDesc,
				fragHandle
			);
			mtlSetPipelineVertexDescriptor(
				pipelineDesc,
				currentVertexDescriptor
			);
			mtlSetDepthAttachmentPixelFormat(
				pipelineDesc,
				GetDepthFormat(currentDepthFormat)
			);
			if (currentDepthFormat == DepthFormat.Depth24Stencil8)
			{
				mtlSetStencilAttachmentPixelFormat(
					pipelineDesc,
					GetDepthFormat(currentDepthFormat)
				);
			}

			mtlSetPipelineSampleCount(
				pipelineDesc,
				Math.Max(1, currentSampleCount)
			);

			// Apply the blend state
			bool alphaBlendEnable = !(
				blendState.ColorSourceBlend == Blend.One &&
				blendState.ColorDestinationBlend == Blend.Zero &&
				blendState.AlphaSourceBlend == Blend.One &&
				blendState.AlphaDestinationBlend == Blend.Zero
			);
			for (int i = 0; i < currentAttachments.Length; i += 1)
			{
				if (currentAttachments[i] == IntPtr.Zero)
				{
					// There's no attachment bound at this index.
					continue;
				}

				IntPtr colorAttachment = mtlGetColorAttachment(
					pipelineDesc,
					i
				);
				mtlSetAttachmentPixelFormat(
					colorAttachment,
					currentColorFormats[i]
				);
				mtlSetAttachmentBlendingEnabled(
					colorAttachment,
					alphaBlendEnable
				);
				if (alphaBlendEnable)
				{
					mtlSetAttachmentSourceRGBBlendFactor(
						colorAttachment,
						XNAToMTL.BlendMode[
							(int) blendState.ColorSourceBlend
						]
					);
					mtlSetAttachmentDestinationRGBBlendFactor(
						colorAttachment,
						XNAToMTL.BlendMode[
							(int) blendState.ColorDestinationBlend
						]
					);
					mtlSetAttachmentSourceAlphaBlendFactor(
						colorAttachment,
						XNAToMTL.BlendMode[
							(int) blendState.AlphaSourceBlend
						]
					);
					mtlSetAttachmentDestinationAlphaBlendFactor(
						colorAttachment,
						XNAToMTL.BlendMode[
							(int) blendState.AlphaDestinationBlend
						]
					);
					mtlSetAttachmentRGBBlendOperation(
						colorAttachment,
						XNAToMTL.BlendOperation[
							(int) blendState.ColorBlendFunction
						]
					);
					mtlSetAttachmentAlphaBlendOperation(
						colorAttachment,
						XNAToMTL.BlendOperation[
							(int) blendState.AlphaBlendFunction
						]
					);
				}

				/* FIXME: So how exactly do we factor in
				 * COLORWRITEENABLE for buffer 0? Do we just assume that
				 * the default is just buffer 0, and all other calls
				 * update the other write masks?
				 */
				if (i == 0)
				{
					mtlSetAttachmentWriteMask(
						colorAttachment,
						XNAToMTL.ColorWriteMask(blendState.ColorWriteChannels)
					);
				}
				else if (i == 1)
				{
					mtlSetAttachmentWriteMask(
						mtlGetColorAttachment(pipelineDesc, 1),
						XNAToMTL.ColorWriteMask(blendState.ColorWriteChannels1)
					);
				}
				else if (i == 2)
				{
					mtlSetAttachmentWriteMask(
						mtlGetColorAttachment(pipelineDesc, 2),
						XNAToMTL.ColorWriteMask(blendState.ColorWriteChannels2)
					);
				}
				else if (i == 3)
				{
					mtlSetAttachmentWriteMask(
						mtlGetColorAttachment(pipelineDesc, 3),
						XNAToMTL.ColorWriteMask(blendState.ColorWriteChannels3)
					);
				}
			}

			// Bake the render pipeline!
			IntPtr pipelineState = mtlNewRenderPipelineStateWithDescriptor(
				device,
				pipelineDesc
			);
			PipelineStateCache[hash] = pipelineState;

			// Clean up
			objc_release(pipelineDesc);
			objc_release(vertHandle);
			objc_release(fragHandle);

			// Return the pipeline!
			return pipelineState;
		}

		private IntPtr FetchDepthStencilState()
		{
			/* Just use the default depth-stencil state
			 * if depth and stencil testing are disabled,
			 * or if there is no bound depth attachment.
			 * This wards off Metal validation errors.
			 * -caleb
			 */
			bool zEnable = depthStencilState.DepthBufferEnable;
			bool sEnable = depthStencilState.StencilEnable;
			bool zFormat = (currentDepthFormat != DepthFormat.None);
			if ((!zEnable && !sEnable) || (!zFormat))
			{
				return defaultDepthStencilState;
			}

			// Can we just reuse an existing state?
			StateHash hash = PipelineCache.GetDepthStencilHash(depthStencilState);
			IntPtr state = IntPtr.Zero;
			if (DepthStencilStateCache.TryGetValue(hash, out state))
			{
				// This state has already been cached!
				return state;
			}

			// We have to make a new DepthStencilState...
			IntPtr dsDesc = mtlNewDepthStencilDescriptor();
			if (zEnable)
			{
				mtlSetDepthCompareFunction(
					dsDesc,
					XNAToMTL.CompareFunc[(int) depthStencilState.DepthBufferFunction]
				);
				mtlSetDepthWriteEnabled(
					dsDesc,
					depthStencilState.DepthBufferWriteEnable
				);
			}

			// Create stencil descriptors
			IntPtr front = IntPtr.Zero;
			IntPtr back = IntPtr.Zero;

			if (sEnable)
			{
				front = mtlNewStencilDescriptor();
				mtlSetStencilFailureOperation(
					front,
					XNAToMTL.StencilOp[(int) depthStencilState.StencilFail]
				);
				mtlSetDepthFailureOperation(
					front,
					XNAToMTL.StencilOp[(int) depthStencilState.StencilDepthBufferFail]
				);
				mtlSetDepthStencilPassOperation(
					front,
					XNAToMTL.StencilOp[(int) depthStencilState.StencilPass]
				);
				mtlSetStencilCompareFunction(
					front,
					XNAToMTL.CompareFunc[(int) depthStencilState.StencilFunction]
				);
				mtlSetStencilReadMask(
					front,
					(uint) depthStencilState.StencilMask
				);
				mtlSetStencilWriteMask(
					front,
					(uint) depthStencilState.StencilWriteMask
				);

				if (!depthStencilState.TwoSidedStencilMode)
				{
					back = front;
				}
			}

			if (front != back)
			{
				back = mtlNewStencilDescriptor();
				mtlSetStencilFailureOperation(
					back,
					XNAToMTL.StencilOp[(int) depthStencilState.CounterClockwiseStencilFail]
				);
				mtlSetDepthFailureOperation(
					back,
					XNAToMTL.StencilOp[(int) depthStencilState.CounterClockwiseStencilDepthBufferFail]
				);
				mtlSetDepthStencilPassOperation(
					back,
					XNAToMTL.StencilOp[(int) depthStencilState.CounterClockwiseStencilPass]
				);
				mtlSetStencilCompareFunction(
					back,
					XNAToMTL.CompareFunc[(int) depthStencilState.CounterClockwiseStencilFunction]
				);
				mtlSetStencilReadMask(
					back,
					(uint) depthStencilState.StencilMask
				);
				mtlSetStencilWriteMask(
					back,
					(uint) depthStencilState.StencilWriteMask
				);
			}

			mtlSetFrontFaceStencil(
				dsDesc,
				front
			);
			mtlSetBackFaceStencil(
				dsDesc,
				back
			);

			// Bake the state!
			state = mtlNewDepthStencilStateWithDescriptor(
				device,
				dsDesc
			);
			DepthStencilStateCache[hash] = state;

			// Clean up
			objc_release(dsDesc);

			// Return the state!
			return state;
		}

		#endif

		#region Private State Object Caches

		private Dictionary<ulong, IntPtr> VertexDescriptorCache =
			new Dictionary<ulong, IntPtr>();

		//private Dictionary<PipelineHash, IntPtr> PipelineStateCache =
		//	new Dictionary<PipelineHash, IntPtr>();

		private Dictionary<StateHash, IntPtr> DepthStencilStateCache =
			new Dictionary<StateHash, IntPtr>();

		private Dictionary<StateHash, IntPtr> SamplerStateCache =
			new Dictionary<StateHash, IntPtr>();

		private List<VulkanTexture> transientTextures =
			new List<VulkanTexture>();

		#endregion


		private IntPtr FetchSamplerState(SamplerState samplerState, bool hasMipmaps)
		{
			// Can we just reuse an existing state?
			StateHash hash = PipelineCache.GetSamplerHash(samplerState);
			IntPtr state = IntPtr.Zero;
			if (SamplerStateCache.TryGetValue(hash, out state))
			{
				// The value is already cached!
				return state;
			}

			// We have to make a new sampler state...
			/*
			IntPtr samplerDesc = mtlNewSamplerDescriptor();

			mtlSetSampler_sAddressMode(
				samplerDesc,
				XNAToMTL.Wrap[(int) samplerState.AddressU]
			);
			mtlSetSampler_tAddressMode(
				samplerDesc,
				XNAToMTL.Wrap[(int) samplerState.AddressV]
			);
			mtlSetSampler_rAddressMode(
				samplerDesc,
				XNAToMTL.Wrap[(int) samplerState.AddressW]
			);
			mtlSetSamplerMagFilter(
				samplerDesc,
				XNAToMTL.MagFilter[(int) samplerState.Filter]
			);
			mtlSetSamplerMinFilter(
				samplerDesc,
				XNAToMTL.MinFilter[(int) samplerState.Filter]
			);
			if (hasMipmaps)
			{
				mtlSetSamplerMipFilter(
					samplerDesc,
					XNAToMTL.MipFilter[(int) samplerState.Filter]
				);
			}

			mtlSetSamplerLodMinClamp(
				samplerDesc,
				samplerState.MaxMipLevel
			);
			mtlSetSamplerMaxAnisotropy(
				samplerDesc,
				(samplerState.Filter == TextureFilter.Anisotropic) ? Math.Max(1, samplerState.MaxAnisotropy) : 1
			);
			*/

			/* FIXME:
			 * The only way to set lod bias in metal is via the MSL
			 * bias() function in a shader. So we can't do:
			 *
			 * 	mtlSetSamplerLodBias(
			 *		samplerDesc,
			 *		samplerState.MipMapLevelOfDetailBias
			 *	);
			 *
			 * What should we do instead?
			 *
			 * -caleb
			 */

			// Bake the sampler state!
			/*
			state = mtlNewSamplerStateWithDescriptor(
				device,
				samplerDesc
			);
			*/
			SamplerStateCache[hash] = state;

			// Clean up
			/*
			objc_release(samplerDesc);
			*/

			// Return the sampler state!
			return state;
		}

		private IntPtr FetchVertexDescriptor(
			VertexBufferBinding[] bindings,
			int numBindings
		)
		{
			// Can we just reuse an existing descriptor?
			ulong hash = PipelineCache.GetVertexBindingHash(
				bindings,
				numBindings,
				(ulong) shaderState.vertexShader
			);
			IntPtr descriptor;
			if (VertexDescriptorCache.TryGetValue(hash, out descriptor))
			{
				// The value is already cached!
				return descriptor;
			}

			// We have to make a new vertex descriptor...
			/*
			descriptor = mtlMakeVertexDescriptor();
			objc_retain(descriptor);
			*/

			/* There's this weird case where you can have overlapping
			 * vertex usage/index combinations. It seems like the first
			 * attrib gets priority, so whenever a duplicate attribute
			 * exists, give it the next available index. If that fails, we
			 * have to crash :/
			 * -flibit
			 */
			var attributeDescriptions = new List<VertexInputAttributeDescription>();
			Array.Clear(attrUse, 0, attrUse.Length);
			int stride = 0;
			for (int i = 0; i < numBindings; i += 1)
			{
				// Describe vertex attributes
				VertexDeclaration vertexDeclaration = bindings[i].VertexBuffer.VertexDeclaration;
				foreach (VertexElement element in vertexDeclaration.elements)
				{
					int usage = (int) element.VertexElementUsage;
					int index = element.UsageIndex;
					if (attrUse[usage, index])
					{
						index = -1;
						for (int j = 0; j < 16; j += 1)
						{
							if (!attrUse[usage, j])
							{
								index = j;
								break;
							}
						}

						if (index < 0)
						{
							throw new InvalidOperationException("Vertex usage collision!");
						}
					}

					attrUse[usage, index] = true;
					int attribLoc = MojoShader.MOJOSHADER_vkGetVertexAttribLocation(
						shaderState.vertexShader,
						XNAToVK.VertexAttribUsage[usage],
						index
					);
					if (attribLoc == -1)
					{
						// Stream not in use!
						continue;
					}

					var vertexInputAttributeDescription = new VertexInputAttributeDescription
					{
						// todo: all of this is hardcoded. why?
						Binding = 0, // This is an input, not a descriptor set. Hard-code to 0.
						Format = XNAToVK.VertexAttribType[(int) element.VertexElementFormat],
						Location = (uint)attribLoc, // todo: fairly certain this is correct.
						Offset = (uint)element.Offset, // todo: maybe correct
					};
					attributeDescriptions.Add(vertexInputAttributeDescription);

					// todo: impl this!!!
					/*
					IntPtr attrib = mtlGetVertexAttributeDescriptor(
						descriptor,
						attribLoc
					);
					mtlSetVertexAttributeFormat(
						attrib,
						XNAToMTL.VertexAttribType[(int) element.VertexElementFormat]
					);
					mtlSetVertexAttributeOffset(
						attrib,
						element.Offset
					);
					mtlSetVertexAttributeBufferIndex(
						attrib,
						i
					);
					*/
				}

				// Describe vertex buffer layout
				var x = 1;
				stride = vertexDeclaration.VertexStride;
				// todo: impl this!!!
				//userVertexStride =
				/*
				IntPtr layout = mtlGetVertexBufferLayoutDescriptor(
					descriptor,
					i
				);
				mtlSetVertexBufferLayoutStride(
					layout,
					vertexDeclaration.VertexStride
				);
				if (bindings[i].InstanceFrequency > 0)
				{
					mtlSetVertexBufferLayoutStepFunction(
						layout,
						MTLVertexStepFunction.PerInstance
					);
					mtlSetVertexBufferLayoutStepRate(
						layout,
						bindings[i].InstanceFrequency
					);
				}
				*/
			}
			_descriptions = attributeDescriptions.ToArray();

			_bindingDescription = new VertexInputBindingDescription
			{
				Stride = (uint)stride,
				Binding = 0,
				InputRate = VertexInputRate.Vertex,
			};

			// todo: this
		/*
		VertexDescriptorCache[hash] = descriptor;
		*/

		return descriptor;
		}

		private VertexInputAttributeDescription []_descriptions = new VertexInputAttributeDescription[] {};
		private VertexInputBindingDescription _bindingDescription;
		private IntPtr FetchVertexDescriptor(
			VertexDeclaration vertexDeclaration,
			int vertexOffset
		)
		{
			// Can we just reuse an existing descriptor?
			ulong hash = PipelineCache.GetVertexDeclarationHash(
				vertexDeclaration,
				(ulong) shaderState.vertexShader
			);
			IntPtr descriptor;
			if (VertexDescriptorCache.TryGetValue(hash, out descriptor))
			{
				// The value is already cached!
				return descriptor;
			}

			// We have to make a new vertex descriptor...
			/*
			descriptor = mtlMakeVertexDescriptor();
			objc_retain(descriptor);
			*/

			/* There's this weird case where you can have overlapping
			 * vertex usage/index combinations. It seems like the first
			 * attrib gets priority, so whenever a duplicate attribute
			 * exists, give it the next available index. If that fails, we
			 * have to crash :/
			 * -flibit
			 */
			var attributeDescriptions = new List<VertexInputAttributeDescription>();
			Array.Clear(attrUse, 0, attrUse.Length);
			foreach (VertexElement element in vertexDeclaration.elements)
			{
				int usage = (int) element.VertexElementUsage;
				int index = element.UsageIndex;
				if (attrUse[usage, index])
				{
					index = -1;
					for (int j = 0; j < 16; j += 1)
					{
						if (!attrUse[usage, j])
						{
							index = j;
							break;
						}
					}

					if (index < 0)
					{
						throw new InvalidOperationException("Vertex usage collision!");
					}
				}

				attrUse[usage, index] = true;
				int attribLoc = MojoShader.MOJOSHADER_vkGetVertexAttribLocation(
					shaderState.vertexShader,
					XNAToVK.VertexAttribUsage[usage],
					index
				);
				if (attribLoc == -1)
				{
					// Stream not in use!
					continue;
				}

				var vertexInputAttributeDescription = new VertexInputAttributeDescription
				{
					// todo: Binding and Location and Offset need a rethink. no gaurantee this is correct.
					Binding = 0,
					Format = XNAToVK.VertexAttribType[(int) element.VertexElementFormat],
					Location = (uint)attribLoc, // todo: play around with this. may not be necessary to hard-code.
					Offset = (uint)element.Offset, // todo: maybe correct
				};
				attributeDescriptions.Add(vertexInputAttributeDescription);

				/*
				IntPtr attrib = mtlGetVertexAttributeDescriptor(
					descriptor,
					attribLoc
				);
				mtlSetVertexAttributeFormat(
					attrib,
					XNAToMTL.VertexAttribType[(int) element.VertexElementFormat]
				);
				mtlSetVertexAttributeOffset(
					attrib,
					element.Offset
				);
				mtlSetVertexAttributeBufferIndex(
					attrib,
					0
				);
				*/
			}
			_descriptions = attributeDescriptions.ToArray();

			// Describe vertex buffer layout
			/*
			IntPtr layout = mtlGetVertexBufferLayoutDescriptor(
				descriptor,
				0
			);
			mtlSetVertexBufferLayoutStride(
				layout,
				vertexDeclaration.VertexStride
			);

			VertexDescriptorCache[hash] = descriptor;
			*/
			_bindingDescription = new VertexInputBindingDescription
			{
				Stride = (uint)vertexDeclaration.VertexStride,
				Binding = 0,
				InputRate = VertexInputRate.Vertex,
			};
			return descriptor;
		}

		private IntPtr FetchTransientTexture(VulkanTexture fromTexture)
		{
			// Can we just reuse an existing texture?
			for (int i = 0; i < transientTextures.Count; i += 1)
			{
				VulkanTexture tex = transientTextures[i];
				if (tex.Format == fromTexture.Format &&
				    tex.Width == fromTexture.Width &&
				    tex.Height == fromTexture.Height &&
				    tex.HasMipmaps == fromTexture.HasMipmaps)
				{
					//todo
					return IntPtr.Zero;
					/*
					mtlSetPurgeableState(
						tex.Handle,
						MTLPurgeableState.NonVolatile
					);
					return tex.Handle;
					*/
				}
			}

			// We have to make a new texture...
			/*
			IntPtr texDesc = mtlMakeTexture2DDescriptor(
				XNAToVK.TextureFormat[(int) fromTexture.Format],
				fromTexture.Width,
				fromTexture.Height,
				fromTexture.HasMipmaps
			);
			VulkanTexture ret = new VulkanTexture(
				mtlNewTextureWithDescriptor(device, texDesc),
				fromTexture.Width,
				fromTexture.Height,
				fromTexture.Format,
				fromTexture.HasMipmaps ? 2 : 0,
				false
			);
			transientTextures.Add(ret);
			return ret.Handle;
			*/
			//todo
			return IntPtr.Zero;
		}

		#endregion
	}
}
