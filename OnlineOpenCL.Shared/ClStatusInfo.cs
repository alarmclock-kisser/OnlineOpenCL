using OnlineOpenCL.OpenCl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlineOpenCL.Shared
{
	public class ClStatusInfo
	{
		public int Index { get; set; } = -1;
		public int DeviceCount { get; set; } = 0;
		public string Device { get; set; } = string.Empty;
		public string Platform { get; set; } = string.Empty;
		public string Type { get; set; } = string.Empty;
		public bool Initialized { get; set; } = false;
		public string Status { get; set; } = string.Empty;
		public string LastError { get; set; } = string.Empty;

		public string InitializedAt { get; set; } = "01.01.1970 00:00:00.000";
		public string UpTime { get; set; } = "0 days, 00:00:00.000";

		public ClStatusInfo()
		{
			// Empty constructor for serialization
		}

		public ClStatusInfo(OpenClService? service = null)
		{
			if (service == null)
			{
				return;
			}

			this.Index = service.Index;
			this.DeviceCount = service.DeviceCount;
			this.Device = service.GetDeviceInfo() ?? "N/A";
			this.Platform = service.GetPlatformInfo() ?? "N/A";
			this.Type = service.GetDeviceType() ?? "N/A";
			this.Initialized = service.Initialized;
			this.Status = service.Initialized ? "Online" : "Disposed";
			this.LastError = service.LastError == OpenTK.Compute.OpenCL.CLResultCode.Success ? "" : service.LastError.ToString();

			this.InitializedAt = service.InitializedAt.HasValue ? service.InitializedAt.Value.ToString("dd.MM.yyyy HH:mm:ss.fff") : "Not initialized yet";
			this.UpTime = service.UpTime.Days + " days, " + service.UpTime.ToString(@"hh\:mm\:ss\.fff");
		}
	}
}
