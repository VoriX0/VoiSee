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
