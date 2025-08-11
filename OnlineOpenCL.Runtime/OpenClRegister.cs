using OpenTK.Compute.OpenCL;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace OnlineOpenCL.OpenCl
{
	public class OpenClRegister
	{
		// ----- Services ----- \\

		// ----- Fields ----- \\
		private CLContext context;
		private CLCommandQueue queue;
		private ConcurrentDictionary<Guid, OpenClMem> memory = [];
		private object memoryLock = new();

		// ----- Attributes ----- \\
		public CLCommandQueue Queue => this.queue;
		public IReadOnlyList<OpenClMem> Memory => this.memory.Values.ToList();

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
		public OpenClRegister(CLContext context, CLDevice device)
		{
			this.context = context;
			this.queue = CL.CreateCommandQueueWithProperties(context, device, 0, out CLResultCode result);
			if (result != CLResultCode.Success)
			{
				this.lastError = result;
			}
		}

		// ----- Methods ----- \\
		public void Dispose()
		{
			// Free every mem obj

		}

		public OpenClMem? GetBuffer(IntPtr pointer)
		{
			// Sperre den Zugriff auf die Memory-Liste während der Iteration
			lock (this.memoryLock)
			{
				return this.memory.Values.FirstOrDefault(mem => mem.IndexHandle == pointer.ToString("X16") || mem.IndexHandle == pointer.ToString(""));
			}
		}

		public long FreeBuffer(IntPtr pointer, bool readable = false)
		{
			OpenClMem? mem = this.GetBuffer(pointer);
			if (mem == null)
			{
				return 0;
			}

			long freedSizeBytes = mem.GetSize(!readable);

			List<CLBuffer> buffers = mem.GetBuffers();
			foreach (CLBuffer buffer in buffers)
			{
				// Free the buffer
				CLResultCode error = CL.ReleaseMemoryObject(buffer);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
				}
			}

			// Remove from memory list
			this.memory.TryRemove(mem.Id, out _);

			// Make readable if requested
			if (readable)
			{
				freedSizeBytes /= 1024 * 1024; // Convert to MB
			}

			return freedSizeBytes;
		}

		public OpenClMem? PushData<T>(T[] data) where T : unmanaged
		{
			// Check data
			if (data.LongLength < 1)
			{
				return null;
			}

			// Get IntPtr length
			IntPtr length = new(data.LongLength);

			// Create buffer
			CLBuffer buffer = CL.CreateBuffer<T>(this.context, MemoryFlags.CopyHostPtr | MemoryFlags.ReadWrite, data, out CLResultCode error);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return null;
			}

			// Add to list
			OpenClMem mem = new(buffer, length, typeof(T));

			// Lock memory list to avoid concurrent access issues
			lock (this.memoryLock)
			{
				// Add to memory list
				this.memory.TryAdd(mem.Id, mem);
			}

			return mem;
		}

		public T[] PullData<T>(IntPtr pointer, bool keep = false) where T : unmanaged
		{
			// Get buffer & length
			OpenClMem? mem = this.GetBuffer(pointer);
			if (mem == null || mem.GetCount() <= 0)
			{
				return [];
			}

			// New array with length
			long length = mem.GetLengths().FirstOrDefault();
			T[] data = new T[length];

			// Read buffer
			CLResultCode error = CL.EnqueueReadBuffer(
				this.queue,
				mem[0],
				true,
				0,
				data,
				null,
				out CLEvent @event
			);

			// Check error
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return [];
			}

			// If not keeping, free buffer
			if (!keep)
			{
				this.FreeBuffer(pointer);
			}

			// Return data
			return data;
		}

		public OpenClMem? AllocateSingle<T>(IntPtr size) where T : unmanaged
		{
			// Check size
			if (size.ToInt64() < 1)
			{
				return null;
			}

			// Create empty array of type and size
			T[] data = new T[size.ToInt64()];
			data = data.Select(x => default(T)).ToArray();

			// Create buffer
			CLBuffer buffer = CL.CreateBuffer<T>(this.context, MemoryFlags.CopyHostPtr | MemoryFlags.ReadWrite, data, out CLResultCode error);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return null;
			}

			// Add to list
			OpenClMem mem = new(buffer, size, typeof(T));

			// Lock memory list to avoid concurrent access issues
			lock (this.memoryLock)
			{
				// Add to memory list
				this.memory.TryAdd(mem.Id, mem);
			}

			// Return mem obj
			return mem;
		}

		public OpenClMem? PushChunks<T>(List<T[]> chunks) where T : unmanaged
		{
			// Check chunks
			if (chunks.Count < 1 || chunks.Any(chunk => chunk.LongLength < 1))
			{
				return null;
			}

			// Get IntPtr[] lengths
			IntPtr[] lengths = chunks.Select(chunk => new IntPtr(chunk.LongLength)).ToArray();

			// Create buffers for each chunk
			CLBuffer[] buffers = new CLBuffer[chunks.Count];
			for (int i = 0; i < chunks.Count; i++)
			{
				buffers[i] = CL.CreateBuffer(this.context, MemoryFlags.CopyHostPtr | MemoryFlags.ReadWrite, chunks[i], out CLResultCode error);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return null;
				}
			}

			// Add to list
			OpenClMem mem = new(buffers, lengths, typeof(T));

			// Lock memory list to avoid concurrent access issues
			lock (this.memoryLock)
			{
				// Add to memory list
				this.memory.TryAdd(mem.Id, mem);
			}

			// Return mem obj
			return mem;
		}

		public List<T[]> PullChunks<T>(IntPtr indexPointer, bool keep = false) where T : unmanaged
		{
			// Get OpenClMem by index pointer
			OpenClMem? mem = this.GetBuffer(indexPointer);
			if (mem == null || mem.GetCount() < 1)
			{
				return [];
			}

			// Chunk list & lengths
			List<T[]> chunks = [];
			IntPtr[] lengths = mem.GetLengths().Select(l => (nint) l).ToArray();

			// Read every buffer
			for (int i = 0; i < mem.GetCount(); i++)
			{
				T[] chunk = new T[lengths[i].ToInt64()];
				CLResultCode error = CL.EnqueueReadBuffer(
					this.queue,
					mem[i],
					true,
					0,
					chunk,
					null,
					out CLEvent @event
				);

				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return [];
				}

				chunks.Add(chunk);
			}

			// Optionally free buffer
			if (!keep)
			{
				// Free the memory if not keeping
				this.FreeBuffer(indexPointer);
			}

			// Return chunks
			return chunks;
		}

		public OpenClMem? AllocateGroup<T>(IntPtr count, IntPtr size) where T : unmanaged
		{
			// Check count and size
			if (count < 1 || size.ToInt64() < 1)
			{
				return null;
			}

			// Create array of IntPtr for handles
			CLBuffer[] buffers = new CLBuffer[count];
			IntPtr[] lengths = new IntPtr[count];
			Type type = typeof(T);

			// Allocate each buffer
			for (int i = 0; i < count; i++)
			{
				buffers[i] = CL.CreateBuffer<T>(this.context, MemoryFlags.CopyHostPtr | MemoryFlags.ReadWrite, new T[size.ToInt64()], out CLResultCode error);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return null;
				}

				lengths[i] = size;
			}

			OpenClMem mem = new(buffers, lengths, type);

			// Lock memory list to avoid concurrent access issues
			lock (this.memoryLock)
			{
				this.memory.TryAdd(mem.Id, mem);
			}

			return mem;
		}
	}
}
