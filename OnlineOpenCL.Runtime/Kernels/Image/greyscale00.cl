__kernel void greyscale00(
    __global uchar* pixels,
    int width,
    int height,
    int bitdepth,
    int channels,
    float intensity
)
{
    // Aktuellen Pixel-Index berechnen
    int x = get_global_id(0);
    int y = get_global_id(1);

    if (x >= width || y >= height) return;

    // Position im Puffer berechnen (abh�ngig von Bit-Tiefe & Kan�len)
    int pixelIndex = (y * width + x) * channels * (bitdepth / 8);
    
    // Nur f�r 8-Bit pro Kanal (typisch f�r RGB/RGBA-Bilder)
    if (bitdepth == 8)
    {
        // RGB-Werte lesen (ignoriert Alpha, falls vorhanden)
        uchar r = pixels[pixelIndex];
        uchar g = pixels[pixelIndex + 1];
        uchar b = pixels[pixelIndex + 2];
        
        // Grauwert berechnen (Luminanz-Methode: 0.299*R + 0.587*G + 0.114*B)
        float grey = 0.299f * r + 0.587f * g + 0.114f * b;
        
        // Mit Intensit�t skalieren (clamp auf 0-255)
        grey = grey * intensity;
        grey = fmin(fmax(grey, 0.0f), 255.0f);
        
        // Grauwert auf alle Kan�le schreiben (au�er Alpha)
        pixels[pixelIndex] = (uchar)grey;
        pixels[pixelIndex + 1] = (uchar)grey;
        pixels[pixelIndex + 2] = (uchar)grey;
        
        // Alpha-Kanal unver�ndert lassen (falls channels == 4)
    }
}