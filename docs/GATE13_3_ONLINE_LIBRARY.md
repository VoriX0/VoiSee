# Gate 13.3 — Online Library

VoiSee uses a provider abstraction so the UI is not tied to a single catalog. The first implementation is Hugging Face Hub.

The provider searches Hub model metadata, rejects obvious non-voice results, identifies RVC/ONNX candidate assets, and exposes compatible repositories in the Online Library. Downloaded assets are copied to a temporary directory and then packed by `AiVoiceStore` into the configured `.vsee` storage.

Compatibility in 13.3 is intentionally pre-runtime: `RVC candidate` means the repository contains a `.pth` model (and preferably `.index`). Final runtime compatibility is verified when the realtime AI backend is introduced.
