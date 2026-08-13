# VoiSee 13.6.1 — static smoke report

## Corrective scope

The 13.6.0 duration/energy gate accepted a long breath as a usable voice reference. 13.6.1 adds speech-content validation to both Quick Clone and Optimize Voice.

## Static checks

- MainWindow.xaml parses as XML.
- All x:Name values are unique.
- All XAML event handlers resolve to C# methods.
- app.manifest still contains PerMonitorV2.
- No bin/ or obj/ directories are packaged.
- AiVoiceStore delimiter balance checked after stripping comments/string literals.
- No extra runtime dependency was introduced.

## Speech gate

The lightweight detector operates on 30 ms frames after resampling analysis audio to 8 kHz. It combines active-frame energy with normalized autocorrelation in the normal pitch range and zero-crossing rate. A reference must contain enough active audio and at least 2.5 seconds of voiced speech-like frames, with voiced frames making up at least 18% of active audio. This is intended to reject long breathing/noise while retaining ordinary spoken references.

Windows/WinUI compilation and testing with a real microphone remain required on the target PC because the current container does not include the .NET Windows toolchain.
