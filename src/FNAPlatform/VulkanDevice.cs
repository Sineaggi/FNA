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
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Security.AccessControl;
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

		#region Vulkan Buffer Container Class

		private class VulkanBuffer : IGLBuffer
		{
			public Buffer Buffer { get; private set; }

			public DeviceMemory BufferMemory { get; private set; }

			public IntPtr BufferSize { get; }

			public VulkanBuffer(
				Buffer buffer,
				DeviceMemory bufferMemory,
				IntPtr bufferSize,
				BufferUsage usage
			)
			{
				Buffer = buffer;
				BufferMemory = bufferMemory;
				BufferSize = bufferSize;
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

		#endregion

				#region XNA->GL Enum Conversion Class

		private static class XNAToVK
		{
			public static readonly Format[] TextureFormat = new Format[]
			{
				Format.R8G8B8Unorm,	// SurfaceFormat.Color
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

			public DepthFormat DepthFormat { get; private set; }

			public int MultiSampleCount { get; private set; }

			public uint Texture;

			private uint colorAttachment;
			private uint depthStencilAttachment;
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
		public int MaxTextureSlots { get; }
		public int MaxMultiSampleCount { get; }
		public IGLBackbuffer Backbuffer { get; private set; }

		private Vulkan.PhysicalDevice physicalDevice;
		private Vulkan.Device device;
		private Vulkan.CommandPool commandPool;
		private Vulkan.Queue graphicsQueue;
		private Vulkan.Queue presentQueue;
		private Vulkan.SurfaceKhr surface;
		private Vulkan.CommandBuffer _commandBuffer;
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
			if (flags.HasFlag(DebugReportFlagsExt.Error))
			{
				var umessage = Marshal.PtrToStringAnsi(message);
				Debug.WriteLine($"{flags}: {umessage}");
			}

			return true;
		}

		#region Public Constructor

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
					ApiVersion = 4194304, // 1.0
					//ApiVersion = 4198400, // 1.1
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
				EnabledExtensionNames = new[] {"VK_KHR_swapchain"}
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

			uint width, height;
			{
				int w, h;
				SDL.SDL_Vulkan_GetDrawableSize(presentationParameters.DeviceWindowHandle, out w, out h);
				width = (uint) w;
				height = (uint) h;
			}

			setupSwapchain(surfaceCaps, width, height);

			var kk = (ulong)((IMarshalling) device).Handle;
			var kkola = Convert.ToString(53, 2);
			var llpla = Convert.ToString((long)kk, 2).PadLeft(64, '0');

			var shaderContext = MojoShader.MOJOSHADER_vkInitDevice(((IMarshalling)device).Handle);
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
			FNALoggerEXT.LogInfo("MojoShader Profile: metal"); // todo: mojo spirv?

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

			stopWatch = new Stopwatch();
			stopWatch.Start();
		}

		#endregion

		private Stopwatch stopWatch;

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
				ImageColorSpace = ColorSpaceKhr.SrgbNonlinear,
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
			//throw new NotImplementedException();
		}

		public void ResetBackbuffer(PresentationParameters presentationParameters, GraphicsAdapter adapter)
		{
			throw new NotImplementedException();
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
			stopWatch.Stop();
			//Console.WriteLine($"{stopWatch.ElapsedMilliseconds}ms");
			stopWatch.Restart();

			// Console.WriteLine($"kromla index {imageIndex}");

			device.ResetCommandPool(commandPool, 0);
			_commandBuffer.Begin(new CommandBufferBeginInfo { });

			currentImage = images[imageIndex];
			var renderBeginBarrier = imageMemoryBarrierz(
				currentImage,
				0,
				AccessFlags.ColorAttachmentWrite,
				ImageLayout.Undefined,
				ImageLayout.ColorAttachmentOptimal
				);
			_commandBuffer.CmdPipelineBarrier(PipelineStageFlags.ColorAttachmentOutput,
				PipelineStageFlags.ColorAttachmentOutput, DependencyFlags.ByRegion, new MemoryBarrier[0], new BufferMemoryBarrier[0], new[] {renderBeginBarrier});


		}



		public void SwapBuffers(Rectangle? sourceRectangle, Rectangle? destinationRectangle,
			IntPtr overrideWindowHandle)
		{
			/* Just in case Present() is called
			* before any rendering happens...
			*/
			BeginFrame();

			//throw new NotImplementedException();
			// todo: what to do if sourceRectangle or destinationRectangle are not null. Also what to do with windowhandle

			var renderEndBarrier = imageMemoryBarrierz(currentImage, AccessFlags.ColorAttachmentWrite, 0,
				ImageLayout.ColorAttachmentOptimal, ImageLayout.PresentSrcKhr);
			_commandBuffer.CmdPipelineBarrier(PipelineStageFlags.ColorAttachmentOutput, PipelineStageFlags.TopOfPipe,
				DependencyFlags.ByRegion, new MemoryBarrier[0], new BufferMemoryBarrier[0], new[] {renderEndBarrier});

			_commandBuffer.End();

			presentQueue.Submit(new SubmitInfo
			{
				WaitSemaphores = new[] {acquireSemaphore},
				WaitDstStageMask = new[] {PipelineStageFlags.ColorAttachmentOutput},
				CommandBuffers = new[] {_commandBuffer}, SignalSemaphores = new[] {releaseSemaphore}
			});

			try
			{
				presentQueue.PresentKHR(new PresentInfoKhr
				{
					WaitSemaphores = new[] {releaseSemaphore}, Swapchains = new[] {_swapchainKhr},
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

			// We're done here.
			frameInProgress = false;
		}

		public void SetStringMarker(string text)
		{
			throw new NotImplementedException();
		}

		public void DrawIndexedPrimitives(PrimitiveType primitiveType, int baseVertex, int minVertexIndex,
			int numVertices,
			int startIndex, int primitiveCount, IndexBuffer indices)
		{
			throw new NotImplementedException();
		}

		public void DrawInstancedPrimitives(PrimitiveType primitiveType, int baseVertex, int minVertexIndex,
			int numVertices,
			int startIndex, int primitiveCount, int instanceCount, IndexBuffer indices)
		{
			throw new NotImplementedException();
		}

		public void DrawPrimitives(PrimitiveType primitiveType, int vertexStart, int primitiveCount)
		{
			throw new NotImplementedException();
		}

		public void DrawUserIndexedPrimitives(PrimitiveType primitiveType, IntPtr vertexData, int vertexOffset,
			int numVertices,
			IntPtr indexData, int indexOffset, IndexElementSize indexElementSize, int primitiveCount)
		{
			throw new NotImplementedException();
		}

		public void DrawUserPrimitives(PrimitiveType primitiveType, IntPtr vertexData, int vertexOffset,
			int primitiveCount)
		{
			/*
			*         vkCmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, trianglePipeline);
			vkCmdDraw(commandBuffer, 3, 1, 0, 0); // triangle



			var vertexBuffer = vertexData;
			var vertexBufferSize = primitiveCount * (sizeof(float) * 3);

			var indexBuffer = new uint[primitiveCount * 3];
			for (uint i = 0; i < primitiveCount * 3; i++)
			{
				indexBuffer[i] = i;
			}
			var indexBufferSize = indexBuffer.Length * sizeof(uint);

			MemoryRequirements memReqs;
			var memAlloc = new MemoryAllocateInfo();

			// there are two types of allocation in vulkan. staging based and non staging based.
			// since this data need be written every(?) frame, we will penalize the caller

			//createBuffer();
			var verticesBuffer = device.CreateBuffer(new BufferCreateInfo
			{
				Size = vertexBufferSize,
				Usage = BufferUsageFlags.VertexBuffer,
			});
			memReqs = device.GetBufferMemoryRequirements(verticesBuffer);
			memAlloc.AllocationSize = memReqs.Size;
			memAlloc.MemoryTypeIndex = getMemoryTypeIndex(memReqs.MemoryTypeBits, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);
			var verticesMemory = device.AllocateMemory(memAlloc);
			var data = device.MapMemory(verticesMemory, 0, memAlloc.AllocationSize);
			SDL.SDL_memcpy(data, vertexData, (IntPtr)vertexBufferSize);
			device.UnmapMemory(verticesMemory);
			device.BindBufferMemory(verticesBuffer, verticesMemory, 0);
*/
			/*
			 * 			VK_CHECK_RESULT(vkAllocateMemory(device, &memAlloc, nullptr, &vertices.memory));
			VK_CHECK_RESULT(vkMapMemory(device, vertices.memory, 0, memAlloc.allocationSize, 0, &data));
			memcpy(data, vertexBuffer.data(), vertexBufferSize);
			vkUnmapMemory(device, vertices.memory);
			VK_CHECK_RESULT(vkBindBufferMemory(device, vertices.buffer, vertices.memory, 0));
			 */

			/*
			var indexBufferInfo = new BufferCreateInfo
			{
				Size = indexBufferSize,
				Usage = BufferUsageFlags.IndexBuffer,
			};
			var indicesBuffer = device.CreateBuffer(indexBufferInfo);
			memReqs = device.GetBufferMemoryRequirements(indicesBuffer);
			memAlloc.AllocationSize = memReqs.Size;
			memAlloc.MemoryTypeIndex = getMemoryTypeIndex(memReqs.MemoryTypeBits, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);;
			var indicesMemory = device.AllocateMemory(memAlloc);
			data = device.MapMemory(indicesMemory, 0, memAlloc.AllocationSize);

			GCHandle pinnedArray = GCHandle.Alloc(indexBuffer, GCHandleType.Pinned);
			IntPtr pointer = pinnedArray.AddrOfPinnedObject();
			// Do your stuff...
			SDL.SDL_memcpy(data, (pointer), (IntPtr)indexBufferSize);
			pinnedArray.Free();
			device.UnmapMemory(indicesMemory);
			device.BindBufferMemory(indicesBuffer, indicesMemory, 0);



			Pipeline trianglePipeline = null;
			uint vertexCount = (uint)primitiveCount;
			uint instanceCount = (uint)1;
			uint firstVertex = 0; // use vertexOffset here?
			uint firstInstance = 0;
*/

			// todo: shader, bindpoints
			// todo: maybe index buffer was uncecessary?
			// since all this code assumed calling drawIndexed in the end, and I just wan to cmDdraw
			// something about needing a pipeline tho? wtf

			String fname, vname;
			ShaderModule fshader, vshader;
			var feld = typeof(ShaderModule).GetField("m", BindingFlags.NonPublic | BindingFlags.Instance);
			unsafe
			{
				MojoShader.MOJOSHADER_vkLinkProgram(shaderState.vertexShader, shaderState.fragmentShader);

				//var entrypoint = Marshal.PtrToStringAnsi(parseData.mainfn);
				var fpd = ((MojoShader.MOJOSHADER_parseData*)
					((MojoShader.MOJOSHADER_vkShader*) shaderState.fragmentShader)->parseData);

				fname = Marshal.PtrToStringAnsi(
					((MojoShader.MOJOSHADER_parseData*)
						((MojoShader.MOJOSHADER_vkShader*) shaderState.fragmentShader)->parseData)->mainfn);

				fshader = (ShaderModule) typeof(ShaderModule).GetConstructor(
					BindingFlags.NonPublic | BindingFlags.Instance,
					null, Type.EmptyTypes, null).Invoke(null);

				var vert = ((MojoShader.MOJOSHADER_vkShader*) shaderState.vertexShader);
				var sm = vert->shaderModule;
				UInt64 sss = (UInt64) sm;
				var frag = ((MojoShader.MOJOSHADER_vkShader*) shaderState.fragmentShader);
				var fshrptr = frag->shaderModule;
				object f = fshader;
				feld.SetValue(f, fshrptr);

				var vparseData = (MojoShader.MOJOSHADER_parseData*)
					((MojoShader.MOJOSHADER_vkShader*) shaderState.vertexShader)->parseData;
				vname = Marshal.PtrToStringAnsi(vparseData->mainfn);
				ShaderModule sh1 = profileCompileShader(*vparseData);

				vshader = (ShaderModule) typeof(ShaderModule).GetConstructor(
					BindingFlags.NonPublic | BindingFlags.Instance,
					null, Type.EmptyTypes, null).Invoke(null);

				var vshrptr = vert->shaderModule;
				object v = vshader;
				feld.SetValue(v, vshrptr);
			}

			Console.WriteLine(fname);
			Console.WriteLine(vname);

			var kjola = device.CreateDescriptorSetLayout(new DescriptorSetLayoutCreateInfo
			{
				Bindings = new DescriptorSetLayoutBinding[] {new DescriptorSetLayoutBinding
				{
					DescriptorType = DescriptorType.UniformBuffer,
					StageFlags = ShaderStageFlags.Vertex,
					Binding = 1,
					DescriptorCount = 1,
					ImmutableSamplers = new Sampler[] {},
				}},
			});

		var layout = device.CreatePipelineLayout(new PipelineLayoutCreateInfo
			{
				SetLayouts = new DescriptorSetLayout[]
				{
					kjola
				},
				PushConstantRanges = new PushConstantRange[] {},
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

				//var stages = new PipelineShaderStageCreateInfo[0];


				var createInfo = new GraphicsPipelineCreateInfo
				{
					Stages = stages,
					VertexInputState = new PipelineVertexInputStateCreateInfo {},
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
						Attachments = new [] {new PipelineColorBlendAttachmentState
							{
								ColorWriteMask = ColorComponentFlags.R | ColorComponentFlags.G | ColorComponentFlags.B | ColorComponentFlags.A,
							}
						}
					},
					DynamicState = new PipelineDynamicStateCreateInfo
					{
						DynamicStates = new []{DynamicState.Viewport, DynamicState.Scissor}
					},
					Layout = layout,// todo
					RenderPass = renderPass,
				};
				var pipelines = device.CreateGraphicsPipelines(null, new[] {createInfo});

				var trianglePipeline = pipelines[0];


			_commandBuffer.CmdBindPipeline(PipelineBindPoint.Graphics, trianglePipeline);
			//_commandBuffer.CmdDraw(vertexCount, instanceCount, firstVertex, firstInstance);

			Console.WriteLine("DrawUserPrimitives");
			// 7 todo
			//throw new NotImplementedException();
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
			throw new NotImplementedException();
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

		public void SetBlendState(BlendState blendState)
		{
			// todo: after apply effect
			//throw new NotImplementedException();
		}

		public void SetDepthStencilState(DepthStencilState depthStencilState)
		{
			//after belnd state
			//throw new NotImplementedException();
		}

		public void ApplyRasterizerState(RasterizerState rasterizerState)
		{
			// after set depth stencil state
			//throw new NotImplementedException();
		}

		public void VerifySampler(int index, Texture texture, SamplerState sampler)
		{
			throw new NotImplementedException();
		}

		public void Clear(ClearOptions options, Vector4 color, float depth, int stencil)
		{
			var clearColorValue = new ClearColorValue {Float32 = new float[4] {color.X, color.Y, color.W, color.Z}};
			// todo: what depth? what stencil? what options?
			_commandBuffer.CmdClearColorImage
			(currentImage,
				ImageLayout.General,
				clearColorValue,
				new ImageSubresourceRange {AspectMask = ImageAspectFlags.Color, LevelCount = 1, LayerCount = 1}
			);
		}

		public void SetRenderTargets(RenderTargetBinding[] renderTargets, IGLRenderbuffer renderbuffer,
			DepthFormat depthFormat)
		{
			throw new NotImplementedException();
		}

		public void ResolveTarget(RenderTargetBinding target)
		{
			throw new NotImplementedException();
		}

		public void ReadBackbuffer(IntPtr data, int dataLen, int startIndex, int elementCount, int elementSizeInBytes,
			int subX,
			int subY, int subW, int subH)
		{
			throw new NotImplementedException();
		}

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

			public TextureAddressMode WrapS;
			public TextureAddressMode WrapT;
			public TextureAddressMode WrapR;
			public TextureFilter Filter;
			public float Anistropy;
			public int MaxMipmapLevel;
			public float LODBias;

			//public DeviceSize ImageSize;
			//public Buffer Buffer;
			//public DeviceMemory DeviceMemory;
			public uint Width;
			public uint Height;

			public Image Image;
			public DeviceMemory ImageMemory;

			public VulkanTexture(
				//DeviceSize imageSize,
				//Buffer buffer,
				//DeviceMemory deviceMemory,
				uint width,
				uint height,
				int levelCount
			) {
				HasMipmaps = levelCount > 1;
				//ImageSize = imageSize;
				//Buffer = buffer;
				//DeviceMemory = deviceMemory;
				Width = width;
				Height = height;

				WrapS = TextureAddressMode.Wrap;
				WrapT = TextureAddressMode.Wrap;
				WrapR = TextureAddressMode.Wrap;
				Filter = TextureFilter.Linear;
				Anistropy = 4.0f;
				MaxMipmapLevel = 0;
				LODBias = 0.0f;
			}

			/*
			// We can't set a SamplerState Texture to null, so use this.
			private VulkanTexture()
			{
				Handle = 0;
				Target = GLenum.GL_TEXTURE_2D; // FIXME: Assumption! -flibit
			}
			public static readonly OpenGLTexture NullTexture = new OpenGLTexture();
			*/
		}

		public IGLTexture CreateTexture2D(SurfaceFormat format, int width, int height, int levelCount,
			bool isRenderTarget)
		{
			// todo: use SurfaceFormat

			Debug.Assert(width > 0);
			Debug.Assert(height > 0);
			return new VulkanTexture((uint)width, (uint)height, levelCount);

			/*
			void* data;
			vkMapMemory(device, stagingBufferMemory, 0, imageSize, 0, &data);
			memcpy(data, pixels, static_cast<size_t>(imageSize));
			vkUnmapMemory(device, stagingBufferMemory);
			*/
			// for an image, todo
			//throw new NotImplementedException();
			return null;
		}

		public IGLTexture CreateTexture3D(SurfaceFormat format, int width, int height, int depth, int levelCount)
		{
			throw new NotImplementedException();
		}

		public IGLTexture CreateTextureCube(SurfaceFormat format, int size, int levelCount, bool isRenderTarget)
		{
			throw new NotImplementedException();
		}

		public void AddDisposeTexture(IGLTexture texture)
		{
			throw new NotImplementedException();
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

		public void SetTextureData2D(IGLTexture texture, SurfaceFormat format, int x, int y, int w, int h, int level,
			IntPtr data,
			int dataLength)
		{
			var vulkanTexture = texture as VulkanTexture;
			var width = vulkanTexture.Width;
			var height = vulkanTexture.Height;
			DeviceSize imageSize = width * height * 4;

			createBuffer(imageSize, BufferUsageFlags.TransferSrc, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out var stagingBuffer, out var stagingBufferMemory);

			Console.WriteLine($"Are they the same? {imageSize} == {dataLength}");

			var dst = device.MapMemory(stagingBufferMemory, 0, imageSize, 0);
			SDL.SDL_memcpy(dst, data, (IntPtr) dataLength);
			device.UnmapMemory(stagingBufferMemory);

			createImage(width, height, Format.R8G8B8A8Srgb, ImageTiling.Optimal, ImageUsageFlags.TransferDst | ImageUsageFlags.Sampled, MemoryPropertyFlags.DeviceLocal, out var textureImage, out var textureImageMemory);

			transitionImageLayout(textureImage, Format.R8G8B8A8Srgb, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
			copyBufferToImage(stagingBuffer, textureImage, width, height);
			transitionImageLayout(textureImage, Format.R8G8B8A8Srgb, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

			device.DestroyBuffer(stagingBuffer);
			device.FreeMemory(stagingBufferMemory);

			vulkanTexture.Image = textureImage;
			vulkanTexture.ImageMemory = textureImageMemory;
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
			throw new NotImplementedException();
		}

		public IGLRenderbuffer GenRenderbuffer(int width, int height, DepthFormat format, int multiSampleCount)
		{
			throw new NotImplementedException();
		}

		public void AddDisposeRenderbuffer(IGLRenderbuffer renderbuffer)
		{
			throw new NotImplementedException();
		}

		public IGLBuffer GenVertexBuffer(bool dynamic, BufferUsage usage, int vertexCount, int vertexStride)
		{
			Buffer buffer;
			DeviceMemory bufferMemory;

			ulong size = (ulong)vertexCount * (ulong)vertexStride;

			createBuffer(size, BufferUsageFlags.VertexBuffer, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out buffer, out bufferMemory);

			IntPtr bufferSize = (IntPtr) (vertexStride * vertexCount);

			return new VulkanBuffer(buffer, bufferMemory, bufferSize, usage);
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
			//throw new NotImplementedException();
		}

		public void SetVertexBufferData(IGLBuffer buffer, int offsetInBytes, IntPtr data, int dataLength, SetDataOptions options)
		{
			throw new NotImplementedException();
		}

		public void GetVertexBufferData(IGLBuffer buffer, int offsetInBytes, IntPtr data, int startIndex, int elementCount,
			int elementSizeInBytes, int vertexStride)
		{
			throw new NotImplementedException();
		}

		public IGLBuffer GenIndexBuffer(bool dynamic, BufferUsage usage, int indexCount, IndexElementSize indexElementSize)
		{
			Buffer buffer;
			DeviceMemory bufferMemory;

			ulong size;
			if (indexElementSize == IndexElementSize.SixteenBits)
			{
				size = (ulong)indexCount * 16;
			}
			else
			{
				size = (ulong)indexCount * 32;
			}

			createBuffer(size, BufferUsageFlags.TransferDst | BufferUsageFlags.IndexBuffer, MemoryPropertyFlags.DeviceLocal, out buffer, out bufferMemory);

			IntPtr bufferSize = (IntPtr) (size);

			return new VulkanBuffer(buffer, bufferMemory, bufferSize, usage);
		}

		public void AddDisposeIndexBuffer(IGLBuffer buffer)
		{
			// todo: prio 1, needs to dispose index buffer?
			//buffer.Dispose();
			//throw new NotImplementedException();
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

			// todo: next task, create an effect. how? who the fuck knows
			throw new NotImplementedException();
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

		public IGLEffect CloneEffect(IGLEffect effect)
		{
			throw new NotImplementedException();
		}

		public void AddDisposeEffect(IGLEffect effect)
		{
			//throw new NotImplementedException();
			// todo: prio 1, shutdown, how to delete effect?
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

		public void ApplyVertexAttributes(VertexBufferBinding[] bindings, int numBindings, bool bindingsUpdated, int baseVertex)
		{
			Console.WriteLine("ApplyVertexAttributes 1");
			//BindPipeline();
			// 8 todo
			// throw new NotImplementedException();
		}

		public void ApplyVertexAttributes(VertexDeclaration vertexDeclaration, IntPtr ptr, int vertexOffset)
		{
			Console.WriteLine("ApplyVertexAttributes 2");
			// 6 todo
			//throw new NotImplementedException();
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
