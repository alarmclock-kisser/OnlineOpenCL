using OnlineOpenCL.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OnlineOpenCL.Shared
{
	public class ImageObjInfo
	{
		public Guid Id { get; set; } = Guid.Empty;
		public DateTime InfoCreated { get; set; } = DateTime.Now;
		public DateTime ObjectCreated { get; set; } = DateTime.Now;
		public int Number { get; set; } = 0;
		public string Name { get; set; } = string.Empty;
		public int Width { get; set; } = 0;
		public int Height { get; set; } = 0;
		public int BitDepth { get; set; } = 0;
		public int Channels { get; set; } = 0;
		public bool OnHost { get; set; } = false;
		public string Location { get; set; } = "N/A";
		public string Pointer { get; set; } = "0";
		public float SizeInMb { get; set; } = 0.0f;
		public float ElapsedProcessing { get; set; } = 0.0f;

		public string ErrorMessage { get; set; } = string.Empty;

		public ImageObjInfo()
		{
			// Empty ctor
		}

		[JsonConstructor]
		public ImageObjInfo(ImageObj? obj = null)
		{
			if (obj == null)
			{
				return;
			}

			this.Id = obj.Id;
			this.ObjectCreated = obj.Created;
			this.Number = obj.Number;
			this.Name = obj.Name;
			this.Width = obj.Width;
			this.Height = obj.Height;
			this.BitDepth = obj.BitDepth;
			this.Channels = obj.Channels;
			this.OnHost = obj.Pointer == IntPtr.Zero;
			this.Location = obj.Pointer != IntPtr.Zero ? "Device" : "Host";
			this.Pointer = obj.Pointer.ToString();
			this.SizeInMb = (float)(obj.SizeInBytes / (1024.0 * 1024.0));
			this.ElapsedProcessing = (float)obj.ElapsedProcessingTime;
		}
	}
}
