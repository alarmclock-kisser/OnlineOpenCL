using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace OnlineOpenCL.Core
{
	public class ImageObj : IDisposable
	{
		public Guid Id { get; } = Guid.NewGuid();
		public DateTime Created { get; } = DateTime.Now;
		public int Number { get; private set; } = 0;
		public string Name => this.Id + "_" + this.Number.ToString("D5");
		public string FilePath { get; set; } = string.Empty;

		public Image<Rgba32> Img { get; private set; }
		public int BitDepth => this.Img.PixelType.BitsPerPixel / this.Channels;
		public int Channels => 4;
		public int Width { get; private set; } = 0;
		public int Height { get; private set; } = 0;
		public bool IsEmpty { get; private set; } = true;

		public byte[] Cache { get; private set; } = [];

		public IntPtr Pointer { get; set; } = IntPtr.Zero;
		public bool OnHost => this.Pointer == IntPtr.Zero;
		public bool OnDevice => this.Pointer != IntPtr.Zero;
		public long SizeInBytes => this.Width * this.Height * 4;

		public double ElapsedProcessingTime { get; set; } = 0.0;


		private ImageObj(int width, int height, SixLabors.ImageSharp.Color? color = null)
		{
			color ??= SixLabors.ImageSharp.Color.Black;
			this.Img = new Image<Rgba32>(width, height, color.Value);
			this.Width = width;
			this.Height = height;
		}


		public static ImageObj Create(int width, int height, SixLabors.ImageSharp.Color? color = null)
		{
			return new ImageObj(width, height, color);
		}

		public static async Task<ImageObj> CreateAsync(int width, int height, SixLabors.ImageSharp.Color? color = null)
		{
			return await Task.Run(() =>
			{
				ImageObj imageObj = new(width, height, color);
				return imageObj;
			});
		}

		public static ImageObj CreateFromFile(string filePath)
		{
			ImageObj imageObj = new(0, 0);
			imageObj.LoadImage(filePath);
			return imageObj;
		}

		public static async Task<ImageObj> CreateFromFileAsync(string filePath)
		{
			ImageObj imageObj = new(0, 0);
			await imageObj.LoadImageAsync(filePath);
			return imageObj;
		}

		public void Dispose()
		{
			this.Img?.Dispose();
			this.Width = 0;
			this.Height = 0;
			this.Cache = [];

			GC.SuppressFinalize(this);
		}

		public async Task<byte[]> GetBytesAsync(bool cache = false)
		{
			if (this.Img == null)
			{
				return [];
			}

			byte[] result = await Task.Run(() =>
			{
				byte[] bytes = new byte[this.Width * this.Height * 4];

				this.Img.ProcessPixelRows(accessor =>
				{
					for (int y = 0; y < this.Height; y++)
					{
						var row = accessor.GetRowSpan(y);
						int byteIndex = y * this.Width * 4;

						for (int x = 0; x < this.Width; x++)
						{
							Rgba32 pixel = row[x];
							int currentPixelIndex = byteIndex + x * 4;

							bytes[currentPixelIndex + 0] = pixel.R;
							bytes[currentPixelIndex + 1] = pixel.G;
							bytes[currentPixelIndex + 2] = pixel.B;
							bytes[currentPixelIndex + 3] = pixel.A;
						}
					}
				});

				return bytes;
			});

			if (cache)
			{
				this.Cache = result;
			}

			return result;
		}

		public async Task<Image<Rgba32>> SetImageAsync(IEnumerable<byte> bytes)
		{
			long byteCount = bytes.LongCount();
			if (byteCount < 1 || byteCount % 4 != 0)
			{
				return this.Img;
			}

			long pixelCount = byteCount / 4;
			if (this.Width * this.Height != pixelCount)
			{
				return this.Img;
			}

			await Task.Run(() =>
			{
				this.Img.ProcessPixelRows(accessor =>
				{
					for (int y = 0; y < this.Height; y++)
					{
						var row = accessor.GetRowSpan(y);
						int byteIndex = y * this.Width * 4;
						for (int x = 0; x < this.Width; x++)
						{
							int currentPixelIndex = byteIndex + x * 4;
							row[x] = new Rgba32(
								bytes.ElementAt(currentPixelIndex + 0),
								bytes.ElementAt(currentPixelIndex + 1),
								bytes.ElementAt(currentPixelIndex + 2),
								bytes.ElementAt(currentPixelIndex + 3));
						}
					}
				});
			});

			this.IsEmpty = false;
			this.Number++;
			return this.Img;
		}

		public async Task<Image<Rgba32>?> LoadImageAsync(string filePath)
		{
			// Check if file exists
			if (!File.Exists(filePath))
			{
				return null;
			}

			try
			{
				this.Img = await Image.LoadAsync<Rgba32>(filePath);
				this.Width = this.Img.Width;
				this.Height = this.Img.Height;
				this.IsEmpty = false;
				this.Number = 0;
				return this.Img;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error loading image: {ex.Message}");
				return null;
			}
		}

		public Image<Rgba32>? LoadImage(string filePath)
		{
			// Check if file exists
			if (!File.Exists(filePath))
			{
				return null;
			}

			try
			{
				this.Img = Image.Load<Rgba32>(filePath);
				this.Width = this.Img.Width;
				this.Height = this.Img.Height;
				this.IsEmpty = false;
				this.Number = 0;
				return this.Img;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error loading image: {ex.Message}");
				return null;
			}
		}

		public async Task<string?> ExportAsync(string filePath = "", string format = "bmp")
		{
			if (string.IsNullOrWhiteSpace(filePath))
			{
				// SpecialFolder(MyPictures) \ BMZMandelbrot
				filePath = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
					"BMZ_Mandelbrot");
			}

			if (!Directory.Exists(filePath))
			{
				Directory.CreateDirectory(filePath);
			}

			string file = Path.Combine(filePath, $"{this.Name}.{format}");

			try
			{
				switch (format.ToLowerInvariant())
				{
					case "png":
						await this.Img.SaveAsPngAsync(file);
						break;
					case "jpg":
					case "jpeg":
						await this.Img.SaveAsJpegAsync(file);
						break;
					default:
						await this.Img.SaveAsBmpAsync(file);
						break;
				}

				// Add timestamp to file info
				FileInfo info = new(file);
				info.CreationTime = this.Created;

				this.FilePath = file;
				return Path.GetFullPath(file);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error exporting image: {ex.Message}");
				return null;
			}
		}

		public async Task<string> GetBase64Async(string format = "png")
		{
			using MemoryStream ms = new();
			switch (format.ToLowerInvariant())
			{
				case "png":
					await this.Img.SaveAsPngAsync(ms);
					break;
				case "jpg":
				case "jpeg":
					await this.Img.SaveAsJpegAsync(ms);
					break;
				default:
					await this.Img.SaveAsBmpAsync(ms);
					break;
			}

			return Convert.ToBase64String(ms.ToArray());
		}

		public string GetBase64(string format = "png")
		{
			using MemoryStream ms = new();
			switch (format.ToLowerInvariant())
			{
				case "png":
					this.Img.SaveAsPng(ms);
					break;
				case "jpg":
				case "jpeg":
					this.Img.SaveAsJpeg(ms);
					break;
				default:
					this.Img.SaveAsBmp(ms);
					break;
			}

			return Convert.ToBase64String(ms.ToArray());
		}
	}
}
