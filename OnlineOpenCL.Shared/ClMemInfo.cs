using OnlineOpenCL.OpenCl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OnlineOpenCL.Shared
{
	public class ClMemInfo
	{
		public Guid Id { get; set; } = Guid.Empty;
		public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		public string ObjectCreatedAt { get; set; } = "01.01.1970 00:00:00.000";
		public string TickAge { get; set; } = "0";
		public string Status { get; set; } = "N/A";
		public string Type { get; set; } = "void";
		public int TypeSize { get; set; } = 0;
		public string Count { get; set; } = "0";
		public int CountInt { get; set; } = 0;
		public string IndexHandle { get; set; } = "0";
		public string IndexLength { get; set; } = "0";
		public string SizeInBytes { get; set; } = "0";

		public IEnumerable<string> Pointers { get; set; } = [];
		public IEnumerable<string> Lengths { get; set; } = [];


		public ClMemInfo()
		{
			// Empty constructor for serialization
		}

		[JsonConstructor]
		public ClMemInfo(OpenClMem? mem = null)
		{
			if (mem == null)
			{
				return;
			}

			this.Id = mem.Id;
			this.ObjectCreatedAt = mem.Timestamp;
			this.TickAge = mem.TickAge;
			this.Status = mem.Status;
			this.Type = mem.Type;
			this.TypeSize = mem.TypeSize;
			this.Count = mem.Count;
			this.CountInt = mem.GetCount();
			this.IndexHandle = mem.IndexHandle;
			this.IndexLength = mem.IndexLength;
			this.SizeInBytes = mem.Size;

			this.Pointers = mem.Pointers;
			this.Lengths = mem.Lengths;
		}
	}
}
