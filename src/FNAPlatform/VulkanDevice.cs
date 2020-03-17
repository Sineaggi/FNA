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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Security.AccessControl;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Media;
using SDL2;
using Vulkan;
using Vulkan.Windows;
using Buffer = Vulkan.Buffer;
using Queue = Vulkan.Queue;

#endregion

namespace Microsoft.Xna.Framework.Graphics
{
	internal partial class VulkanDevice : IGLDevice
	{
		private class VulkanTexture : IGLTexture
		{
			public uint Handle
			{
				get;
				private set;
			}

			public bool HasMipmaps
			{
				get;
				private set;
			}

			public uint Width
			{
				get;
				private set;
			}

			public uint Height
			{
				get;
				private set;
			}

			public bool IsPrivate
			{
				get;
				private set;
			}

			public SurfaceFormat Format;
			public TextureAddressMode WrapS;
			public TextureAddressMode WrapT;
			public TextureAddressMode WrapR;
			public TextureFilter Filter;
			public float Anisotropy;
			public int MaxMipmapLevel;
			public float LODBias;

			//public DeviceSize ImageSize;
			//public Buffer Buffer;
			//public DeviceMemory DeviceMemory;
			//public uint Width;
			//public uint Height;

			public Image Image;
			public ImageView ImageView;
			public Sampler Sampler;
			public DeviceMemory ImageMemory;
			private Device device;

			public VulkanTexture(
				Device device,
				//DeviceSize imageSize,
				//Buffer buffer,
				//DeviceMemory deviceMemory,
				uint width,
				uint height,
				SurfaceFormat format,
				int levelCount,
				bool isPrivate
			)
			{
				this.device = device;
				HasMipmaps = levelCount > 1;
				//ImageSize = imageSize;
				//Buffer = buffer;
				//DeviceMemory = deviceMemory;
				Width = width;
				Height = height;
				Format = format;
				IsPrivate = isPrivate;

				WrapS = TextureAddressMode.Wrap;
				WrapT = TextureAddressMode.Wrap;
				WrapR = TextureAddressMode.Wrap;
				Filter = TextureFilter.Linear; // todo;
				Anisotropy = 4.0f;
				MaxMipmapLevel = 0;
				LODBias = 0.0f;
			}


			// We can't set a SamplerState Texture to null, so use this.
			private VulkanTexture()
			{
				Handle = 0;
				//Target = GLenum.GL_TEXTURE_2D; // FIXME: Assumption! -flibit
			}

			public static readonly VulkanTexture NullTexture = new VulkanTexture();

			public void Dispose()
			{
				device.DestroyImage(Image);
				device.FreeMemory(ImageMemory);
			}
		}


		#region Vulkan Renderbuffer Container Class

		private class VulkanRenderbuffer : IGLRenderbuffer
		{
			public IntPtr Handle
			{
				get;
				private set;
			}

			public IntPtr MultiSampleHandle
			{
				get;
				private set;
			}

			public Format PixelFormat
			{
				get;
				private set;
			}

			public int MultiSampleCount
			{
				get;
				private set;
			}

			public VulkanRenderbuffer(
				IntPtr handle,
				Format pixelFormat,
				int multiSampleCount,
				IntPtr multiSampleHandle
			) {
				Handle = handle;
				PixelFormat = pixelFormat;
				MultiSampleCount = multiSampleCount;
				MultiSampleHandle = multiSampleHandle;
			}

			public void Dispose()
			{
				if (MultiSampleHandle == IntPtr.Zero)
				{
					//todo
					//objc_release(Handle);
					Handle = IntPtr.Zero;
				}
				else
				{
					//todo
					//objc_release(MultiSampleHandle);
					MultiSampleHandle = IntPtr.Zero;

					/* Don't release the regular Handle since
					 * it's owned by the associated IGLTexture.
					 */
					Handle = IntPtr.Zero;
				}
			}
		}

		#endregion

		#region Vulkan Buffer Container Class

		private class VulkanBuffer : IGLBuffer
		{
			public IntPtr Contents
			{
				get;
				private set;
			}

			public Buffer Buffer { get; private set; }

			public DeviceMemory BufferMemory { get; private set; }

			public IntPtr BufferSize { get; }

			private VulkanDevice device;
			private BufferUsageFlags usageFlags;
			private int internalBufferSize = 0;
			private int prevDataLength = 0;
			private int prevInternalOffset;
			private BufferUsage usage;
			private bool boundThisFrame;

			public int InternalOffset
			{
				get;
				private set;
			}

			public void Bound()
			{
				boundThisFrame = true;
			}

			public void Reset()
			{
				InternalOffset = 0;
				boundThisFrame = false;
				prevDataLength = 0;
			}

			public void Dispose()
			{
				//objc_release(Handle);
				//Handle = IntPtr.Zero;
				device.device.DestroyBuffer(Buffer);
				device.device.FreeMemory(BufferMemory);
			}

			public VulkanBuffer(VulkanDevice device,
				BufferUsageFlags usageFlags,
				BufferUsage usage,
				IntPtr bufferSize)
			{
				this.device = device;
				this.usageFlags = usageFlags;
				this.usage = usage;

				BufferSize = bufferSize;
				internalBufferSize = (int) bufferSize;

				CreateBackingBuffer(-1);
			}

			public Boolean DataWasSet { get; private set; }

			private void CreateBackingBuffer(int prevSize)
			{
				Buffer oldBuffer = Buffer;
				DeviceMemory oldDeviceMemory = BufferMemory;
				IntPtr oldContents = Contents;

				// todo: consult FNA usage flags.
				// todo: use more efficient texture access? make host coherent buffers only for user data
				// otherwise, shuffle data with copy buffer calls.
				device.createBuffer(internalBufferSize, usageFlags, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out var createdBuffer, out var createdBufferMemory);
				Buffer = createdBuffer;
				BufferMemory = createdBufferMemory;
				Contents = device.device.MapMemory(BufferMemory, 0, internalBufferSize, 0);

				// Copy over data from old buffer
				if (oldBuffer != null)
				{
					SDL.SDL_memcpy(
						Contents,
						oldContents,
						(IntPtr) prevSize
					);
					// todo: add support for this.
					//device.device.WaitIdle();
					//device.device.UnmapMemory(oldDeviceMemory);
					//device.device.DestroyBuffer(oldBuffer);
					//device.device.FreeMemory(oldDeviceMemory);
				}
			}

			public void SetData(
				int offsetInBytes,
				IntPtr data,
				int dataLength,
				SetDataOptions options
			)
			{
				DataWasSet = true;
				if (options == SetDataOptions.None && boundThisFrame)
				{
					throw new Exception("this block isn't supported");
					device.device.WaitIdle();
					//device.Stall();
					boundThisFrame = true;
				}
				else if (options == SetDataOptions.Discard && boundThisFrame)
				{
					InternalOffset += (int) BufferSize;
					if (InternalOffset + dataLength > internalBufferSize)
					{
						// Expand!
						int prevSize = internalBufferSize;
						internalBufferSize *= 2;
						CreateBackingBuffer(prevSize);
					}
				}

				// Copy previous contents, if needed
				if (prevInternalOffset != InternalOffset && dataLength < (int) BufferSize)
				{
					SDL.SDL_memcpy(
						Contents + InternalOffset,
						Contents + prevInternalOffset,
						BufferSize
					);
				}

				// Copy the data into the buffer
				SDL.SDL_memcpy(
					Contents + InternalOffset + offsetInBytes,
					data,
					(IntPtr) dataLength
				);

				prevInternalOffset = InternalOffset;
				prevDataLength = (int) BufferSize;
			}

			public void SetData(
				int offsetInBytes,
				IntPtr data,
				int dataLength
			)
			{
				InternalOffset += prevDataLength;
				if (InternalOffset + dataLength > internalBufferSize)
				{
					// Expand!
					int prevSize = internalBufferSize;
					internalBufferSize = Math.Max(
						internalBufferSize * 2,
						internalBufferSize + dataLength
					);
					CreateBackingBuffer(prevSize);
				}

				// Copy the data into the buffer
				throw new NotImplementedException();
				/*
				SDL.SDL_memcpy(
					Contents + InternalOffset,
					data + offsetInBytes,
					(IntPtr) dataLength
				);
				*/

				prevDataLength = dataLength;
			}
		}

		#endregion

		#region Vulkan Effect Container Class

		private class VulkanEffect : IGLEffect
		{
			public IntPtr EffectData { get; private set; }

			public IntPtr VKEffectData { get; private set; }

			public List<ShaderBundle> ShaderBundles { get; set; }

			public uint NumShaders { get; set; }
			public uint NumPreshaders { get; set; }
			public uint[] ShaderIndices { get; set; }
			public uint[] PreshaderIndices { get; set; }

			public VkShader[] Shaders { get; set; }

			public VulkanEffect(IntPtr effect, IntPtr vkEffect)
			{
				EffectData = effect;
				VKEffectData = vkEffect;
			}
		}

		private ClearColorValue _clearColorValue = new ClearColorValue
		{
			Float32 = new []{0.0f, 0.0f, 0.0f, 1.0f}
		};

		#endregion

				#region XNA->GL Enum Conversion Class

		private static class XNAToVK
		{
			public static readonly Format[] TextureFormat = new Format[]
			{
				Format.R8G8B8A8Unorm,	// SurfaceFormat.Color
				Format.B5G6R5UnormPack16,	// SurfaceFormat.Bgr565
				Format.B5G5R5A1UnormPack16,	// SurfaceFormat.Bgra5551
				Format.B4G4R4A4UnormPack16,	// SurfaceFormat.Bgra4444
				Format.Bc1RgbaUnormBlock,	// SurfaceFormat.Dxt1
				Format.Bc2UnormBlock,	// SurfaceFormat.Dxt3
				Format.Bc3UnormBlock,	// SurfaceFormat.Dxt5
				Format.R8G8Snorm,	// SurfaceFormat.NormalizedByte2
				Format.Undefined,	// SurfaceFormat.NormalizedByte4
				Format.A2R10G10B10UnormPack32, // todo: unsupported format?	// SurfaceFormat.Rgba1010102
				Format.R16G16Unorm,	// SurfaceFormat.Rg32
				Format.R16G16B16A16Unorm,	// SurfaceFormat.Rgba64
				Format.R8Unorm,		// SurfaceFormat.Alpha8
				Format.R32Sfloat,	// SurfaceFormat.Single
				Format.R32G32Sfloat,	// SurfaceFormat.Vector2
				Format.R32G32B32A32Sfloat,	// SurfaceFormat.Vector4
				Format.R16Sfloat,	// SurfaceFormat.HalfSingle
				Format.R16G16Sfloat,	// SurfaceFormat.HalfVector2
				Format.R16G16B16A16Sfloat,	// SurfaceFormat.HalfVector4
				Format.R16G16B16A16Sfloat,	// SurfaceFormat.HdrBlendable
				Format.B8G8R8A8Unorm,	// SurfaceFormat.ColorBgraEXT
			};

			public static readonly MojoShader.MOJOSHADER_usage[] VertexAttribUsage = new MojoShader.MOJOSHADER_usage[]
			{
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_POSITION,		// VertexElementUsage.Position
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_COLOR,		// VertexElementUsage.Color
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_TEXCOORD,		// VertexElementUsage.TextureCoordinate
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_NORMAL,		// VertexElementUsage.Normal
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_BINORMAL,		// VertexElementUsage.Binormal
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_TANGENT,		// VertexElementUsage.Tangent
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_BLENDINDICES,	// VertexElementUsage.BlendIndices
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_BLENDWEIGHT,	// VertexElementUsage.BlendWeight
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_DEPTH,		// VertexElementUsage.Depth
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_FOG,		// VertexElementUsage.Fog
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_POINTSIZE,		// VertexElementUsage.PointSize
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_SAMPLE,		// VertexElementUsage.Sample
				MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_TESSFACTOR		// VertexElementUsage.TessellateFactor
			};

			// "A bit confusingly, the formats are specified using the same enumeration as color formats"
			// source: https://vulkan-tutorial.com/Vertex_buffers/Vertex_input_description
			public static readonly Format[] VertexAttribType = new Format[]
			{
				Format.R32Sfloat,			// VertexElementFormat.Single
				Format.R32G32Sfloat,			// VertexElementFormat.Vector2
				Format.R32G32B32Sfloat,			// VertexElementFormat.Vector3
				Format.R32G32B32A32Sfloat,			// VertexElementFormat.Vector4
				Format.R8G8B8A8Unorm,	// VertexElementFormat.Color
				Format.R8G8B8A8Uint,			// VertexElementFormat.Byte4
				Format.R16G16Sint,			// VertexElementFormat.Short2
				Format.R16G16B16A16Sint,			// VertexElementFormat.Short4
				Format.R16G16Snorm,	// VertexElementFormat.NormalizedShort2
				Format.R16G16B16A16Snorm,	// VertexElementFormat.NormalizedShort4
				Format.R16G16Sfloat,			// VertexElementFormat.HalfVector2
				Format.R16G16B16A16Sfloat,			// VertexElementFormat.HalfVector4
			};

			public static readonly IndexType[] IndexType = new IndexType[]
			{
				Vulkan.IndexType.Uint16,	// IndexElementSize.SixteenBits
				Vulkan.IndexType.Uint32,	// IndexElementSize.ThirtyTwoBits
			};

			public static readonly int[] IndexSize = new int[]
			{
				2,	// IndexElementSize.SixteenBits
				4	// IndexElementSize.ThirtyTwoBits
			};

			public static readonly BlendFactor[] BlendMode = new BlendFactor[]
			{

				Vulkan.BlendFactor.One,			// Blend.One
				Vulkan.BlendFactor.Zero,			// Blend.Zero
				Vulkan.BlendFactor.SrcColor,		// Blend.SourceColor
				Vulkan.BlendFactor.OneMinusDstColor,	// Blend.InverseSourceColor
				Vulkan.BlendFactor.SrcAlpha,		// Blend.SourceAlpha
				Vulkan.BlendFactor.OneMinusSrcAlpha,	// Blend.InverseSourceAlpha
				Vulkan.BlendFactor.DstColor,	// Blend.DestinationColor
				Vulkan.BlendFactor.OneMinusDstColor,// Blend.InverseDestinationColor
				Vulkan.BlendFactor.DstAlpha,	// Blend.DestinationAlpha
				Vulkan.BlendFactor.OneMinusDstAlpha,// Blend.InverseDestinationAlpha
				Vulkan.BlendFactor.ConstantColor,		// Blend.BlendFactor
				Vulkan.BlendFactor.OneMinusConstantColor,	// Blend.InverseBlendFactor
				Vulkan.BlendFactor.SrcAlphaSaturate,	// Blend.SourceAlphaSaturation
			};

			public static readonly BlendOp[] BlendOperation = new BlendOp[]
			{
				BlendOp.Add,			// BlendFunction.Add
				BlendOp.Subtract,		// BlendFunction.Subtract
				BlendOp.ReverseSubtract,	// BlendFunction.ReverseSubtract
				BlendOp.Max,			// BlendFunction.Max
				BlendOp.Min,			// BlendFunction.Min
			};

			public static int ColorWriteMask(ColorWriteChannels channels)
			{
				if (channels == ColorWriteChannels.None)
				{
					return 0x0;
				}
				if (channels == ColorWriteChannels.All)
				{
					return 0xf;
				}

				int ret = 0;
				if ((channels & ColorWriteChannels.Red) != 0)
				{
					ret |= (0x1 << 3);
				}
				if ((channels & ColorWriteChannels.Green) != 0)
				{
					ret |= (0x1 << 2);
				}
				if ((channels & ColorWriteChannels.Blue) != 0)
				{
					ret |= (0x1 << 1);
				}
				if ((channels & ColorWriteChannels.Alpha) != 0)
				{
					ret |= (0x1 << 0);
				}
				return ret;
			}

			public static readonly CompareOp[] CompareFunc = new CompareOp[]
			{
				CompareOp.Always,	// CompareFunction.Always
				CompareOp.Never,	// CompareFunction.Never
				CompareOp.Less,	// CompareFunction.Less
				CompareOp.LessOrEqual,	// CompareFunction.LessEqual
				CompareOp.Equal,	// CompareFunction.Equal
				CompareOp.GreaterOrEqual,// CompareFunction.GreaterEqual
				CompareOp.Greater,	// CompareFunction.Greater
				CompareOp.NotEqual	// CompareFunction.NotEqual
			};



			public static readonly StencilOp[] StencilOp = new StencilOp[]
			{
				Vulkan.StencilOp.Keep,		// StencilOperation.Keep
				Vulkan.StencilOp.Zero,		// StencilOperation.Zero
				Vulkan.StencilOp.Replace,		// StencilOperation.Replace
				Vulkan.StencilOp.IncrementAndWrap,	// StencilOperation.Increment
				Vulkan.StencilOp.DecrementAndWrap,	// StencilOperation.Decrement
				Vulkan.StencilOp.IncrementAndClamp,	// StencilOperation.IncrementSaturation
				Vulkan.StencilOp.DecrementAndClamp,	// StencilOperation.DecrementSaturation
				Vulkan.StencilOp.Invert		// StencilOperation.Invert
			};

			public static readonly PolygonMode[] FillMode = new Vulkan.PolygonMode[]
			{
				PolygonMode.Fill,	// FillMode.Solid
				PolygonMode.Line	// FillMode.WireFrame
			};

			public static readonly float[] DepthBiasScale = new float[]
			{
				0.0f,				// DepthFormat.None
				(float) ((1 << 16) - 1),	// DepthFormat.Depth16
				(float) ((1 << 24) - 1),	// DepthFormat.Depth24
				(float) ((1 << 24) - 1)		// DepthFormat.Depth24Stencil8
			};

			public static readonly CullModeFlags[] CullingEnabled = new CullModeFlags[]
			{
				CullModeFlags.None,	// CullMode.None
				CullModeFlags.Front,	// CullMode.CullClockwiseFace
				CullModeFlags.Back	// CullMode.CullCounterClockwiseFace
			};

			public static readonly SamplerAddressMode[] Wrap = new SamplerAddressMode[]
			{
				SamplerAddressMode.Repeat,		// TextureAddressMode.Wrap
				SamplerAddressMode.ClampToEdge,	// TextureAddressMode.Clamp
				SamplerAddressMode.MirroredRepeat	// TextureAddressMode.Mirror
			};

			public static readonly Filter[] MagFilter = new Filter[]
			{
				Filter.Linear,	// TextureFilter.Linear
				Filter.Nearest,	// TextureFilter.Point
				Filter.Linear,	// TextureFilter.Anisotropic
				Filter.Linear,	// TextureFilter.LinearMipPoint
				Filter.Nearest,	// TextureFilter.PointMipLinear
				Filter.Nearest,	// TextureFilter.MinLinearMagPointMipLinear
				Filter.Nearest,	// TextureFilter.MinLinearMagPointMipPoint
				Filter.Linear,	// TextureFilter.MinPointMagLinearMipLinear
				Filter.Linear	// TextureFilter.MinPointMagLinearMipPoint
			};

			public static readonly Filter[] MipFilter = new Filter[]
			{
				Filter.Linear,	// TextureFilter.Linear
				Filter.Nearest,	// TextureFilter.Point
				Filter.Linear,	// TextureFilter.Anisotropic
				Filter.Nearest,	// TextureFilter.LinearMipPoint
				Filter.Linear,	// TextureFilter.PointMipLinear
				Filter.Linear,	// TextureFilter.MinLinearMagPointMipLinear
				Filter.Nearest,	// TextureFilter.MinLinearMagPointMipPoint
				Filter.Linear,	// TextureFilter.MinPointMagLinearMipLinear
				Filter.Nearest	// TextureFilter.MinPointMagLinearMipPoint
			};

			public static readonly Filter[] MinFilter = new Filter[]
			{
				Filter.Linear,	// TextureFilter.Linear
				Filter.Nearest,	// TextureFilter.Point
				Filter.Linear,	// TextureFilter.Anisotropic
				Filter.Linear,	// TextureFilter.LinearMipPoint
				Filter.Nearest,	// TextureFilter.PointMipLinear
				Filter.Linear,	// TextureFilter.MinLinearMagPointMipLinear
				Filter.Linear,	// TextureFilter.MinLinearMagPointMipPoint
				Filter.Nearest,	// TextureFilter.MinPointMagLinearMipLinear
				Filter.Nearest	// TextureFilter.MinPointMagLinearMipPoint
			};

			public static readonly PrimitiveTopology[] Primitive = new PrimitiveTopology[]
			{
				PrimitiveTopology.TriangleList,	// PrimitiveType.TriangleList
				PrimitiveTopology.TriangleStrip,	// PrimitiveType.TriangleStrip
				PrimitiveTopology.LineList,		// PrimitiveType.LineList
				PrimitiveTopology.LineStrip,	// PrimitiveType.LineStrip
				PrimitiveTopology.PointList		// PrimitiveType.PointListEXT
			};

			public static int PrimitiveVerts(PrimitiveType primitiveType, int primitiveCount)
			{
				switch (primitiveType)
				{
					case PrimitiveType.TriangleList:
						return primitiveCount * 3;
					case PrimitiveType.TriangleStrip:
						return primitiveCount + 2;
					case PrimitiveType.LineList:
						return primitiveCount * 2;
					case PrimitiveType.LineStrip:
						return primitiveCount + 1;
					case PrimitiveType.PointListEXT:
						return primitiveCount;
				}
				throw new NotSupportedException();
			}

			public static SampleCountFlags SampleCountFlags(int sampleCount)
			{
				switch (sampleCount)
				{
					case 64:
						return Vulkan.SampleCountFlags.Count64;
					case 32:
						return Vulkan.SampleCountFlags.Count32;
					case 16:
						return Vulkan.SampleCountFlags.Count16;
					case 8:
						return Vulkan.SampleCountFlags.Count8;
					case 4:
						return Vulkan.SampleCountFlags.Count4;
					case 2:
						return Vulkan.SampleCountFlags.Count2;
					case 1:
						return Vulkan.SampleCountFlags.Count1;
				}
				throw new NotSupportedException();
			}
		}

		#endregion


		#region The Faux-Backbuffer

		private bool UseFauxBackbuffer(PresentationParameters presentationParameters, DisplayMode mode)
		{
			int drawX, drawY;
			SDL.SDL_GL_GetDrawableSize(
				presentationParameters.DeviceWindowHandle,
				out drawX,
				out drawY
			);
			bool displayMismatch = (drawX != presentationParameters.BackBufferWidth ||
			                        drawY != presentationParameters.BackBufferHeight);
			return displayMismatch || (presentationParameters.MultiSampleCount > 0);
		}

		private class VulkanBackbuffer : IGLBackbuffer
		{
			public uint Handle { get; private set; }

			public int Width { get; private set; }

			public int Height { get; private set; }

			public Format PixelFormat
			{
				get;
				private set;
			}

			public DepthFormat DepthFormat { get; private set; }

			public int MultiSampleCount { get; private set; }

			public uint Texture;

			private uint colorAttachment;
			private uint depthStencilAttachment;

			public IntPtr ColorBuffer = IntPtr.Zero;
			public IntPtr MultiSampleColorBuffer = IntPtr.Zero;
			public IntPtr DepthStencilBuffer = IntPtr.Zero;

			private VulkanDevice vkDevice;

			public VulkanBackbuffer(
				VulkanDevice device,
				int width,
				int height,
				DepthFormat depthFormat,
				int multiSampleCount
			)
			{
				Width = width;
				Height = height;

				vkDevice = device;
				DepthFormat = depthFormat;
				MultiSampleCount = multiSampleCount;
				Texture = 0;

				// Generate and bind the FBO.
				uint handle = 0;
				Handle = handle;

				if (depthFormat == DepthFormat.None)
				{
					// Don't bother creating a depth/stencil buffer.
					depthStencilAttachment = 0;
					return;
				}

				PixelFormat = Format.R8G8B8A8Unorm;
			}

			public void Dispose()
			{
				uint handle = Handle;
				vkDevice = null;
				Handle = 0;
			}

			public void ResetFramebuffer(
				PresentationParameters presentationParameters
			)
			{
				Width = presentationParameters.BackBufferWidth;
				Height = presentationParameters.BackBufferHeight;

				DepthFormat depthFormat = presentationParameters.DepthStencilFormat;
				MultiSampleCount = presentationParameters.MultiSampleCount;

				DepthFormat = depthFormat;
			}
		}

		#endregion


		public Color BlendFactor { get; set; }
		public int MultiSampleMask { get; set; }
		public int ReferenceStencil { get; set; }
		public bool SupportsDxt1 { get; }
		public bool SupportsS3tc { get; }
		public bool SupportsHardwareInstancing { get; }
		public bool SupportsNoOverwrite { get; }
		public int MaxTextureSlots
		{
			get
			{
				return 16;
			}
		}
		public int MaxMultiSampleCount { get; }
		public IGLBackbuffer Backbuffer { get; private set; }

		private PhysicalDevice physicalDevice;
		private Device device;
		private CommandPool commandPool;
		private Queue graphicsQueue;
		private Queue presentQueue;
		private SurfaceKhr surface;
		private CommandBuffer _commandBuffer;
		private PhysicalDeviceMemoryProperties _deviceMemoryProperties;

		private Image[] images;
		private ImageView[] imageViews;
		private Framebuffer[] _framebuffers;

		private Semaphore acquireSemaphore;
		private Semaphore releaseSemaphore;

		private uint imageIndex;
		private Vulkan.Image currentImage;

		private Vulkan.SwapchainKhr _swapchainKhr;

		private MojoShader.MOJOSHADER_vkShaderState shaderState = new MojoShader.MOJOSHADER_vkShaderState();
		private MojoShader.MOJOSHADER_vkShaderState prevShaderState;

		struct QueueFamilyIndices
		{
			internal uint? GraphicsFamily { get; set; }
			internal uint? PresentFamily { get; set; }

			internal bool IsComplete => GraphicsFamily != null && PresentFamily != null;
		}

		QueueFamilyIndices findQueueFamilies(PhysicalDevice physicalDevice)
		{
			var indices = new QueueFamilyIndices();

			var props = physicalDevice.GetQueueFamilyProperties();
			uint i = 0;
			foreach (var queueFamilyPropertiese in props)
			{
				if (queueFamilyPropertiese.QueueFlags.HasFlag(QueueFlags.Graphics))
				{
					indices.GraphicsFamily = i;
				}

				if (physicalDevice.GetSurfaceSupportKHR(i, surface))
				{
					indices.PresentFamily = i;
				}

				if (indices.IsComplete)
				{
					break;
				}

				i++;
			}

			return indices;
		}

		private Bool32 DebugCallback(DebugReportFlagsExt flags, DebugReportObjectTypeExt objectType, ulong objectHandle,
			IntPtr location, int messageCode, IntPtr layerPrefix, IntPtr message, IntPtr userData)
		{
			var umessage = Marshal.PtrToStringAnsi(message);
			// todo: if I come across something I want to ignore, return false

			if (umessage.Contains("Device Extension")
			    || umessage.Contains("Loading layer library")
			    || umessage.Contains("Inserted device layer"))
			{

			}
			else
			{
				int x = 2;
			}

			if (flags.HasFlag(DebugReportFlagsExt.Error))
			{
				Debug.WriteLine($"{flags}: {umessage}");
			}


			return true;
		}

		#region Public Constructor

		private uint windowWidth, windowHeight; // todo: need's support for re-creating swapchain.

		public VulkanDevice(
			PresentationParameters presentationParameters,
			GraphicsAdapter adapter
		)
		{
			//device = MTLCreateSystemDefaultDevice();
			//queue = mtlNewCommandQueue(device);
/*
 *     const char *debugLayers[] = {
            //"VK_LAYER_LUNARG_standard_validation"
            "VK_LAYER_KHRONOS_validation"
    };

    createInfo.ppEnabledLayerNames = debugLayers;
    createInfo.enabledLayerCount = sizeof(debugLayers) / sizeof(debugLayers[0]);
 */


			var instance = new Instance(new InstanceCreateInfo
			{
				ApplicationInfo = new ApplicationInfo
				{
					//ApiVersion = 4194304, // 1.0
					ApiVersion = 4198400, // 1.1 4198400
				},
				EnabledLayerNames = new [] {"VK_LAYER_KHRONOS_validation"},
				//VK_EXT_debug_report
				EnabledExtensionNames = new string[] {"VK_KHR_surface", "VK_KHR_win32_surface", "VK_EXT_debug_report"}
			});

			instance.EnableDebug(DebugCallback);

			//var hWnd = presentationParameters.DeviceWindowHandle;

			//typeof(SDL).Module

			//var hInstance4 = System.Runtime.InteropServices.Marshal.GetHINSTANCE(typeof(Marshal).Module);
			var hInstance = System.Runtime.InteropServices.Marshal.GetHINSTANCE(typeof(SDL).Module);
			//var hInstance2 = System.Runtime.InteropServices.Marshal.GetHINSTANCE(typeof(VulkanDevice).Module);
			//var hInstance3 = System.Runtime.InteropServices.Marshal.GetHINSTANCE(typeof(SDL).Module);

			//var hWnd = new System.Windows.Interop.WindowInteropHelper (this).EnsureHandle ();
			//var hInstance = System.Runtime.InteropServices.Marshal.GetHINSTANCE (typeof (App).Module);

			//SDL.SDL_ShowWindow(presentationParameters.DeviceWindowHandle);

			//ulong surfacee;
			//SDL.SDL_Vulkan_CreateSurface(presentationParameters.DeviceWindowHandle, horntstance, out surfacee);

			foreach (var enumeratePhysicalDevice in instance.EnumeratePhysicalDevices())
			{
				var properties = enumeratePhysicalDevice.GetProperties();
				if (properties.DeviceType == PhysicalDeviceType.DiscreteGpu)
				{
					physicalDevice = enumeratePhysicalDevice;
				}
			}

			if (physicalDevice == null)
			{
				throw new Exception("Could nof ind a fisical depice");
			}

			_deviceMemoryProperties = physicalDevice.GetMemoryProperties();

			// surface
			// supported

			// todo: this code is problematic. unlike the other devices, which uses the related sdl.createXsurface functions,
			//   this uses the CreateWin32SurfaceKHR function directly. Why? I need a SurfaceKHR and can't create one any
			//  other way
			if (false)
			{
				ulong surfaceLong;
				var instancePtr = ((IMarshalling) instance).Handle;
				if (SDL.SDL_Vulkan_CreateSurface(presentationParameters.DeviceWindowHandle, instancePtr,
					    out surfaceLong) ==
				    SDL.SDL_bool.SDL_FALSE)
				{
					throw new Exception($"Failed to create boiii {SDL.SDL_GetError()}");
				}

				// todo: how to convert surfaceLong to SurfaceKHR
			}

			{
				SDL.SDL_SysWMinfo wmInfo = new SDL.SDL_SysWMinfo();
				SDL.SDL_VERSION(out wmInfo.version);
				SDL.SDL_GetWindowWMInfo(
					presentationParameters.DeviceWindowHandle,
					ref wmInfo
				);
				var hWnd = wmInfo.info.win.window;

				surface = instance.CreateWin32SurfaceKHR(new Win32SurfaceCreateInfoKhr
					{Hwnd = hWnd, Hinstance = hInstance});
			}

			//physicalDevice.EnumerateDeviceExtensionProperties();
			//physicalDevice.EnumerateDeviceLayerProperties();

			var surfaceCaps = physicalDevice.GetSurfaceCapabilitiesKHR(surface);

			QueueFamilyIndices indices = findQueueFamilies(physicalDevice);
			if (!indices.GraphicsFamily.HasValue || !indices.PresentFamily.HasValue)
			{
				throw new Exception("YO INCOMPLETE");
			}
			uint graphicsQueueIndex = indices.GraphicsFamily.Value;
			uint presentQueueIndex = indices.PresentFamily.Value;

			var uniqueQueueFamilies = new HashSet<uint>();
			uniqueQueueFamilies.Add(graphicsQueueIndex);
			uniqueQueueFamilies.Add(presentQueueIndex);

			var queueCreateInfos = uniqueQueueFamilies.Select(queueFamily => new DeviceQueueCreateInfo
				{QueueFamilyIndex = queueFamily, QueueCount = 1, QueuePriorities = new[] {1.0f}}).ToArray();

			device = physicalDevice.CreateDevice(new DeviceCreateInfo
			{
				QueueCreateInfos = queueCreateInfos,
				EnabledExtensionNames = new[]
				{
					"VK_KHR_swapchain",
					//"VK_KHR_push_descriptor"
				},
				EnabledFeatures = new PhysicalDeviceFeatures
				{
					SamplerAnisotropy = true,
				}
			});

			graphicsQueue = device.GetQueue(graphicsQueueIndex, 0);
			presentQueue = device.GetQueue(presentQueueIndex, 0);

			commandPool = device.CreateCommandPool(new CommandPoolCreateInfo
				{Flags = CommandPoolCreateFlags.ResetCommandBuffer});

			Backbuffer = new VulkanBackbuffer(
				this,
				presentationParameters.BackBufferWidth,
				presentationParameters.BackBufferHeight,
				presentationParameters.DepthStencilFormat,
				presentationParameters.MultiSampleCount
			);

			_commandBuffer = device.AllocateCommandBuffers(new CommandBufferAllocateInfo
				{CommandPool = commandPool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1})[0];

			// Set up the CAMetalLayer
			//mtlSetLayerDevice(layer, device);
			//mtlSetLayerFramebufferOnly(layer, true);
			//mtlSetLayerMagnificationFilter(layer, UTF8ToNSString("nearest"));

			{
				int w, h;
				SDL.SDL_Vulkan_GetDrawableSize(presentationParameters.DeviceWindowHandle, out w, out h);
				windowWidth = (uint) w;
				windowHeight = (uint) h;
			}

			setupSwapchain(surfaceCaps, windowWidth, windowHeight);

			var kk = (ulong)((IMarshalling) device).Handle;
			var kkola = Convert.ToString(53, 2);
			var llpla = Convert.ToString((long)kk, 2).PadLeft(64, '0');

			var shaderContext = MojoShader.MOJOSHADER_vkInitDevice(((IMarshalling)device).Handle, ((IMarshalling)physicalDevice).Handle);
			if (shaderContext == IntPtr.Zero)
			{
				throw new Exception("Failed to init device");
			}

			// make instance
			// make fucker
			// make physicaldevice, device

			// Log GLDevice info
			FNALoggerEXT.LogInfo("IGLDevice: VulkanDevice");
			FNALoggerEXT.LogInfo("Device Name: " + physicalDevice.GetProperties().DeviceName);
			FNALoggerEXT.LogInfo("MojoShader Profile: spirv");

			// Add fallbacks for missing texture formats
			if (true)
			{
				XNAToVK.TextureFormat[(int) SurfaceFormat.Bgr565]
					= Format.B8G8R8A8Unorm;
				XNAToVK.TextureFormat[(int) SurfaceFormat.Bgra5551]
					= Format.B8G8R8A8Unorm;
				XNAToVK.TextureFormat[(int) SurfaceFormat.Bgra4444]
					= Format.B8G8R8A8Unorm;
			}

			// Initialize texture and sampler collections
			Textures = new VulkanTexture[MaxTextureSlots];
			Samplers = new IntPtr[MaxTextureSlots];
			for (int i = 0; i < MaxTextureSlots; i += 1)
			{
				Textures[i] = VulkanTexture.NullTexture;
				Samplers[i] = IntPtr.Zero;
			}
			textureNeedsUpdate = new bool[MaxTextureSlots];
			samplerNeedsUpdate = new bool[MaxTextureSlots];

			foreach (var format in XNAToVK.TextureFormat)
			{
				var properties = physicalDevice.GetFormatProperties(format);
			}

			var rgbaprops = physicalDevice.GetFormatProperties(Format.R8G8B8A8Unorm);
			var bgraprops = physicalDevice.GetFormatProperties(Format.B8G8R8A8Unorm);

			// Add fallbacks for missing texture formats on NVidia hardware
			for (int i = 0; i < XNAToVK.TextureFormat.Length; i++)
			{
				// todo: check if format is supported. if not, fallback.
				var format = XNAToVK.TextureFormat[i];
				var properties = physicalDevice.GetFormatProperties(format);
				// These will have flags like 'can be used for VertexBuffer' usage.
				// We only care about image feature flags.

				var bufferFeatures = properties.BufferFeatures;
				var linearTilingFeatures = properties.LinearTilingFeatures;
				var optimalTilingFeatures = properties.OptimalTilingFeatures;

				// todo: need to figure out what flags actually need checking.
				// todo: need to find out exactly what flags NVidia 1080ti supports.
				if (!bufferFeatures.HasFlag(FormatFeatureFlags.SampledImage) ||
				    !linearTilingFeatures.HasFlag(FormatFeatureFlags.SampledImageFilterLinear) ||
				    !optimalTilingFeatures.HasFlag(FormatFeatureFlags.SampledImageFilterLinear))
				{
					Console.WriteLine($"Unsupported tex {format}");
					if (format == Format.R8G8B8Unorm)
					{
						XNAToVK.TextureFormat[i] = Format.R8G8B8A8Unorm;
					}
					// todo: else handle other formats too.
				}



				if (!bufferFeatures.HasFlag(FormatFeatureFlags.ColorAttachment))
				{
					int x = 2;
				}
				else
				{

				}
			}

			/*
			// Add fallbacks for missing texture formats on macOS
			if (isMac)
			{
				XNAToMTL.TextureFormat[(int) SurfaceFormat.Bgr565]
					= MTLPixelFormat.BGRA8Unorm;
				XNAToMTL.TextureFormat[(int) SurfaceFormat.Bgra5551]
					= MTLPixelFormat.BGRA8Unorm;
				XNAToMTL.TextureFormat[(int) SurfaceFormat.Bgra4444]
					= MTLPixelFormat.BGRA8Unorm;
			}
			*/

			// Initialize attachment arrays
			int numAttachments = GraphicsDevice.MAX_RENDERTARGET_BINDINGS;
			currentAttachments = new IntPtr[numAttachments];
			currentColorFormats = new Format[numAttachments];
			currentMSAttachments = new IntPtr[numAttachments];
			currentAttachmentSlices = new CubeMapFace[numAttachments];

			// Initialize vertex buffer cache
			ldVertexBuffers = new ulong[MAX_BOUND_VERTEX_BUFFERS];
			ldVertexBufferOffsets = new int[MAX_BOUND_VERTEX_BUFFERS];

			/*
			try
			{
				var managedArray = File.ReadAllBytes("jella.monk.fe_ShaderFunction24_first.frag");
				uint[] decoded = new uint[managedArray.Length / 4];
				System.Buffer.BlockCopy(managedArray, 0, decoded, 0, managedArray.Length);
				var kk1 = device.CreateShaderModule(new ShaderModuleCreateInfo
				{
					Code = decoded
				});

				Console.WriteLine($"Got {kk1}");
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
			try {
				var managedArray = File.ReadAllBytes("jella.monk.fe_ShaderFunction4_first.vert");
				uint[] decoded = new uint[managedArray.Length / 4];
				System.Buffer.BlockCopy(managedArray, 0, decoded, 0, managedArray.Length);
				var kk1 = device.CreateShaderModule(new ShaderModuleCreateInfo
				{
					Code = decoded
				});

				Console.WriteLine($"Got {kk1}");
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
			*/
			/*
			// Some users might want pixely upscaling...
			backbufferScaleMode = Environment.GetEnvironmentVariable(
				"FNA_GRAPHICS_BACKBUFFER_SCALE_NEAREST"
			) == "1" ? MTLSamplerMinMagFilter.Nearest : MTLSamplerMinMagFilter.Linear;

			// Set device properties
			isMac = SDL.SDL_GetPlatform().Equals("Mac OS X");
			SupportsS3tc = SupportsDxt1 = isMac;
			MaxMultiSampleCount = mtlSupportsSampleCount(device, 8) ? 8 : 4;
			supportsOcclusionQueries = isMac || HasModernAppleGPU();

			// Determine supported depth formats
			D16Format = MTLPixelFormat.Depth32Float;
			D24Format = MTLPixelFormat.Depth32Float;
			D24S8Format = MTLPixelFormat.Depth32Float_Stencil8;

			if (isMac)
			{
				bool supportsD24S8 = mtlSupportsDepth24Stencil8(device);
				if (supportsD24S8)
				{
					D24S8Format = MTLPixelFormat.Depth24Unorm_Stencil8;

					// Gross, but at least it's a unorm format! -caleb
					D24Format = MTLPixelFormat.Depth24Unorm_Stencil8;
					D16Format = MTLPixelFormat.Depth24Unorm_Stencil8;
				}

				// Depth16Unorm requires macOS 10.12+
				if (OperatingSystemAtLeast(10, 12, 0))
				{
					D16Format = MTLPixelFormat.Depth16Unorm;
					if (!supportsD24S8)
					{
						// Less precision, but oh well!
						D24Format = MTLPixelFormat.Depth16Unorm;
					}
				}
			}
			else
			{
				// Depth16Unorm requires iOS 13+
				if (OperatingSystemAtLeast(13, 0, 0))
				{
					D16Format = MTLPixelFormat.Depth16Unorm;
					D24Format = MTLPixelFormat.Depth16Unorm;
				}
			}

			// Add fallbacks for missing texture formats on macOS
			if (isMac)
			{
				XNAToMTL.TextureFormat[(int) SurfaceFormat.Bgr565]
					= MTLPixelFormat.BGRA8Unorm;
				XNAToMTL.TextureFormat[(int) SurfaceFormat.Bgra5551]
					= MTLPixelFormat.BGRA8Unorm;
				XNAToMTL.TextureFormat[(int) SurfaceFormat.Bgra4444]
					= MTLPixelFormat.BGRA8Unorm;
			}

			// Initialize texture and sampler collections
			Textures = new MetalTexture[MaxTextureSlots];
			Samplers = new IntPtr[MaxTextureSlots];
			for (int i = 0; i < MaxTextureSlots; i += 1)
			{
				Textures[i] = MetalTexture.NullTexture;
				Samplers[i] = IntPtr.Zero;
			}
			textureNeedsUpdate = new bool[MaxTextureSlots];
			samplerNeedsUpdate = new bool[MaxTextureSlots];

			// Initialize attachment arrays
			int numAttachments = GraphicsDevice.MAX_RENDERTARGET_BINDINGS;
			currentAttachments = new IntPtr[numAttachments];
			currentColorFormats = new MTLPixelFormat[numAttachments];
			currentMSAttachments = new IntPtr[numAttachments];
			currentAttachmentSlices = new CubeMapFace[numAttachments];

			// Initialize vertex buffer cache
			ldVertexBuffers = new IntPtr[MAX_BOUND_VERTEX_BUFFERS];
			ldVertexBufferOffsets = new int[MAX_BOUND_VERTEX_BUFFERS];

			// Create a default depth stencil state
			IntPtr defDS = mtlNewDepthStencilDescriptor();
			defaultDepthStencilState = mtlNewDepthStencilStateWithDescriptor(device, defDS);
			objc_release(defDS);

			// Create and setup the faux-backbuffer
			InitializeFauxBackbuffer(presentationParameters);
			*/
		}

		#endregion

		private void setupSwapchain(SurfaceCapabilitiesKhr surfaceCaps, uint width, uint height)
		{

			SwapchainKhr oldSwapchain = null;

			CompositeAlphaFlagsKhr surfaceComposite;
			if (surfaceCaps.SupportedCompositeAlpha.HasFlag(CompositeAlphaFlagsKhr.Opaque))
			{
				surfaceComposite = CompositeAlphaFlagsKhr.Opaque;
			}
			else if (surfaceCaps.SupportedCompositeAlpha.HasFlag(CompositeAlphaFlagsKhr.PreMultiplied))
			{
				surfaceComposite = CompositeAlphaFlagsKhr.PreMultiplied;
			}
			else if (surfaceCaps.SupportedCompositeAlpha.HasFlag(CompositeAlphaFlagsKhr.PostMultiplied))
			{
				surfaceComposite = CompositeAlphaFlagsKhr.PostMultiplied;
			}
			else
			{
				surfaceComposite = CompositeAlphaFlagsKhr.Inherit;
			}

			uint graphicsQueueIndex = (uint) findQueueFamilies(physicalDevice).GraphicsFamily.Value;

			//var format = Format.R8G8B8A8Unorm;
			var format = Format.B8G8R8A8Unorm;

			_swapchainKhr = device.CreateSwapchainKHR(new SwapchainCreateInfoKhr
			{
				Surface = surface,
				MinImageCount = Math.Max(2, surfaceCaps.MinImageCount),
				ImageFormat = format, // todo: get this correctly
				ImageColorSpace = ColorSpaceKhr.SrgbNonlinear, // set this
				ImageExtent = new Extent2D
				{
					Width = width,
					Height = height,
				},
				ImageArrayLayers = 1,
				ImageUsage = ImageUsageFlags.ColorAttachment,
				QueueFamilyIndexCount = 1,
				QueueFamilyIndices = new uint[1] {graphicsQueueIndex},
				PreTransform = SurfaceTransformFlagsKhr.Identity,
				CompositeAlpha = surfaceComposite,
				PresentMode = PresentModeKhr.Fifo,
				OldSwapchain = oldSwapchain,
			});

			renderPass = device.CreateRenderPass(new RenderPassCreateInfo
			{
				Attachments = new[]
				{
					new AttachmentDescription
					{
						Format = format,
						Samples = SampleCountFlags.Count1,
						LoadOp = AttachmentLoadOp.Clear,
						StoreOp = AttachmentStoreOp.Store,
						StencilLoadOp = AttachmentLoadOp.DontCare,
						StencilStoreOp = AttachmentStoreOp.DontCare,
						InitialLayout = ImageLayout.ColorAttachmentOptimal,
						FinalLayout = ImageLayout.ColorAttachmentOptimal,
					}
				},
				Subpasses = new[]
				{
					new SubpassDescription
					{
						ColorAttachments = new[]
						{
							new AttachmentReference
							{
								Attachment = 0,
								Layout = ImageLayout.ColorAttachmentOptimal,
							}
						}
					}
				},
			});

			// todo: check h, w against surfaceCaps

			images = device.GetSwapchainImagesKHR(_swapchainKhr);
			imageViews = images.Select(image => device.CreateImageView(new ImageViewCreateInfo
			{
				Image = image, ViewType = ImageViewType.View2D, Format = format,
				SubresourceRange = new ImageSubresourceRange
					{AspectMask = ImageAspectFlags.Color, LevelCount = 1, LayerCount = 1}
			})).ToArray();
			_framebuffers = imageViews.Select(imageView => device.CreateFramebuffer(new FramebufferCreateInfo
			{
				RenderPass = renderPass, Attachments = new[] {imageView}, Width = width, Height = height,
				Layers = 1
			})).ToArray();

			acquireSemaphore = device.CreateSemaphore(new SemaphoreCreateInfo { });
			releaseSemaphore = device.CreateSemaphore(new SemaphoreCreateInfo { });

			Console.WriteLine("swp" + _swapchainKhr);

		}

		public void Dispose()
		{
			// todo: start disposing
			throw new NotImplementedException();
		}

		public void ResetBackbuffer(PresentationParameters presentationParameters, GraphicsAdapter adapter)
		{
			//throw new NotImplementedException();
		}

		ImageMemoryBarrier imageMemoryBarrierz(Image image, AccessFlags srcAccessMask, AccessFlags dstAccessMask, ImageLayout oldLayout, ImageLayout newLayout)
		{
			return new ImageMemoryBarrier
			{
					SrcAccessMask = srcAccessMask,
					DstAccessMask = dstAccessMask,
					OldLayout = oldLayout,
					NewLayout = newLayout,
					SrcQueueFamilyIndex = uint.MaxValue,
					DstQueueFamilyIndex = uint.MaxValue,
					Image = image,
					SubresourceRange = new ImageSubresourceRange
					{
						AspectMask = ImageAspectFlags.Color,
						LevelCount = uint.MaxValue,
						LayerCount = uint.MaxValue,
					}
			}
			;
		}

		private bool frameInProgress = false;

		public void BeginFrame()
		{
			if (frameInProgress) return;

			// The cycle begins anew!
			frameInProgress = true;

			imageIndex = device.AcquireNextImageKHR(_swapchainKhr, ulong.MaxValue, acquireSemaphore);

			// Console.WriteLine($"kromla index {imageIndex}");

			device.ResetCommandPool(commandPool, 0);
			_commandBuffer.Begin(new CommandBufferBeginInfo
			{
				Flags = CommandBufferUsageFlags.OneTimeSubmit,
			});

			currentImage = images[imageIndex];
			var renderBeginBarrier = imageMemoryBarrierz(
				currentImage,
				0,
				AccessFlags.ColorAttachmentWrite,
				ImageLayout.Undefined,
				ImageLayout.ColorAttachmentOptimal
				);
			//_commandBuffer.CmdPipelineBarrier(PipelineStageFlags.ColorAttachmentOutput,
	//			PipelineStageFlags.ColorAttachmentOutput, DependencyFlags.ByRegion, new MemoryBarrier[0], new BufferMemoryBarrier[0], new[] {renderBeginBarrier});
			_commandBuffer.CmdPipelineBarrier(PipelineStageFlags.ColorAttachmentOutput,
				PipelineStageFlags.ColorAttachmentOutput, DependencyFlags.ByRegion, null, null, renderBeginBarrier);

			// flipping the viewport coords is core in vulkan 1.1
			_commandBuffer.CmdSetViewport(0, new Vulkan.Viewport
			{
				Height = -windowHeight,
				Width = windowWidth,
				X = 0.0f,
				Y = windowHeight,
				MaxDepth = 1,
				MinDepth = 0,
			});

			_commandBuffer.CmdSetScissor(0, new Rect2D
			{
				Offset = new Offset2D(),
				Extent = new Extent2D
				{
					Height = windowHeight,
					Width = windowWidth,
				}
			});
		}

		private ClearValue[] getCurrentClearValues()
		{
			return new[] {new ClearValue { Color = new ClearColorValue
			{
				Float32 = new[] {clearColor.X, clearColor.Y, clearColor.Z, clearColor.W},
			} }};
		}

		public void SwapBuffers(Rectangle? sourceRectangle, Rectangle? destinationRectangle,
			IntPtr overrideWindowHandle)
		{
			/* Just in case Present() is called
			 * before any rendering happens...
			 */
			BeginFrame();

			// Bind the backbuffer and finalize rendering
			// SetRenderTargets(null, null, DepthFormat.None); // todo
			EndPass();

			// todo: what to do if sourceRectangle or destinationRectangle are not null. Also what to do with windowhandle

			//_commandBuffer.CmdEndRenderPass();

			var renderEndBarrier = imageMemoryBarrierz(currentImage, AccessFlags.ColorAttachmentWrite, 0,
				ImageLayout.ColorAttachmentOptimal, ImageLayout.PresentSrcKhr);
			_commandBuffer.CmdPipelineBarrier(PipelineStageFlags.ColorAttachmentOutput, PipelineStageFlags.TopOfPipe,
				DependencyFlags.ByRegion, null, null, renderEndBarrier);

			_commandBuffer.End();

			presentQueue.Submit(new SubmitInfo
			{
				WaitSemaphores = new[] {acquireSemaphore},
				WaitDstStageMask = new[] {PipelineStageFlags.ColorAttachmentOutput},
				CommandBuffers = new[] {_commandBuffer},
				SignalSemaphores = new[] {releaseSemaphore}
			});

			try
			{
				presentQueue.PresentKHR(new PresentInfoKhr
				{
					WaitSemaphores = new[] {releaseSemaphore},
					Swapchains = new[] {_swapchainKhr},
					ImageIndices = new[] {imageIndex}
				});

			}
			catch (ResultException e)
			{
				if (e.Result == Result.ErrorOutOfDateKhr)
				{
					// re-create swapchain
					/*
					 * VK_ERROR_OUT_OF_DATE_KHR:
					 * The swap chain has become incompatible with the surface and can no longer be used for rendering. Usually happens after a window resize.
					 */
				}
				else
				{

				}
				throw new Exception("Failed to present. Needs work.", e);
			}
			graphicsQueue.WaitIdle();

			// Reset buffers
			for (int i = 0; i < Buffers.Count; i += 1)
			{
				Buffers[i].Reset();
			}
			MojoShader.MOJOSHADER_vkEndFrame();

			// We're done here.
			frameInProgress = false;
		}

		public void SetStringMarker(string text)
		{
			throw new NotImplementedException();
		}

		void getMaybeCachedShaders(out ShaderModule fshader, out String fname, out ShaderModule vshader, out String vname)
		{
			var fieldInfo = typeof(ShaderModule).GetField("m", BindingFlags.NonPublic | BindingFlags.Instance);
			Debug.Assert(fieldInfo != null);
			unsafe
			{
				MojoShader.MOJOSHADER_vkLinkProgram(shaderState.vertexShader, shaderState.fragmentShader);

				var fParseData = ((MojoShader.MOJOSHADER_parseData*)
					((MojoShader.MOJOSHADER_vkShader*) shaderState.fragmentShader)->parseData);

				fname = Marshal.PtrToStringAnsi(fParseData->mainfn);

				fshader = (ShaderModule) typeof(ShaderModule).GetConstructor(
					BindingFlags.NonPublic | BindingFlags.Instance,
					null, Type.EmptyTypes, null).Invoke(null);

				var vert = ((MojoShader.MOJOSHADER_vkShader*) shaderState.vertexShader);
				var sm = vert->shaderModule;
				UInt64 sss = (UInt64) sm;
				var frag = ((MojoShader.MOJOSHADER_vkShader*) shaderState.fragmentShader);
				var fshrptr = frag->shaderModule;
				object f = fshader;
				fieldInfo.SetValue(f, fshrptr);

				var vParseData = (MojoShader.MOJOSHADER_parseData*)
					((MojoShader.MOJOSHADER_vkShader*) shaderState.vertexShader)->parseData;
				vname = Marshal.PtrToStringAnsi(vParseData->mainfn);

				vshader = (ShaderModule) typeof(ShaderModule).GetConstructor(
					BindingFlags.NonPublic | BindingFlags.Instance,
					null, Type.EmptyTypes, null).Invoke(null);

				var vshrptr = vert->shaderModule;
				object v = vshader;
				fieldInfo.SetValue(v, vshrptr);

				//var fola = MyClass.makeT<ShaderModule>(vert->shaderModule);
				//Console.WriteLine($"{((IMarshalling)fola).Handle}");
				//var s = 2;
			}
		}

		private DescriptorSet[] descriptorSets;

		private DescriptorSetLayout _setLayout;

		private List<VulkanBuffer> Buffers = new List<VulkanBuffer>();

		DescriptorSet [] CreateDescriptorSets ()
		{
			var ubo = new DescriptorPoolSize {
				Type = DescriptorType.UniformBuffer,
				DescriptorCount = 1
			};
			var sampler = new DescriptorPoolSize {
				Type = DescriptorType.CombinedImageSampler,
				DescriptorCount = 1
			};
			var descriptorPoolCreateInfo = new DescriptorPoolCreateInfo {
				PoolSizes = new DescriptorPoolSize [] { ubo, sampler },
				MaxSets = 1
			};
			// todo: delete the fuck out of this
			var descriptorPool = device.CreateDescriptorPool (descriptorPoolCreateInfo);

			var descriptorSetAllocateInfo = new DescriptorSetAllocateInfo {
				SetLayouts = new DescriptorSetLayout [] { _setLayout },
				DescriptorPool = descriptorPool
			};

			return device.AllocateDescriptorSets (descriptorSetAllocateInfo);
		}

		private Buffer uniformBufferV;
		private uint uniformBufferOffsetV;
		private DeviceSize uniformBufferSizeV;

		void UpdateDescriptorSets()
		{
			var uniformBufferInfo = new DescriptorBufferInfo
			{
				Buffer = uniformBufferV,
				Offset = uniformBufferOffsetV,
				Range = uniformBufferSizeV - uniformBufferOffsetV, // todo: how much data are we writing?
			};
			var writeDescriptorSet = new WriteDescriptorSet
			{
				DstSet = descriptorSets[0],
				DstBinding = 0, // yass
				DstArrayElement = 0,
				DescriptorType = DescriptorType.UniformBuffer,
				BufferInfo = new DescriptorBufferInfo[] {uniformBufferInfo}
			};

			List<WriteDescriptorSet> writeDescriptorSets = new List<WriteDescriptorSet>();
			writeDescriptorSets.Add(writeDescriptorSet);

			if (_texture.Sampler != null) {
				var imageInfo = new DescriptorImageInfo
				{
					ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
					ImageView = _texture.ImageView,
					Sampler = _texture.Sampler,
				};

				var imageWriteDescriptorSet = new WriteDescriptorSet
				{
					DstSet = descriptorSets[0],
					DstBinding = 4, // YASS!!!
					DstArrayElement = 0,
					DescriptorType = DescriptorType.CombinedImageSampler,
					ImageInfo = new [] {imageInfo},
				};
				writeDescriptorSets.Add(imageWriteDescriptorSet);
			}

			device.UpdateDescriptorSets (writeDescriptorSets.ToArray(), null);
		}

		private uint getMemoryTypeIndex(uint memoryTypeBits, MemoryPropertyFlags properties)
		{

			for (uint i = 0; i < _deviceMemoryProperties.MemoryTypeCount; i++)
			{
				if (Convert.ToBoolean(memoryTypeBits & 1))
				{
					if (_deviceMemoryProperties.MemoryTypes[i].PropertyFlags == properties)
					{
						return i;
					}
				}
			}
			throw new Exception("Could not find a suitable memory type!");
		}

		public void SetPresentationInterval(PresentInterval presentInterval)
		{
			// todo: handle this with vulkan
			Console.WriteLine($"Dropping present interval {presentInterval}");
			//throw new NotImplementedException();
		}

		private Rectangle scissorRectangle = new Rectangle(
			0,
			0,
			0,
			0
		);

		private Rectangle viewport = new Rectangle(
			0,
			0,
			0,
			0
		);

		private float depthRangeMin = 0.0f;
		private float depthRangeMax = 1.0f;
		private RenderPass renderPass;

		public void SetViewport(Viewport vp)
		{
			if (vp.Bounds != viewport ||
			    vp.MinDepth != depthRangeMin ||
			    vp.MaxDepth != depthRangeMax)
			{
				// todo: impl this?
				// they're not different so set them? what does this do? when can you call it?
				// when can we set it in vulkan?
			}
		}

		public void SetScissorRect(Rectangle scissorRect)
		{
			if (scissorRect != scissorRectangle)
			{
				// todo: also when are we allowed to set this?
				scissorRectangle = scissorRect;
			}
		}

		private BlendState blendState;

		public void SetBlendState(BlendState blendState)
		{
			this.blendState = blendState;
			BlendFactor = blendState.BlendFactor; // Dynamic state!
		}

		private DepthStencilState depthStencilState;

		public void SetDepthStencilState(DepthStencilState depthStencilState)
		{
			this.depthStencilState = depthStencilState;
			ReferenceStencil = depthStencilState.ReferenceStencil; // Dynamic state!
		}

		private bool scissorTestEnable = false;
		private CullMode cullFrontFace = CullMode.None;
		private FillMode fillMode = FillMode.Solid;
		private float depthBias = 0.0f;
		private float slopeScaleDepthBias = 0.0f;
		private bool multiSampleEnable = true;

		private void SetEncoderDepthBias() {}
		private void SetEncoderScissorRect(){}
		private void SetEncoderCullMode(){}
		private void SetEncoderFillMode(){}

		/*
		private MTLPixelFormat GetDepthFormat(DepthFormat format)
		{
			switch (format)
			{
				case DepthFormat.Depth16:		return D16Format;
				case DepthFormat.Depth24:		return D24Format;
				case DepthFormat.Depth24Stencil8:	return D24S8Format;
				default:				return MTLPixelFormat.Invalid;
			}
		}*/

		public void ApplyRasterizerState(RasterizerState rasterizerState)
		{
			if (rasterizerState.ScissorTestEnable != scissorTestEnable)
			{
				scissorTestEnable = rasterizerState.ScissorTestEnable;
				SetEncoderScissorRect(); // Dynamic state!
			}

			if (rasterizerState.CullMode != cullFrontFace)
			{
				cullFrontFace = rasterizerState.CullMode;
				SetEncoderCullMode(); // Dynamic state!
			}

			if (rasterizerState.FillMode != fillMode)
			{
				fillMode = rasterizerState.FillMode;
				SetEncoderFillMode(); // Dynamic state!
			}

			/*
			float realDepthBias = rasterizerState.DepthBias;
			realDepthBias *= XNAToVK.DepthBiasScale(
				GetDepthFormat(currentDepthFormat)
			);
			if (	realDepthBias != depthBias ||
			        rasterizerState.SlopeScaleDepthBias != slopeScaleDepthBias	)
			{
				depthBias = realDepthBias;
				slopeScaleDepthBias = rasterizerState.SlopeScaleDepthBias;
				SetEncoderDepthBias(); // Dynamic state!
			}
			*/

			if (rasterizerState.MultiSampleAntiAlias != multiSampleEnable)
			{
				multiSampleEnable = rasterizerState.MultiSampleAntiAlias;
				// FIXME: Metal does not support toggling MSAA. Workarounds...?
			}
		}

		private VulkanTexture[] Textures;
		private IntPtr[] Samplers;
		private bool[] textureNeedsUpdate;
		private bool[] samplerNeedsUpdate;

		public void VerifySampler(int index, Texture texture, SamplerState sampler)
		{
			if (texture == null)
			{
				if (Textures[index] != VulkanTexture.NullTexture)
				{
					Textures[index] = VulkanTexture.NullTexture;
					textureNeedsUpdate[index] = true;
				}
				if (Samplers[index] == IntPtr.Zero)
				{
					/* Some shaders require non-null samplers
					 * even if they aren't actually used.
					 * -caleb
					 */
					Samplers[index] = FetchSamplerState(sampler, false);
					samplerNeedsUpdate[index] = true;
				}
				return;
			}

			VulkanTexture tex = texture.texture as VulkanTexture;
			if (	tex == Textures[index] &&
			        sampler.AddressU == tex.WrapS &&
			        sampler.AddressV == tex.WrapT &&
			        sampler.AddressW == tex.WrapR &&
			        sampler.Filter == tex.Filter &&
			        sampler.MaxAnisotropy == tex.Anisotropy &&
			        sampler.MaxMipLevel == tex.MaxMipmapLevel &&
			        sampler.MipMapLevelOfDetailBias == tex.LODBias	)
			{
				// Nothing's changing, forget it.
				return;
			}

			// Bind the correct texture
			if (tex != Textures[index])
			{
				Textures[index] = tex;
				textureNeedsUpdate[index] = true;
			}

			// Update the texture sampler info
			tex.WrapS = sampler.AddressU;
			tex.WrapT = sampler.AddressV;
			tex.WrapR = sampler.AddressW;
			tex.Filter = sampler.Filter;
			tex.Anisotropy = sampler.MaxAnisotropy;
			tex.MaxMipmapLevel = sampler.MaxMipLevel;
			tex.LODBias = sampler.MipMapLevelOfDetailBias;

			// Update the sampler state, if needed
			IntPtr ss = FetchSamplerState(sampler, tex.HasMipmaps);
			if (ss != Samplers[index])
			{
				Samplers[index] = ss;
				samplerNeedsUpdate[index] = true;
			}
		}

		#region Clear Cache Variables

		private Vector4 clearColor = new Vector4(0, 0, 0, 0);
		private float clearDepth = 1.0f;
		private int clearStencil = 0;

		private bool shouldClearColor = false;
		private bool shouldClearDepth = false;
		private bool shouldClearStencil = false;

		#endregion

		public void Clear(ClearOptions options, Vector4 color, float depth, int stencil)
		{
			bool clearTarget = (options & ClearOptions.Target) == ClearOptions.Target;
			bool clearDepth = (options & ClearOptions.DepthBuffer) == ClearOptions.DepthBuffer;
			bool clearStencil = (options & ClearOptions.Stencil) == ClearOptions.Stencil;

			if (clearTarget)
			{
				clearColor = color;
				shouldClearColor = true;
			}
			if (clearDepth)
			{
				this.clearDepth = depth;
				shouldClearDepth = true;
			}
			if (clearStencil)
			{
				this.clearStencil = stencil;
				shouldClearStencil = true;
			}

			needNewRenderPass |= clearTarget | clearDepth | clearStencil;
		}

		#region Render Target Cache Variables

		private readonly IntPtr[] currentAttachments;
		private readonly Format[] currentColorFormats;
		private readonly IntPtr[] currentMSAttachments;
		private readonly CubeMapFace[] currentAttachmentSlices;
		private IntPtr currentDepthStencilBuffer;
		private DepthFormat currentDepthFormat;
		private int currentSampleCount;

		#endregion

		#region SetRenderTargetsMethods

		public void SetRenderTargets(RenderTargetBinding[] renderTargets, IGLRenderbuffer renderbuffer,
			DepthFormat depthFormat)
		{
			// Perform any pending clears before switching render targets
			if (shouldClearColor || shouldClearDepth || shouldClearStencil)
			{
				UpdateRenderPass();
			}

			// Force an update to the render pass
			needNewRenderPass = true;

			// Bind the correct framebuffer
			ResetAttachments();
			if (renderTargets == null)
			{
				BindBackbuffer();
				return;
			}

			// Update color buffers
			int i;
			for (i = 0; i < renderTargets.Length; i += 1)
			{
				IRenderTarget rt = renderTargets[i].RenderTarget as IRenderTarget;
				currentAttachmentSlices[i] = renderTargets[i].CubeMapFace;
				if (rt.ColorBuffer != null)
				{
					VulkanRenderbuffer rb = rt.ColorBuffer as VulkanRenderbuffer;
					currentAttachments[i] = rb.Handle;
					currentColorFormats[i] = rb.PixelFormat;
					currentSampleCount = rb.MultiSampleCount;
					currentMSAttachments[i] = rb.MultiSampleHandle;
				}
				else
				{
					VulkanTexture tex = renderTargets[i].RenderTarget.texture as VulkanTexture;
					currentAttachments[i] = (IntPtr)tex.Handle;//todo
					currentColorFormats[i] = XNAToVK.TextureFormat[(int) tex.Format];
					currentSampleCount = 0;
				}
			}

			// Update depth stencil buffer
			IntPtr handle = IntPtr.Zero;
			if (renderbuffer != null)
			{
				handle = (renderbuffer as VulkanRenderbuffer).Handle;
			}
			currentDepthStencilBuffer = handle;
			currentDepthFormat = (
				(currentDepthStencilBuffer == IntPtr.Zero) ?
					DepthFormat.None :
					depthFormat
			);
		}

		private void ResetAttachments()
		{
			for (int i = 0; i < currentAttachments.Length; i += 1)
			{
				currentAttachments[i] = IntPtr.Zero;
				currentColorFormats[i] = Format.Undefined;
				currentMSAttachments[i] = IntPtr.Zero;
				currentAttachmentSlices[i] = (CubeMapFace) 0;
			}
			currentDepthStencilBuffer = IntPtr.Zero;
			currentDepthFormat = DepthFormat.None;
			currentSampleCount = 0;
		}

		private void BindBackbuffer()
		{
			VulkanBackbuffer bb = Backbuffer as VulkanBackbuffer;
			currentAttachments[0] = bb.ColorBuffer;
			currentColorFormats[0] = bb.PixelFormat;
			currentDepthStencilBuffer = bb.DepthStencilBuffer;
			currentDepthFormat = bb.DepthFormat;
			currentSampleCount = bb.MultiSampleCount;
			currentMSAttachments[0] = bb.MultiSampleColorBuffer;
			currentAttachmentSlices[0] = (CubeMapFace) 0;
		}

		#endregion

		public void ResolveTarget(RenderTargetBinding target)
		{
			// The target is resolved at the end of each render pass.

			// If the target has mipmaps, regenerate them now
			if (target.RenderTarget.LevelCount > 1)
			{
				EndPass();

				throw new Exception("Can't generate mips yet.");
				/*
				IntPtr blit = mtlMakeBlitCommandEncoder(commandBuffer);
				mtlGenerateMipmapsForTexture(
					blit,
					(target.RenderTarget.texture as MetalTexture).Handle
				);
				mtlEndEncoding(blit);
				*/

				needNewRenderPass = true;
			}
		}

		public void ReadBackbuffer(IntPtr data, int dataLen, int startIndex, int elementCount, int elementSizeInBytes,
			int subX,
			int subY, int subW, int subH)
		{
			throw new NotImplementedException();
		}

		#region DeleteBuffer Methods

		private void DeleteBuffer(IGLBuffer buffer)
		{
			Buffers.Remove(buffer as VulkanBuffer);
			(buffer as VulkanBuffer).Dispose();
		}

		#endregion

		public IGLTexture CreateTexture2D(SurfaceFormat format, int width, int height, int levelCount,
			bool isRenderTarget)
		{
			ImageUsageFlags usageFlags;
			if (!isRenderTarget)
			{
				usageFlags = ImageUsageFlags.TransferDst | ImageUsageFlags.Sampled;

			}
			else
			{
				Console.WriteLine("USing render targets\n");
				usageFlags = ImageUsageFlags.ColorAttachment | ImageUsageFlags.Sampled;
			}

			if (format==SurfaceFormat.ColorBgraEXT)
			{
				int x = 2;
			}
			//usageFlags = ImageUsageFlags.TransferDst | ImageUsageFlags.Sampled;

			var textureImage = device.CreateImage(new ImageCreateInfo
			{
				ImageType = ImageType.Image2D,
				Extent = new Extent3D {
					Depth = 1,
					Height = (uint)height,
					Width = (uint)width,
				},
				MipLevels = (uint)levelCount,
				ArrayLayers = 1,
				Format = XNAToVK.TextureFormat[(uint)format],
				Tiling = ImageTiling.Optimal, // todo: linear or optimal?
				InitialLayout = ImageLayout.Undefined,
				Usage = usageFlags, // todo: use isRenderTarget here?
				Samples = SampleCountFlags.Count1,
				SharingMode = SharingMode.Exclusive,
			});

			return new VulkanTexture(
				device,
				(uint) width,
				(uint) height,
				format,
				levelCount,
				isRenderTarget
				)
			{
				Image = textureImage,
			};
		}

		public IGLTexture CreateTexture3D(SurfaceFormat format, int width, int height, int depth, int levelCount)
		{
			var textureImage = device.CreateImage(new ImageCreateInfo
			{
				ImageType = ImageType.Image3D,
				Extent = new Extent3D {
					Depth = (uint)depth,
					Height = (uint)height,
					Width = (uint)width,
				},
				MipLevels = (uint)levelCount,
				ArrayLayers = 1,
				Format = XNAToVK.TextureFormat[(uint)format],
				Tiling = ImageTiling.Linear,
				InitialLayout = ImageLayout.Undefined,
				Usage = ImageUsageFlags.TransferDst | ImageUsageFlags.Sampled,
				Samples = SampleCountFlags.Count1,
				SharingMode = SharingMode.Exclusive,
			});

			throw new NotImplementedException();
		}

		public IGLTexture CreateTextureCube(SurfaceFormat format, int size, int levelCount, bool isRenderTarget)
		{
			throw new NotImplementedException();
		}

		#region DeleteTexture Method

		private void DeleteTexture(IGLTexture texture)
		{
			VulkanTexture tex = texture as VulkanTexture;
			/*
			for (int i = 0; i < currentAttachments.Length; i += 1)
			{
				if (tex.Handle == currentAttachments[i])
				{
					currentAttachments[i] = IntPtr.Zero;
				}
			}
			*/
			for (int i = 0; i < Textures.Length; i += 1)
			{
				if (tex.Handle == Textures[i].Handle)
				{
					Textures[i] = VulkanTexture.NullTexture;
					textureNeedsUpdate[i] = true;
				}
			}
			tex.Dispose();
		}

		#endregion

		public void AddDisposeTexture(IGLTexture texture)
		{
			DeleteTexture(texture);
		}

		CommandBuffer beginSingleTimeCommands()
		{
			var commandBuffer = device.AllocateCommandBuffers(new CommandBufferAllocateInfo
			{
				Level = CommandBufferLevel.Primary,
				CommandPool = commandPool,
				CommandBufferCount = 1,
			})[0];

			commandBuffer.Begin(new CommandBufferBeginInfo
			{
				Flags = CommandBufferUsageFlags.OneTimeSubmit,
			});

			return commandBuffer;
		}

		void endSingleTimeCommands(CommandBuffer commandBuffer)
		{
			commandBuffer.End();

			graphicsQueue.Submit(new SubmitInfo
			{
				CommandBuffers = new []{commandBuffer},
			});
			graphicsQueue.WaitIdle();

			device.FreeCommandBuffer(commandPool, commandBuffer);
		}

		public void SetTextureData2D(
			IGLTexture texture,
			SurfaceFormat format,
			int x,
			int y,
			int w,
			int h,
			int level,
			IntPtr data,
			int dataLength)
		{
			var tex = texture as VulkanTexture;
			var width = tex.Width;
			var height = tex.Height;
			DeviceSize imageSize = dataLength; // todo: not hard-code

			if (level > 0)
			{
				int xx = 0;
				return;
			}

			/*
			if (tex.IsPrivate)
			{
				// We need an active command buffer
				BeginFrame();

				// Fetch a CPU-accessible texture
				handle = FetchTransientTexture(tex);
			}
			*/

			// Write the data
			/*
			mtlReplaceRegion(
				handle,
				new MTLRegion(origin, size),
				level,
				0,
				data,
				BytesPerRow(w, format),
				0
			);
			*/

			// Blit the temp texture to the actual texture
			/*
			if (tex.IsPrivate)
			{
				// End the render pass
				EndPass();

				// Blit!
				IntPtr blit = mtlMakeBlitCommandEncoder(commandBuffer);
				mtlBlitTextureToTexture(
					blit,
					handle,
					0,
					level,
					origin,
					size,
					tex.Handle,
					0,
					level,
					origin
				);

				// Submit the blit command to the GPU and wait...
				mtlEndEncoding(blit);
				Stall();

				// We're done with the temp texture
				mtlSetPurgeableState(
					handle,
					MTLPurgeableState.Empty
				);
			}
			*/

			createBuffer(imageSize, BufferUsageFlags.TransferSrc, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out var stagingBuffer, out var stagingBufferMemory);

			Console.WriteLine($"Are they the same? {imageSize} == {dataLength}");

			var dst = device.MapMemory(stagingBufferMemory, 0, imageSize, 0);
			SDL.SDL_memcpy(dst, data, (IntPtr) dataLength);
			device.UnmapMemory(stagingBufferMemory);

			createImage(MemoryPropertyFlags.DeviceLocal, tex.Image, out var textureImageMemory);

			var fformat = XNAToVK.TextureFormat[(int) format];

			transitionImageLayout(tex.Image, fformat, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
			copyBufferToImage(stagingBuffer, tex.Image, width, height);
			transitionImageLayout(tex.Image, fformat, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

			device.DestroyBuffer(stagingBuffer);
			device.FreeMemory(stagingBufferMemory);

			//vulkanTexture.Image = textureImage;
			tex.ImageMemory = textureImageMemory;

			tex.ImageView = device.CreateImageView(new ImageViewCreateInfo
			{
				Image = tex.Image,
				ViewType = ImageViewType.View2D,
				Format = XNAToVK.TextureFormat[(uint) format],
				SubresourceRange = new ImageSubresourceRange
				{
					AspectMask = ImageAspectFlags.Color,
					BaseMipLevel = 0,
					LevelCount = 1,
					BaseArrayLayer = 0,
					LayerCount = 1,
				},
			});

			tex.Sampler = device.CreateSampler(new SamplerCreateInfo
			{
				MagFilter = Filter.Linear, // todo
				MinFilter = Filter.Linear, // todo
				AddressModeU = SamplerAddressMode.Repeat,
				AddressModeV = SamplerAddressMode.Repeat,
				AddressModeW = SamplerAddressMode.Repeat,
				AnisotropyEnable = true,
				MaxAnisotropy = 16,
				BorderColor = BorderColor.IntOpaqueBlack,
				UnnormalizedCoordinates = false,
				CompareEnable = false,
				CompareOp = CompareOp.Always,
				MipmapMode = SamplerMipmapMode.Linear, // todo
				// todo: handle lods
			});

			// Blit the temp texture to the actual texture
			if (tex.IsPrivate)
			{
				//todo: should we end renderpass in vulkan?
				// End the render pass
				EndPass();

				//todo: blit?
				// Blit!
				//_commandBuffer.CmdBlitImage();
				/*
				IntPtr blit = mtlMakeBlitCommandEncoder(commandBuffer);
				mtlBlitTextureToTexture(
					blit,
					handle,
					0,
					level,
					origin,
					size,
					tex.Handle,
					0,
					level,
					origin
				);
				*/

				//todo: stall
				// Submit the blit command to the GPU and wait...
				//mtlEndEncoding(blit);
				//Stall();

				//todo: delete temp texture
				// We're done with the temp texture
				//mtlSetPurgeableState(
			//		handle,
			//		MTLPurgeableState.Empty
			//	);
			}
		}
		void createImage(MemoryPropertyFlags properties, Image image, out DeviceMemory imageMemory) {
			var memRequirements = device.GetImageMemoryRequirements(image);
			imageMemory = device.AllocateMemory(new MemoryAllocateInfo
			{
				AllocationSize = memRequirements.Size,
				MemoryTypeIndex = findMemoryType(memRequirements.MemoryTypeBits, properties),
			});
			device.BindImageMemory(image, imageMemory, 0);
		}

		void createImage(uint width, uint height, Format format, ImageTiling tiling, ImageUsageFlags usage, MemoryPropertyFlags properties, out Image image, out DeviceMemory imageMemory) {

			image = device.CreateImage(new ImageCreateInfo
			{
				ImageType = ImageType.Image2D,
				Extent = new Extent3D
				{
					Width = width,
					Height = height,
					Depth = 1,
				},
				MipLevels = 1, // todo
				ArrayLayers = 1,
				Format = format,
				Tiling = tiling,
				InitialLayout = ImageLayout.Undefined,
				Usage = usage,
				SharingMode = SharingMode.Exclusive,
				Samples = SampleCountFlags.Count1,
			});

			var memRequirements = device.GetImageMemoryRequirements(image);
			imageMemory = device.AllocateMemory(new MemoryAllocateInfo
			{
				AllocationSize = memRequirements.Size,
				MemoryTypeIndex = findMemoryType(memRequirements.MemoryTypeBits, properties),
			});
			device.BindImageMemory(image, imageMemory, 0);
		}

		public void SetTextureData3D(IGLTexture texture, SurfaceFormat format, int level, int left, int top, int right,
			int bottom,
			int front, int back, IntPtr data, int dataLength)
		{
			throw new NotImplementedException();
		}

		public void SetTextureDataCube(IGLTexture texture, SurfaceFormat format, int xOffset, int yOffset, int width,
			int height,
			CubeMapFace cubeMapFace, int level, IntPtr data, int dataLength)
		{
			throw new NotImplementedException();
		}

		public void SetTextureDataYUV(Texture2D[] textures, IntPtr ptr)
		{
			throw new NotImplementedException();
		}

		public void GetTextureData2D(IGLTexture texture, SurfaceFormat format, int width, int height, int level,
			int subX, int subY,
			int subW, int subH, IntPtr data, int startIndex, int elementCount, int elementSizeInBytes)
		{
			throw new NotImplementedException();
		}

		public void GetTextureData3D(IGLTexture texture, SurfaceFormat format, int left, int top, int front, int right,
			int bottom,
			int back, int level, IntPtr data, int startIndex, int elementCount, int elementSizeInBytes)
		{
			throw new NotImplementedException();
		}

		public void GetTextureDataCube(IGLTexture texture, SurfaceFormat format, int size, CubeMapFace cubeMapFace,
			int level,
			int subX, int subY, int subW, int subH, IntPtr data, int startIndex, int elementCount,
			int elementSizeInBytes)
		{
			throw new NotImplementedException();
		}

		public IGLRenderbuffer GenRenderbuffer(int width, int height, SurfaceFormat format, int multiSampleCount,
			IGLTexture texture)
		{
			Format pixelFormat = XNAToVK.TextureFormat[(int) format];
			int sampleCount = GetCompatibleSampleCount(multiSampleCount);

			throw new NotImplementedException();
		}

		public IGLRenderbuffer GenRenderbuffer(int width, int height, DepthFormat format, int multiSampleCount)
		{
			Format pixelFormat = XNAToVK.TextureFormat[(int) format];
			int sampleCount = GetCompatibleSampleCount(multiSampleCount);

			// Generate a depth texture
			var colorImage = device.CreateImage(new ImageCreateInfo
			{
				Format = pixelFormat,
				ImageType = ImageType.Image2D,
				Extent = new Extent3D
				{
					Depth = 1,
					Height = (uint)height,
					Width = (uint)width,
				},
				MipLevels = 1,
				ArrayLayers = 1,
				Samples = sampleCount == 0 ? SampleCountFlags.Count1 : XNAToVK.SampleCountFlags(sampleCount),
				Tiling = ImageTiling.Optimal,
				Usage = ImageUsageFlags.ColorAttachment | ImageUsageFlags.Sampled
			});

			/*
			IntPtr desc = mtlMakeTexture2DDescriptor(
				pixelFormat,
				width,
				height,
				false
			);
			mtlSetStorageMode(
				desc,
				MTLStorageMode.Private
			);
			mtlSetTextureUsage(
				desc,
				MTLTextureUsage.RenderTarget
			);
			*/

			// We're done!
			return new VulkanRenderbuffer(
				IntPtr.Zero,
				pixelFormat,
				sampleCount,
				IntPtr.Zero
			);
		}

		private int GetCompatibleSampleCount(int sampleCount)
		{
			/* If the device does not support the requested
			 * multisample count, halve it until we find a
			 * value that is supported.
			 */
			var physicalDeviceProperties = physicalDevice.GetProperties();
			var counts = physicalDeviceProperties.Limits.FramebufferColorSampleCounts &
			             physicalDeviceProperties.Limits.FramebufferDepthSampleCounts;
			while (sampleCount > 0 && counts.HasFlag(XNAToVK.SampleCountFlags(sampleCount)))
			{
				sampleCount = MathHelper.ClosestMSAAPower(
					sampleCount / 2
				);
			}
			return sampleCount;
		}

		public void AddDisposeRenderbuffer(IGLRenderbuffer renderbuffer)
		{
			throw new NotImplementedException();
		}

		public IGLBuffer GenVertexBuffer(bool dynamic, BufferUsage usage, int vertexCount, int vertexStride)
		{
			//ulong size = (ulong)vertexCount * (ulong)vertexStride;

			//createBuffer(size, BufferUsageFlags.VertexBuffer, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out buffer, out bufferMemory);
			//createBuffer(size, BufferUsageFlags.VertexBuffer | BufferUsageFlags.TransferDst, MemoryPropertyFlags.DeviceLocal, out var buffer, out var bufferMemory);

			IntPtr bufferSize = (IntPtr) (vertexStride * vertexCount);

			VulkanBuffer newbuf = new VulkanBuffer(this, BufferUsageFlags.VertexBuffer, usage, bufferSize);
			Buffers.Add(newbuf);
			return newbuf;
		}

		private void createBuffer(DeviceSize size, BufferUsageFlags usage, MemoryPropertyFlags properties, out Buffer buffer, out DeviceMemory bufferMemory)
		{
			buffer = device.CreateBuffer(new BufferCreateInfo
				{Size = size, Usage = usage, SharingMode = SharingMode.Exclusive});

			var memRequirements = device.GetBufferMemoryRequirements(buffer);

			bufferMemory = device.AllocateMemory(new MemoryAllocateInfo { AllocationSize = memRequirements.Size, MemoryTypeIndex = findMemoryType(memRequirements.MemoryTypeBits, properties)});

			device.BindBufferMemory(buffer, bufferMemory, 0);
		}

		private uint findMemoryType(uint memoryTypeBits, MemoryPropertyFlags properties)
		{
			var memProperties = physicalDevice.GetMemoryProperties();

			for (uint i = 0; i < memProperties.MemoryTypeCount; ++i)
			{
				if (Convert.ToBoolean(memoryTypeBits & 1 << (int) i) &&
				    memProperties.MemoryTypes[i].PropertyFlags.HasFlag(properties))
				{
					return i;
				}

			}
			throw new Exception("Could not find a supported memory type that supported " + memoryTypeBits);
		}

		public void AddDisposeVertexBuffer(IGLBuffer buffer)
		{
			DeleteBuffer(buffer);
		}

		public void SetVertexBufferData(IGLBuffer buffer, int offsetInBytes, IntPtr data, int dataLength, SetDataOptions options)
		{
			(buffer as VulkanBuffer).SetData(
				offsetInBytes,
				data,
				dataLength,
				options
			);
			/*
			var vulkanBuffer = (buffer as VulkanBuffer);
			var vertexBuffer = vulkanBuffer.Buffer;

			DeviceSize bufferSize = (uint)vulkanBuffer.BufferSize;
			createBuffer(bufferSize, BufferUsageFlags.TransferSrc, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out var stagingBuffer, out var stagingBufferMemory);
			var dst = device.MapMemory(stagingBufferMemory, 0, bufferSize, 0);
			SDL.SDL_memcpy(dst, data, (IntPtr)dataLength);
			device.UnmapMemory(stagingBufferMemory);

			//createBuffer(bufferSize, BufferUsageFlags.TransferDst | BufferUsageFlags.VertexBuffer, MemoryPropertyFlags.DeviceLocal, out var vertexBuffer, out var vertexBufferMemory);

			copyBuffer(stagingBuffer, vertexBuffer, bufferSize);

			device.DestroyBuffer(stagingBuffer);
			device.FreeMemory(stagingBufferMemory);

			//var vulkanBuffer = (buffer as VulkanBuffer);
			//vulkanBuffer.Buffer = vertexBuffer;
			//vulkanBuffer.BufferMemory = vertexBufferMemory;
			*/
		}

		public void GetVertexBufferData(IGLBuffer buffer, int offsetInBytes, IntPtr data, int startIndex, int elementCount,
			int elementSizeInBytes, int vertexStride)
		{
			throw new NotImplementedException();
		}

		public IGLBuffer GenIndexBuffer(bool dynamic, BufferUsage usage, int indexCount, IndexElementSize indexElementSize)
		{
			int elementSize = XNAToVK.IndexSize[(int) indexElementSize];

			//DeviceSize size = (indexCount * elementSize);
			//createBuffer(size, BufferUsageFlags.TransferDst | BufferUsageFlags.IndexBuffer, MemoryPropertyFlags.DeviceLocal, out var buffer, out var bufferMemory);

			IntPtr bufferSize = (IntPtr)(indexCount * elementSize);

			VulkanBuffer newbuf = new VulkanBuffer(this, BufferUsageFlags.IndexBuffer, usage, bufferSize);
			Buffers.Add(newbuf);
			return newbuf;
		}

		public void AddDisposeIndexBuffer(IGLBuffer buffer)
		{
			DeleteBuffer(buffer);
		}

		private void copyBuffer(Buffer srcBuffer, Buffer dstBuffer, DeviceSize size)
		{
			var commandBuffer = beginSingleTimeCommands();

			commandBuffer.CmdCopyBuffer(srcBuffer, dstBuffer, new BufferCopy { Size = size});

			endSingleTimeCommands(commandBuffer);
		}

		void transitionImageLayout(Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout) {
			CommandBuffer commandBuffer = beginSingleTimeCommands();

			var barrier = new ImageMemoryBarrier
			{
				OldLayout = oldLayout,
				NewLayout = newLayout,
				SrcQueueFamilyIndex = ~0U,
				DstQueueFamilyIndex = ~0U,
				Image = image,
				SubresourceRange = new ImageSubresourceRange
				{
					AspectMask = ImageAspectFlags.Color,
					BaseMipLevel = 0,
					LevelCount = 1,
					BaseArrayLayer = 0,
					LayerCount = 1,
				},
			};

			PipelineStageFlags sourceStage;
			PipelineStageFlags destinationStage;

			if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
			{
				barrier.SrcAccessMask = 0;
				barrier.DstAccessMask = AccessFlags.TransferWrite;

				sourceStage = PipelineStageFlags.TopOfPipe;
				destinationStage = PipelineStageFlags.Transfer;
			} else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
			{
				barrier.SrcAccessMask = AccessFlags.TransferWrite;
				barrier.DstAccessMask = AccessFlags.ShaderRead;

				sourceStage = PipelineStageFlags.Transfer;
				destinationStage = PipelineStageFlags.FragmentShader;
			}
			else
			{
				throw new Exception("unsupported layout transition!");
			}

			commandBuffer.CmdPipelineBarrier(sourceStage, destinationStage,
				0,
				null,
				null,
				barrier);

			endSingleTimeCommands(commandBuffer);
		}

		void copyBufferToImage(Buffer buffer, Image image, uint width, uint height) {
			CommandBuffer commandBuffer = beginSingleTimeCommands();

			commandBuffer.CmdCopyBufferToImage(buffer, image, ImageLayout.TransferDstOptimal, new BufferImageCopy
			{
				BufferOffset = 0,
				BufferRowLength = 0,
				BufferImageHeight = 0,
				ImageSubresource = new ImageSubresourceLayers
				{
					AspectMask = ImageAspectFlags.Color,
					MipLevel = 0,
					BaseArrayLayer = 0,
					LayerCount = 1,
				},
				ImageOffset = new Offset3D
				{
					X = 0, Y = 0, Z = 0
				},
				ImageExtent = new Extent3D
				{
					Width = width,
					Height = height,
					Depth = 1,
				}
			});

			endSingleTimeCommands(commandBuffer);
		}

		public void SetIndexBufferData(IGLBuffer buffer, int offsetInBytes, IntPtr data, int dataLength, SetDataOptions options)
		{
			(buffer as VulkanBuffer).SetData(
				offsetInBytes,
				data,
				dataLength,
				options
				);
			/*
			VulkanBuffer buf = buffer as VulkanBuffer;

			// todo: is datalength already correct?
			DeviceSize bufferSize = dataLength;

			Buffer stagingBuffer;
			DeviceMemory stagingBufferMemory;
			createBuffer(bufferSize, BufferUsageFlags.TransferSrc, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out stagingBuffer, out stagingBufferMemory);

			// return; // todo: returning early as the next function fails. not sure why.

			var dst = device.MapMemory(stagingBufferMemory, offsetInBytes, bufferSize, 0);
			var len = (IntPtr) dataLength;
			SDL.SDL_memcpy(dst, data, len);
			device.UnmapMemory(stagingBufferMemory);

			copyBuffer(stagingBuffer, buf.Buffer, bufferSize);

			device.DestroyBuffer(stagingBuffer);
			device.FreeMemory(stagingBufferMemory);
			*/
		}

		struct ShaderBundle
		{
			internal ShaderModule ShaderModule { get; set; }
			internal String Name{ get; set; }
		}
		struct UnitOfShader
		{
			internal ShaderBundle vs { get; set; }
			internal ShaderBundle fs { get; set; }
		}

		public void GetIndexBufferData(IGLBuffer buffer, int offsetInBytes, IntPtr data, int startIndex, int elementCount,
			int elementSizeInBytes)
		{
			throw new NotImplementedException();
		}

		struct VkShader
		{
			internal MojoShader.MOJOSHADER_parseData ParseData { get; set; }
			internal ShaderModule Handle { get; set; }
			internal uint Refcount { get; set; }
		}

		public IGLEffect CreateEffect(byte[] effectCode)
		{
			IntPtr effect = IntPtr.Zero;
			//IntPtr vkEffect = IntPtr.Zero;

			effect = MojoShader.MOJOSHADER_parseEffect(
				MojoShader.MOJOSHADER_PROFILE_SPIRV,
				effectCode,
				(uint) effectCode.Length,
				null,
				0,
				null,
				0,
				null,
				null,
				IntPtr.Zero
			);

			//var vola = MojoShader.MOJOSHADER_parse("spriv", "moola", )

#if DEBUG
			unsafe
			{
				MojoShader.MOJOSHADER_effect *effectPtr = (MojoShader.MOJOSHADER_effect*) effect;
				MojoShader.MOJOSHADER_error* err = (MojoShader.MOJOSHADER_error*) effectPtr->errors;
				for (int i = 0; i < effectPtr->error_count; i += 1)
				{
					// From the SDL2# LPToUtf8StringMarshaler
					byte* endPtr = (byte*) err[i].error;
					while (*endPtr != 0)
					{
						endPtr++;
					}
					byte[] bytes = new byte[endPtr - (byte*) err[i].error];
					Marshal.Copy(err[i].error, bytes, 0, bytes.Length);

					FNALoggerEXT.LogError(
						"MOJOSHADER_parseEffect Error: " +
						System.Text.Encoding.UTF8.GetString(bytes)
					);
				}
			}
#endif

			var vkEffect = MojoShader.MOJOSHADER_vkCompileEffect(effect);
			if (vkEffect == IntPtr.Zero)
			{
				throw new InvalidOperationException(
					MojoShader.MOJOSHADER_glGetError()
				);
			}

			var vulkanEffect = new VulkanEffect(effect, vkEffect);

			//var shaderBundles = new List<ShaderBundle>();
			//if (false)
			//{
				unsafe
				{
					MojoShader.MOJOSHADER_effect* effectPtr = (MojoShader.MOJOSHADER_effect*) effect;
					MojoShader.MOJOSHADER_effectObject* objects =
						(MojoShader.MOJOSHADER_effectObject*) effectPtr->objects;

					var kkkkk = (MojoShader.MOJOSHADER_effectTechnique*) effectPtr->current_technique;

					for (int i = 0; i < effectPtr->object_count; i += 1)
					{
						var effectObject = objects[i];
						if (effectObject.type == MojoShader.MOJOSHADER_symbolType.MOJOSHADER_SYMTYPE_PIXELSHADER
						    || effectObject.type == MojoShader.MOJOSHADER_symbolType.MOJOSHADER_SYMTYPE_VERTEXSHADER)
						{
							if (Convert.ToBoolean(effectObject.shader.is_preshader))
							{
								vulkanEffect.NumPreshaders++;
							}
							else
							{
								vulkanEffect.NumShaders++;
							}
						}
					}

					vulkanEffect.ShaderIndices = new uint[vulkanEffect.NumShaders];
					vulkanEffect.PreshaderIndices = new uint[vulkanEffect.NumPreshaders];
					vulkanEffect.Shaders = new VkShader[vulkanEffect.NumShaders];

					int current_shader = 0;
					var current_preshader = 0;

					for (uint i = 0; i < effectPtr->object_count; i += 1)
					{
						var effectObject = objects[i];
						if (effectObject.type == MojoShader.MOJOSHADER_symbolType.MOJOSHADER_SYMTYPE_PIXELSHADER
						    || effectObject.type == MojoShader.MOJOSHADER_symbolType.MOJOSHADER_SYMTYPE_VERTEXSHADER)
						{
							if (Convert.ToBoolean(effectObject.shader.is_preshader))
							{
								vulkanEffect.PreshaderIndices[current_preshader++] = i;
								continue;
							}

							var parseData = ((MojoShader.MOJOSHADER_parseData*) effectObject.shader.shader)[0];

							var shader = profileCompileShader(parseData);
							//vulkanEffect.Shaders[current_shader] = new VkShader();
							vulkanEffect.Shaders[current_shader].ParseData = parseData;
							vulkanEffect.Shaders[current_shader].Handle = shader;
							vulkanEffect.Shaders[current_shader].Refcount = 1;
							vulkanEffect.ShaderIndices[current_shader] = i;
							current_shader++;
						}
					}

				}
			//}

			//vulkanEffect.ShaderBundles = shaderBundles;
			return vulkanEffect;
		}

		private ShaderModule profileCompileShader(MojoShader.MOJOSHADER_parseData parseData)
		{
			var entrypoint = Marshal.PtrToStringAnsi(parseData.mainfn);
			var size = parseData.output_len - 168; // GET FUCKING FUCKED sizeof(SpirvPatchTable)
			var pnt = parseData.output;
			byte[] managedArray = new byte[size];
			Marshal.Copy(pnt, managedArray, 0, size);
			uint[] decoded = new uint[managedArray.Length / 4];
			System.Buffer.BlockCopy(managedArray, 0, decoded, 0, managedArray.Length);
			// yes i copy once to byte[] then once to uint[]
			// todo: it's a fucking hack.
			try
			{
				return device.CreateShaderModule(new ShaderModuleCreateInfo
					{Code = decoded});
			}
			catch (Exception e)
			{
				return null;
			}

			//return shaderModule;
		}

		private void DeleteEffect(IGLEffect effect)
		{
			IntPtr mtlEffectData = (effect as VulkanEffect).VKEffectData;
			if (mtlEffectData == currentEffect)
			{
				MojoShader.MOJOSHADER_vkEffectEndPass(currentEffect);
				MojoShader.MOJOSHADER_vkEffectEnd(
					currentEffect,
					ref shaderState
				);
				currentEffect = IntPtr.Zero;
				currentTechnique = IntPtr.Zero;
				currentPass = 0;
				shaderState = new MojoShader.MOJOSHADER_vkShaderState();
			}
			MojoShader.MOJOSHADER_vkDeleteEffect(mtlEffectData);
			MojoShader.MOJOSHADER_freeEffect(effect.EffectData);
		}

		public IGLEffect CloneEffect(IGLEffect effect)
		{
			throw new NotImplementedException();
		}

		public void AddDisposeEffect(IGLEffect effect)
		{
			DeleteEffect(effect);
		}

		private IntPtr currentEffect = IntPtr.Zero;
		private IntPtr currentTechnique = IntPtr.Zero;
		private uint currentPass = 0;

		private bool effectApplied = false;

		public void ApplyEffect(
			IGLEffect effect,
			IntPtr technique,
			uint pass,
			IntPtr stateChanges
		) {
			/* If a frame isn't already in progress,
			 * wait until one begins to avoid overwriting
			 * the previous frame's uniform buffers.
			 */
			BeginFrame();

			IntPtr vkEffectData = (effect as VulkanEffect).VKEffectData;
			if (vkEffectData == currentEffect)
			{
				if (technique == currentTechnique && pass == currentPass)
				{
					MojoShader.MOJOSHADER_vkEffectCommitChanges(
						currentEffect,
						ref shaderState
					);
					return;
				}
				MojoShader.MOJOSHADER_vkEffectEndPass(currentEffect);
				MojoShader.MOJOSHADER_vkEffectBeginPass(
					currentEffect,
					pass,
					ref shaderState
				);
				currentTechnique = technique;
				currentPass = pass;
				return;
			}
			else if (currentEffect != IntPtr.Zero)
			{
				MojoShader.MOJOSHADER_vkEffectEndPass(currentEffect);
				MojoShader.MOJOSHADER_vkEffectEnd(
					currentEffect,
					ref shaderState
				);
			}
			uint whatever;
			MojoShader.MOJOSHADER_vkEffectBegin(
				vkEffectData,
				out whatever,
				0,
				stateChanges
			);
			MojoShader.MOJOSHADER_vkEffectBeginPass(
				vkEffectData,
				pass,
				ref shaderState
			);
			currentEffect = vkEffectData;
			currentTechnique = technique;
			currentPass = pass;
		}

		public void BeginPassRestore(IGLEffect effect, IntPtr stateChanges)
		{
			throw new NotImplementedException();
		}

		public void EndPassRestore(IGLEffect effect)
		{
			throw new NotImplementedException();
		}

		private IntPtr currentVertexDescriptor;		// MTLVertexDescriptor*

		public void ApplyVertexAttributes(VertexBufferBinding[] bindings, int numBindings, bool bindingsUpdated, int baseVertex)
		{
			// Translate the bindings array into a descriptor
			currentVertexDescriptor = FetchVertexDescriptor(
				bindings,
				numBindings
			);

			// Prepare for rendering
			UpdateRenderPass();
			BindResources();

			// Bind the vertex buffers
			for (int i = 0; i < bindings.Length; i += 1)
			{
				var binding = bindings[i];
				VertexBuffer vertexBuffer = binding.VertexBuffer;
				if (vertexBuffer != null)
				{
					int stride = vertexBuffer.VertexDeclaration.VertexStride;
					int offset = (
						((binding.VertexOffset + baseVertex) * stride) +
						(vertexBuffer.buffer as VulkanBuffer).InternalOffset
					);

					ulong handle = ((INonDispatchableHandleMarshalling)(vertexBuffer.buffer as VulkanBuffer).Buffer).Handle;
					(vertexBuffer.buffer as VulkanBuffer).Bound();
					if (ldVertexBuffers[i] != handle)
					{
						_commandBuffer.CmdBindVertexBuffer(
							0,
							(vertexBuffer.buffer as VulkanBuffer).Buffer,
							offset
						);
						ldVertexBuffers[i] = handle;
						ldVertexBufferOffsets[i] = offset;
					}
					else if (ldVertexBufferOffsets[i] != offset)
					{
						throw new NotImplementedException();
						//_commandBuffer.CmdBindVertexBuffer();
						//mtlSetVertexBufferOffset(
						//	renderCommandEncoder,
						//	offset,
						//	i
						//);
						ldVertexBufferOffsets[i] = offset;
					}
				}
			}
		}

		private IntPtr renderCommandEncoder; // todo: replace this with a boolean 'renderPassActive'

		private void EndPass()
		{
			if (renderCommandEncoder != IntPtr.Zero)
			{
				_commandBuffer.CmdEndRenderPass();
				renderCommandEncoder = IntPtr.Zero;
			}

			/*
			if (renderCommandEncoder != IntPtr.Zero)
			{
				mtlEndEncoding(renderCommandEncoder);
				renderCommandEncoder = IntPtr.Zero;
			}
			*/
		}

		private bool needNewRenderPass;

		private void UpdateRenderPass()
		{
			if (!needNewRenderPass) return;

			/* Normally the frame begins in BeginDraw(),
			 * but some games perform drawing outside
			 * of the Draw method (e.g. initializing
			 * render targets in LoadContent). This call
			 * ensures that we catch any unexpected draws.
			 * -caleb
			 */
			BeginFrame();

			// Wrap up rendering with the old encoder
			EndPass();

			// Make a new render pass
			_commandBuffer.CmdBeginRenderPass(new RenderPassBeginInfo
			{
				RenderPass = renderPass,
				Framebuffer = _framebuffers[imageIndex],
				RenderArea = new Rect2D
				{
					Extent = new Extent2D
					{
						Width = windowWidth,
						Height = windowHeight,
					},
					Offset = new Offset2D(),
				},
				ClearValues = getCurrentClearValues(),
			}, SubpassContents.Inline);
			renderCommandEncoder = (IntPtr)1; // not null

			// Reset the flags
			needNewRenderPass = false;
			shouldClearColor = false;
			shouldClearDepth = false;
			shouldClearStencil = false;
		}

		private VulkanBuffer userVertexBuffer = null;
		private VulkanBuffer userIndexBuffer = null;
		private int userVertexStride = 0;

		// Some vertex declarations may have overlapping attributes :/
		private bool[,] attrUse = new bool[(int) MojoShader.MOJOSHADER_usage.MOJOSHADER_USAGE_TOTAL, 16];

		#region Private Buffer Binding Cache

		private const int MAX_BOUND_VERTEX_BUFFERS = 16;

		private IntPtr ldVertUniformBuffer = IntPtr.Zero;
		private IntPtr ldFragUniformBuffer = IntPtr.Zero;
		private int ldVertUniformOffset = 0;
		private int ldFragUniformOffset = 0;

		private ulong[] ldVertexBuffers;
		private int[] ldVertexBufferOffsets;

		#endregion

		#region Resource Binding Method

		// todo: delete this when refactoring
		VulkanTexture _texture = null;

		private void BindResources()
		{
			getMaybeCachedShaders(out var fshader, out var fname, out var vshader, out var vname);

			var setLayout = device.CreateDescriptorSetLayout(new DescriptorSetLayoutCreateInfo
			{
				Bindings = new[]
				{
					// who are these? why do they both exist?

					/*
					new DescriptorSetLayoutBinding
					{
						DescriptorType = DescriptorType.StorageBuffer,
						StageFlags = ShaderStageFlags.Vertex,
						Binding = 1,
						DescriptorCount = 1,
					},
					*/

						// todo: we can't hard-code this. this is so wrong.
					new DescriptorSetLayoutBinding
					{
						//VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER
						//DescriptorType = DescriptorType.UniformBufferDynamic,
						//DescriptorType = DescriptorType.Blo // VK_DESCRIPTOR_TYPE_INLINE_UNIFORM_BLOCK_EXT
						DescriptorType = DescriptorType.UniformBuffer,
						StageFlags = ShaderStageFlags.Vertex,
						Binding = 0,
						DescriptorCount = 1,
					},
					new DescriptorSetLayoutBinding
					{
						DescriptorType = DescriptorType.CombinedImageSampler,
						StageFlags = ShaderStageFlags.Fragment,
						Binding = 4,
						DescriptorCount = 1,
					}
				},
				//Flags = DescriptorSetLayoutCreateFlags.PushDescriptorKhr
			});
			_setLayout = setLayout;

			var layout = device.CreatePipelineLayout(new PipelineLayoutCreateInfo
			{
				SetLayouts = new[] {setLayout},
				//PushConstantRanges = new PushConstantRange[] { },
			});

			PipelineShaderStageCreateInfo vertexCreateInfo = new PipelineShaderStageCreateInfo
				{
					Stage = ShaderStageFlags.Vertex,
					Module = vshader,
					Name = vname,
				},
				fragmentCreateInfo = new PipelineShaderStageCreateInfo
				{
					Stage = ShaderStageFlags.Fragment,
					Module = fshader,
					Name = fname,
				};

			var stages = new[] {vertexCreateInfo, fragmentCreateInfo};

			var createInfo = new GraphicsPipelineCreateInfo
			{
				Stages = stages,
				VertexInputState = new PipelineVertexInputStateCreateInfo
				{
					/*
					VertexAttributeDescriptions = new VertexInputAttributeDescription[]
					{
						new VertexInputAttributeDescription
						{
							// todo: all of this is hardcoded. why?
							Binding = 0,
							Format = Format.R32G32B32Sfloat, // IMPORTANT NOTE: this is set to 3 floats, not 4.
							Location = 0,
							Offset = 0,
						}
					},*/
					VertexAttributeDescriptions = _descriptions, // todo: how to always make sure this is valid?
					VertexBindingDescriptions = new [] { _bindingDescription },
					/*
					VertexBindingDescriptions = new VertexInputBindingDescription[]
					{
						new VertexInputBindingDescription
						{
							// todo: all of this is hardcoded. why?
							Binding = 0, // todo: assuming there's only ever 1 vertex input binding
							Stride = (uint)userVertexStride,
							InputRate = VertexInputRate.Vertex,
						}
					},*/
				},
				InputAssemblyState = new PipelineInputAssemblyStateCreateInfo
				{
					Topology = PrimitiveTopology.TriangleList // todo, case off of primitive type
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
				DepthStencilState = new PipelineDepthStencilStateCreateInfo { },
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
				Layout = layout,
				RenderPass = renderPass,
			};
			var pipelines = device.CreateGraphicsPipelines(null, new[] {createInfo});

			var trianglePipeline = pipelines[0];

			_commandBuffer.CmdBindPipeline(PipelineBindPoint.Graphics, trianglePipeline);

			/*
			Buffer buffer;
			DeviceSize bufferSizeii;
			unsafe
			{
				var vertexUniformBuffer = (MojoShader.MOJOSHADER_vkBuffer*) shaderState.vertexUniformBuffer;
				var offset = shaderState.vertexUniformOffset;

				if (vertexUniformBuffer != null)
				{
					var size = vertexUniformBuffer->size;
					var bufferPtr = vertexUniformBuffer->buffer;
					buffer = MyClass.makeT<Buffer>(bufferPtr);
					bufferSizeii = size;
				}
				else
				{
					// todo: handle this case
					throw new Exception("Unset vertex uniform buffer");
				}

				var fragmentUniformBuffer = (MojoShader.MOJOSHADER_vkBuffer*) shaderState.fragmentUniformBuffer;
				// todo: handle when fragment shader has data
				if (fragmentUniformBuffer != null)
				{
					// todo: handle this case
					throw new Exception("Unset fragment uniform buffer");
				}
				else
				{
					// todo: handle this case
				}
			}

			uniformBufferV = buffer;
			uniformBufferSizeV = bufferSizeii;
			*/

			// Bind textures and their sampler states



			for (int i = 0; i < Textures.Length; i += 1)
			{
				if (textureNeedsUpdate[i])
				{
					/*
					mtlSetFragmentTexture(
						renderCommandEncoder,
						Textures[i].Handle,
						i
					);
					*/
					_texture = Textures[i];
					int textureBindingOffset = 4;

					textureNeedsUpdate[i] = false;
				}
				if (samplerNeedsUpdate[i])
				{
					/*
					mtlSetFragmentSamplerState(
						renderCommandEncoder,
						Samplers[i],
						i
					);
					*/
					throw new Exception("Unsupoprted sampler");
					samplerNeedsUpdate[i] = false;
				}
			}

			// Bind the uniform buffers
			const int UNIFORM_REG = 16; // In MojoShader output it's always 16

			IntPtr vUniform = shaderState.vertexUniformBuffer;
			int vOff = shaderState.vertexUniformOffset;
			if (vUniform != ldVertUniformBuffer)
			{
				unsafe
				{
					var vertexUniformBuffer = (MojoShader.MOJOSHADER_vkBuffer*) vUniform;
					var size = vertexUniformBuffer->size;
					var bufferPtr = vertexUniformBuffer->buffer;
					uniformBufferV = MyClass.makeT<Buffer>(bufferPtr);
					uniformBufferSizeV = size;
					uniformBufferOffsetV = (uint)vOff;
				}

				ldVertUniformBuffer = vUniform;
				ldVertUniformOffset = vOff;

			}
			else if (vOff != ldVertUniformOffset)
			{
				unsafe
				{
					var vertexUniformBuffer = (MojoShader.MOJOSHADER_vkBuffer*) vUniform;
					var size = vertexUniformBuffer->size;
					var bufferPtr = vertexUniformBuffer->buffer;
					uniformBufferV = MyClass.makeT<Buffer>(bufferPtr);
					uniformBufferSizeV = size;
					uniformBufferOffsetV = (uint)vOff;
				}

				ldVertUniformOffset = vOff;
			}

			IntPtr fUniform = shaderState.fragmentUniformBuffer;
			int fOff = shaderState.fragmentUniformOffset;
			if (fUniform != ldFragUniformBuffer)
			{
				throw new NotImplementedException("Fragment Uniform Buffer support incomplete");
				/*
				mtlSetFragmentBuffer(
					renderCommandEncoder,
					fUniform,
					fOff,
					UNIFORM_REG
				);
				*/
				ldFragUniformBuffer = fUniform;
				ldFragUniformOffset = fOff;
			}
			else if (fOff != ldFragUniformOffset)
			{
				throw new NotImplementedException("Fragment Uniform Buffer support incomplete");
				/*
				mtlSetFragmentBufferOffset(
					renderCommandEncoder,
					fOff,
					UNIFORM_REG
				);
				*/
				ldFragUniformOffset = fOff;
			}

			descriptorSets = CreateDescriptorSets();
			UpdateDescriptorSets();
			_commandBuffer.CmdBindDescriptorSet(PipelineBindPoint.Graphics, layout, 0, descriptorSets[0], null);

			// Bind the depth-stencil state
			/*
			IntPtr depthStencilState = FetchDepthStencilState();
			if (depthStencilState != ldDepthStencilState)
			{
				mtlSetDepthStencilState(
					renderCommandEncoder,
					depthStencilState
				);
				ldDepthStencilState = depthStencilState;
			}
			*/

			// Finally, bind the pipeline state
			/*
			IntPtr pipelineState = FetchRenderPipeline();
			if (pipelineState != ldPipelineState)
			{
				mtlSetRenderPipelineState(
					renderCommandEncoder,
					pipelineState
				);
				ldPipelineState = pipelineState;
			}
			*/
			//ldDepthStencilState = IntPtr.Zero;
			ldFragUniformBuffer = IntPtr.Zero;
			ldFragUniformOffset = 0;
			ldVertUniformBuffer = IntPtr.Zero;
			ldVertUniformOffset = 0;
			//ldPipelineState = IntPtr.Zero;
			for (int i = 0; i < MAX_BOUND_VERTEX_BUFFERS; i += 1)
			{
				ldVertexBuffers[i] = 0;
				ldVertexBufferOffsets[i] = 0;
			}
		}

		#endregion

		public void ApplyVertexAttributes(VertexDeclaration vertexDeclaration, IntPtr ptr, int vertexOffset)
		{
			// Translate the declaration into a descriptor
			currentVertexDescriptor = FetchVertexDescriptor(
				vertexDeclaration,
				vertexOffset
			);
			userVertexStride = vertexDeclaration.VertexStride;

			// Prepare for rendering
			UpdateRenderPass();
			BindResources();

			// The rest happens in DrawUser[Indexed]Primitives.
		}

		public IGLQuery CreateQuery()
		{
			throw new NotImplementedException();
		}

		public void AddDisposeQuery(IGLQuery query)
		{
			throw new NotImplementedException();
		}

		public void QueryBegin(IGLQuery query)
		{
			throw new NotImplementedException();
		}

		public void QueryEnd(IGLQuery query)
		{
			throw new NotImplementedException();
		}

		public bool QueryComplete(IGLQuery query)
		{
			throw new NotImplementedException();
		}

		public int QueryPixelCount(IGLQuery query)
		{
			throw new NotImplementedException();
		}
	}
}
