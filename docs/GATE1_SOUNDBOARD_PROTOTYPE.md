# Gate 1 SoundBoard Prototype

Goal: prove that VoiSe can play a one-shot sound into the same virtual microphone route while the live microphone is running.

## Run

```powershell
dotnet run --project src/VoiSe.Gate0.Cli -- `
  --input "Микрофон (Fifine Microphone)" `
  --virtual-output "CABLE Input" `
  --monitor "Наушники (Realtek(R) Audio)" `
  --sound-file "C:\Path\To\sound.wav"
```

Supported test formats:

- WAV
- MP3
- OGG/Vorbis via NAudio.Vorbis

## Runtime keys

- `S` — play selected one-shot sound
- `X` — stop selected one-shot sound
- `Ctrl+C` — stop VoiSe

## Notes

This is still a prototype. The soundboard sound is played through additional shared-mode WASAPI outputs to the same VB-CABLE and monitoring devices. The final MVP mixer should move this into a single internal mix bus with one limiter.


## Gate 1.1: soundboard monitor sync

If the soundboard reaches friends through the virtual microphone later than you hear it in headphones, your singing or voice can arrive earlier than the song moment.

Use `--sound-monitor-delay-ms` to delay only the soundboard playback in headphones. The soundboard signal sent to `CABLE Input` is not delayed.

Start with 80 ms and tune by ear:

```powershell
dotnet run --project src/VoiSe.Gate0.Cli -- --input "Микрофон (Fifine Microphone)" --virtual-output "CABLE Input" --monitor "Наушники (Realtek(R) Audio)" --sound-file "C:\Path\To\song.wav" --sound-monitor-delay-ms 80
```

Suggested values to try: 40, 80, 120, 160 ms.

This is a temporary Gate 1 sync control. The final app should expose it as a SoundBoard monitoring delay slider.


## Gate 1.3: virtual microphone delay

`--sound-virtual-delay-ms` delays only the soundboard stream that goes to the virtual microphone. The monitor output starts immediately.

This is different from Gate 1.2, where the prototype delayed the monitor path. Gate 1.3 matches the intended MVP behaviour: headphones are a timing cue, virtual microphone output is delayed.

Recommended values to test: 40, 80, 120, 160, 200 ms.
