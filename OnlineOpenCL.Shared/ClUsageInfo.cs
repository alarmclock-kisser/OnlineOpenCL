using OnlineOpenCL.OpenCl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OnlineOpenCL.Shared
{
	public class ClUsageInfo
	{
		public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

		public string TotalMemory { get; set; } = "0";
		public string UsedMemory { get; set; } = "0";
		public string AvailableMemory { get; set; } = "0";
		public string AllocatedBuffers { get; set; } = "0";
		public string SizeUnit { get; set; } = "MB";

		public IEnumerable<float> SizesMb { get; set; } = [0.0f, 0.0f, 0.0f];
		public float Utilization { get; set; } = 0.0f;

		// private dict for sizes and their quotients
		private static readonly Dictionary<string, double> SizeQuotients = new()
		{
			{ "B", 1 },
			{ "KB", 1024 },
			{ "MB", 1024 * 1024 },
			{ "GB", 1024 * 1024 * 1024 },
			{ "TB", 1024L * 1024 * 1024 * 1024 }
		};

		public ClUsageInfo()
		{
			// Empty constructor for serialization
		}

		[JsonConstructor]
		public ClUsageInfo(OpenClService? service = null, string size = "MB", int decimals = 2)
		{
			if (service == null)
			{
				return;
			}

			// Verify the size unit is valid
			size = size.ToUpperInvariant();
			if (!SizeQuotients.ContainsKey(size))
			{
				size = "MB";
			}
			long quotient = (long)SizeQuotients[size];
			string formatString = "0." + new string('0', decimals);

			// Get sizes in bytes
			decimal totalMemory = service.GetTotalMemory();
			decimal usedMemory = service.GetUsedMemory();
			decimal availableMemory = service.GetAvailableMemory();
			long allocatedBuffers = service.GetAllocatedBuffers();

			// Set properties
			this.TotalMemory = (totalMemory / quotient).ToString(formatString);
			this.UsedMemory = (usedMemory / quotient).ToString(formatString);
			this.AvailableMemory = (availableMemory / quotient).ToString(formatString);
			this.AllocatedBuffers = allocatedBuffers.ToString("N0");
			this.SizeUnit = size;
			this.SizesMb =
			[
				(float) Math.Round(totalMemory / (1024 * 1024), decimals),
				(float) Math.Round(usedMemory / (1024 * 1024), decimals),
				(float) Math.Round(availableMemory / (1024 * 1024), decimals)
			];

			// Calculate utilization
			this.Utilization = (float) Math.Round(service.GetMemoryUtilization(), decimals);
		}
	}
}
