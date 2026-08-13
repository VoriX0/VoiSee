# VoiSee 13.6.1 — speech-content validation

Corrective release before the realtime AI runtime.

- Quick Clone and Optimize Voice no longer accept duration/energy alone as proof of a useful voice reference.
- Added lightweight offline speech-content analysis over 30 ms frames at 8 kHz.
- The validator measures active audio plus periodic/voiced speech-like material in the normal human pitch band.
- A long breath, broadband noise, or mostly silence is rejected with a user-facing explanation.
- Both Quick Clone and Optimize use the same validation boundary.
- Speech-analysis metrics are written into quick/profile.json and optimized/profile.json for diagnostics and future runtime work.
- No new runtime dependency is introduced.
