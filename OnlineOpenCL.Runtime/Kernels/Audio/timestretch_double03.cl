#pragma OPENCL EXTENSION cl_khr_fp64 : enable // Optional: Nur um sicherzustellen, dass double-Literale korrekt behandelt werden

typedef struct {
    float x; // Components remain float
    float y; // Components remain float
} Vector2;

#ifndef M_PI
#define M_PI 3.14159265358979323846f // M_PI remains float
#endif

__kernel void timestretch_double03(
    __global const Vector2* input,       // Complex FFT chunks
    __global Vector2* output,            // Output: stretched FFTs
    const int chunkSize,                 // e.g. 1024
    const int overlapSize,               // e.g. 512
    const int samplerate,                // e.g. 44100
    const double factor                  // IMPORTANT: factor is now double
) {
    int bin = get_global_id(0); // Bin index
    int chunk = get_global_id(1); // Chunk index

    int hopIn = chunkSize - overlapSize;
    // hopOut berechnung mit double factor, dann zu int gerundet
    int hopOut = (int)((double)hopIn * factor + 0.5); 

    int totalBins = chunkSize;
    // totalChunks = get_num_groups(1); // This implicitly means global_size(1) in current OpenCL C

    int idx = chunk * chunkSize + bin;
    // Previous index for phase comparison (from previous output chunk)
    int prevIdx = (chunk > 0) ? (chunk - 1) * chunkSize + bin : idx; 

    // Handle initial chunk and invalid bins
    if (bin >= totalBins || chunk == 0) {
        // For chunk 0, simply copy (no previous phase to compare)
        // Or if bin is out of bounds for some reason (though should be constrained by global_size(0))
        output[idx] = input[idx];
        return;
    }

    // Current and previous complex spectrum values
    Vector2 cur = input[idx];
    Vector2 prev = input[prevIdx]; // Assuming this read is safe (prev chunk already processed or state managed)

    // Extract phases (remain float)
    float phaseCur = atan2(cur.y, cur.x);
    float phasePrev = atan2(prev.y, prev.x);

    // Magnitude (remains float)
    float mag = hypot(cur.x, cur.y);

    // Phase difference (wrapped between -PI and PI)
    float deltaPhase = phaseCur - phasePrev;

    // Expected phase advance per bin (remains float calculation)
    float freqPerBin = (float)samplerate / (float)chunkSize;
    float expectedPhaseAdv = 2.0f * M_PI * freqPerBin * bin * hopIn / (float)samplerate;

    // Compensate for phase difference
    float delta = deltaPhase - expectedPhaseAdv;

    // Bring delta into -PI..PI range
    delta = fmod(delta + M_PI, 2.0f * M_PI) - M_PI;

    // Calculate new phase using double factor for the 'delta' scaling
    float phaseOut = phasePrev + expectedPhaseAdv + (float)((double)delta * factor);

    // Output new complex number with calculated magnitude and phase
    output[idx].x = mag * cos(phaseOut);
    output[idx].y = mag * sin(phaseOut);
}