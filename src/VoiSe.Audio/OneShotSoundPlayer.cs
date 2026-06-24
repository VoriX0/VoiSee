using NAudio.CoreAudioApi;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiSe.Audio;

public sealed class OneShotSoundPlayer : IDisposable
{
    private readonly MMDevice _virtualOutputDevice;
    private readonly MMDevice? _monitorDevice;
    private readonly float _virtualVolume;
    private readonly float _monitorVolume;
    private readonly int _virtualStartDelayMs;

    private readonly object _sync = new();
    private ActivePlayback? _virtualPlayback;
    private ActivePlayback? _monitorPlayback;
    private bool _disposed;

    public OneShotSoundPlayer(
        MMDevice virtualOutputDevice,
        MMDevice? monitorDevice,
        float virtualVolume = 1.0f,
        float monitorVolume = 1.0f,
        int virtualStartDelayMs = 0)
    {
        _virtualOutputDevice = virtualOutputDevice;
        _monitorDevice = monitorDevice;
        _virtualVolume = Math.Clamp(virtualVolume, 0.0f, 2.0f);
        _monitorVolume = Math.Clamp(monitorVolume, 0.0f, 2.0f);
        _virtualStartDelayMs = Math.Clamp(virtualStartDelayMs, 0, 1000);
    }

    public void Play(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Sound file was not found.", filePath);
        }

        lock (_sync)
        {
            StopLocked();

            // The monitor starts immediately. This gives the user a cue before the same sound
            // reaches the virtual microphone, which is useful for singing along or timing voice cues.
            if (_monitorDevice is not null)
            {
                _monitorPlayback = StartPlayback(_monitorDevice, filePath, _monitorVolume, startDelayMs: 0);
            }

            _virtualPlayback = StartPlayback(_virtualOutputDevice, filePath, _virtualVolume, _virtualStartDelayMs);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            StopLocked();
        }
    }

    private static ActivePlayback StartPlayback(MMDevice outputDevice, string filePath, float volume, int startDelayMs)
    {
        var reader = CreateReader(filePath);
        var sampleProvider = reader.ToSampleProvider();
        ISampleProvider playbackProvider = new VolumeSampleProvider(sampleProvider)
        {
            Volume = volume
        };

        if (startDelayMs > 0)
        {
            playbackProvider = new StartDelaySampleProvider(playbackProvider, startDelayMs);
        }

        var waveProvider = new SampleToWaveProvider(playbackProvider);
        var output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 50);
        output.Init(waveProvider);
        output.PlaybackStopped += (_, _) =>
        {
            output.Dispose();
            reader.Dispose();
        };

        output.Play();

        return new ActivePlayback(output, reader);
    }

    private static WaveStream CreateReader(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".ogg" => new VorbisWaveReader(filePath),
            _ => new AudioFileReader(filePath)
        };
    }

    private void StopLocked()
    {
        _virtualPlayback?.Dispose();
        _monitorPlayback?.Dispose();
        _virtualPlayback = null;
        _monitorPlayback = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private sealed class ActivePlayback : IDisposable
    {
        private readonly WasapiOut _output;
        private readonly WaveStream _reader;
        private bool _disposed;

        public ActivePlayback(WasapiOut output, WaveStream reader)
        {
            _output = output;
            _reader = reader;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _output.Stop();
            _output.Dispose();
            _reader.Dispose();
            _disposed = true;
        }
    }

    private sealed class StartDelaySampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private int _remainingDelaySamples;

        public StartDelaySampleProvider(ISampleProvider source, int delayMs)
        {
            _source = source;
            WaveFormat = source.WaveFormat;
            var delayFrames = (int)Math.Round(WaveFormat.SampleRate * (delayMs / 1000.0));
            _remainingDelaySamples = Math.Max(0, delayFrames * WaveFormat.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var written = 0;

            if (_remainingDelaySamples > 0)
            {
                var silenceSamples = Math.Min(count, _remainingDelaySamples);
                Array.Clear(buffer, offset, silenceSamples);
                _remainingDelaySamples -= silenceSamples;
                written += silenceSamples;
                offset += silenceSamples;
                count -= silenceSamples;
            }

            if (count > 0)
            {
                written += _source.Read(buffer, offset, count);
            }

            return written;
        }
    }
}
