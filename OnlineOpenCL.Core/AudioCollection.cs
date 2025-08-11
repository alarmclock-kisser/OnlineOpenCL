using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlineOpenCL.Core
{
	public class AudioCollection
	{
		private ConcurrentDictionary<Guid, AudioObj> tracks = [];
		public IReadOnlyList<AudioObj> Tracks => this.tracks.Values.ToList();
		public int Count => this.tracks.Count;

		public AudioObj? this[Guid guid]
		{
			get => this.tracks.TryGetValue(guid, out var track) ? track : null;
		}

		public AudioObj? this[string name]
		{
			get => this.tracks.Values.FirstOrDefault(t => t.Name.ToLower() == name.ToLower());
		}

		public AudioObj? this[int index]
		{
			get => index >= 0 && index < this.Count ? this.tracks.Values.ElementAt(index) : null;
		}

		public AudioObj? this[IntPtr pointer]
		{
			get => pointer != IntPtr.Zero ? this.tracks.Values.FirstOrDefault(t => t.Pointer == pointer) : null;
		}

		public AudioCollection()
		{

		}

		public async Task<AudioObj?> ImportAsync(string filePath, bool linearLoad = false)
		{
			AudioObj? obj = null;
			if (linearLoad)
			{
				obj = new AudioObj(filePath, true);
			}
			else
			{
				obj = await AudioObj.CreateAsync(filePath);
			}
			if (obj == null)
			{
				return null;
			}

			// Try add to tracks
			if (!this.tracks.TryAdd(obj.Id, obj))
			{
				obj.Dispose();
				return null;
			}

			return obj;
		}

		public async Task RemoveAsync(AudioObj obj)
		{
			// Remove from tracks + Dispose obj
			await Task.Run(() =>
			{
				if (this.tracks.TryRemove(obj.Id, out var removed))
				{
					removed.Dispose();
				}
			});
		}

		public void StopAll()
		{
			foreach (var track in this.tracks.Values)
			{
				track.Stop();
			}
		}

	}
}
