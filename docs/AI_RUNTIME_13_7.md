# VoiSee 13.7 — Realtime AI Voice runtime

## Scope

13.7 is the first VoiSee gate that puts a neural voice-conversion backend into the live microphone path. The first supported live backend is RVC for `.vsee` packages that contain an RVC `.pth` model. The AI runtime is installed separately from the base application.

The audio order is fixed:

`Physical Mic -> Noise Suppression -> AI Voice -> Voice Changer DSP -> Mixer -> Virtual Mic / Voice Monitor`

VoiSee remains the only owner of microphone capture, monitor rendering and virtual-microphone routing. The Python process is compute-only: VoiSee sends 48 kHz mono float PCM blocks to it and receives converted PCM blocks back. It does not open a microphone or output device.

## Runtime installation

Open **Settings -> AI Voice Runtime** and choose **Install / Repair**, or activate an RVC voice when the runtime is missing. The installer opens in a separate PowerShell window and installs under:

`%LOCALAPPDATA%\VoiSe\AI Runtime\rvc`

The installer pins the upstream RVC source to commit:

`81eed5e8f68b6bed1789f682fe78cdd324495afc`

It installs Python 3.12 into an isolated virtual environment, selects an NVIDIA CUDA dependency set when an NVIDIA GPU is detected, otherwise uses the upstream CPU/AMD/Intel dependency set, and downloads the HuBERT/ContentVec and RMVPE inference assets required by the realtime backend.

A failed repair attempts to restore the previous working runtime.


## Downloaded-model safety boundary

Before an imported/downloaded `.pth` reaches the upstream RVC loader, VoiSee opens it with PyTorch's restricted `weights_only=True` loader, validates the expected RVC `config`/`weight` structure, and serializes a clean temporary checkpoint. A model that requires arbitrary pickle objects is rejected instead of being loaded unrestricted.

The optional `.index` is preserved inside `.vsee` and in the model cache, but 13.7 starts realtime RVC with retrieval rate `0.0`. This matches the upstream realtime default and avoids extra FAISS retrieval latency/native index parsing in the first runtime gate. Retrieval strength can be added as a later quality control after live latency is measured.

## Performance profiles

The existing VoiSee performance selector now controls the RVC stream block size:

- Gaming: 80 ms block, 20 ms SOLA crossfade.
- Balanced: 120 ms block, 30 ms crossfade.
- Quality: 200 ms block, 40 ms crossfade.

These are first-gate engineering defaults, not final latency guarantees. VoiSee reports the measured neural inference time, queued AI audio and estimated total path in Advanced Settings. If inference cannot keep up with the selected block budget, VoiSee keeps the AI route active and emits silence/artifacts rather than silently leaking the raw microphone; an overload warning is logged.

## Model lifecycle boundary

13.7 enables realtime inference for imported/downloaded **RVC `.pth`** voices first.

`Reference`, `Quick Clone` and `Optimized` `.vsee` states remain valid and keep all work performed by 13.5/13.6, but they do not contain a trained RVC synthesizer. Their activation control is therefore disabled in 13.7 instead of pretending they can run through RVC. A later zero-shot backend will consume those prepared reference packages directly.

## Model cache

On activation VoiSee extracts the `.pth` and optional `.index` from the selected `.vsee` into:

`%LOCALAPPDATA%\VoiSe\AI Runtime\model-cache\<voice-id>\<revision>`

The cache revision is tied to the package size and modification timestamp. Old revisions for the same voice are removed after a new one is prepared.

## Diagnostics

Use **Settings -> Advanced Settings -> Latency diagnostics**. With RVC active it includes:

- processing path (`Ai` or `AiFull`);
- AI block size;
- last neural inference time and C#/Python IPC round-trip;
- AI input/output queued milliseconds;
- AI output underrun samples;
- estimated AI stage latency;
- full VoiSee estimated latency.

A helper is also included:

```powershell
.\scripts\check-ai-runtime.ps1
```

It verifies the isolated Python environment, RVC imports and the required inference assets.

- The installer keeps both `assets/rmvpe/rmvpe.pt` and `assets/rmvpe/rmvpe.onnx`; NVIDIA uses the PyTorch asset while the Windows DirectML fallback uses the ONNX asset.
