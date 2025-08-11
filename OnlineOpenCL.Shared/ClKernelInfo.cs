using OnlineOpenCL.OpenCl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlineOpenCL.Shared
{
	public class ClKernelInfo
	{
		public string FilePath { get; set; } = string.Empty;
		public string FunctionName { get; set; } = string.Empty;
		public IEnumerable<string> ArgumentNames { get; set; } = [];
		public IEnumerable<string> ArgumentTypes { get; set; } = [];
	
		public int ArgumentsCount { get; set; } = 0;
		public int InputBuffersCount { get; set; } = 0;

		public bool Cached { get; set; } = false;
		public bool Loaded { get; set; } = false;

		public ClKernelInfo()
		{
			// Empty constructor for serialization
		}

		public ClKernelInfo(OpenClCompiler? compiler = null, string kernel = "")
		{
			if (compiler == null)
			{
				return;
			}

			if (string.IsNullOrEmpty(kernel))
			{
				kernel = compiler.KernelFile;
			}

			string? kernelFile = compiler.KernelFiles.FirstOrDefault(k => k.Contains(kernel, StringComparison.OrdinalIgnoreCase));
			if (string.IsNullOrEmpty(kernelFile))
			{
				return;
			}

			this.FilePath = kernelFile;
			this.FunctionName = compiler.GetKernelName(kernelFile) ?? "N/A";
			
			var arguments = compiler.GetKernelArguments(null, kernelFile);
			this.ArgumentNames = arguments.Keys;
			this.ArgumentTypes = arguments.Values.Select(t => t.Name);
			this.ArgumentsCount = this.ArgumentNames.Count() == this.ArgumentTypes.Count() ? this.ArgumentNames.Count() : -1;

			this.InputBuffersCount = this.ArgumentTypes.Where(t => t.Contains('*')).Count();

			this.Cached = false; // Placeholder for caching logic
			this.Loaded = compiler.KernelFile == kernelFile && !string.IsNullOrEmpty(this.FunctionName) && this.ArgumentsCount >= 0;
		}
	}
}
