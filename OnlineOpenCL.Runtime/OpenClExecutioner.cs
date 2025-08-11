using OpenTK.Compute.OpenCL;
using OpenTK.Mathematics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace OnlineOpenCL.OpenCl
{
	public class OpenClExecutioner
	{
		// ----- Services ----- \\
		private OpenClRegister register;

		// ----- Fields ----- \\
		private CLContext context;
		private CLDevice device;
		private CLCommandQueue queue => this.register.Queue;
		private OpenClCompiler compiler;

		private CLKernel? Kernel => this.compiler.Kernel;
		private string KernelFile => this.compiler.KernelFile;

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

		public ConcurrentDictionary<DateTime, CLResultCode> ErrorLog = [];




		// ----- Constructor ----- \\
		public OpenClExecutioner(CLContext context, CLDevice device, OpenClRegister register, OpenClCompiler compiler)
		{
			this.context = context;
			this.device = device;
			this.register = register;
			this.compiler = compiler;
		}

		// ----- Methods ----- \\
		public void Dispose()
		{
			// Free

		}

		// Exec
		public IntPtr ExecuteFFT(IntPtr pointer, string version = "01", char form = 'f', int chunkSize = 16384, float overlap = 0.5f, bool free = true)
		{
			int overlapSize = (int) (overlap * chunkSize);

			string kernelsPath = Path.Combine(this.compiler.KernelPath, "Audio");
			string file = "";
			if (form == 'f')
			{
				file = Path.Combine(kernelsPath, $"fft{version}.cl");
			}
			else if (form == 'c')
			{
				file = Path.Combine(kernelsPath, $"ifft{version}.cl");
			}

			// Load kernel from file, else abort
			this.compiler.LoadKernel("", file);
			if (this.Kernel == null)
			{
				return pointer;
			}

			// Get input buffers
			OpenClMem? inputBuffers = this.register.GetBuffer(pointer);
			if (inputBuffers == null || inputBuffers.GetCount() <= 0)
			{
				return pointer;
			}

			// Get output buffers
			OpenClMem? outputBuffers = null;
			if (form == 'f')
			{
				outputBuffers = this.register.AllocateGroup<Vector2>(inputBuffers.GetCount(), (nint) inputBuffers.GetLengths().FirstOrDefault());
			}
			else if (form == 'c')
			{
				outputBuffers = this.register.AllocateGroup<float>(inputBuffers.GetCount(), (nint) inputBuffers.GetLengths().FirstOrDefault());
			}
			if (outputBuffers == null || outputBuffers.GetCount() <= 0 || outputBuffers.GetLengths().Any(l => l < 1))
			{
				return pointer;
			}


			// Set static args
			CLResultCode error = this.SetKernelArgSafe(2, (int) inputBuffers.GetLengths().FirstOrDefault());
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return pointer;
			}
			error = this.SetKernelArgSafe(3, overlapSize);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return pointer;
			}

			// Calculate optimal work group size
			uint maxWorkGroupSize = this.GetMaxWorkGroupSize();
			uint globalWorkSize = 1;
			uint localWorkSize = 1;


			// Loop through input buffers
			int count = inputBuffers.GetCount();
			for (int i = 0; i < count; i++)
			{
				error = this.SetKernelArgSafe(0, inputBuffers[i]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return pointer;
				}
				error = this.SetKernelArgSafe(1, outputBuffers[i]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return pointer;
				}

				// Execute kernel
				error = CL.EnqueueNDRangeKernel(this.queue, this.Kernel.Value, 1, null, [(UIntPtr) globalWorkSize], [(UIntPtr) localWorkSize], 0, null, out CLEvent evt);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return pointer;
				}

				// Wait for completion
				error = CL.WaitForEvents(1, [evt]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
				}

				// Release event
				CL.ReleaseEvent(evt);
			}

			if (outputBuffers != null && free)
			{
				this.register.FreeBuffer(pointer);
			}

			if (outputBuffers == null)
			{
				return pointer;
			}

			return outputBuffers[0].Handle;
		}

		public async Task<IntPtr> ExecuteFFTAsync(IntPtr pointer, string version = "01", char form = 'f', int chunkSize = 16384, float overlap = 0.5f, bool free = true, IProgress<int>? progress = null)
		{
			// Die gesamte blockierende Logik wird in einem Hintergrund-Task ausgeführt.
			return await Task.Run(() =>
			{
				int overlapSize = (int) (overlap * chunkSize);

				string kernelsPath = Path.Combine(this.compiler.KernelPath, "Audio");
				string file = "";
				if (form == 'f')
				{
					file = Path.Combine(kernelsPath, $"fft{version}.cl");
				}
				else if (form == 'c')
				{
					file = Path.Combine(kernelsPath, $"ifft{version}.cl");
				}

				// Load kernel from file, else abort
				this.compiler.LoadKernel("", file);
				if (this.Kernel == null)
				{
					return pointer;
				}

				// Get input buffers
				OpenClMem? inputBuffers = this.register.GetBuffer(pointer);
				if (inputBuffers == null || inputBuffers.GetCount() <= 0)
				{
					return pointer;
				}

				// Get output buffers
				OpenClMem? outputBuffers = null;
				if (form == 'f')
				{
					outputBuffers = this.register.AllocateGroup<Vector2>(inputBuffers.GetCount(), (nint) inputBuffers.GetLengths().FirstOrDefault());
				}
				else if (form == 'c')
				{
					outputBuffers = this.register.AllocateGroup<float>(inputBuffers.GetCount(), (nint) inputBuffers.GetLengths().FirstOrDefault());
				}
				if (outputBuffers == null || outputBuffers.GetCount() <= 0 || outputBuffers.GetLengths().Any(l => l < 1))
				{
					return pointer;
				}

				// Set static args
				CLResultCode error = this.SetKernelArgSafe(2, (int) inputBuffers.GetLengths().FirstOrDefault());
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return pointer;
				}
				error = this.SetKernelArgSafe(3, overlapSize);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return pointer;
				}

				// Calculate optimal work group size
				uint maxWorkGroupSize = this.GetMaxWorkGroupSize();
				uint globalWorkSize = 1;
				uint localWorkSize = 1;

				// Loop through input buffers
				int count = inputBuffers.GetCount();
				for (int i = 0; i < count; i++)
				{
					error = this.SetKernelArgSafe(0, inputBuffers[i]);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return pointer;
					}
					error = this.SetKernelArgSafe(1, outputBuffers[i]);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return pointer;
					}

					// Execute kernel
					error = CL.EnqueueNDRangeKernel(this.queue, this.Kernel.Value, 1, null, [(UIntPtr) globalWorkSize], [(UIntPtr) localWorkSize], 0, null, out CLEvent evt);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return pointer;
					}

					// Wait for completion
					error = CL.WaitForEvents(1, [evt]);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
					}

					// Release event
					CL.ReleaseEvent(evt);

					// Update progress inkrementell
					progress?.Report(1);
				}

				if (outputBuffers != null && free)
				{
					this.register.FreeBuffer(pointer);
				}

				if (outputBuffers == null)
				{
					return pointer;
				}

				return outputBuffers[0].Handle;
			});
		}

		public IntPtr ExecuteAudioKernel(IntPtr objPointer, out double factor, long length = 0, string kernelName = "normalize", string version = "00", int chunkSize = 1024, float overlap = 0.5f, int samplerate = 44100, int bitdepth = 24, int channels = 2, Dictionary<string, object>? optionalArguments = null)
		{
			factor = 1.000d; // Default factor

			// Get kernel path
			string kernelPath = this.compiler.KernelFiles.FirstOrDefault(f => f.ToLower().Contains((kernelName + version).ToLower())) ?? "";
			if (string.IsNullOrEmpty(kernelPath))
			{
				return IntPtr.Zero;
			}

			// Load kernel if not loaded
			if (this.Kernel == null || this.KernelFile != kernelPath)
			{
				this.compiler.LoadKernel("", kernelPath);
				if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Audio\\"))
				{
					return IntPtr.Zero;
				}
			}

			// Get input buffers
			OpenClMem? inputMem = this.register.GetBuffer(objPointer);
			if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
			{
				return IntPtr.Zero;
			}

			// Get variable arguments
			object[] variableArguments = this.compiler.GetArgumentDefaultValues();

			// Check if FFT is needed
			bool didFft = false;
			if (this.compiler.PointerInputType.Contains("Vector2") && inputMem.Type == typeof(float).Name)
			{
				if (optionalArguments != null && optionalArguments.ContainsKey("factor"))
				{
					// Set factor to optional argument if provided (contains "stretch") can be float or double depending on the kernel
					if (optionalArguments["factor"] is double dFactor)
					{
						factor = dFactor;
					}
					else if (optionalArguments["factor"] is float fFactor)
					{
						factor = fFactor;
					}
					else
					{
						return IntPtr.Zero;
					}
				}
				else
				{
					factor = 1.000d;
				}

				IntPtr fftPointer = this.ExecuteFFT(objPointer, "01", 'f', chunkSize, overlap, true);
				if (fftPointer == IntPtr.Zero)
				{
					return IntPtr.Zero;
				}

				objPointer = fftPointer;
				didFft = true;

				// Load kernel if not loaded
				if (this.Kernel == null || this.KernelFile != kernelPath)
				{
					this.compiler.LoadKernel("", kernelPath);
					if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Audio\\"))
					{
						return IntPtr.Zero;
					}
				}
			}

			// Get input buffers
			inputMem = this.register.GetBuffer(objPointer);
			if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
			{
				return IntPtr.Zero;
			}

			// Get output buffers
			OpenClMem? outputMem = null;
			if (this.compiler.PointerInputType == typeof(float*).Name)
			{
				outputMem = this.register.AllocateGroup<float>(inputMem.GetCount(), (nint) inputMem.GetLengths().FirstOrDefault());
			}
			else if (this.compiler.PointerOutputType == typeof(Vector2*).Name)
			{
				outputMem = this.register.AllocateGroup<Vector2>(inputMem.GetCount(), (nint) inputMem.GetLengths().FirstOrDefault());
			}
			else
			{
				return IntPtr.Zero;
			}

			if (outputMem == null || outputMem.GetCount() == 0 || outputMem.GetLengths().Any(l => l < 1))
			{
				return IntPtr.Zero;
			}

			// Loop through input buffers
			int count = inputMem.GetCount();
			for (int i = 0; i < count; i++)
			{
				// Get buffers
				CLBuffer inputBuffer = inputMem[i];
				CLBuffer outputBuffer = outputMem[i];

				// Merge arguments
				List<object> arguments = this.MergeArgumentsAudio(variableArguments, inputBuffer, outputBuffer, length, chunkSize, overlap, samplerate, bitdepth, channels, optionalArguments);
				if (arguments == null || arguments.Count == 0)
				{
					return IntPtr.Zero;
				}

				// Set kernel arguments
				CLResultCode error = CLResultCode.Success;
				for (uint j = 0; j < arguments.Count; j++)
				{
					error = this.SetKernelArgSafe(j, arguments[(int) j]);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return IntPtr.Zero;
					}
				}

				// Get work dimensions
				uint maxWorkGroupSize = this.GetMaxWorkGroupSize();
				uint globalWorkSize = (uint) inputMem.GetLengths()[i];
				uint localWorkSize = Math.Min(maxWorkGroupSize, globalWorkSize);
				if (localWorkSize == 0)
				{
					localWorkSize = 1; // Fallback to 1 if no valid local size
				}
				if (globalWorkSize < localWorkSize)
				{
					globalWorkSize = localWorkSize; // Ensure global size is at least local size
				}

				// Execute kernel
				error = CL.EnqueueNDRangeKernel(this.queue, this.Kernel.Value, 1, null, [(UIntPtr) globalWorkSize], [(UIntPtr) localWorkSize], 0, null, out CLEvent evt);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return IntPtr.Zero;
				}

				// Wait for completion
				error = CL.WaitForEvents(1, [evt]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return objPointer;
				}

				// Release event
				error = CL.ReleaseEvent(evt);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
				}
			}

			// Free input buffer if necessary
			if (outputMem[0].Handle != IntPtr.Zero)
			{
				long freed = this.register.FreeBuffer(objPointer, true);
				if (freed > 0)
				{
					Console.WriteLine("Freed: " + freed + " MB");
				}
			}

			// Optionally execute IFFT if FFT was done
			IntPtr outputPointer = outputMem[0].Handle;
			if (didFft && outputMem.Type == typeof(Vector2).Name)
			{
				IntPtr ifftPointer = this.ExecuteFFT(outputMem[0].Handle, "01", 'c', chunkSize, overlap, true);
				if (ifftPointer == IntPtr.Zero)
				{
					return IntPtr.Zero;
				}

				outputPointer = ifftPointer; // Update output pointer to IFFT result
			}

			// Return output buffer handle if available, else return original pointer
			return outputPointer != IntPtr.Zero ? outputPointer : objPointer;
		}

		public async Task<(IntPtr Pointer, double Factor)> ExecuteAudioKernelAsync(IntPtr objPointer, long length = 0, string kernelName = "normalize", string version = "00", int chunkSize = 1024, float overlap = 0.5f, int samplerate = 44100, int bitdepth = 24, int channels = 2, Dictionary<string, object>? optionalArguments = null, IProgress<int>? progress = null)
		{
			return await Task.Run(async () =>
			{
				double factor = 1.000d; // Initialisiere den Faktor

				// Lade Kernel-Pfad
				string kernelPath = this.compiler.KernelFiles.FirstOrDefault(f => f.ToLower().Contains((kernelName + version).ToLower())) ?? "";
				if (string.IsNullOrEmpty(kernelPath))
				{
					return (IntPtr.Zero, factor);
				}

				// Lade Kernel, falls nicht geladen
				if (this.Kernel == null || this.KernelFile != kernelPath)
				{
					this.compiler.LoadKernel("", kernelPath);
					if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Audio\\"))
					{
						return (IntPtr.Zero, factor);
					}
				}

				// Eingabepuffer abrufen
				OpenClMem? inputMem = this.register.GetBuffer(objPointer);
				if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
				{
					return (IntPtr.Zero, factor);
				}

				// Variable Argumente abrufen
				object[] variableArguments = this.compiler.GetArgumentDefaultValues();

				// Prüfen, ob FFT benötigt wird
				bool didFft = false;
				if (this.compiler.PointerInputType.Contains("Vector2") && inputMem.Type == typeof(float).Name)
				{
					if (optionalArguments != null && optionalArguments.ContainsKey("factor"))
					{
						// Setze den Faktor
						if (optionalArguments["factor"] is double dFactor)
						{
							factor = dFactor;
						}
						else if (optionalArguments["factor"] is float fFactor)
						{
							factor = fFactor;
						}
						else
						{
							return (IntPtr.Zero, factor);
						}
					}
					else
					{
						factor = 1.000d;
					}

					// Führe die asynchrone FFT aus
					IntPtr fftPointer = await this.ExecuteFFTAsync(objPointer, "01", 'f', chunkSize, overlap, true, progress);
					if (fftPointer == IntPtr.Zero)
					{
						return (IntPtr.Zero, factor);
					}

					objPointer = fftPointer;
					didFft = true;

					// Kernel nach FFT erneut laden, falls nötig
					if (this.Kernel == null || this.KernelFile != kernelPath)
					{
						this.compiler.LoadKernel("", kernelPath);
						if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Audio\\"))
						{
							return (IntPtr.Zero, factor);
						}
					}
				}

				// Eingabepuffer abrufen (möglicherweise neu)
				inputMem = this.register.GetBuffer(objPointer);
				if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
				{
					return (IntPtr.Zero, factor);
				}

				// Ausgabepuffer abrufen
				OpenClMem? outputMem = null;
				if (this.compiler.PointerInputType == typeof(float*).Name)
				{
					outputMem = this.register.AllocateGroup<float>(inputMem.GetCount(), (nint) inputMem.GetLengths().FirstOrDefault());
				}
				else if (this.compiler.PointerOutputType == typeof(Vector2*).Name)
				{
					outputMem = this.register.AllocateGroup<Vector2>(inputMem.GetCount(), (nint) inputMem.GetLengths().FirstOrDefault());
				}
				else
				{
					return (IntPtr.Zero, factor);
				}

				if (outputMem == null || outputMem.GetCount() == 0 || outputMem.GetLengths().Any(l => l < 1))
				{
					return (IntPtr.Zero, factor);
				}

				// Schleife durch Eingabepuffer
				int count = inputMem.GetCount();
				for (int i = 0; i < count; i++)
				{
					// Puffer abrufen
					CLBuffer inputBuffer = inputMem[i];
					CLBuffer outputBuffer = outputMem[i];

					// Argumente zusammenführen
					List<object> arguments = this.MergeArgumentsAudio(variableArguments, inputBuffer, outputBuffer, length, chunkSize, overlap, samplerate, bitdepth, channels, optionalArguments);
					if (arguments == null || arguments.Count == 0)
					{
						return (IntPtr.Zero, factor);
					}

					// Kernel-Argumente setzen
					CLResultCode error = CLResultCode.Success;
					for (uint j = 0; j < arguments.Count; j++)
					{
						error = this.SetKernelArgSafe(j, arguments[(int) j]);
						if (error != CLResultCode.Success)
						{
							this.lastError = error;
							return (IntPtr.Zero, factor);
						}
					}

					// Arbeitsdimensionen ermitteln
					uint maxWorkGroupSize = this.GetMaxWorkGroupSize();
					uint globalWorkSize = (uint) inputMem.GetLengths()[i];
					uint localWorkSize = Math.Min(maxWorkGroupSize, globalWorkSize);
					if (localWorkSize == 0)
					{
						localWorkSize = 1;
					}
					if (globalWorkSize < localWorkSize)
					{
						globalWorkSize = localWorkSize;
					}

					// Kernel ausführen
					error = CL.EnqueueNDRangeKernel(this.queue, this.Kernel.Value, 1, null, [(UIntPtr) globalWorkSize], [(UIntPtr) localWorkSize], 0, null, out CLEvent evt);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return (IntPtr.Zero, factor);
					}

					// Auf Fertigstellung warten
					error = CL.WaitForEvents(1, [evt]);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return (objPointer, factor);
					}

					// Event freigeben
					error = CL.ReleaseEvent(evt);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
					}

					// Fortschritt melden
					progress?.Report(1);
				}

				// Eingabepuffer freigeben
				if (outputMem[0].Handle != IntPtr.Zero)
				{
					long freed = this.register.FreeBuffer(objPointer, true);
					if (freed > 0)
					{
						Console.WriteLine("Freed: " + freed + " MB");
					}
				}

				// Optional IFFT ausführen
				IntPtr outputPointer = outputMem[0].Handle;
				if (didFft && outputMem.Type == typeof(Vector2).Name)
				{
					// Führe die asynchrone IFFT aus
					IntPtr ifftPointer = await this.ExecuteFFTAsync(outputMem[0].Handle, "01", 'c', chunkSize, overlap, true, progress);
					if (ifftPointer == IntPtr.Zero)
					{
						return (IntPtr.Zero, factor);
					}

					outputPointer = ifftPointer;
				}

				// Rückgabe des Tupels
				return (outputPointer != IntPtr.Zero ? outputPointer : objPointer, factor);
			});
		}

		public IntPtr ExecuteImageFractalKernel(string kernel, int width, int height, object zoom, object xOffset, object yOffset, int iter, int red, int green, int blue)
		{
			// Load kernel
			string? kernelFile = this.compiler.KernelFiles.FirstOrDefault(f => f.ToLower().Contains(kernel.ToLower()));
			if (string.IsNullOrEmpty(kernelFile))
			{
				return IntPtr.Zero;
			}
			this.compiler.LoadKernel("", kernelFile);
			if (this.Kernel == null)
			{
				return IntPtr.Zero;
			}

			// Create output buffer
			IntPtr outputBufferSize = (IntPtr) (width * height * 4);
			var outputBuffer = this.register.AllocateSingle<byte>(outputBufferSize)?.GetBuffers().FirstOrDefault();
			if (outputBuffer == null)
			{
				return IntPtr.Zero;
			}

			// Set kernel arguments
			this.SetKernelArgSafe(0, outputBuffer);
			this.SetKernelArgSafe(1, width);
			this.SetKernelArgSafe(2, height);
			this.SetKernelArgSafe(3, zoom);
			this.SetKernelArgSafe(4, xOffset);
			this.SetKernelArgSafe(5, yOffset);
			this.SetKernelArgSafe(6, iter);
			this.SetKernelArgSafe(7, red);
			this.SetKernelArgSafe(8, green);
			this.SetKernelArgSafe(9, blue);

			// Dimensions
			int pixelsTotal = width * height * 4; // Anzahl der Pixel
			int workWidth = width > 0 ? width : pixelsTotal; // Falls kein width gegeben, 1D
			int workHeight = height > 0 ? height : 1;        // Falls kein height, 1D

			// Work dimensions
			uint workDim = (width > 0 && height > 0) ? 2u : 1u;
			UIntPtr[] globalWorkSize = workDim == 2
				? [(UIntPtr) workWidth, (UIntPtr) workHeight]
				: [(UIntPtr) pixelsTotal];

			// Execute kernel
			CLResultCode error = CL.EnqueueNDRangeKernel(
				this.queue,
				this.Kernel.Value,
				workDim,          // 1D oder 2D
				null,             // Kein Offset
				globalWorkSize,   // Work-Größe in Pixeln
				null,             // Lokale Work-Size (automatisch)
				0, null, out CLEvent evt
			);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return IntPtr.Zero;
			}

			// Wait for completion
			error = CL.WaitForEvents(1, [@evt]);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return IntPtr.Zero;
			}

			// Return output buffer pointer
			return outputBuffer.Value.Handle;
		}

		public IntPtr ExecuteImageKernel(IntPtr objPointer,  string kernelName = "greyscale", string version = "00", int width = 0, int height = 0,  int bitdepth = 8, int channels = 4, Dictionary<string, object>? optionalArguments = null)
		{
			// Get kernel path
			string kernelPath = this.compiler.KernelFiles.FirstOrDefault(f => f.ToLower().Contains((kernelName + version).ToLower())) ?? "";
			if (string.IsNullOrEmpty(kernelPath))
			{
				return IntPtr.Zero;
			}

			// Load kernel if not loaded
			if (this.Kernel == null || this.KernelFile != kernelPath)
			{
				this.compiler.LoadKernel("", kernelPath);
				if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Image\\"))
				{
					return IntPtr.Zero;
				}
			}

			// Get input buffers
			OpenClMem? inputMem = this.register.GetBuffer(objPointer);
			if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
			{
				return IntPtr.Zero;
			}

			long length = inputMem.GetLengths().Sum();

			// Get variable arguments
			object[] variableArguments = this.compiler.GetArgumentDefaultValues();

			// Get input buffers
			inputMem = this.register.GetBuffer(objPointer);
			if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
			{
				return IntPtr.Zero;
			}

			// Get output buffers
			OpenClMem? outputMem = this.register.AllocateSingle<byte>((nint) length * channels);

			if (outputMem == null || outputMem.GetCount() == 0 || outputMem.GetLengths().Any(l => l < 1))
			{
				return IntPtr.Zero;
			}

			// Loop through input buffers
			int count = inputMem.GetCount();
			for (int i = 0; i < count; i++)
			{
				// Get buffers
				CLBuffer inputBuffer = inputMem[i];
				CLBuffer outputBuffer = outputMem[i];

				// Merge arguments
				List<object> arguments = this.MergeArgumentsImage(variableArguments, inputBuffer, outputBuffer, width, height, bitdepth, channels, optionalArguments);
				if (arguments == null || arguments.Count == 0)
				{
					return IntPtr.Zero;
				}

				// Set kernel arguments
				CLResultCode error = CLResultCode.Success;
				for (uint j = 0; j < arguments.Count; j++)
				{
					error = this.SetKernelArgSafe(j, arguments[(int) j]);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return IntPtr.Zero;
					}
				}

				// Get work dimensions
				uint maxWorkGroupSize = this.GetMaxWorkGroupSize();
				uint globalWorkSize = (uint) inputMem.GetLengths()[i];
				uint localWorkSize = Math.Min(maxWorkGroupSize, globalWorkSize);
				if (localWorkSize == 0)
				{
					localWorkSize = 1; // Fallback to 1 if no valid local size
				}
				if (globalWorkSize < localWorkSize)
				{
					globalWorkSize = localWorkSize; // Ensure global size is at least local size
				}

				// Execute kernel
				error = CL.EnqueueNDRangeKernel(this.queue, this.Kernel.Value, 2, null, [(UIntPtr) globalWorkSize], [(UIntPtr) localWorkSize], 0, null, out CLEvent evt);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return IntPtr.Zero;
				}

				// Wait for completion
				error = CL.WaitForEvents(1, [evt]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return objPointer;
				}

				// Release event
				error = CL.ReleaseEvent(evt);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
				}
			}

			// Free input buffer if necessary
			if (outputMem.indexHandle != IntPtr.Zero)
			{
				long freed = this.register.FreeBuffer(objPointer, true);
				if (freed > 0)
				{
					Console.WriteLine("Freed: " + freed + " MB");
				}
			}

			// Optionally execute IFFT if FFT was done
			IntPtr outputPointer = outputMem.indexHandle;

			// Return output buffer handle if available, else return original pointer
			return outputPointer != IntPtr.Zero ? outputPointer : objPointer;
		}

		public async Task<IntPtr> ExecuteImageKernelAsync(IntPtr objPointer, string kernelName = "greyscale", string version = "00", int width = 0, int height = 0, int bitdepth = 8, int channels = 4, Dictionary<string, object>? optionalArguments = null, IProgress<int>? progress = null)
		{
			return await Task.Run(async () =>
			{
				// Lade Kernel-Pfad
				string kernelPath = this.compiler.KernelFiles.FirstOrDefault(f => f.ToLower().Contains((kernelName + version).ToLower())) ?? "";
				if (string.IsNullOrEmpty(kernelPath))
				{
					return IntPtr.Zero;
				}

				// Lade Kernel, falls nicht geladen
				if (this.Kernel == null || this.KernelFile != kernelPath)
				{
					this.compiler.LoadKernel("", kernelPath);
					if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Audio\\"))
					{
						return IntPtr.Zero;
					}
				}

				// Eingabepuffer abrufen
				OpenClMem? inputMem = this.register.GetBuffer(objPointer);
				if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
				{
					return IntPtr.Zero;
				}

				// Variable Argumente abrufen
				object[] variableArguments = this.compiler.GetArgumentDefaultValues();

				// Eingabepuffer abrufen (möglicherweise neu)
				inputMem = this.register.GetBuffer(objPointer);
				if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
				{
					return IntPtr.Zero;
				}

				long length = inputMem.GetLengths().Sum();

				// Ausgabepuffer abrufen
				OpenClMem? outputMem = this.register.AllocateSingle<byte>((nint) length * channels);
				if (outputMem == null || outputMem.GetCount() == 0 || outputMem.GetLengths().Any(l => l < 1))
				{
					return IntPtr.Zero;
				}

				// Schleife durch Eingabepuffer
				int count = inputMem.GetCount();
				for (int i = 0; i < count; i++)
				{
					// Puffer abrufen
					CLBuffer inputBuffer = inputMem[i];
					CLBuffer outputBuffer = outputMem[i];

					// Argumente zusammenführen
					List<object> arguments = this.MergeArgumentsImage(variableArguments, inputBuffer, outputBuffer, width, height, bitdepth, channels, optionalArguments);
					if (arguments == null || arguments.Count == 0)
					{
						return IntPtr.Zero;
					}

					// Kernel-Argumente setzen
					CLResultCode error = CLResultCode.Success;
					for (uint j = 0; j < arguments.Count; j++)
					{
						error = this.SetKernelArgSafe(j, arguments[(int) j]);
						if (error != CLResultCode.Success)
						{
							this.lastError = error;
							return IntPtr.Zero;
						}
					}

					// Arbeitsdimensionen ermitteln
					uint maxWorkGroupSize = this.GetMaxWorkGroupSize();
					uint globalWorkSize = (uint) inputMem.GetLengths()[i];
					uint localWorkSize = Math.Min(maxWorkGroupSize, globalWorkSize);
					if (localWorkSize == 0)
					{
						localWorkSize = 1;
					}
					if (globalWorkSize < localWorkSize)
					{
						globalWorkSize = localWorkSize;
					}

					// Kernel ausführen
					error = CL.EnqueueNDRangeKernel(this.queue, this.Kernel.Value, 2, null, [(UIntPtr) globalWorkSize], [(UIntPtr) localWorkSize], 0, null, out CLEvent evt);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return IntPtr.Zero;
					}

					// Auf Fertigstellung warten
					error = CL.WaitForEvents(1, [evt]);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return objPointer;
					}

					// Event freigeben
					error = CL.ReleaseEvent(evt);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
					}

					// Fortschritt melden
					progress?.Report(1);

					await Task.Yield();
				}

				// Eingabepuffer freigeben
				if (outputMem.indexHandle != IntPtr.Zero)
				{
					long freed = this.register.FreeBuffer(objPointer, true);
					if (freed > 0)
					{
						Console.WriteLine("Freed: " + freed + " MB");
					}
				}

				IntPtr outputPointer = outputMem.indexHandle;

				// Rückgabe des pointers
				return outputPointer != IntPtr.Zero ? outputPointer : objPointer;
			});
		}



		// Helpers
		public List<object> MergeArgumentsAudio(object[] variableArguments, CLBuffer inputBuffer, CLBuffer outputBuffer, long length, int chunkSize, float overlap, int samplerate, int bitdepth, int channels, Dictionary<string, object>? optionalArgs = null)
		{
			List<object> arguments = [];

			// Make overlap to size
			int overlapSize = (int) (overlap * chunkSize);

			// Get argument definition
			Dictionary<string, Type> definitions = this.compiler.Arguments;
			if (definitions == null || definitions.Count == 0)
			{
				return arguments;
			}

			// Merge args
			int found = 0;
			for (int i = 0; i < definitions.Count; i++)
			{
				string key = definitions.Keys.ElementAt(i);
				Type type = definitions[key];
				if (type.Name.Contains("*") && key.Contains("in"))
				{
					arguments.Add(inputBuffer);
					found++;
				}
				else if (type.Name.Contains("*") && key.Contains("out"))
				{
					arguments.Add(outputBuffer);
					found++;
				}
				else if ((type == typeof(long) || type == typeof(int)) && key.Contains("len"))
				{
					arguments.Add(chunkSize > 0 ? chunkSize : length);
					found++;
				}
				else if (type == typeof(int) && key.Contains("chunk"))
				{
					arguments.Add(chunkSize);
					found++;
				}
				else if (type == typeof(int) && key.Contains("overlap"))
				{
					arguments.Add(overlapSize);
					found++;
				}
				else if (type == typeof(int) && key == "samplerate")
				{
					arguments.Add(samplerate);
					found++;
				}
				else if (type == typeof(int) && key == "bit")
				{
					arguments.Add(bitdepth);
					found++;
				}
				else if (type == typeof(int) && key == "channel")
				{
					arguments.Add(channels);
					found++;
				}
				else
				{
					if (found < variableArguments.Length)
					{
						arguments.Add(variableArguments[found]);
						found++;
					}
					else
					{
						return arguments; // Return early if a required argument is missing
					}
				}
			}

			// Integrate / replace with optional arguments
			if (optionalArgs != null && optionalArgs.Count > 0)
			{
				foreach (var kvp in optionalArgs)
				{
					string key = kvp.Key.ToLowerInvariant();
					object value = kvp.Value;

					// Find matching argument by name
					int index = definitions.Keys.ToList().FindIndex(k => k.ToLower().Contains(key.ToLower()));
					if (index >= 0 && index < arguments.Count)
					{
						arguments[index] = value; // Replace existing argument
					}
					else
					{
						arguments.Add(value); // Add new optional argument
					}
				}
			}

			return arguments;
		}

		public List<object> MergeArgumentsImage(object[] variableArguments, CLBuffer inputBuffer, CLBuffer outputBuffer, int width, int height, int bitdepth, int channels, Dictionary<string, object>? optionalArgs = null)
		{
			List<object> arguments = [];

			// Get argument definition
			Dictionary<string, Type> definitions = this.compiler.Arguments;
			if (definitions == null || definitions.Count == 0)
			{
				return arguments;
			}

			// Merge args
			int found = 0;
			for (int i = 0; i < definitions.Count; i++)
			{
				string key = definitions.Keys.ElementAt(i);
				Type type = definitions[key];
				if (type.Name.Contains("*") && key.Contains("in"))
				{
					arguments.Add(inputBuffer);
					found++;
				}
				else if (type.Name.Contains("*") && key.Contains("out"))
				{
					arguments.Add(outputBuffer);
					found++;
				}
				else if ((type == typeof(int)) && key.Contains("width"))
				{
					arguments.Add(width);
					found++;
				}
				else if ((type == typeof(int)) && key.Contains("height"))
				{
					arguments.Add(width);
					found++;
				}
				else if (type == typeof(int) && key.Contains("bit"))
				{
					arguments.Add(bitdepth);
					found++;
				}
				else if (type == typeof(int) && key.Contains("channel"))
				{
					arguments.Add(channels);
					found++;
				}
				else
				{
					if (found < variableArguments.Length)
					{
						arguments.Add(variableArguments[found]);
						found++;
					}
					else
					{
						return arguments; // Return early if a required argument is missing
					}
				}
			}

			// Integrate / replace with optional arguments
			if (optionalArgs != null && optionalArgs.Count > 0)
			{
				foreach (var kvp in optionalArgs)
				{
					string key = kvp.Key.ToLowerInvariant();
					object value = kvp.Value;

					// Find matching argument by name
					int index = definitions.Keys.ToList().FindIndex(k => k.ToLower().Contains(key.ToLower()));
					if (index >= 0 && index < arguments.Count)
					{
						arguments[index] = value;
					}
					else
					{
						arguments.Add(value);
					}
				}
			}

			return arguments;
		}


		private CLResultCode SetKernelArgSafe(uint index, object value)
		{
			// Check kernel
			if (this.Kernel == null)
			{
				return CLResultCode.InvalidKernelDefinition;
			}

			switch (value)
			{
				case CLBuffer buffer:
					return CL.SetKernelArg(this.Kernel.Value, index, buffer);

				case int i:
					return CL.SetKernelArg(this.Kernel.Value, index, i);

				case long l:
					return CL.SetKernelArg(this.Kernel.Value, index, l);

				case float f:
					return CL.SetKernelArg(this.Kernel.Value, index, f);

				case double d:
					return CL.SetKernelArg(this.Kernel.Value, index, d);

				case byte b:
					return CL.SetKernelArg(this.Kernel.Value, index, b);

				case IntPtr ptr:
					return CL.SetKernelArg(this.Kernel.Value, index, ptr);

				// Spezialfall für lokalen Speicher (Größe als uint)
				case uint u:
					return CL.SetKernelArg(this.Kernel.Value, index, new IntPtr(u));

				// Fall für Vector2
				case Vector2 v:
					// Vector2 ist ein Struct, daher muss es als Array übergeben werden
					return CL.SetKernelArg(this.Kernel.Value, index, v);

				default:
					throw new ArgumentException($"Unsupported argument type: {value?.GetType().Name ?? "null"}");
			}
		}

		private uint GetMaxWorkGroupSize()
		{
			const uint FALLBACK_SIZE = 64;

			if (!this.Kernel.HasValue)
			{
				return FALLBACK_SIZE;
			}

			try
			{
				// 1. Zuerst die benötigte Puffergröße ermitteln
				CLResultCode result = CL.GetKernelWorkGroupInfo(
					this.Kernel.Value,
					this.device,
					KernelWorkGroupInfo.WorkGroupSize,
					UIntPtr.Zero,
					null,
					out nuint requiredSize);

				if (result != CLResultCode.Success || requiredSize == 0)
				{
					this.lastError = result;
					return FALLBACK_SIZE;
				}

				// 2. Puffer mit korrekter Größe erstellen
				byte[] paramValue = new byte[requiredSize];

				// 3. Tatsächliche Abfrage durchführen
				result = CL.GetKernelWorkGroupInfo(
					this.Kernel.Value,
					this.device,
					KernelWorkGroupInfo.WorkGroupSize,
					new UIntPtr(requiredSize),
					paramValue,
					out _);

				if (result != CLResultCode.Success)
				{
					this.lastError = result;
					return FALLBACK_SIZE;
				}

				// 4. Ergebnis konvertieren (abhängig von der Plattform)
				uint maxSize;
				if (requiredSize == sizeof(uint))
				{
					maxSize = BitConverter.ToUInt32(paramValue, 0);
				}
				else if (requiredSize == sizeof(ulong))
				{
					maxSize = (uint) BitConverter.ToUInt64(paramValue, 0);
				}
				else
				{
					return FALLBACK_SIZE;
				}

				// 5. Gültigen Wert sicherstellen
				if (maxSize == 0)
				{
					return FALLBACK_SIZE;
				}

				return maxSize;
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return FALLBACK_SIZE;
			}
		}

	}
}
