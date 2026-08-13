# VoiSee 13.7.0 — first realtime AI Voice runtime

## Added

- Separate installable RVC runtime; the base VoiSee installer does not bundle Python, PyTorch or model weights.
- Compute-only RVC bridge using redirected binary PCM streams; VoiSee remains the single audio-device owner.
- Live processing order: Noise Suppression -> AI Voice -> Voice Changer DSP.
- RVC `.pth` extraction from `.vsee` into a revisioned local runtime cache.
- Restricted `weights_only` checkpoint validation and clean reserialization before the upstream RVC loader sees downloaded `.pth` files.
- FAISS `.index` assets are retained but retrieval is disabled in 13.7 (`index_rate=0`) until latency/safety tuning is measured.
- Hardware-aware runtime installation for NVIDIA vs CPU/AMD/Intel dependency paths.
- Runtime Install / Repair and Open Runtime Folder controls in Settings.
- AI runtime state/latency diagnostics: block size, neural inference time, IPC round-trip, input/output queues and underruns.
- Overload warnings when inference exceeds the selected block budget or output underruns grow.
- Runtime repair rollback to the previous installation if a repair fails.

## Behavior changes

- Imported/downloaded RVC `.pth` voices now perform actual realtime neural conversion after successful activation.
- `Gaming`, `Balanced` and `Quality` restart the active AI backend with different stream block/crossfade profiles.
- `Disable Voice` terminates the AI process and immediately returns the microphone path to the pre-AI Fast/Full route.
- A faulted active AI route does not silently bypass to raw microphone audio.
- `Reference`, `Quick Clone` and `Optimized` lifecycle packages are no longer marked directly activatable by the RVC backend; they await the later zero-shot runtime.

## Not yet in this gate

- Zero-shot realtime inference for Reference/Quick/Optimized voices.
- Automatic enforcement of the 250 ms Gaming ceiling based on measured hardware capability.
- Two-model GPU cache/preload policy.
- Scene persistence of an AI Voice selection.
