using OnlineOpenCL.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OnlineOpenCL.Shared
{
	public class AudioData64
	{
		public Guid Id { get; set; } = Guid.NewGuid();
		public Guid ObjectId { get; set; } = Guid.Empty;
		public DateTime Created { get; set; } = DateTime.Now;

		public int Width { get; set; } = 0;
		public int Height { get; set; } = 0;
		public string Format { get; set; } = "N/A";

		public string RawData64 { get; set; } = string.Empty;
		public string Data64Prefix { get; set; } = string.Empty;

		public string Data64
		{
			get => this.Data64Prefix + this.RawData64;
			set => this.RawData64 = value;
		}

		public string Length
		{
			get => this.RawData64.Length.ToString();
			set { /* Intentionally left empty */ }
		}

		private static readonly String[] allowedFormats = ["bmp", "png", "jpg"];

		public AudioData64()
		{
			// Empty ctor
		}

		[JsonConstructor]
		public AudioData64(AudioObj? obj = null, string format = "png")
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
			this.Width = obj.WaveformSize.Width;
			this.Height = obj.WaveformSize.Height;
			this.Format = format.ToLowerInvariant();
			this.RawData64 = obj.GetBase64(format);
			this.Data64Prefix = $"data:image/{this.Format};base64,";
		}
	}
}
