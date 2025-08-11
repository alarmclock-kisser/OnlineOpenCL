using OnlineOpenCL.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OnlineOpenCL.Shared
{
	public class AudioObjInfo
	{
		public Guid Id { get; set; } = Guid.Empty;
		public DateTime InfoCreated { get; set; } = DateTime.Now;
		public string FilePath { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;

		public int SampleRate { get; set; } = 0;
		public int Channels { get; set; } = 0;
		public int BitDepth { get; set; } = 0;
		public double TotalSeconds { get; set; } = 0.0;
		public string DurationTimeSpan { get; set; } = "00:00.000";
		public float SizeInMb { get; set; } = 0.0f;

		public float ElapsedLoading { get; set; } = 0.0f;
		public float ElapsedProcessing { get; set; } = 0.0f;

		public bool OnHost { get; set; } = false;
		public string Location { get; set; } = "N/A";
		public string Pointer { get; set; } = "0";

		public int ChunkSize { get; set; } = 0;
		public int OverlapSize { get; set; } = 0;
		public string Form { get; set; } = "N/A";
		public double StretchFactor { get; set; } = 1.0;
		public float Bpm { get; set; } = 0.0f;

		public int Volume { get; set; } = 0;
		public bool Playing { get; set; } = false;
		public string CurrentPosition { get; set; } = "00:00.000";


		public AudioObjInfo()
		{
			// Empty ctor
		}

		[JsonConstructor]
		public AudioObjInfo(AudioObj? obj = null)
		{
			if (obj == null)
			{
				return;
			}

			this.Id = obj.Id;
			this.FilePath = obj.FilePath;
			this.Name = obj.Name;
			this.SampleRate = obj.SampleRate;
			this.Channels = obj.Channels;
			this.BitDepth = obj.BitDepth;
			this.TotalSeconds = obj.TotalSeconds;
			this.DurationTimeSpan = obj.Duration.ToString(@"mm\:ss\.fff");
			this.SizeInMb = obj.SizeInMb;
			this.ElapsedLoading = obj.ElapsedLoadingTime;
			this.ElapsedProcessing = obj.ElapsedProcessingTime;
			this.OnHost = obj.OnHost;
			this.Location = obj.OnDevice ? "Device" : "Host";
			this.Pointer = obj.Pointer.ToString();
			this.ChunkSize = obj.ChunkSize;
			this.OverlapSize = obj.OverlapSize;
			this.Form = obj.Form;
			this.StretchFactor = obj.StretchFactor;
			this.Bpm = obj.Bpm;
			this.Volume = obj.Volume;
			this.Playing = obj.Playing;
			this.CurrentPosition = obj.CurrentTime.ToString(@"mm\:ss\.fff");
		}
	}
}
