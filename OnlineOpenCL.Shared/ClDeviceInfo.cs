using OnlineOpenCL.OpenCl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OnlineOpenCL.Shared
{
	public class ClDeviceInfo
	{
		public int Index { get; set; } = -1;
		public string Name { get; set; } = string.Empty;
		public string PlatformName { get; set; } = string.Empty;
		public string Version { get; set; } = string.Empty;
		public string MemorySize { get; set; } = string.Empty;
		public int ComputeUnits { get; set; } = 0;

		public ClDeviceInfo()
		{
			// Empty constructor for serialization
		}

		[JsonConstructor]
		public ClDeviceInfo(OpenClService? service = null, int index = -1)
		{
			this.Index = index;

			if (service == null)
			{
				return;
			}

			if (index < 0 || index >= service.DeviceCount)
			{
				return;
			}

			this.Name = service.GetDeviceInfo(index, OpenTK.Compute.OpenCL.DeviceInfo.Name) ?? "N/A";
			this.PlatformName = service.GetDeviceInfo(index, OpenTK.Compute.OpenCL.DeviceInfo.Platform) ?? "N/A";
			this.Version = service.GetDeviceInfo(index, OpenTK.Compute.OpenCL.DeviceInfo.Version) ?? "N/A";
			this.MemorySize = service.GetDeviceInfo(index, OpenTK.Compute.OpenCL.DeviceInfo.GlobalMemorySize) ?? "N/A";
			if (int.TryParse(service.GetDeviceInfo(index, OpenTK.Compute.OpenCL.DeviceInfo.MaximumComputeUnits) ?? "0", out int maxCu))
			{
				this.ComputeUnits = maxCu;
			}
			else
			{
				this.ComputeUnits = -1;
			}
		}
	}
}
