using System.Text.Json;

namespace VoiSe.Audio;

public sealed class AudioEngineRequestEnvelope
{
    public Guid RequestId { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }

    public static AudioEngineRequestEnvelope Create<T>(string command, T payload)
    {
        return new AudioEngineRequestEnvelope
        {
            RequestId = Guid.NewGuid(),
            Command = command,
            PayloadJson = JsonSerializer.Serialize(payload, AudioEngineJson.Options)
        };
    }

    public static AudioEngineRequestEnvelope Create(string command)
    {
        return new AudioEngineRequestEnvelope
        {
            RequestId = Guid.NewGuid(),
            Command = command
        };
    }
}

public sealed class AudioEngineResponseEnvelope
{
    public Guid RequestId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? PayloadJson { get; set; }

    public static AudioEngineResponseEnvelope Ok<T>(Guid requestId, T payload)
    {
        return new AudioEngineResponseEnvelope
        {
            RequestId = requestId,
            Success = true,
            PayloadJson = JsonSerializer.Serialize(payload, AudioEngineJson.Options)
        };
    }

    public static AudioEngineResponseEnvelope Ok(Guid requestId)
    {
        return new AudioEngineResponseEnvelope
        {
            RequestId = requestId,
            Success = true
        };
    }

    public static AudioEngineResponseEnvelope Fail(Guid requestId, Exception exception)
    {
        return new AudioEngineResponseEnvelope
        {
            RequestId = requestId,
            Success = false,
            Error = $"{exception.GetType().Name}: {exception.Message}"
        };
    }
}

public static class AudioEngineJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static T DeserializePayload<T>(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new InvalidDataException($"Audio host request did not contain payload {typeof(T).Name}.");
        }

        return JsonSerializer.Deserialize<T>(payloadJson, Options)
            ?? throw new InvalidDataException($"Audio host request payload {typeof(T).Name} was empty.");
    }
}

public sealed class AudioEngineStartRequest
{
    public string InputDeviceId { get; set; } = string.Empty;
    public string VirtualOutputDeviceId { get; set; } = string.Empty;
    public string? MonitorDeviceId { get; set; }
    public EffectSettings Settings { get; set; } = new();
}

public sealed class AudioEngineStartResult
{
    public int HostProcessId { get; set; }
    public string InputFriendlyName { get; set; } = string.Empty;
    public string VirtualOutputFriendlyName { get; set; } = string.Empty;
    public string? MonitorFriendlyName { get; set; }
}

public sealed class AudioEngineSnapshot
{
    public bool Running { get; set; }
    public bool VoiceMonitorRouteEnabled { get; set; }
    public bool IsMediaBridgeBroadcasting { get; set; }
    public bool IsMediaBridgePaused { get; set; }
    public float MediaBridgeSourcePeak { get; set; }
    public float MediaBridgeOutputPeak { get; set; }
    public string? MediaBridgeError { get; set; }
    public string? HostError { get; set; }
}

public sealed class AudioEnginePlaySoundRequest
{
    public string FilePath { get; set; } = string.Empty;
    public float VirtualVolume { get; set; }
    public float MonitorVolume { get; set; }
    public int VirtualDelayMs { get; set; }
    public bool Loop { get; set; }
    public string? PlaybackKey { get; set; }
}

public sealed class AudioEngineSoundKeyRequest
{
    public string? PlaybackKey { get; set; }
}

public sealed class AudioEngineSeekSoundRequest
{
    public double Seconds { get; set; }
    public string? PlaybackKey { get; set; }
}

public sealed class AudioEngineSoundVolumesRequest
{
    public float VirtualVolume { get; set; }
    public float MonitorVolume { get; set; }
    public string? PlaybackKey { get; set; }
}

public sealed class AudioEngineSoundLoopRequest
{
    public bool Loop { get; set; }
    public string? PlaybackKey { get; set; }
}

public sealed class AudioEngineBoolRequest
{
    public bool Value { get; set; }
}

public sealed class AudioEngineFloatRequest
{
    public float Value { get; set; }
}

public sealed class AudioEngineProcessRequest
{
    public int ProcessId { get; set; }
}
