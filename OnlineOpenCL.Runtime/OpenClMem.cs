using OpenTK.Compute.OpenCL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OnlineOpenCL.OpenCl
{
	public class OpenClMem
	{
		public Guid Id { get; } = Guid.NewGuid();
		public int Index => 0;
		public bool Online => true;
		public string Status => this.Buffers.LongLength == this.lengths.LongLength ? "OK" : "Error: Lengths mismatch";
		public IEnumerable<string> ErrorMessages => [];

		private DateTime createdAt = DateTime.Now;
		public string Timestamp => this.createdAt.ToString("yyyy-MM-dd HH:mm:ss.fff");
		public string TickAge => (DateTime.Now.Ticks - this.createdAt.Ticks).ToString();

		private CLBuffer[] Buffers { get; set; } = [];
		public CLBuffer this[int index] => index >= 0 && index < this.Buffers.Length ? this.Buffers[index] : this.Buffers[0];

		private IntPtr[] lengths { get; set; } = [];
		public IEnumerable<string> Lengths => this.lengths.Select(length => length.ToString());

		private System.Type elementType { get; set; } = typeof(void);
		public string Type => this.elementType.Name;

		public bool AsHex { get; set; } = false;
		private string formatting => this.AsHex ? "X16" : "";


		public bool IsSingle => this.Buffers.Length == 1;
		public bool IsArray => this.Buffers.Length > 1;

		private IntPtr count => (nint) this.Buffers.LongLength;
		public string Count => this.count.ToString();

		public string TotalLength => this.GetTotalLength().ToString();


		public int TypeSize => Marshal.SizeOf(this.elementType);
		private IntPtr size => (nint) this.lengths.Sum(length => length.ToInt64() * this.TypeSize);
		public string Size => this.size.ToString();

		public IntPtr[] pointers => this.Buffers.Select(buffer => buffer.Handle).ToArray();
		public IEnumerable<string> Pointers => this.pointers.Select(p => p.ToString(this.formatting));


		public IntPtr indexHandle => this.Buffers.FirstOrDefault().Handle;
		public string IndexHandle => this.indexHandle.ToString(this.formatting);

		private IntPtr indexLength => this.lengths.FirstOrDefault();
		public string IndexLength => this.indexLength.ToString();



		public OpenClMem(CLBuffer[] buffers, IntPtr[] lengths, Type? elementType = null)
		{
			this.Buffers = buffers;
			this.lengths = lengths;
			this.elementType = elementType ?? typeof(void);
		}

		public OpenClMem(CLBuffer buffer, IntPtr length, Type? elementType = null)
		{
			this.Buffers = [buffer];
			this.lengths = [length];
			this.elementType = elementType ?? typeof(void);
		}



		public int GetSize(bool asBytes = false)
		{
			long size = this.size.ToInt64();
			if (!asBytes)
			{
				size /= 1024 * 1024; // Convert to MB
			}

			return (int) size;
		}

		public int GetCount()
		{
			return (int) this.count.ToInt64();
		}

		public long GetTotalLength()
		{
			return this.lengths.Sum(length => length.ToInt64());
		}

		public List<long> GetLengths()
		{
			return this.lengths.Select(length => length.ToInt64()).ToList();
		}

		public List<CLBuffer> GetBuffers()
		{
			return this.Buffers.ToList();
		}
	}
}
