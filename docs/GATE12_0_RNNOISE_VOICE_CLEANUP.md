# Gate 12.0 — RNNoise Voice Cleanup

## Goal

Add real-time microphone noise suppression as the first VoiSee 12 Voice Changer enhancement while preserving all existing audio routes and keeping the initial interface understandable.

## Design decisions

### Cleanup is separate from character presets

Noise suppression describes the user's microphone environment, not the fictional voice being selected. It is therefore stored in `VoiSeUserSettings` as a global setting instead of in `VoicePresetModels`.

This prevents switching between presets such as Clean Voice, Demon or Anonymous from changing the user's background-noise configuration.

### Cleanup runs before voice transformation

RNNoise is placed before `SimpleVoiceProcessor`:

```text
Capture conversion
    → RnNoiseSuppressionProcessor
    → SimpleVoiceProcessor
    → microphone queues
```

This allows RNNoise to work on the most natural form of the speech signal, before pitch, formant, distortion and other effects can amplify or reshape background noise.

### Only the microphone is processed

The suppressor is called in `Gate2UnifiedAudioEngine.OnDataAvailable`, immediately after physical microphone capture is converted to the 48 kHz stereo mix format.

It is not part of `RouteMixSampleProvider`, so it cannot process:

- SoundBoard audio;
- scene loop sounds;
- Media Bridge audio.

### Minimal first interface

The initial card exposes only:

- enabled state;
- suppression strength.

The more complex draggable effect-panel system is intentionally deferred. Its layout, drag behavior, collapsed state, accessibility, touch targets and preset serialization need a dedicated design stage rather than being improvised around the first effect.

## Implementation

### `RnNoiseSuppressionProcessor`

File:

```text
src/VoiSe.Audio/RnNoiseSuppressionProcessor.cs
```

Responsibilities:

- accept VoiSee's interleaved 48 kHz stereo microphone samples;
- downmix each frame to mono;
- buffer exact 480-sample RNNoise blocks;
- run `RNNoise.NET.Denoiser`;
- retain an aligned dry block for continuous Strength blending;
- duplicate the resulting mono sample to the left and right channels;
- reset internal streaming state when enabled state changes;
- fall back to bypass if native initialization or processing fails.

The implementation performs no model loading on the audio callback after successful initialization. Working arrays and queue storage are allocated when the processor is created.

### Strength behavior

`NoiseSuppressionStrength` is stored as 0–100 in the UI and persistent settings, then converted to 0.0–1.0 in `EffectSettings`.

The output is:

```text
aligned dry + (RNNoise output - aligned dry) × strength
```

Because both paths share the same block delay, intermediate values do not mix a delayed signal with a non-delayed signal.

### Audio latency

RNNoise requires a 480-sample frame at 48 kHz, equal to 10 ms. The adapter initially emits silence while the first frame is collected, then continuously returns aligned processed frames.

### Live updates

- On/Off is applied immediately through the existing live-settings route.
- Strength uses the existing debounced Voice Changer settings update.
- Toggling the processor resets its frame buffers to avoid stale audio from the previous state.

### Persistence

`VoiSeUserSettings.SchemaVersion` is raised to 8.

New fields:

```csharp
public bool NoiseSuppressionEnabled { get; set; } = false;
public double NoiseSuppressionStrength { get; set; } = 70.0;
```

### Dependency packaging

`VoiSe.Audio.csproj` references:

```xml
<PackageReference Include="YellowDogMan.RRNoise.NET" Version="0.1.9" />
```

The package provides the managed wrapper and the Windows x64 native `rnnoise.dll` runtime asset. Normal .NET publish dependency handling is expected to copy it into the published application.

Third-party license files are included under:

```text
src/VoiSe.App/ThirdPartyLicenses/
```

The application project marks these files for output and publish copying.

## Failure behavior

If RNNoise cannot initialize:

- the audio engine still starts;
- the microphone remains audible through the unprocessed path;
- `NoiseSuppressionAvailable` becomes false;
- the application log reports the initialization error when suppression was requested.

If processing throws during an audio callback, the suppressor disables its native processing state and subsequent microphone blocks pass through.

## Deferred VoiSee 12 effect-panel design

Before adding the general effect-chain interface, define:

- panel header and drag handle separation;
- expanded and collapsed forms;
- where On/Off, reset, delete and duplicate actions live;
- keyboard-accessible reordering;
- drag placeholder and autoscroll behavior;
- fixed cleanup section versus reorderable creative effects;
- parameter value entry alongside sliders;
- serialization format and backward compatibility;
- maximum chain length and real-time CPU budget.

Noise suppression can later be represented by that system without requiring the current DSP integration to be rewritten.

## Verification status

No build, automated test or smoke test was run. The user will perform compilation and runtime validation on the target Windows system.
