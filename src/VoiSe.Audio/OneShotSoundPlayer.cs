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

    private readonly object _sync = new();
    private ActivePlayback? _virtualPlayback;
    private ActivePlayback? _monitorPlayback;
    private bool _disposed;

    public OneShotSoundPlayer(
        MMDevice virtualOutputDevice,
        MMDevice? monitorDevice,
        float virtualVolume = 1.0f,
        float monitorVolume = 1.0f)
    {
        _virtualOutputDevice = virtualOutputDevice;
        _monitorDevice = monitorDevice;
        _virtualVolume = Math.Clamp(virtualVolume, 0.0f, 2.0f);
        _monitorVolume = Math.Clamp(monitorVolume, 0.0f, 2.0f);
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
            _virtualPlayback = StartPlayback(_virtualOutputDevice, filePath, _virtualVolume);

            if (_monitorDevice is not null)
            {
                _monitorPlayback = StartPlayback(_monitorDevice, filePath, _monitorVolume);
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            StopLocked();
        }
    }

    private static ActivePlayback StartPlayback(MMDevice outputDevice, string filePath, float volume)
    {
        var reader = CreateReader(filePath);
        var sampleProvider = reader.ToSampleProvider();
        var volumeProvider = new VolumeSampleProvider(sampleProvider)
        {
            Volume = volume
        };

        var waveProvider = new SampleToWaveProvider(volumeProvider);
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
}
