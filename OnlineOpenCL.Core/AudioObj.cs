using NAudio.Flac;
using NAudio.Wave;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace OnlineOpenCL.Core
{
	public class AudioObj : IDisposable
	{
		public Guid Id { get; private set; } = Guid.Empty;
		public string FilePath { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;

		public float[] Data { get; private set; } = [];
		public int SampleRate { get; private set; } = 0;
		public int Channels { get; private set; } = 0;
		public int BitDepth { get; private set; } = 0;
		public long Length => this.Data.LongLength;
		public double TotalSeconds => (this.SampleRate > 0 && this.Channels > 0) ? (double) this.Length / (this.SampleRate * this.Channels) : 0;
		public TimeSpan Duration => TimeSpan.FromSeconds(this.TotalSeconds);
		public float SizeInMb => this.Data.LongLength * sizeof(float) / (1024.0f * 1024.0f);

		public float ElapsedLoadingTime { get; set; } = 0.0f;
		public float ElapsedProcessingTime { get; set; } = 0.0f;

		public bool OnHost => this.Data.LongLength > 0 && this.Pointer == IntPtr.Zero;
		public bool OnDevice => this.Pointer != IntPtr.Zero && this.Data.LongLength == 0;
		public IntPtr Pointer { get; set; } = IntPtr.Zero;
		public int ChunkSize { get; set; } = 0;
		public int OverlapSize { get; set; } = 0;
		public string Form { get; set; } = "f";
		public double StretchFactor { get; set; } = 1.0;
		public float Bpm { get; private set; } = 0.0f;

		public int Volume { get; set; } = 100;
		private WaveOutEvent player;
		public bool Playing => this.player?.PlaybackState == PlaybackState.Playing;
		private long position => this.player == null || this.player.PlaybackState != PlaybackState.Playing ? 0 : this.player.GetPosition() / (this.Channels * (this.BitDepth / 8));
		private double positionSeconds => this.SampleRate <= 0 ? 0 : (double) this.position / this.SampleRate;
		public TimeSpan CurrentTime => TimeSpan.FromSeconds(this.positionSeconds);

		private System.Timers.Timer waveformUpdateTimer;
		private int refreshRateHz = 30;
		public int RefreshRateHz
		{
			get => this.refreshRateHz;
			set => this.SetupTimer(value);
		}
		public System.Drawing.Size WaveformSize { get; set; } = new System.Drawing.Size(800, 200);
		public SixLabors.ImageSharp.Image WaveformImage { get; private set; } = new Image<Rgba32>(800, 200);

		public AudioObj(string filePath, bool linearLoad = false, int fps = 20)
		{
			this.Id = Guid.NewGuid();
			this.FilePath = filePath;
			this.Name = Path.GetFileNameWithoutExtension(this.FilePath);
			this.player = new WaveOutEvent();

			this.waveformUpdateTimer = this.SetupTimer(fps);
			this.ReadBpmTag();

			if (this.Data.LongLength <= 0 && linearLoad)
			{
				this.LoadAudioFile();
			}
		}

		public void Dispose()
		{
			this.Data = [];

			GC.SuppressFinalize(this);
		}

		public void LoadAudioFile()
		{
			if (string.IsNullOrEmpty(this.FilePath))
			{
				throw new FileNotFoundException("File path is empty");
			}

			Stopwatch sw = Stopwatch.StartNew();

			using AudioFileReader reader = new(this.FilePath);
			this.SampleRate = reader.WaveFormat.SampleRate;
			this.BitDepth = reader.WaveFormat.BitsPerSample;
			this.Channels = reader.WaveFormat.Channels;

			// Calculate number of samples
			long numSamples = reader.Length / (reader.WaveFormat.BitsPerSample / 8);
			this.Data = new float[numSamples];

			int read = reader.Read(this.Data, 0, (int) numSamples);
			if (read != numSamples)
			{
				float[] resizedData = new float[read];
				Array.Copy(this.Data, resizedData, read);
				this.Data = resizedData;
			}

			sw.Stop();
			this.ElapsedLoadingTime = (float) sw.Elapsed.TotalMilliseconds;

			// Read bpm metadata if available
			this.ReadBpmTag();
		}

		public static async Task<AudioObj?> CreateAsync(string filePath, int refreshRateHz = 30)
		{
			// Check file
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				return null;
			}

			var obj = new AudioObj(filePath, false, refreshRateHz);

			Stopwatch sw = Stopwatch.StartNew();

			using var mainReader = new AudioFileReader(filePath);

			obj.SampleRate = mainReader.WaveFormat.SampleRate;
			obj.Channels = mainReader.WaveFormat.Channels;
			obj.BitDepth = mainReader.WaveFormat.BitsPerSample;

			long totalBytes = mainReader.Length;

			// Total length float[] (Data)
			int dataLengthInFloats = (int) totalBytes / sizeof(float);
			obj.Data = new float[dataLengthInFloats];

			// Worker count
			int workerCount = Math.Min(Environment.ProcessorCount, 16);
			int floatsPerWorker = dataLengthInFloats / workerCount;
			var tasks = new List<Task>();

			for (int i = 0; i < workerCount; i++)
			{
				int startFloatIndex = i * floatsPerWorker;
				int floatsToRead = (i == workerCount - 1) ? dataLengthInFloats - startFloatIndex : floatsPerWorker;

				tasks.Add(Task.Run(() =>
				{
					// Each worker gets a new reader for thread safety
					using var workerReader = new AudioFileReader(filePath);

					// Set start position in bytes
					workerReader.Position = startFloatIndex * sizeof(float);

					// KORREKTUR: Verwende einen float[]-Puffer
					var floatBuffer = new float[floatsToRead];

					// KORREKTUR: Verwende die korrekte Read-Methode für float[]
					int readFloats = workerReader.Read(floatBuffer, 0, floatsToRead);

					// Write to Data
					Array.Copy(floatBuffer, 0, obj.Data, startFloatIndex, readFloats);
				}));
			}

			// Wait for all to finish
			await Task.WhenAll(tasks);

			sw.Stop();
			obj.ElapsedLoadingTime = (float) sw.Elapsed.TotalMilliseconds;

			return obj;
		}

		public async Task ReloadAsync()
		{
			Stopwatch sw = Stopwatch.StartNew();

			using var mainReader = new AudioFileReader(this.FilePath);

			this.SampleRate = mainReader.WaveFormat.SampleRate;
			this.Channels = mainReader.WaveFormat.Channels;
			this.BitDepth = mainReader.WaveFormat.BitsPerSample;

			long totalBytes = mainReader.Length;

			// Total length float[] (Data)
			int dataLengthInFloats = (int) totalBytes / sizeof(float);
			this.Data = new float[dataLengthInFloats];

			// Worker count
			int workerCount = Math.Min(Environment.ProcessorCount, 16);
			int floatsPerWorker = dataLengthInFloats / workerCount;
			var tasks = new List<Task>();

			for (int i = 0; i < workerCount; i++)
			{
				int startFloatIndex = i * floatsPerWorker;
				int floatsToRead = (i == workerCount - 1) ? dataLengthInFloats - startFloatIndex : floatsPerWorker;

				tasks.Add(Task.Run(() =>
				{
					// Each worker gets a new reader for thread safety
					using var workerReader = new AudioFileReader(this.FilePath);

					// Set start position in bytes
					workerReader.Position = startFloatIndex * sizeof(float);

					// KORREKTUR: Verwende einen float[]-Puffer
					var floatBuffer = new float[floatsToRead];

					// KORREKTUR: Verwende die korrekte Read-Methode für float[]
					int readFloats = workerReader.Read(floatBuffer, 0, floatsToRead);

					// Write to Data
					Array.Copy(floatBuffer, 0, this.Data, startFloatIndex, readFloats);
				}));
			}

			// Wait for all to finish
			await Task.WhenAll(tasks);

			sw.Stop();
			this.ElapsedLoadingTime = (float) sw.Elapsed.TotalMilliseconds;
		}

		public float ReadBpmTag(string tag = "TBPM", bool set = true)
		{
			// Read bpm metadata if available
			float bpm = 0.0f;
			float roughBpm = 0.0f;

			try
			{
				if (!string.IsNullOrEmpty(this.FilePath) && File.Exists(this.FilePath))
				{
					using (var file = TagLib.File.Create(this.FilePath))
					{
						if (file.Tag.BeatsPerMinute > 0)
						{
							roughBpm = (float) file.Tag.BeatsPerMinute;
						}
						if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
						{
							var id3v2Tag = (TagLib.Id3v2.Tag) file.GetTag(TagLib.TagTypes.Id3v2);

							var tagTextFrame = TagLib.Id3v2.TextInformationFrame.Get(id3v2Tag, tag, false);

							if (tagTextFrame != null && tagTextFrame.Text.Any())
							{
								string bpmString = tagTextFrame.Text.FirstOrDefault() ?? "0,0";
								if (!string.IsNullOrEmpty(bpmString))
								{
									bpmString = bpmString.Replace(',', '.');

									if (float.TryParse(bpmString, NumberStyles.Any, CultureInfo.InvariantCulture, out float parsedBpm))
									{
										bpm = parsedBpm;
									}
								}
							}
							else
							{
								bpm = 0.0f;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Fehler beim Lesen des Tags {tag.ToUpper()}: {ex.Message} ({ex.InnerException?.Message ?? " - "})");
			}

			// Take rough bpm if <= 0.0f
			if (bpm <= 0.0f && roughBpm > 0.0f)
			{
				Console.WriteLine($"No value found for '{tag.ToUpper()}', taking rough BPM value from legacy tag.");
				bpm = roughBpm;
			}

			if (set)
			{
				this.Bpm = bpm;
				if (this.Bpm <= 10)
				{
					this.ReadBpmTagLegacy();
				}
			}

			return bpm;
		}

		public float ReadBpmTagLegacy()
		{
			// Read bpm metadata if available
			float bpm = 0.0f;

			try
			{
				if (!string.IsNullOrEmpty(this.FilePath) && File.Exists(this.FilePath))
				{
					using (var file = TagLib.File.Create(this.FilePath))
					{
						// Check for BPM in standard ID3v2 tag
						if (file.Tag.BeatsPerMinute > 0)
						{
							bpm = (float) file.Tag.BeatsPerMinute;
						}
						// Alternative für spezielle Tags (z.B. TBPM Frame)
						else if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
						{
							var id3v2Tag = (TagLib.Id3v2.Tag) file.GetTag(TagLib.TagTypes.Id3v2);
							var bpmFrame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2Tag, "BPM", false);

							if (bpmFrame != null && float.TryParse(bpmFrame.Text.FirstOrDefault(), out float parsedBpm))
							{
								bpm = parsedBpm;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Fehler beim Lesen der BPM: {ex.Message}");
			}
			this.Bpm = bpm > 0 ? bpm / 100.0f : 0.0f;
			return this.Bpm;
		}

		public void UpdateBpm(float newValue = 0.0f)
		{
			this.Bpm = newValue;
		}

		private System.Timers.Timer SetupTimer(int hz = 100)
		{
			hz = Math.Clamp(hz, 1, 144);
			this.refreshRateHz = hz;

			var timer = new System.Timers.Timer(1000.0 / hz);

			// Register tick event
			timer.Elapsed += (sender, e) =>
			{
				if (this.Data != null && this.Data.Length > 0)
				{
					// Dispose old image if exists
					this.WaveformImage.Dispose();
					this.WaveformImage = this.GetWaveformImageAsync(this.Data, this.WaveformSize.Width, this.WaveformSize.Height).Result;

					GC.Collect();
				}
			};

			timer.AutoReset = false;
			timer.Enabled = true;

			return timer;
		}

		public async Task<byte[]> GetBytesAsync(int maxWorkers = 0)
		{
			if (this.Data == null || this.Data.Length == 0)
			{
				return [];
			}

			// Negative maxWorkers means subtract from total (to have free workers left)
			maxWorkers = CommonStatics.AdjustWorkersCount(maxWorkers);

			int bytesPerSample = this.BitDepth / 8;
			byte[] result = new byte[this.Data.Length * bytesPerSample];

			await Task.Run(() =>
			{
				var options = new ParallelOptions
				{
					MaxDegreeOfParallelism = maxWorkers
				};

				Parallel.For(0, this.Data.Length, options, i =>
				{
					float sample = this.Data[i];

					switch (this.BitDepth)
					{
						case 8:
							result[i] = (byte) (sample * 127f);
							break;

						case 16:
							short sample16 = (short) (sample * short.MaxValue);
							Span<byte> target16 = result.AsSpan(i * 2, 2);
							BitConverter.TryWriteBytes(target16, sample16);
							break;

						case 24:
							int sample24 = (int) (sample * 8_388_607f); // 2^23 - 1
							Span<byte> target24 = result.AsSpan(i * 3, 3);
							target24[0] = (byte) sample24;
							target24[1] = (byte) (sample24 >> 8);
							target24[2] = (byte) (sample24 >> 16);
							break;

						case 32:
							Span<byte> target32 = result.AsSpan(i * 4, 4);
							BitConverter.TryWriteBytes(target32, sample);
							break;
					}
				});
			});

			return result;
		}

		public byte[] GetBytes()
		{
			int bytesPerSample = this.BitDepth / 8;
			byte[] bytes = new byte[this.Data.Length * bytesPerSample];

			Parallel.For(0, this.Data.Length, i =>
			{
				switch (this.BitDepth)
				{
					case 8:
						bytes[i] = (byte) (this.Data[i] * 127);
						break;
					case 16:
						short sample16 = (short) (this.Data[i] * short.MaxValue);
						Buffer.BlockCopy(BitConverter.GetBytes(sample16), 0, bytes, i * bytesPerSample, bytesPerSample);
						break;
					case 24:
						int sample24 = (int) (this.Data[i] * 8388607);
						Buffer.BlockCopy(BitConverter.GetBytes(sample24), 0, bytes, i * bytesPerSample, 3);
						break;
					case 32:
						Buffer.BlockCopy(BitConverter.GetBytes(this.Data[i]), 0, bytes, i * bytesPerSample, bytesPerSample);
						break;
				}
			});

			return bytes;
		}

		public async Task<IEnumerable<float[]>> GetChunks(int size = 2048, float overlap = 0.5f, bool keepData = false, int maxWorkers = 0)
		{
			// Input Validation (sync part for fast fail)
			if (this.Data == null || this.Data.Length == 0)
			{
				return [];
			}

			if (size <= 0 || overlap < 0 || overlap >= 1)
			{
				return [];
			}

			// Calculate chunk metrics (sync)
			this.ChunkSize = size;
			this.OverlapSize = (int) (size * overlap);
			int step = size - this.OverlapSize;
			int numChunks = (this.Data.Length - size) / step + 1;

			// Prepare result array
			float[][] chunks = new float[numChunks][];

			await Task.Run(() =>
			{
				// Parallel processing with optimal worker count
				Parallel.For(0, numChunks, new ParallelOptions
				{
					MaxDegreeOfParallelism = CommonStatics.AdjustWorkersCount(maxWorkers)
				}, i =>
				{
					int sourceOffset = i * step;
					float[] chunk = new float[size];
					Buffer.BlockCopy( // Faster than Array.Copy for float[]
						src: this.Data,
						srcOffset: sourceOffset * sizeof(float),
						dst: chunk,
						dstOffset: 0,
						count: size * sizeof(float));
					chunks[i] = chunk;
				});
			});

			// Cleanup if requested
			if (!keepData)
			{
				this.Data = [];
			}

			return chunks;
		}

		public async Task AggregateStretchedChunks(IEnumerable<float[]> chunks, bool keepPointer = false, int maxWorkers = 0)
		{
			if (chunks == null || chunks.LongCount() <= 0)
			{
				return;
			}

			// Pointer
			this.Pointer = keepPointer ? this.Pointer : IntPtr.Zero;

			// Pre-calculate all values that don't change
			double stretchFactor = this.StretchFactor;
			int chunkSize = this.ChunkSize;
			int overlapSize = this.OverlapSize;
			int originalHopSize = chunkSize - overlapSize;
			int stretchedHopSize = (int) Math.Round(originalHopSize * stretchFactor);
			int outputLength = (chunks.Count() - 1) * stretchedHopSize + chunkSize;

			// Create window function (cosine window)
			double[] window = await Task.Run(() =>
				Enumerable.Range(0, chunkSize)
						  .Select(i => 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (chunkSize - 1))))
						  .ToArray()  // Korrekte Methode ohne Punkt
			).ConfigureAwait(false);

			// Initialize accumulators in parallel
			double[] outputAccumulator = new double[outputLength];
			double[] weightSum = new double[outputLength];

			await Task.Run(() =>
			{
				var parallelOptions = new ParallelOptions
				{
					MaxDegreeOfParallelism = CommonStatics.AdjustWorkersCount(maxWorkers)
				};

				// Phase 1: Process chunks in parallel
				Parallel.For(0, chunks.LongCount(), parallelOptions, chunkIndex =>
				{
					var chunk = chunks.ElementAt((int) chunkIndex);
					int offset = (int) chunkIndex * stretchedHopSize;

					for (int j = 0; j < Math.Min(chunkSize, chunk.Length); j++)
					{
						int idx = offset + j;
						if (idx >= outputLength) break;

						double windowedSample = chunk[j] * window[j];

						// Using Interlocked for thread-safe accumulation
						Interlocked.Exchange(ref outputAccumulator[idx], outputAccumulator[idx] + windowedSample);
						Interlocked.Exchange(ref weightSum[idx], weightSum[idx] + window[j]);
					}
				});

				// Phase 2: Normalize results
				float[] finalOutput = new float[outputLength];
				Parallel.For(0, outputLength, parallelOptions, i =>
				{
					finalOutput[i] = weightSum[i] > 1e-6
						? (float) (outputAccumulator[i] / weightSum[i])
						: 0.0f;
				});

				// Final assignment (thread-safe)
				this.Data = finalOutput;
			}).ConfigureAwait(true);
		}


		// Playback
		public async Task Play(CancellationToken cancellationToken, Action? onPlaybackStopped = null, float? initialVolume = null)
		{
			initialVolume ??= this.Volume / 100f;

			// Stop any existing playback and cleanup
			this.waveformUpdateTimer.Stop();
			this.player?.Stop();
			this.player?.Dispose();

			if (this.Data == null || this.Data.Length == 0)
			{
				return;
			}

			try
			{
				// Initialize player with cancellation support
				this.player = new WaveOutEvent
				{
					Volume = initialVolume ?? 1.0f,
					DesiredLatency = 100 // Lower latency for better responsiveness
				};

				// Async audio data preparation with cancellation
				byte[] bytes = await this.GetBytesAsync();

				var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(this.SampleRate, this.Channels);
				var memoryStream = new MemoryStream(bytes);
				var audioStream = new RawSourceWaveStream(memoryStream, waveFormat);

				// Setup playback stopped handler
				this.player.PlaybackStopped += (sender, args) =>
				{
					try
					{
						onPlaybackStopped?.Invoke();
					}
					finally
					{
						audioStream.Dispose();
						memoryStream.Dispose();
						this.player?.Dispose();
					}
				};

				// Register cancellation callback
				using (cancellationToken.Register(() =>
				{
					this.player?.Stop();
					this.waveformUpdateTimer.Stop();
				}))
				{
					// Start playback in background
					this.player.Init(audioStream);
					// this.waveformUpdateTimer.Start();

					// Non-blocking play (fire-and-forget with error handling)
					_ = Task.Run(() =>
					{
						try
						{
							this.player.Play();
							while (this.player.PlaybackState == PlaybackState.Playing)
							{
								cancellationToken.ThrowIfCancellationRequested();
								Thread.Sleep(50); // Reduce CPU usage
							}
						}
						catch (OperationCanceledException)
						{
							// Cleanup handled by cancellation callback
						}
						catch (Exception ex)
						{
							Debug.WriteLine($"Playback error: {ex.Message}");
						}
					}, cancellationToken);
				}

				// Return immediately (non-blocking)
			}
			catch (OperationCanceledException)
			{
				Debug.WriteLine("Playback preparation was canceled");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Playback initialization failed: {ex.Message}");
				this.player?.Dispose();
				throw;
			}
		}

		public void Stop()
		{
			this.waveformUpdateTimer.Stop();
			this.player.Stop();
		}

		public async Task Normalize(float maxAmplitude = 1.0f)
		{
			if (this.Data == null || this.Data.Length == 0)
			{
				return;
			}

			// Phase 1: Find global maximum (parallel + async)
			float globalMax = await Task.Run(() =>
			{
				float max = 0f;
				Parallel.For(0, this.Data.Length,
					() => 0f,
					(i, _, localMax) => Math.Max(Math.Abs(this.Data[i]), localMax),
					localMax => { lock (this) { max = Math.Max(max, localMax); } }
				);
				return max;
			}).ConfigureAwait(false);

			if (globalMax == 0f)
			{
				return;
			}

			// Phase 2: Apply scaling (parallel + async)
			float scale = maxAmplitude / globalMax;
			await Task.Run(() =>
			{
				Parallel.For(0, this.Data.Length, i =>
				{
					this.Data[i] *= scale;
				});
			}).ConfigureAwait(false);
		}

		public async Task<Image<Rgba32>> GetWaveformImageAsync(float[]? data, int width = 720, int height = 480,
			int samplesPerPixel = 128, float amplifier = 1.0f, long offset = 0,
			SixLabors.ImageSharp.Color? graphColor = null, SixLabors.ImageSharp.Color? backgroundColor = null, bool smoothEdges = true, int workerCount = -2)
		{
			// Normalize image dimensions
			width = Math.Max(100, width);
			height = Math.Max(100, height);

			// Normalize colors & get rgba values
			graphColor ??= SixLabors.ImageSharp.Color.BlueViolet;
			backgroundColor ??= SixLabors.ImageSharp.Color.White;

			// New result image + color fill
			Image<Rgba32> image = new(width, height);
			await Task.Run(() =>
			{
				image.Mutate(ctx => ctx.BackgroundColor(backgroundColor.Value));
			});

			// Verify data
			data ??= this.Data;
			if (data == null || data.LongLength <= 0)
			{
				return image;
			}

			// Adjust offset if necessary
			if (data.Length <= offset)
			{
				offset = 0;
			}

			workerCount = CommonStatics.AdjustWorkersCount(workerCount);

			// Calculate the number of samples to process -> take array from data
			long totalSamples = Math.Min(data.Length - offset, width * samplesPerPixel);
			if (totalSamples <= 0)
			{
				return image;
			}

			float[] samples = new float[totalSamples];
			Array.Copy(data, offset, samples, 0, totalSamples);

			// Split into concurrent chunks for each worker (even count, last chunk fills up with zeros)
			int chunkSize = (int) Math.Ceiling((double) totalSamples / workerCount);
			ConcurrentDictionary<int, float[]> chunks = await Task.Run(() =>
			{
				ConcurrentDictionary<int, float[]> chunkDict = new();
				Parallel.For(0, workerCount, i =>
				{
					int start = i * chunkSize;
					int end = Math.Min(start + chunkSize, (int) totalSamples);
					if (start < totalSamples)
					{
						float[] chunk = new float[chunkSize];
						Array.Copy(samples, start, chunk, 0, end - start);
						chunkDict[i] = chunk;
					}
				});
				return chunkDict;
			});

			// Draw each chunk at the corresponding position with amplification per worker on bitmap
			Parallel.ForEach(chunks, parallelOptions: new ParallelOptions { MaxDegreeOfParallelism = workerCount }, kvp =>
			{
				int workerIndex = kvp.Key;
				float[] chunk = kvp.Value;
				int xStart = workerIndex * (width / workerCount);
				int xEnd = (workerIndex + 1) * (width / workerCount);
				if (xEnd > width)
				{
					xEnd = width;
				}
				for (int x = xStart; x < xEnd; x++)
				{
					int sampleIndex = (x - xStart) * samplesPerPixel;
					if (sampleIndex < chunk.Length)
					{
						float sampleValue = chunk[sampleIndex] * amplifier;
						int yPos = (int) ((sampleValue + 1.0f) / 2.0f * height);
						yPos = Math.Clamp(yPos, 0, height - 1);

						// Draw vertical line for this sample
						for (int y = 0; y < height; y++)
						{
							if (y == yPos)
							{
								image[x, y] = graphColor.Value;
							}
							else
							{
								image[x, y] = backgroundColor.Value;
							}

							// Optionally: Apply anti-aliasing for smoother edges
							if (smoothEdges && y > 0 && y < height - 1)
							{
								float edgeFactor = (float) (1.0 - Math.Abs(y - yPos) / (height / 2.0));
								if (edgeFactor > 0.1f)
								{
									image[x, y] = new Rgba32(
										(byte) (graphColor.Value.ToPixel<Rgba32>().R * edgeFactor + backgroundColor.Value.ToPixel<Rgba32>().R * (1 - edgeFactor)),
										(byte) (graphColor.Value.ToPixel<Rgba32>().G * edgeFactor + backgroundColor.Value.ToPixel<Rgba32>().G * (1 - edgeFactor)),
										(byte) (graphColor.Value.ToPixel<Rgba32>().B * edgeFactor + backgroundColor.Value.ToPixel<Rgba32>().B * (1 - edgeFactor)),
										(byte) (graphColor.Value.ToPixel<Rgba32>().A * edgeFactor + backgroundColor.Value.ToPixel<Rgba32>().A * (1 - edgeFactor))
									);
								}
							}
						}
					}
				}
			});

			// Wait for all tasks to complete
			await Task.Yield();

			// Return the generated waveform image
			return image;
		}

		// Export
		public async Task<string?> Export(string outPath = "")
		{
			string baseFileName = $"{this.Name} [{this.Bpm:F1}]";

			// Validate and prepare output directory
			outPath = (await this.PrepareOutputPath(outPath, baseFileName)) ?? Path.GetTempPath();
			if (string.IsNullOrEmpty(outPath))
			{
				return null;
			}

			try
			{
				// Process audio data in parallel
				byte[] bytes = await this.GetBytesAsync();

				var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
					this.SampleRate,
					this.Channels);

				await using (var memoryStream = new MemoryStream(bytes))
				await using (var audioStream = new RawSourceWaveStream(memoryStream, waveFormat))
				await using (var fileStream = new FileStream(
					outPath,
					FileMode.Create,
					FileAccess.Write,
					FileShare.None,
					bufferSize: 4096,
					useAsync: true))
				{
					await Task.Run(() =>
					{
						WaveFileWriter.WriteWavFileToStream(fileStream, audioStream);
					});
				}

				// Add BPM metadata if needed
				if (this.Bpm > 0.0f)
				{
					await this.AddBpmTag(outPath, this.Bpm)
						.ConfigureAwait(false);
				}

				return outPath;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Audio export failed: {ex.Message}");
				return null;
			}
		}

		private async Task<string?> PrepareOutputPath(string outPath, string baseFileName)
		{
			// Check directory existence asynchronously
			if (!string.IsNullOrEmpty(outPath))
			{
				var dirExists = await Task.Run(() => Directory.Exists(outPath))
										.ConfigureAwait(false);
				if (!dirExists)
				{
					outPath = Path.GetDirectoryName(outPath) ?? string.Empty;
				}
			}

			// Fallback to temp directory if needed
			if (string.IsNullOrEmpty(outPath) ||
				!await Task.Run(() => Directory.Exists(outPath))
						  .ConfigureAwait(false))
			{
				outPath = Path.Combine(
					Path.GetTempPath(),
					$"{this.Name}_[{this.Bpm:F2}].wav");
			}

			// Build final file path
			if (Path.HasExtension(outPath))
			{
				return outPath;
			}

			return Path.Combine(outPath, $"{baseFileName}.wav");
		}

		private async Task AddBpmTag(string filePath, float bpm)
		{
			try
			{
				await Task.Run(() =>
				{
					using var file = TagLib.File.Create(filePath);
					file.Tag.BeatsPerMinute = (uint) (bpm * 100);
					file.Save();
				}).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"BPM tag writing failed: {ex.Message}");
			}
		}

		public async Task<string> GetBase64Async(string format = "png")
		{
			var img = this.WaveformImage;

			using MemoryStream ms = new();
			switch (format.ToLowerInvariant())
			{
				case "png":
					await img.SaveAsPngAsync(ms);
					break;
				case "jpg":
				case "jpeg":
					await img.SaveAsJpegAsync(ms);
					break;
				default:
					await img.SaveAsBmpAsync(ms);
					break;
			}

			return Convert.ToBase64String(ms.ToArray());
		}

		public string GetBase64(string format = "png")
		{
			var img = this.WaveformImage;

			using MemoryStream ms = new();
			switch (format.ToLowerInvariant())
			{
				case "png":
					img.SaveAsPng(ms);
					break;
				case "jpg":
				case "jpeg":
					img.SaveAsJpeg(ms);
					break;
				default:
					img.SaveAsBmp(ms);
					break;
			}

			return Convert.ToBase64String(ms.ToArray());
		}
	}
}
