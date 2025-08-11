using OpenTK.Compute.OpenCL;
using OpenTK.Mathematics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlineOpenCL.OpenCl
{
	public class OpenClCompiler
	{
		// ----- Services ----- \\
		private OpenClRegister register;

		// ----- Fields ----- \\
		private CLContext context;
		private CLDevice device;
		private CLCommandQueue queue => this.register.Queue;
		private CLKernel? kernel = null;
		private Dictionary<string, string> Files => this.GetKernelFiles();
		private Dictionary<string, Type> GetArguments(bool analog = false) => analog ? this.GetKernelArgumentsAnalog() : this.GetKernelArguments();
		private bool tryAnalog = false;



		// ----- Attributes ----- \\
		private int errorLogLength = 8192;
		public int ErrorLogLength
		{
			get => this.errorLogLength;
			set
			{
				// Erst den neuen Wert setzen
				this.errorLogLength = value;

				// Dann prüfen, ob die Log-Größe angepasst werden muss
				if (this.ErrorLog.Count > this.errorLogLength)
				{
					// Älteste Einträge entfernen (nach Schlüssel sortiert)
					var oldestKeys = this.ErrorLog.Keys
						.OrderBy(k => k)
						.Take(this.ErrorLog.Count - this.errorLogLength)
						.ToList();

					foreach (var key in oldestKeys)
					{
						this.ErrorLog.TryRemove(key, out _);
					}
				}
			}
		}
		private CLResultCode lastError = CLResultCode.Success;
		public CLResultCode LastError
		{
			get => this.lastError;
			set
			{
				this.lastError = value;
				if (value != CLResultCode.Success)
				{
					this.ErrorLog[DateTime.Now] = value;
					if (this.ErrorLog.Count > this.ErrorLogLength)
					{
						// Remove oldest error log entry if limit exceeded
						var oldestKey = this.ErrorLog.Keys.OrderBy(k => k).FirstOrDefault();
						if (oldestKey != default)
						{
							this.ErrorLog.TryRemove(oldestKey, out _);
						}
					}
				}
			}
		}

		public ConcurrentDictionary<DateTime, CLResultCode> ErrorLog { get; set; } = [];

		public string KernelPath { get; set; } = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "OnlineOpenCL.OpenCl", "Kernels"));
		public string KernelFile = string.Empty;
		public CLKernel? Kernel => this.kernel;

		public IEnumerable<string> KernelFiles => this.Files.Keys;
		public IEnumerable<string> KernelNames => this.Files.Values;

		public bool TryAnalog
		{
			get => this.tryAnalog;
			set
			{
				this.tryAnalog = value;
				if (value)
				{
					this.Arguments = this.GetArguments(true);
				}
				else
				{
					this.Arguments = this.GetArguments();
				}
			}
		}
		public Dictionary<string, Type> Arguments
		{
			get => this.GetArguments(this.tryAnalog);
			set
			{
				if (value == null || value.Count == 0)
				{
					return;
				}
			}
		}
		public IEnumerable<string> ArgumentNames => this.Arguments.Keys;
		public IEnumerable<string> ArgumentTypes => this.Arguments.Values.Select(t => t.Name);
		public int PointerInputCount => this.GetArgumentPointerCount();
		public string PointerInputType => this.GetKernelPointerInputType().Name;
		public string PointerOutputType => this.GetKernelPointerOutputType().Name;

		// ----- Constructor ----- \\
		public OpenClCompiler(CLContext context, CLDevice device, OpenClRegister register)
		{
			this.context = context;
			this.device = device;
			this.register = register;

			if (!Directory.Exists(this.KernelPath))
			{
				// When in Production environment, Kernels are at root dir of assembly
				this.KernelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kernels");
			}
		}

		// ----- Methods ----- \\
		public void Dispose()
		{
			// Free kernels

		}

		private Dictionary<string, string> GetKernelFiles()
		{
			string dir = Path.GetFullPath(this.KernelPath);

			// Build dir if it doesn't exist
			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			// Get all .cl files in the directory
			string[] files = Directory.GetFiles(dir, "*.cl", SearchOption.AllDirectories);

			// Check if any files were found
			if (files.LongLength <= 0)
			{
				return [];
			}

			// Verify each file
			Dictionary<string, string> verifiedFiles = [];
			foreach (string file in files)
			{
				string? verifiedFile = this.VerifyKernelFile(file);
				if (verifiedFile != null)
				{
					string? name = this.GetKernelName(verifiedFile);
					verifiedFiles.Add(verifiedFile, name ?? "N/A");
				}
			}

			// Return
			return verifiedFiles;
		}

		public string? VerifyKernelFile(string filePath)
		{
			// Check if file exists & is .cl
			if (!File.Exists(filePath))
			{
				return null;
			}

			if (Path.GetExtension(filePath) != ".cl")
			{
				return null;
			}

			// Check if file is empty
			string[] lines = File.ReadAllLines(filePath);
			if (lines.Length == 0)
			{
				return null;
			}

			// Check if file contains kernel function
			if (!lines.Any(line => line.Contains("__kernel")))
			{
				return null;
			}

			return Path.GetFullPath(filePath);
		}

		public string? GetKernelName(string filePath)
		{
			// Verify file
			string? verifiedFilePath = this.VerifyKernelFile(filePath);
			if (verifiedFilePath == null)
			{
				return null;
			}

			// Try to extract function name from kernel code text
			string code = File.ReadAllText(filePath);

			// Find index of first "__kernel void "
			int index = code.IndexOf("__kernel void ");
			if (index == -1)
			{
				return null;
			}

			// Find index of first "(" after "__kernel void "
			int startIndex = index + "__kernel void ".Length;
			int endIndex = code.IndexOf("(", startIndex);
			if (endIndex == -1)
			{
				return null;
			}

			// Extract function name
			string functionName = code.Substring(startIndex, endIndex - startIndex).Trim();
			if (functionName.Contains(" ") || functionName.Contains("\t") ||
				functionName.Contains("\n") || functionName.Contains("\r"))
			{
				return null;
			}

			// Check if function name is empty
			if (string.IsNullOrEmpty(functionName))
			{
				return null;
			}

			// Compare to file name without ext
			string fileName = Path.GetFileNameWithoutExtension(filePath);
			if (string.Compare(functionName, fileName, StringComparison.OrdinalIgnoreCase) != 0)
			{
				Console.WriteLine("Kernel function name does not match file name: " + filePath);
			}

			return functionName;
		}

		// Compile
		private CLKernel? CompileFile(string filePath)
		{
			// Verify file
			string? verifiedFilePath = this.VerifyKernelFile(filePath);
			if (verifiedFilePath == null)
			{
				return null;
			}

			// Get kernel name
			string? kernelName = this.GetKernelName(verifiedFilePath);
			if (kernelName == null)
			{
				return null;
			}

			// Read kernel code
			string code = File.ReadAllText(verifiedFilePath);

			// Create program
			CLProgram program = CL.CreateProgramWithSource(this.context, code, out CLResultCode error);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return null;
			}

			// Create callback
			CL.ClEventCallback callback = new((program, userData) =>
			{
				// Check build log
				//
			});

			// When building the kernel
			string buildOptions = "-cl-std=CL1.2 -cl-fast-relaxed-math";
			CL.BuildProgram(program, 1, [this.device], buildOptions, 0, IntPtr.Zero);

			// Build program
			error = CL.BuildProgram(program, [this.device], buildOptions, callback);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;

				// Get build log
				error = CL.GetProgramBuildInfo(program, this.device, ProgramBuildInfo.Log, out byte[] buildLog);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
				}
				else
				{
					string log = Encoding.UTF8.GetString(buildLog);
					Console.WriteLine("Build log: " + log);
				}

				error = CL.ReleaseProgram(program);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
				}

				return null;
			}

			// Create kernel
			CLKernel kernel = CL.CreateKernel(program, kernelName, out error);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				Console.WriteLine("Error creating kernel: " + error.ToString());

				// Get build log
				error = CL.GetProgramBuildInfo(program, this.device, ProgramBuildInfo.Log, out byte[] buildLog);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					Console.WriteLine("Error getting build log: " + error.ToString());
				}
				else
				{
					string log = Encoding.UTF8.GetString(buildLog);
					Console.WriteLine("Build log: " + log);
				}

				CL.ReleaseProgram(program);
				return null;
			}

			// Return kernel
			return kernel;
		}

		public Dictionary<string, Type> GetKernelArguments(CLKernel? kernel = null, string filePath = "")
		{
			Dictionary<string, Type> arguments = [];

			// Verify kernel
			kernel ??= this.kernel;
			if (kernel == null)
			{
				// Try get kernel by file path
				kernel = this.CompileFile(filePath);
				if (kernel == null)
				{
					return arguments;
				}
			}

			// Get kernel info
			CLResultCode error = CL.GetKernelInfo(kernel.Value, KernelInfo.NumberOfArguments, out byte[] argCountBytes);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return arguments;
			}

			// Get number of arguments
			int argCount = BitConverter.ToInt32(argCountBytes, 0);

			// Loop through arguments
			for (int i = 0; i < argCount; i++)
			{
				// Get argument info type name
				error = CL.GetKernelArgInfo(kernel.Value, (uint) i, KernelArgInfo.TypeName, out byte[] argTypeBytes);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					continue;
				}

				// Get argument info arg name
				error = CL.GetKernelArgInfo(kernel.Value, (uint) i, KernelArgInfo.Name, out byte[] argNameBytes);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					continue;
				}

				// Get argument type & name
				string argName = Encoding.UTF8.GetString(argNameBytes).TrimEnd('\0');
				string typeName = Encoding.UTF8.GetString(argTypeBytes).TrimEnd('\0');
				Type? type = null;

				// Switch for typeName
				if (typeName.EndsWith("*"))
				{
					typeName = typeName.Replace("*", "").ToLower();
					switch (typeName)
					{
						case "int":
							type = typeof(int*);
							break;
						case "float":
							type = typeof(float*);
							break;
						case "long":
							type = typeof(long*);
							break;
						case "uchar":
							type = typeof(byte*);
							break;
						case "vector2":
							type = typeof(Vector2*);
							break;
						default:
							break;
					}
				}
				else
				{
					switch (typeName)
					{
						case "int":
							type = typeof(int);
							break;
						case "float":
							type = typeof(float);
							break;
						case "double":
							type = typeof(double);
							break;
						case "char":
							type = typeof(char);
							break;
						case "uchar":
							type = typeof(byte);
							break;
						case "short":
							type = typeof(short);
							break;
						case "ushort":
							type = typeof(ushort);
							break;
						case "long":
							type = typeof(long);
							break;
						case "ulong":
							type = typeof(ulong);
							break;
						case "vector2":
							type = typeof(Vector2);
							break;
						default:
							break;
					}
				}

				// Add to dictionary
				arguments.Add(argName, type ?? typeof(object));
			}

			// Return arguments
			return arguments;
		}

		public Dictionary<string, Type> GetKernelArgumentsAnalog(string? filepath = null)
		{
			Dictionary<string, Type> arguments = [];
			if (string.IsNullOrEmpty(filepath))
			{
				filepath = this.KernelFile;
			}

			// Read kernel code
			filepath = this.VerifyKernelFile(filepath ?? "");
			if (filepath == null)
			{
				return arguments;
			}

			string code = File.ReadAllText(filepath);
			if (string.IsNullOrEmpty(code))
			{
				return arguments;
			}

			// Find kernel function
			int index = code.IndexOf("__kernel void ");
			if (index == -1)
			{
				return arguments;
			}
			int startIndex = index + "__kernel void ".Length;
			int endIndex = code.IndexOf("(", startIndex);
			if (endIndex == -1)
			{
				return arguments;
			}

			string functionName = code.Substring(startIndex, endIndex - startIndex).Trim();
			if (string.IsNullOrEmpty(functionName))
			{
				return arguments;
			}

			if (functionName.Contains(" ") || functionName.Contains("\t") ||
				functionName.Contains("\n") || functionName.Contains("\r"))
			{
				return arguments;
			}

			// Get arguments string
			int argsStartIndex = code.IndexOf("(", endIndex) + 1;
			int argsEndIndex = code.IndexOf(")", argsStartIndex);
			if (argsEndIndex == -1)
			{
				return arguments;
			}
			string argsString = code.Substring(argsStartIndex, argsEndIndex - argsStartIndex).Trim();
			if (string.IsNullOrEmpty(argsString))
			{
				return arguments;
			}

			string[] args = argsString.Split(',');

			foreach (string arg in args)
			{
				string[] parts = arg.Trim().Split(' ');
				if (parts.Length < 2)
				{
					continue;
				}
				string typeName = parts[^2].Trim();
				string argName = parts[^1].Trim().TrimEnd(';', ')', '\n', '\r', '\t');
				Type? type = null;
				if (typeName.EndsWith("*"))
				{
					typeName = typeName.Replace("*", "");
					switch (typeName)
					{
						case "int":
							type = typeof(int*);
							break;
						case "float":
							type = typeof(float*);
							break;
						case "long":
							type = typeof(long*);
							break;
						case "uchar":
							type = typeof(byte*);
							break;
						case "Vector2":
							type = typeof(Vector2*);
							break;
						default:
							break;
					}
				}
				else
				{
					switch (typeName)
					{
						case "int":
							type = typeof(int);
							break;
						case "float":
							type = typeof(float);
							break;
						case "double":
							type = typeof(double);
							break;
						case "char":
							type = typeof(char);
							break;
						case "uchar":
							type = typeof(byte);
							break;
						case "short":
							type = typeof(short);
							break;
						case "ushort":
							type = typeof(ushort);
							break;
						case "long":
							type = typeof(long);
							break;
						case "ulong":
							type = typeof(ulong);
							break;
						case "Vector2":
							type = typeof(Vector2);
							break;
						default:
							break;
					}
				}
				if (type != null)
				{
					arguments.Add(argName, type ?? typeof(object));
				}
			}

			return arguments;
		}

		private int GetArgumentPointerCount()
		{
			// Get kernel argument types
			Type[] argTypes = this.Arguments.Values.ToArray();

			// Count pointer arguments
			int count = 0;
			foreach (Type type in argTypes)
			{
				if (type.Name.EndsWith("*"))
				{
					count++;
				}
			}

			return count;
		}

		private Type GetKernelPointerInputType()
		{
			// Get kernel argument types
			Type[] argTypes = this.Arguments.Values.ToArray();

			// Find first pointer type
			foreach (Type type in argTypes)
			{
				if (type.Name.EndsWith("*"))
				{
					return type;
				}
			}

			// If no pointer type found, return object
			return typeof(object);
		}

		private Type GetKernelPointerOutputType()
		{
			// Get kernel argument types
			Type[] argTypes = this.Arguments.Values.ToArray();
			// Find last pointer type
			for (int i = argTypes.Length - 1; i >= 0; i--)
			{
				if (argTypes[i].Name.EndsWith("*"))
				{
					return argTypes[i];
				}
			}

			// If no pointer type found, return object
			return typeof(object);
		}

		// Load
		private CLKernel? Load(string kernelName = "", string filePath = "")
		{
			// Get kernel file path
			if (!string.IsNullOrEmpty(filePath))
			{
				kernelName = Path.GetFileNameWithoutExtension(filePath);
			}
			else
			{
				filePath = Directory.GetFiles(Path.Combine(this.KernelPath), kernelName + "*.cl", SearchOption.AllDirectories).Where(f => Path.GetFileNameWithoutExtension(f).Length == kernelName.Length).FirstOrDefault() ?? "";
			}

			// Compile kernel if not cached
			if (this.kernel != null && this.KernelFile == filePath)
			{
				return this.kernel;
			}

			CLKernel? kernel = this.kernel = this.CompileFile(filePath);
			this.KernelFile = filePath;

			// Check if kernel is null
			if (this.kernel == null)
			{
				return null;
			}
			else
			{
				// String of args like "(byte*)'pixels', (int)'width', (int)'height'"
				string argNamesString = string.Join(", ", this.Arguments.Keys.Select((arg, i) => $"({this.Arguments.Values.ElementAt(i).Name}) '{arg}'"));
				// this.Log("Kernel arguments: [" + argNamesString + "]", "", 1);
			}

			// TryAdd to cached
			// this.kernelCache.TryAdd(this.kernel.Value, filePath);

			return kernel;
		}

		public string LoadKernel(string kernelName = "", string filePath = "")
		{
			// Load kernel
			CLKernel? kernel = this.Load(kernelName, filePath);
			if (kernel == null)
			{
				return string.Empty;
			}

			// Return kernel name
			return this.GetKernelName(filePath) ?? kernelName ?? string.Empty;
		}

		public bool UnloadKernel()
		{
			// Release kernel
			if (this.kernel != null)
			{
				CLResultCode error = CL.ReleaseKernel(this.kernel.Value);
				this.kernel = null;
				this.KernelFile = string.Empty;
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return false;
				}
			}
			else
			{
				return true;
			}

			return this.kernel == null;
		}

		public object[] GetArgumentDefaultValues()
		{
			// Get kernel arguments
			Dictionary<string, Type> arguments = this.Arguments;

			// Create array of argument values
			object[] argValues = new object[arguments.Count];

			// Loop through arguments and set values
			int i = 0;
			foreach (var arg in arguments)
			{
				Type type = arg.Value;
				if (type.IsPointer)
				{
					argValues[i] = IntPtr.Zero;
				}
				else if (type == typeof(int))
				{
					argValues[i] = 0;
				}
				else if (type == typeof(float))
				{
					argValues[i] = 0f;
				}
				else if (type == typeof(double))
				{
					argValues[i] = 0d;
				}
				else if (type == typeof(byte))
				{
					argValues[i] = (byte) 0;
				}
				else if (type == typeof(Vector2))
				{
					argValues[i] = Vector2.Zero;
				}
				else
				{
					argValues[i] = 0;
				}
				i++;
			}
			return argValues;
		}

	}
}
