using SixLabors.ImageSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlineOpenCL.Core
{
	public class ImageCollection
	{
		private ConcurrentDictionary<Guid, ImageObj> images = [];
		public IReadOnlyList<ImageObj> Images => this.images.Values.ToList();

		public ImageObj? this[Guid id]
		{
			get
			{
				if (this.images.TryGetValue(id, out ImageObj? image))
				{
					return image;
				}
				return null;
			}
			set
			{
				if (value == null)
				{
					this.images.TryRemove(id, out ImageObj? obj);
					obj?.Dispose();
					GC.Collect();
				}
			}
		}

		public ImageObj? this[int index]
		{
			get
			{
				if (index >= 0 && index < this.images.Count)
				{
					return this.images.Values.ElementAt(index);
				}
				return null;
			}
		}

		public ImageObj Add(int width = 1920, int height = 1080, System.Drawing.Color? color = null)
		{
			color ??= System.Drawing.Color.Black;
			SixLabors.ImageSharp.Color colorSharp = color.HasValue
				? SixLabors.ImageSharp.Color.FromRgba(color.Value.R, color.Value.G, color.Value.B, color.Value.A)
				: SixLabors.ImageSharp.Color.Black;

			ImageObj image = ImageObj.Create(width, height, colorSharp);
			this.images.TryAdd(image.Id, image);
			return image;
		}

		public bool Add(ImageObj obj)
		{
			if (obj == null || obj.Id == Guid.Empty)
			{
				return false;
			}
			if (this.images.TryAdd(obj.Id, obj))
			{
				return true;
			}
			else
			{
				obj.Dispose();
				return false;
			}
		}

		public async Task<ImageObj> AddAsync(int width = 1920, int height = 1080, Color? color = null)
		{
			color ??= Color.Black;
			ImageObj image = await ImageObj.CreateAsync(width, height, color);
			this.images.TryAdd(image.Id, image);
			return image;
		}



	}
}
