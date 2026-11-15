using NAudio.Wave;

namespace GlitterVis;

/// <summary>
/// Listens to whatever the PC is playing (Windows "loopback" capture of the default output), and turns it into
/// numbers the scenes use: 64 log-spaced spectrum bands, bass / mid / treble energy, overall level, a beat pulse and
/// a tempo estimate. Thread-safe enough for one reader (the render loop) and one writer (the audio thread).
/// </summary>
public sealed class Audio : IDisposable
{
    public const int Bands = 64;
    const int N = 2048;                       // FFT size
    readonly float[] ring = new float[N * 2];
    int ringPos;
    readonly object gate = new();
    WasapiLoopbackCapture cap;
    int sampleRate = 48000;

    public readonly float[] Spectrum = new float[Bands];   // 0..1, smoothed, for drawing
    public float Bass, Mid, Treble, Level;                 // 0..~1.5, smoothed
    public float Beat;                                     // jumps to 1 on a beat, decays
    public int BeatCount;
    public double LastBeatTime = -10;
    public float Bpm = 0;                                  // 0 = not known yet
    public bool Silent = true;

    readonly float[] bassHist = new float[43];             // about one second of bass energy at 43 frames/s
    int histPos;
    readonly List<double> beatTimes = new();
    float avgLevel = 0.05f;

    public void Start()
    {
        try
        {
            cap = new WasapiLoopbackCapture();
            sampleRate = cap.WaveFormat.SampleRate;
            cap.DataAvailable += OnData;
            cap.StartRecording();
        }
        catch (Exception e)
        {
            Log.Write("audio capture failed: " + e.Message);
        }
    }

    void OnData(object s, WaveInEventArgs e)
    {
        var fmt = cap.WaveFormat;
        int ch = fmt.Channels;
        if (fmt.Encoding != WaveFormatEncoding.IeeeFloat && !(fmt is WaveFormatExtensible)) return;
        int frames = e.BytesRecorded / (4 * ch);
        lock (gate)
        {
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int c = 0; c < ch; c++) sum += BitConverter.ToSingle(e.Buffer, (i * ch + c) * 4);
                ring[ringPos] = sum / ch;
                ringPos = (ringPos + 1) % ring.Length;
            }
        }
    }

    readonly float[] re = new float[N], im = new float[N], win = MakeWindow();
    static float[] MakeWindow() { var w = new float[N]; for (int i = 0; i < N; i++) w[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / (N - 1)); return w; }

    /// <summary>Call once per rendered frame with the frame time step (seconds) and the clock (seconds).</summary>
    public void Update(float dt, double now)
    {
        lock (gate)
        {
            int start = (ringPos - N + ring.Length) % ring.Length;
            for (int i = 0; i < N; i++) { re[i] = ring[(start + i) % ring.Length] * win[i]; im[i] = 0; }
        }
        Fft(re, im);
        float binHz = (float)sampleRate / N;
        float bass = 0, mid = 0, treb = 0, total = 0;
        var raw = new float[Bands];
        for (int b = 0; b < Bands; b++)
        {
            // log-spaced bands from 30 Hz to 16 kHz
            double f0 = 30 * Math.Pow(16000.0 / 30, (double)b / Bands), f1 = 30 * Math.Pow(16000.0 / 30, (double)(b + 1) / Bands);
            int i0 = Math.Max(1, (int)(f0 / binHz)), i1 = Math.Max(i0 + 1, (int)(f1 / binHz));
            float m = 0;
            for (int i = i0; i < i1 && i < N / 2; i++) m = MathF.Max(m, MathF.Sqrt(re[i] * re[i] + im[i] * im[i]));
            raw[b] = m;
        }
        for (int b = 0; b < Bands; b++)
        {
            float v = MathF.Log10(1 + raw[b] * 4) * 0.9f;                // perceptual-ish compression
            float target = Math.Clamp(v, 0, 1.5f);
            Spectrum[b] += (target - Spectrum[b]) * (target > Spectrum[b] ? 0.6f : 0.12f);   // fast attack, slow release
            if (b < 10) bass += target; else if (b < 40) mid += target; else treb += target;
            total += target;
        }
        bass /= 10; mid /= 30; treb /= 24; total /= Bands;
        Smooth(ref Bass, bass, dt); Smooth(ref Mid, mid, dt); Smooth(ref Treble, treb, dt); Smooth(ref Level, total, dt);
        avgLevel += (total - avgLevel) * Math.Min(1, dt * 0.3f);
        Silent = total < 0.01f;

        // Beat: this frame's bass well above the last second's average, and not too soon after the previous beat.
        float avg = 0; foreach (var v in bassHist) avg += v; avg /= bassHist.Length;
        float var_ = 0; foreach (var v in bassHist) var_ += (v - avg) * (v - avg); var_ /= bassHist.Length;
        float thresh = avg * 1.25f + MathF.Sqrt(var_) * 0.8f + 0.02f;
        bassHist[histPos] = bass; histPos = (histPos + 1) % bassHist.Length;
        if (!Silent && bass > thresh && now - LastBeatTime > 0.28)
        {
            Beat = 1; BeatCount++;
            beatTimes.Add(now); if (beatTimes.Count > 16) beatTimes.RemoveAt(0);
            LastBeatTime = now;
            if (beatTimes.Count >= 8)
            {
                var gaps = new List<double>();
                for (int i = 1; i < beatTimes.Count; i++) gaps.Add(beatTimes[i] - beatTimes[i - 1]);
                gaps.Sort();
                double g = gaps[gaps.Count / 2];
                if (g > 0.25 && g < 1.5) Bpm = (float)(60 / g);
            }
        }
        Beat = MathF.Max(0, Beat - dt * 3.2f);
    }

    static void Smooth(ref float x, float target, float dt) => x += (target - x) * Math.Min(1, dt * 8);

    static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    float tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                    float nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
        float scale = 2f / n;
        for (int i = 0; i < n; i++) { re[i] *= scale; im[i] *= scale; }
    }

    public void Dispose() { try { cap?.StopRecording(); cap?.Dispose(); } catch { } }
}
