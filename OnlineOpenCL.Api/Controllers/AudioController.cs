using System.Diagnostics;
using OnlineOpenCL.Core;
using OnlineOpenCL.Shared;
using Microsoft.AspNetCore.Mvc;

namespace OnlineOpenCL.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class AudioController : ControllerBase
	{
		private readonly AudioCollection audioCollection;

		public AudioController(AudioCollection audioCollection)
		{
			this.audioCollection = audioCollection;
		}

		[HttpGet("tracks")]
		[ProducesResponseType(typeof(IEnumerable<AudioObjInfo>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<AudioObjInfo>>> GetTracks()
		{
			try
			{
				var infos = await Task.Run(() =>
					this.audioCollection.Tracks.Select(i => new AudioObjInfo(i)).ToArray());
				return this.Ok(infos.Length > 0 ? infos : []);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Get Audios Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("info/{guid}")]
		[ProducesResponseType(typeof(AudioObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjInfo>> GetAudioInfo(Guid guid)
		{
			try
			{
				var obj = this.audioCollection[guid];
				return this.Ok(obj != null ? new AudioObjInfo(obj) : new AudioObjInfo());
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Audio Info Error",
					Detail = ex.Message,
					Status = 500
				});
			}
			finally
			{
				await Task.Yield();
			}
		}

		[HttpDelete("remove/{guid}")]
		[ProducesResponseType(typeof(bool), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<bool>> RemoveAudio(Guid guid)
		{
			try
			{
				var obj = this.audioCollection[guid];
				if (obj == null)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio found with Guid '{guid}'",
						Status = 404
					});
				}

				await this.audioCollection.RemoveAsync(obj);
				try
				{
					obj.Dispose();
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error disposing audio object: {ex.Message}");
				}

				var result = this.audioCollection[guid] == null;
				if (!result)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Remove Failed",
						Detail = $"Couldn't remove audio with Guid '{guid}'",
						Status = 400
					});
				}

				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Remove Audio Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("upload")]
		[RequestSizeLimit(128_000_000)]
		[ProducesResponseType(typeof(AudioObjInfo), 201)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjInfo>> UploadAudio(IFormFile file, bool copyGuid = false)
		{
			if (file.Length == 0)
			{
				return this.BadRequest(new ProblemDetails
				{
					Title = "Empty File",
					Detail = "Uploaded file is empty",
					Status = 400
				});
			}

			Stopwatch sw = Stopwatch.StartNew();
			var tempDir = Path.Combine(Path.GetTempPath(), "audio_uploads");
			Directory.CreateDirectory(tempDir);
			var tempPath = Path.Combine(tempDir, Path.GetFileName(file.FileName));

			try
			{
				await using (var stream = System.IO.File.Create(tempPath))
				{
					await file.CopyToAsync(stream);
				}

				var obj = await this.audioCollection.ImportAsync(tempPath, true);
				if (obj == null)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Upload Failed",
						Detail = "Failed to load audio from uploaded file",
						Status = 400
					});
				}

				obj.Name = Path.GetFileNameWithoutExtension(file.FileName);
				obj.FilePath = file.FileName;

				var info = new AudioObjInfo(obj);
				if (info.Id == Guid.Empty)
				{
					obj.Dispose();
					return this.NotFound(new ProblemDetails
					{
						Title = "Upload Info Missing",
						Detail = "Failed to retrieve audio information after upload",
						Status = 404
					});
				}

				return this.Created($"api/audio/info/{info.Id}", info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Upload Error",
					Detail = ex.Message,
					Status = 500
				});
			}
			finally
			{
				try 
				{
					if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error deleting temp file: {ex.Message}");
				}

				sw.Stop();
			}
		}

		[HttpGet("download/{guid}")]
		[ProducesResponseType(typeof(FileResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> DownloadAudio(Guid guid)
		{
			try
			{
				var obj = await Task.Run(() => this.audioCollection[guid]);
				if (obj == null)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio found with Guid '{guid}'",
						Status = 404
					});
				}

				if (obj.Data.LongLength <= 0)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Empty Audio",
						Detail = "Audio contains no data",
						Status = 400
					});
				}

				var format = obj.FilePath?.Split('.').Last() ?? "wav";
				var filePath = await Task.Run(() => obj.Export());
				if (string.IsNullOrEmpty(filePath))
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Export Failed",
						Detail = "Failed to export audio to file",
						Status = 400
					});
				}

				var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
				return this.File(fileBytes, "application/octet-stream", Path.GetFileName(filePath));
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Download Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("waveform64/{guid}")]
		[ProducesResponseType(typeof(AudioData64), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioData64>> GetBase64Waveform(Guid guid)
		{
			try
			{
				var obj = this.audioCollection[guid];
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio found with Guid '{guid}'",
						Status = 404
					});
				}

				var data = await Task.Run(() => new AudioData64(obj));
				if (string.IsNullOrEmpty(data.Data64))
				{
					return this.Ok(new AudioData64());
				}

				return this.Ok(data);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Waveform Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("play/{volume}/{guid}")]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> PlayAudio(Guid guid, float volume = 0.66f)
		{
			try
			{
				volume = Math.Clamp(volume, 0.0f, 1.0f);
				var obj = await Task.Run(() => this.audioCollection[guid]);

				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio found with Guid '{guid}'",
						Status = 404
					});
				}

				await Task.Run(() => obj.Play(this.HttpContext.RequestAborted, null, volume));
				return this.Ok();
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Play Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("stop/{guid}")]
		[ProducesResponseType(typeof(AudioObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjInfo>> StopAudio(Guid guid)
		{
			try
			{
				var obj = this.audioCollection[guid];
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio found with Guid '{guid}'",
						Status = 404
					});
				}

				obj.Stop();
				var info = await Task.Run(() => new AudioObjInfo(obj));

				if (info.Playing)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Stop Failed",
						Detail = $"Audio with Guid '{guid}' is still playing",
						Status = 400
					});
				}

				return this.Ok(info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Stop Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("stopAll")]
		[ProducesResponseType(typeof(int), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<int>> StopAllAudio()
		{
			try
			{
				this.audioCollection.StopAll();
				if (this.audioCollection.Tracks.Any(t => t.Playing))
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Stop Failed",
						Detail = "Some tracks are still playing",
						Status = 400
					});
				}

				return this.Ok(this.audioCollection.Tracks.Count(t => t.Playing));
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Stop All Error",
					Detail = ex.Message,
					Status = 500
				});
			}
			finally
			{
				await Task.Yield();
			}
		}




	}
}
