#!/usr/bin/env python3
"""VoiSee 13.7 realtime RVC bridge.

Binary protocol:
- stdin: little-endian float32 mono PCM blocks, fixed size selected by profile
- stdout: little-endian float32 mono PCM blocks of the same size
- stderr: human-readable status/log messages only

The bridge imports the official RVC runtime from a separately installed checkout.
VoiSee keeps audio device capture/rendering in its own C# engine; this process is
compute-only and never opens an audio device.
"""

from __future__ import annotations

import argparse
import os
import sys
import time
import traceback
from dataclasses import dataclass

# Reserve the original stdout for binary PCM and redirect ordinary Python/RVC
# prints to stderr so a log line can never corrupt the audio protocol.
_BINARY_STDOUT = sys.stdout.buffer
sys.stdout = sys.stderr


def log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


def read_exact(stream, size: int) -> bytes:
    chunks: list[bytes] = []
    remaining = size
    while remaining > 0:
        chunk = stream.read(remaining)
        if not chunk:
            return b""
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


@dataclass(frozen=True)
class Profile:
    block_ms: int
    crossfade_ms: int
    extra_ms: int


PROFILES = {
    "gaming": Profile(block_ms=80, crossfade_ms=20, extra_ms=500),
    "balanced": Profile(block_ms=120, crossfade_ms=30, extra_ms=800),
    "quality": Profile(block_ms=200, crossfade_ms=40, extra_ms=1200),
}


class StreamRvc:
    def __init__(self, model_path: str, index_path: str, profile: Profile, sample_rate: int) -> None:
        import numpy as np
        import torch
        import torch.nn.functional as F
        import torchaudio.transforms as tat

        from configs.config import Config
        from infer import rtrvc

        self.np = np
        self.torch = torch
        self.F = F
        self.sample_rate = sample_rate
        self.profile = profile
        self.config = Config()

        index_exists = bool(index_path and os.path.isfile(index_path))
        # 13.7 starts with the upstream realtime default of no FAISS retrieval.
        # The .index remains packaged/cached for a later quality control, but is
        # not parsed by the first live runtime. This reduces latency and keeps an
        # additional untrusted native-file parser out of the activation boundary.
        index_rate = 0.0
        self.rvc = rtrvc.RVC(
            0,                 # pitch shift
            0.0,               # formant shift
            model_path,
            index_path if index_exists else "",
            index_rate,
            self.config,
            None,
        )
        if not getattr(self.rvc, "net_g", None):
            raise RuntimeError("RVC synthesizer could not be loaded from the selected .pth model.")

        # RVC's realtime path is quantized to 10 ms units.
        self.zc = sample_rate // 100
        self.block_frame = max(
            self.zc,
            int(round((profile.block_ms / 1000.0) * sample_rate / self.zc)) * self.zc,
        )
        self.block_frame_16k = 160 * self.block_frame // self.zc
        self.crossfade_frame = max(
            self.zc,
            int(round((profile.crossfade_ms / 1000.0) * sample_rate / self.zc)) * self.zc,
        )
        self.sola_buffer_frame = min(self.crossfade_frame, 4 * self.zc)
        self.sola_search_frame = self.zc
        self.extra_frame = max(
            self.zc,
            int(round((profile.extra_ms / 1000.0) * sample_rate / self.zc)) * self.zc,
        )

        device = self.config.device
        self.input_wav = torch.zeros(
            self.extra_frame + self.crossfade_frame + self.sola_search_frame + self.block_frame,
            device=device,
            dtype=torch.float32,
        )
        self.input_wav_res = torch.zeros(
            160 * self.input_wav.shape[0] // self.zc,
            device=device,
            dtype=torch.float32,
        )
        self.sola_buffer = torch.zeros(self.sola_buffer_frame, device=device, dtype=torch.float32)
        self.sola_den_kernel = torch.ones(1, 1, self.sola_buffer_frame, device=device, dtype=torch.float32)
        self.fade_in = (
            torch.sin(
                0.5 * np.pi * torch.linspace(0.0, 1.0, steps=self.sola_buffer_frame, device=device)
            )
            ** 2
        )
        self.fade_out = 1.0 - self.fade_in
        self.skip_head = self.extra_frame // self.zc
        self.return_length = (
            self.block_frame + self.sola_buffer_frame + self.sola_search_frame
        ) // self.zc

        self.resample_in = tat.Resample(sample_rate, 16000, dtype=torch.float32).to(device)
        self.resample_out = (
            tat.Resample(self.rvc.tgt_sr, sample_rate, dtype=torch.float32).to(device)
            if self.rvc.tgt_sr != sample_rate
            else None
        )

    @property
    def block_samples(self) -> int:
        return self.block_frame

    def _sync_if_needed(self) -> None:
        if self.torch.cuda.is_available() and str(self.config.device).startswith("cuda"):
            self.torch.cuda.synchronize(self.config.device)

    def warmup(self) -> None:
        # Warm the feature extractor, pitch extractor and synthesizer before VoiSee
        # receives READY, so the first live speech block does not pay all lazy-load cost.
        phase = self.torch.arange(self.block_frame, device=self.config.device, dtype=self.torch.float32)
        probe = 0.03 * self.torch.sin(2.0 * self.np.pi * 180.0 * phase / float(self.sample_rate))
        self.process(probe.detach().cpu().numpy().astype("float32", copy=False))
        self.input_wav.zero_()
        self.input_wav_res.zero_()
        self.sola_buffer.zero_()
        if hasattr(self.rvc, "cache_pitch"):
            self.rvc.cache_pitch.zero_()
        if hasattr(self.rvc, "cache_pitchf"):
            self.rvc.cache_pitchf.zero_()

    def process(self, mono_block):
        torch = self.torch
        if mono_block.shape[0] != self.block_frame:
            raise ValueError(f"Expected {self.block_frame} samples, received {mono_block.shape[0]}.")

        # Maintain the rolling context used by the official realtime RVC algorithm.
        self.input_wav[:-self.block_frame] = self.input_wav[self.block_frame:].clone()
        self.input_wav[-self.block_frame:] = torch.from_numpy(mono_block).to(self.config.device)

        self.input_wav_res[:-self.block_frame_16k] = self.input_wav_res[self.block_frame_16k:].clone()
        # Include 20 ms of history around each input resample boundary.
        resample_source = self.input_wav[-self.block_frame - 2 * self.zc:]
        resampled = self.resample_in(resample_source)
        needed = self.block_frame_16k + 160
        self.input_wav_res[-needed:] = resampled[-needed:]

        converted = self.rvc.infer(
            self.input_wav_res,
            self.block_frame_16k,
            self.skip_head,
            self.return_length,
            "rmvpe",
        )
        if self.resample_out is not None:
            converted = self.resample_out(converted)

        # SOLA aligns the new block with the previous tail and cross-fades them.
        candidate = converted[None, None, : self.sola_buffer_frame + self.sola_search_frame]
        numerator = self.F.conv1d(candidate, self.sola_buffer[None, None, :])
        denominator = torch.sqrt(self.F.conv1d(candidate ** 2, self.sola_den_kernel) + 1e-8)
        offset = int(torch.argmax(numerator[0, 0] / denominator[0, 0]).item())
        converted = converted[offset:]

        if converted.shape[0] < self.block_frame + self.sola_buffer_frame:
            converted = self.F.pad(converted, (0, self.block_frame + self.sola_buffer_frame - converted.shape[0]))

        converted[: self.sola_buffer_frame] *= self.fade_in
        converted[: self.sola_buffer_frame] += self.sola_buffer * self.fade_out
        self.sola_buffer[:] = converted[self.block_frame:self.block_frame + self.sola_buffer_frame]
        result = converted[:self.block_frame].float().clamp_(-1.0, 1.0)
        self._sync_if_needed()
        return result.detach().cpu().numpy().astype("float32", copy=False)


def make_safe_checkpoint(model_path: str) -> str:
    """Load an untrusted downloaded checkpoint through PyTorch's restricted
    weights-only unpickler, validate the RVC shape, then reserialize a clean
    checkpoint before the official RVC loader sees it.
    """
    import tempfile
    import torch

    try:
        checkpoint = torch.load(model_path, map_location="cpu", weights_only=True)
    except TypeError as exc:
        raise RuntimeError("This PyTorch runtime does not support safe weights-only checkpoint loading.") from exc
    except Exception as exc:
        raise RuntimeError(
            "The selected .pth model failed VoiSee's safe weights-only validation. "
            "It may be incompatible or contain unsupported pickle objects."
        ) from exc

    if not isinstance(checkpoint, dict):
        raise RuntimeError("The selected .pth is not an RVC checkpoint dictionary.")
    config = checkpoint.get("config")
    weights = checkpoint.get("weight")
    if not isinstance(config, (list, tuple)) or not isinstance(weights, dict):
        raise RuntimeError("The selected .pth does not contain the expected RVC config/weight structure.")
    if len(config) < 3 or "emb_g.weight" not in weights:
        raise RuntimeError("The selected .pth is missing required RVC synthesizer metadata.")
    # Upstream mutates cpt["config"] while loading. Normalize a safely-loaded
    # tuple to a list before handing the clean checkpoint to that loader.
    checkpoint["config"] = list(config)

    fd, safe_path = tempfile.mkstemp(prefix="voisee-rvc-safe-", suffix=".pth")
    os.close(fd)
    try:
        torch.save(checkpoint, safe_path)
        return safe_path
    except Exception:
        try:
            os.remove(safe_path)
        except OSError:
            pass
        raise


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rvc-root", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--index", default="")
    parser.add_argument("--profile", default="balanced", choices=tuple(PROFILES.keys()))
    parser.add_argument("--sample-rate", type=int, default=48000)
    args = parser.parse_args()

    # RVC Config owns its own argparse parser. Remove VoiSee bridge arguments
    # before constructing Config so the official runtime does not reject them as
    # unknown application options.
    sys.argv = [sys.argv[0]]

    root = os.path.abspath(args.rvc_root)
    if not os.path.isdir(root):
        raise FileNotFoundError(f"RVC runtime root not found: {root}")
    if not os.path.isfile(args.model):
        raise FileNotFoundError(f"RVC model not found: {args.model}")

    os.chdir(root)
    sys.path.insert(0, root)
    os.environ.setdefault("OPENBLAS_NUM_THREADS", "1")
    os.environ.setdefault("OMP_NUM_THREADS", "4")

    profile = PROFILES[args.profile]
    safe_model_path = make_safe_checkpoint(args.model)
    try:
        engine = StreamRvc(safe_model_path, args.index, profile, args.sample_rate)
    finally:
        try:
            os.remove(safe_model_path)
        except OSError:
            pass

    warm_start = time.perf_counter()
    engine.warmup()
    warm_ms = (time.perf_counter() - warm_start) * 1000.0
    log(
        "VOISEE_READY "
        f"block_ms={profile.block_ms} block_samples={engine.block_samples} "
        f"target_sr={engine.rvc.tgt_sr} device={engine.config.device} warmup_ms={warm_ms:.0f}"
    )

    block_bytes = engine.block_samples * 4
    source = sys.stdin.buffer
    while True:
        payload = read_exact(source, block_bytes)
        if not payload:
            return 0
        input_block = engine.np.frombuffer(payload, dtype="<f4").astype("float32", copy=False)
        start = time.perf_counter()
        output_block = engine.process(input_block)
        infer_ms = (time.perf_counter() - start) * 1000.0
        _BINARY_STDOUT.write(output_block.astype("<f4", copy=False).tobytes())
        _BINARY_STDOUT.flush()
        log(f"VOISEE_INFER inference_ms={infer_ms:.2f}")


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception as exc:
        log(f"VOISEE_FATAL {type(exc).__name__}: {exc}")
        traceback.print_exc(file=sys.stderr)
        raise SystemExit(2)
