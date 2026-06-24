# ADR-002: Аудиоконтракт MVP

## Внутренний формат

- Частота: 48 kHz, если устройство позволяет; иначе используется формат устройства и фиксируется в логах Gate 0.
- Внутренний формат обработки: float32.
- Голосовой тракт: mono по смыслу, но Gate 0 обрабатывает количество каналов, которое отдаёт устройство.
- Размер буфера Gate 0: задаётся WASAPI/NAudio; целевой runtime-диапазон 256–512 samples будет уточнён после измерений.

## Цепочка Gate 0

```text
microphone
-> optional input gain
-> noise gate
-> compressor
-> voice gain
-> limiter
-> VB-CABLE output
-> optional monitoring output
```

## Цепочка целевого MVP

```text
microphone
-> input normalization
-> noise gate
-> compressor
-> pitch shift
-> formant shift
-> character effect: radio / robot / demon
-> voice reverb
-> voice gain
-> mixer with soundboard/background
-> limiter
-> VB-CABLE output + monitoring output
```

## Важное правило

Voice reverb применяется только к голосовому тракту. SoundBoard, background loop и one-shot звуки через voice reverb не проходят.
