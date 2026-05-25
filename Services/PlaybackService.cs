// 音频播放引擎 —— 整曲内存解码 + NAudio 播放 + 4096 点 FFT 频谱
using NAudio.Wave;

namespace MusicPlayer.Services;

public enum PlaybackState { Stopped, Playing, Paused }

public class PlaybackService : IDisposable
{
    private WaveOutEvent? _wavePlayer;
    private AudioFileReader? _playbackReader;
    private float[]? _decodedAudio;      // 整首歌解码到内存（立体声交织）
    private System.Diagnostics.Stopwatch? _playStopwatch; // 精确播放时间计时
    private int _channels = 2;
    private int _sampleRate = 44100;
    private float _volume = 0.8f;
    private bool _disposed;

    private const int FftLength = 4096;

    // 预分配 FFT 工作数组，避免每帧 new（消除 GC 卡顿）
    private readonly float[] _fftIn = new float[FftLength];
    private readonly NAudio.Dsp.Complex[] _fftComplex = new NAudio.Dsp.Complex[FftLength];
    private readonly float[] _magnitudes = new float[FftLength / 2];

    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    // 上次 Seek/加载时的文件位置偏移
    private double _positionOffset;

    public double CurrentPositionSeconds => (_playStopwatch?.Elapsed.TotalSeconds ?? 0) + _positionOffset;
    public double TotalDurationSeconds { get; private set; }
    public string? CurrentFilePath { get; private set; }

    public int SampleRate => _sampleRate;
    public int BitsPerSample { get; private set; }
    public int Channels => _channels;
    public string FormatInfo =>
        SampleRate > 0
            ? $"{SampleRate / 1000.0:F1}kHz / {BitsPerSample}bit / {(_channels == 2 ? "立体声" : _channels == 1 ? "单声道" : $"{_channels}声道")}"
            : string.Empty;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_playbackReader != null) _playbackReader.Volume = _volume;
        }
    }

    public event Action<PlaybackState>? StateChanged;
    public event Action<double>? PositionChanged;
    public event Action? PlaybackFinished;

    private System.Timers.Timer? _positionTimer;
    private readonly System.Threading.SynchronizationContext? _uiContext;

    public PlaybackService()
    {
        _uiContext = System.Threading.SynchronizationContext.Current;

        _positionTimer = new System.Timers.Timer(100);
        _positionTimer.Elapsed += (_, _) =>
        {
            if (State == PlaybackState.Playing)
                PositionChanged?.Invoke(CurrentPositionSeconds);
        };
    }

    public void Load(string filePath)
    {
        CleanupPlayer();

        _playStopwatch = new System.Diagnostics.Stopwatch();
        _positionOffset = 0;

        // 1. 用临时解码器把整首歌读到内存（给 FFT 用），读完即销毁
        using (var tempReader = new AudioFileReader(filePath))
        {
            _channels = tempReader.WaveFormat.Channels;
            _sampleRate = tempReader.WaveFormat.SampleRate;
            BitsPerSample = tempReader.WaveFormat.BitsPerSample;

            var list = new System.Collections.Generic.List<float>(_sampleRate * 600 * _channels);
            var buf = new float[8192];
            int n;
            while ((n = tempReader.Read(buf, 0, buf.Length)) > 0)
                for (int i = 0; i < n; i++) list.Add(buf[i]);
            _decodedAudio = list.ToArray();
        }

        // 2. 创建全新的播放解码器（不受前一步 EOF 影响）
        _playbackReader = new AudioFileReader(filePath) { Volume = _volume };

        // 3. 创建播放器
        _wavePlayer = new WaveOutEvent();
        _wavePlayer.PlaybackStopped += OnPlaybackStopped;
        _wavePlayer.Init(_playbackReader);

        CurrentFilePath = filePath;
        TotalDurationSeconds = _playbackReader.TotalTime.TotalSeconds;
    }

    private void CleanupPlayer()
    {
        _positionTimer?.Stop();

        if (_wavePlayer != null)
        {
            _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
            try { _wavePlayer.Stop(); } catch { }
            _wavePlayer.Dispose();
            _wavePlayer = null;
        }

        _playbackReader?.Dispose();
        _playbackReader = null;
        _decodedAudio = null;
        State = PlaybackState.Stopped;
    }

    public void Play()
    {
        if (_wavePlayer == null) return;
        _wavePlayer.Play();
        _playStopwatch?.Start();
        State = PlaybackState.Playing;
        _positionTimer?.Start();
        StateChanged?.Invoke(State);
    }

    public void Pause()
    {
        if (_wavePlayer == null) return;
        _wavePlayer.Pause();
        _playStopwatch?.Stop();
        State = PlaybackState.Paused;
        _positionTimer?.Stop();
        StateChanged?.Invoke(State);
    }

    public void Stop()
    {
        if (_wavePlayer != null && State != PlaybackState.Stopped)
        {
            try { _wavePlayer.Stop(); } catch { }
        }
        _playStopwatch?.Reset();
        _positionOffset = 0;
        State = PlaybackState.Stopped;
        _positionTimer?.Stop();
        StateChanged?.Invoke(State);
    }

    public void Seek(double seconds)
    {
        if (_playbackReader == null) return;
        seconds = Math.Clamp(seconds, 0, TotalDurationSeconds);
        _playbackReader.CurrentTime = TimeSpan.FromSeconds(seconds);
        _positionOffset = seconds;
        _playStopwatch?.Restart();
        PositionChanged?.Invoke(CurrentPositionSeconds);
    }

    // 获取 FFT 频谱 —— 从内存数组采样，64 柱，对数频率，-60dB~0dB
    public float[] GetFFTSamples(int barCount = 64)
    {
        if (_decodedAudio == null || _playbackReader == null || State != PlaybackState.Playing)
            return new float[barCount];

        try
        {
            // 当前播放位置对应的采样索引
            long samplePos = (long)(_playbackReader.CurrentTime.TotalSeconds * _sampleRate) * _channels;

            // 混音为单声道（复用预分配数组）
            int srcLen = _decodedAudio.Length;
            int monoIdx = 0;
            long idx = samplePos;

            Array.Clear(_fftIn, 0, FftLength);
            while (monoIdx < FftLength && idx + _channels - 1 < srcLen)
            {
                float sum = 0;
                for (int c = 0; c < _channels; c++)
                    sum += _decodedAudio[idx + c];
                _fftIn[monoIdx] = sum / _channels;
                monoIdx++;
                idx += _channels;
            }

            if (monoIdx < 64) return new float[barCount];

            // 汉明窗 + FFT（复用预分配数组）
            for (int i = 0; i < FftLength; i++)
            {
                _fftComplex[i].X = _fftIn[i] * (float)NAudio.Dsp.FastFourierTransform.HammingWindow(i, FftLength);
                _fftComplex[i].Y = 0;
            }

            NAudio.Dsp.FastFourierTransform.FFT(true, (int)Math.Log2(FftLength), _fftComplex);

            // 计算幅度（复用预分配数组）
            float freqPerBin = (float)_sampleRate / FftLength;
            int halfLen = FftLength / 2;
            float maxFreq = 8000f;
            float minFreq = 100f;
            int maxBin = Math.Min(halfLen - 1, (int)(maxFreq / freqPerBin));

            for (int i = 0; i <= maxBin; i++)
            {
                var c = _fftComplex[i];
                _magnitudes[i] = (float)Math.Sqrt(c.X * c.X + c.Y * c.Y);
            }

            float refMag = 1f;

            // 64 柱对数频率 20Hz~8kHz → -60dB~0dB 归一化
            var bars = new float[barCount];
            float logMin = (float)Math.Log10(minFreq);
            float logMax = (float)Math.Log10(maxFreq);

            for (int i = 0; i < barCount; i++)
            {
                float t = (float)i / (barCount - 1);
                float cf = (float)Math.Pow(10, logMin + (logMax - logMin) * t);
                float nf = (float)Math.Pow(10, logMin + (logMax - logMin) * (i + 1f) / (barCount - 1));
                float bw = Math.Max(1, nf - cf);

                int start = Math.Max(0, (int)((cf - bw * 0.5f) / freqPerBin));
                int end = Math.Min(maxBin, (int)((cf + bw * 0.5f) / freqPerBin));

                float sum = 0;
                int cnt = 0;
                for (int j = start; j <= end; j++) { sum += _magnitudes[j]; cnt++; }

                float avg = cnt > 0 ? sum / cnt : 0;
                float db = avg > 1e-6f ? 20f * (float)Math.Log10(avg / refMag) : -60f;
                bars[i] = (Math.Max(db, -60f) + 60f) / 60f; // 返回 0~1 归一化值，像素转换交给 UI 层
            }

            return bars;
        }
        catch
        {
            return new float[barCount];
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (State != PlaybackState.Playing)
            return;

        State = PlaybackState.Stopped;
        _positionTimer?.Stop();

        // NAudio 回调在后台线程，封送回 UI 线程再触发 PlaybackFinished
        if (_uiContext != null)
            _uiContext.Post(_ => PlaybackFinished?.Invoke(), null);
        else
            PlaybackFinished?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _positionTimer?.Stop();
        _positionTimer?.Dispose();

        if (_wavePlayer != null)
        {
            _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
            try { _wavePlayer.Stop(); } catch { }
            _wavePlayer.Dispose();
            _wavePlayer = null;
        }

        _playbackReader?.Dispose();
        _playbackReader = null;
        _decodedAudio = null;
    }
}
