# Gate 13.5 — Quick Clone lifecycle

Reference -> Quick Clone is an in-place `.vsee` state transition. The reference audio remains in the package. Quick preparation validates >=10 seconds of decodable reference audio, stores reference duration, preparation timestamp and `quick/profile.json`. This Gate prepares the voice package/state for the later realtime AI runtime; it does not claim that neural inference is already connected. Optimize Voice remains the Gate 13.6 boundary.
