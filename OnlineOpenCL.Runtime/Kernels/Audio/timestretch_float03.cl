typedef struct {
    float x;
    float y;
} Vector2;

__kernel void timestretch_float03(
    __global const Vector2* input,   // komplexe FFT-Chunks, linear aneinandergereiht
    __global Vector2* output,        // Output: gestreckte FFTs
    const int chunkSize,            // z.B. 1024
    const int overlapSize,          // z.B. 512
    const int samplerate,           // z.B. 44100
    const float factor              // z.B. 1.5 = langsamer (gestreckt)
) {
    int bin = get_global_id(0); // Bin innerhalb eines Chunks (0..chunkSize-1)
    int chunk = get_global_id(1); // Welcher Chunk

    int hopIn = chunkSize - overlapSize;
    int hopOut = (int)(hopIn * factor + 0.5f);

    int totalBins = chunkSize;
    int totalChunks = get_num_groups(1); // Anzahl Chunks ergibt sich aus global size

    int idx = chunk * chunkSize + bin;
    int prevIdx = (chunk > 0) ? (chunk - 1) * chunkSize + bin : idx;

    // Nur gültige Indizes verarbeiten
    if (bin >= totalBins || chunk == 0) {
        output[idx] = input[idx];
        return;
    }

    // Aktuelles und vorheriges komplexes Spektrum
    Vector2 cur = input[idx];
    Vector2 prev = input[prevIdx];

    // Phasen extrahieren
    float phaseCur = atan2(cur.y, cur.x);
    float phasePrev = atan2(prev.y, prev.x);

    // Amplitude bleibt gleich
    float mag = hypot(cur.x, cur.y);

    // Phasendifferenz (wrapped zwischen -PI und PI)
    float deltaPhase = phaseCur - phasePrev;

    // Frequenz pro Bin (in Hz)
    float freqPerBin = (float)samplerate / (float)chunkSize;
    float expectedPhaseAdv = 2.0f * M_PI * freqPerBin * bin * hopIn / samplerate;

    // Phasendifferenz kompensieren
    float delta = deltaPhase - expectedPhaseAdv;

    // Bring delta in -π..π
    delta = fmod(delta + M_PI, 2.0f * M_PI) - M_PI;

    // Neue Phase: vorherige Phase + korrigierter delta * factor
    float phaseOut = phasePrev + expectedPhaseAdv + delta * factor;

    // Output = neue komplexe Zahl
    output[idx].x = mag * cos(phaseOut);
    output[idx].y = mag * sin(phaseOut);
}
