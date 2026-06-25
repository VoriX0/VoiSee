# Gate 5 — SoundBoard Library

Goal: replace the single test sound file with a persistent SoundBoard library.

## Data

- Library file: `%LOCALAPPDATA%\VoiSe\soundboard.json`
- Copied sound files: `%LOCALAPPDATA%\VoiSe\sounds`
- User settings still live in `%LOCALAPPDATA%\VoiSe\settings.json`

## Entities

### Category

- `id`
- `name`
- `sortOrder`

### Sound

- `id`
- `name`
- `categoryId`
- `filePath`
- `originalFileName`
- `extension`
- `createdAtUtc`
- `updatedAtUtc`

## MVP behavior

- Files are copied into the VoiSe data folder.
- Supported formats: WAV / MP3 / OGG.
- A selected sound can be played through the already-proven unified mixer.
- Category and selected sound are restored on app restart.
