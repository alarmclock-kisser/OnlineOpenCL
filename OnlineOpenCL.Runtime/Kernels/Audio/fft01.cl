typedef struct {
    float x;
    float y;
} Vector2;

#define M_PI 3.14159265358979323846f

__kernel void fft01(
    __global const float* inputSamples,
    __global Vector2* outputComplexes,
    int chunkSize,
    int overlapSize)
{
    int chunkIndex = get_global_id(0);
    int baseIndex = chunkIndex * chunkSize;

    // 1. Init complex array
    for (int i = 0; i < chunkSize; i++) {
        outputComplexes[baseIndex + i].x = inputSamples[baseIndex + i];
        outputComplexes[baseIndex + i].y = 0.0f;
    }

    barrier(CLK_GLOBAL_MEM_FENCE);

    // 2. FFT butterfly
    for (int s = 1; s < chunkSize; s <<= 1) {
        int m = s << 1;
        float theta = -2.0f * M_PI / (float)m;

        for (int k = 0; k < chunkSize; k += m) {
            for (int j = 0; j < s; j++) {
                int idx1 = baseIndex + k + j;
                int idx2 = idx1 + s;

                Vector2 u = outputComplexes[idx1];
                Vector2 v = outputComplexes[idx2];

                float t_real = cos(j * theta) * v.x - sin(j * theta) * v.y;
                float t_imag = sin(j * theta) * v.x + cos(j * theta) * v.y;

                outputComplexes[idx1].x = u.x + t_real;
                outputComplexes[idx1].y = u.y + t_imag;

                outputComplexes[idx2].x = u.x - t_real;
                outputComplexes[idx2].y = u.y - t_imag;
            }
        }
    }

    // 3. Bit reversal
    for (int i = 0; i < chunkSize; i++) {
        int j = 0, bit = 0;
        int n = i;
        for (int b = chunkSize >> 1; b > 0; b >>= 1) {
            j = (j << 1) | (n & 1);
            n >>= 1;
        }
        if (j > i) {
            int idx1 = baseIndex + i;
            int idx2 = baseIndex + j;
            Vector2 tmp = outputComplexes[idx1];
            outputComplexes[idx1] = outputComplexes[idx2];
            outputComplexes[idx2] = tmp;
        }
    }
}
