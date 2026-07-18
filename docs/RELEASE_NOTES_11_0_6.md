# VoiSee 11.0.6

## Media Bridge level meters

- Replaced the technical negative dBFS scale with a direct 0–100% user-facing scale.
- 0% means silence.
- 100% is the digital maximum and corresponds to 0 dBFS, immediately before clipping.
- Scale labels are now placed directly beside each Source and To Mic meter.
- The top of each meter is marked `100 MAX`.
- Current values below the meters are shown as percentages.
- A value at the limit is shown as `100% MAX`.
- Meter movement is now linear and matches the visible percentage scale.

The underlying audio path, capture, Pause, Stop, SoundBoard and scene behavior were not changed.

Build and automated tests were not run by request.
