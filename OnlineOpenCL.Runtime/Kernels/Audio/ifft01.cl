typedef struct {
    float x;
    float y;
} Vector2;

#define M_PI 3.14159265358979323846f

__kernel void ifft01(
    __global const Vector2* inputComplexes,
    __global float* outputSamples,
    int chunkSize,
    int overlapSize)
{
    int chunkIndex = get_global_id(0);
    int baseIndex = chunkIndex * chunkSize;

    // 1. Init working buffer (complex)
    __global Vector2* buffer = (__global Vector2*)inputComplexes;

    // 2. IFFT butterfly
    for (int s = 1; s < chunkSize; s <<= 1) {
        int m = s << 1;
        float theta = 2.0f * M_PI / (float)m; // Positive sign for IFFT

        for (int k = 0; k < chunkSize; k += m) {
            for (int j = 0; j < s; j++) {
                int idx1 = baseIndex + k + j;
                int idx2 = idx1 + s;

                Vector2 u = buffer[idx1];
                Vector2 v = buffer[idx2];

                float t_real = cos(j * theta) * v.x - sin(j * theta) * v.y;
                float t_imag = sin(j * theta) * v.x + cos(j * theta) * v.y;

                buffer[idx1].x = u.x + t_real;
                buffer[idx1].y = u.y + t_imag;

                buffer[idx2].x = u.x - t_real;
                buffer[idx2].y = u.y - t_imag;
            }
        }
    }

    // 3. Bit reversal
    for (int i = 0; i < chunkSize; i++) {
        int j = 0, n = i;
        for (int b = chunkSize >> 1; b > 0; b >>= 1) {
            j = (j << 1) | (n & 1);
            n >>= 1;
        }
        if (j > i) {
            int idx1 = baseIndex + i;
            int idx2 = baseIndex + j;
            Vector2 tmp = buffer[idx1];
            buffer[idx1] = buffer[idx2];
            buffer[idx2] = tmp;
        }
    }

    // 4. Output real part divided by chunkSize (normalization)
    for (int i = 0; i < chunkSize; i++) {
        outputSamples[baseIndex + i] = buffer[baseIndex + i].x / (float)chunkSize;
    }
}
