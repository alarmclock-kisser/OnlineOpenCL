using System.Diagnostics;
using OnlineOpenCL.Core;
using OnlineOpenCL.OpenCl;
using OnlineOpenCL.Shared;
using Microsoft.AspNetCore.Mvc;
using System.Drawing;

namespace OnlineOpenCL.Api.Controllers
{
	[ApiController]
    [Route("api/[controller]")]
    public class OpenClController : ControllerBase
    {
        private readonly OpenClService openClService;
        private readonly ImageCollection imageCollection;
        private readonly AudioCollection audioCollection;

		// ApiConfig pipethrough
		public ApiConfig Config { get; }


		public string sizeMagnitude { get; set; } = "MB";
		public int Decimals { get; set; } = 2;

		private string kernelFilter { get; set; } = string.Empty;

		public bool FlagReadable { get; set; } = false;

		// Info tasks
		private Task<ClStatusInfo> statusInfoTask => Task.Run(() => new ClStatusInfo(this.openClService));
		private Task<IEnumerable<ClDeviceInfo>> deviceInfosTask =>Task.Run(() => this.openClService.DevicesPlatforms.Select((device, index) => new ClDeviceInfo(this.openClService, index)));
		private Task<IEnumerable<ClMemInfo>> memoryInfosTask => Task.Run(() => this.openClService.Register?.Memory.Select((mem, index) => new ClMemInfo(mem)) ?? []);
		private Task<IEnumerable<ClKernelInfo>> kernelInfosTask => Task.Run(() => this.openClService.Compiler?.KernelFiles.Select(k => new ClKernelInfo(this.openClService.Compiler, k)) ?? []);
		private Task<ClUsageInfo> usageInfoTask => Task.Run(() => new ClUsageInfo(this.openClService, this.sizeMagnitude, this.Decimals));



		public OpenClController(OpenClService openClService, ImageCollection imageCollection, AudioCollection audioCollection, ApiConfig apiConfig)
        {
			this.openClService = openClService;
            this.imageCollection = imageCollection;
            this.audioCollection = audioCollection;

			this.Config = apiConfig;
		}

		// Endpoint to reflect the ApiConfig
		[HttpGet("config")]
		[ProducesResponseType(typeof(ApiConfig), 200)]
		[ProducesResponseType(typeof(ApiConfig), 500)]
		public ActionResult<ApiConfig> GetConfig()
		{
			try
			{
				return this.Ok(this.Config);
			}
			catch (Exception ex)
			{
				Console.WriteLine("No ApiConfig available (!)");

				var error = new ProblemDetails
				{
					Title = "Error retrieving configuration",
					Detail = ex.Message,
					Status = 500
				};

				return this.Ok(new ApiConfig());
			}
		}

		[HttpGet("status")]
		[ProducesResponseType(typeof(ClStatusInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ClStatusInfo>> GetStatus()
		{
			try
			{
				return this.Ok(await this.statusInfoTask);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error getting OpenCL status",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("devices")]
		[ProducesResponseType(typeof(IEnumerable<ClDeviceInfo>), 200)]
		[ProducesResponseType(typeof(ClDeviceInfo[]), 204)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<ClDeviceInfo>>> GetDevices()
		{
			try
			{
				var infos = await this.deviceInfosTask;
				return this.Ok(infos.Any() ? infos : []);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error getting devices",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("initialize/{deviceId}")]
		[ProducesResponseType(typeof(ClStatusInfo), 201)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ClStatusInfo>> Initialize(int deviceId = 2)
		{
			int count = this.openClService.DeviceCount;

			if (deviceId < 0 || deviceId >= count)
			{
				return this.NotFound(new ProblemDetails
				{
					Title = "Invalid device ID",
					Detail = $"Invalid device ID (max:{count})",
					Status = 404
				});
			}

			try
			{
				await Task.Run(() => this.openClService.Initialize(deviceId));
				var status = await this.statusInfoTask;

				if (!status.Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Initialization failed",
						Detail = "OpenCL service could not be initialized. Device might not be available.",
						Status = 400
					});
				}

				return this.Created($"api/opencl/status", status);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Initialization error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpDelete("dispose")]
		[ProducesResponseType(typeof(ClStatusInfo), 200)]
		[ProducesResponseType(typeof(ClStatusInfo), 400)]
		[ProducesResponseType(typeof(ClStatusInfo), 500)]
		public async Task<ActionResult<ClStatusInfo>> Dispose()
		{
			try
			{
				await Task.Run(() => this.openClService.Dispose());

				if ((await this.statusInfoTask).Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Disposing failed",
						Detail = "OpenCL service could not be disposed since it's still initialized on " + (await this.statusInfoTask).Device,
						Status = 400
					});
				}

				return this.Ok(await this.statusInfoTask);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Dispose error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("usage")]
		[ProducesResponseType(typeof(ClUsageInfo), 200)]
		[ProducesResponseType(typeof(ClUsageInfo), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ClUsageInfo>> GetUsage(string magnitude = "KB")
		{
			this.sizeMagnitude = magnitude;

			try
			{
				if (this.openClService.Register == null)
				{
					return this.Ok(new ClUsageInfo());
				}

				return this.Ok(await this.usageInfoTask);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error getting usage info",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("memory")]
		[ProducesResponseType(typeof(IEnumerable<ClMemInfo>), 200)]
		[ProducesResponseType(typeof(ClMemInfo[]), 204)]
		[ProducesResponseType(typeof(ClMemInfo[]), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<ClMemInfo>>> GetMemoryObjects()
		{
			try
			{
				if (this.openClService.Register == null)
				{
					return this.Ok(Array.Empty<ClMemInfo>());
				}

				var infos = await this.memoryInfosTask;
				return this.Ok(infos.Any() ? infos : []);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error getting memory objects",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("kernels/{filter?}")]
		public async Task<ActionResult<IEnumerable<ClKernelInfo>>> GetKernels(string filter = "")
		{
			if (this.openClService.Compiler == null)
			{
				return this.Ok(Array.Empty<ClKernelInfo>());
			}

			this.kernelFilter = filter ?? string.Empty;

			var kernels = await this.kernelInfosTask;
			return this.Ok(kernels.Any() ? kernels : []);
		}

		[HttpGet("executeFractal/{kernel?}/{version?}/{width?}/{height?}/{zoom?}/{x?}/{y?}/{iter?}/{r?}/{g?}/{b?}/{randomColors?}")]
		[ProducesResponseType(typeof(ImageObjInfo), 201)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> ExecuteMandelbrot(string kernel = "mandelbrot", string version = "00", int width = 1920, int height = 1080,
			double zoom = 1.0, double x = 0.0, double y = 0.0, int iter = 16, int r = 0, int g = 0, int b = 0, bool randomColors = true)
		{
			// Optional random colors
			if (randomColors)
			{
				Random rand = new();
				r = rand.Next(0, 256);
				g = rand.Next(0, 256);
				b = rand.Next(0, 256);
			}

			var color = Color.FromArgb(r, g, b);

			try
			{
				// Get status
				if (!(await this.statusInfoTask).Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "OpenCL Not Initialized",
						Detail = "OpenCL service is not initialized. Please initialize it first and check devices available.",
						Status = 400
					});
				}

				// Create an empty image
				var obj = await Task.Run(() => this.imageCollection.Add(width, height));
				if (obj == null || !this.imageCollection.Images.Contains(obj))
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Image Creation Failed",
						Detail = "Failed to create empty image or couldn't add it to the collection.",
						Status = 404
					});
				}

				// Build variable arguments
				object[] variableArgs = [0, 0, width, height, zoom, x, y, iter, r, g, b];

				var result = await this.openClService.ExecuteImageFractal(obj, kernel, version, zoom, x, y, iter, color);

				var info = await Task.Run(() => new ImageObjInfo(obj));
				if (!info.OnHost)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Execution Failed",
						Detail = "Failed to execute OpenCL kernel or image is not on the host after execution call.",
						Status = 404
					});
				}

				return this.Created($"api/image/image64/{info.Id}", info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Mandelbrot Execution Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("executeTimestretch/{guid}/{factor}/{kernel?}/{version?}/{chunkSize?}/{overlap?}")]
		[ProducesResponseType(typeof(AudioObjInfo), 201)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjInfo>> ExecuteTimestretch(Guid guid, string kernel = "timestretch_double", string version = "03", double factor = 0.8, int chunkSize = 16384, float overlap = 0.5f)
		{
			try
			{
				var obj = this.audioCollection[guid];
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio object found with Guid '{guid}'",
						Status = 404
					});
				}

				if (!(await this.statusInfoTask).Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "OpenCL Not Initialized",
						Detail = "OpenCL service is not initialized. Please initialize it first.",
						Status = 400
					});
				}

				var optionalArguments = new Dictionary<string, object>
				{
					["factor"] = kernel.Contains("double", StringComparison.OrdinalIgnoreCase)
						? (double) factor
						: (float) factor
				};

				Stopwatch sw = Stopwatch.StartNew();
				var result = await this.openClService.ExecuteAudioKernel(
					obj, kernel, version, chunkSize, overlap, optionalArguments, true);
				
				sw.Stop();

				var info = await Task.Run(() => new AudioObjInfo(obj));
				return this.Created($"api/audio/{obj.Id}/info", info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Timestretch Execution Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("moveAudio/{guid}")]
		[ProducesResponseType(typeof(AudioObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjInfo>> MoveAudio(Guid guid)
		{
			try
			{
				var obj = this.audioCollection[guid];
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio object found with Guid '{guid}'",
						Status = 404
					});
				}

				var wasOnHost = new AudioObjInfo(obj).OnHost;

				if (!(await this.statusInfoTask).Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "OpenCL Not Initialized",
						Detail = "OpenCL service is not initialized. Please initialize it first.",
						Status = 400
					});
				}

				var result = await this.openClService.MoveAudio(obj);
				var info = new AudioObjInfo(obj);

				if (info.OnHost == wasOnHost)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Move Operation Failed",
						Detail = $"Audio object was not moved to the host or device as expected. Now on {(info.OnHost ? "Host" : "OpenCL")}",
						Status = 400
					});
				}

				return this.Ok(info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Audio Move Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("moveImage/{guid}")]
		[ProducesResponseType(typeof(ImageObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> MoveImage(Guid guid)
		{
			try
			{
				var obj = this.imageCollection[guid];
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Image Not Found",
						Detail = $"No image object found with Guid '{guid}'",
						Status = 404
					});
				}

				var wasOnHost = new ImageObjInfo(obj).OnHost;

				if (!(await this.statusInfoTask).Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "OpenCL Not Initialized",
						Detail = "OpenCL service is not initialized. Please initialize it first.",
						Status = 400
					});
				}

				var result = await this.openClService.MoveImage(obj);
				var info = new ImageObjInfo(obj);

				if (info.OnHost == wasOnHost)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Move Operation Failed",
						Detail = $"Image object was not moved to the host or device as expected. Now on {(info.OnHost ? "Host" : "OpenCL")}",
						Status = 400
					});
				}

				return this.Ok(info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Image Move Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

	}
}
