using OnlineOpenCL.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OnlineOpenCL.Shared
{
	public class ImageData64
	{
		public Guid Id { get; set; } = Guid.NewGuid();
		public Guid ObjectId { get; set; } = Guid.Empty;
		public DateTime Created { get; set; } = DateTime.Now;

		public int Width { get; set; } = 0;
		public int Height { get; set; } = 0;
		public string Format { get; set; } = "N/A";

		public string Data64Prefix { get; set; } = string.Empty;
		public string RawData64 { get; set; } = string.Empty;

		public string Data64
		{
			get => this.Data64Prefix + this.RawData64;
			set => this.RawData64 = value;
		}

		public string Length
		{
			get => this.RawData64.Length.ToString();
			set { /* Do nothing, this is read-only */ }
		}

		private static readonly String[] allowedFormats = ["bmp", "png", "jpg"];

		public ImageData64()
		{
			// Empty ctor
		}

		[JsonConstructor]
		public ImageData64(ImageObj? obj = null, string format = "png")
		{
			if (obj == null)
			{
				return;
			}

			// Verify format being bmp, png, jpg, jpg
			if (string.IsNullOrWhiteSpace(format) || !allowedFormats.Contains(format.ToLowerInvariant()))
			{
				format = "png";
			}

			this.ObjectId = obj.Id;
			this.Width = obj.Width;
			this.Height = obj.Height;
			this.Format = format.ToLowerInvariant();
			this.Data64Prefix = $"data:image/{this.Format};base64,";
			this.RawData64 = obj.GetBase64(format);
		}
	}
}
