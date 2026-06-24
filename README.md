# VoiSe Development Starter

Стартовый пакет для перехода от ТЗ VoiSe v0.3 к разработке.

Первый этап — **Gate 0 Audio Prototype**: проверить тракт:

```text
Physical microphone -> VoiSe processing -> VB-CABLE Input -> VB-CABLE Output -> Discord/Telegram
```

## Требования

- Windows 10 x64 22H2 или Windows 11 x64
- .NET SDK 8 или новее
- Visual Studio 2022 с Windows App SDK workload — для будущего WinUI 3 UI
- Установленный VB-CABLE

## Быстрый старт Gate 0

1. Установить VB-CABLE.
2. Открыть PowerShell в корне проекта.
3. Выполнить:

```powershell
./scripts/bootstrap.ps1
```

4. Посмотреть аудиоустройства:

```powershell
dotnet run --project src/VoiSe.Gate0.Cli -- --list-devices
```

5. Запустить passthrough в VB-CABLE:

```powershell
dotnet run --project src/VoiSe.Gate0.Cli -- --input "Ваш микрофон" --virtual-output "CABLE Input" --monitor "Ваши наушники"
```

В Discord/Telegram нужно выбрать входной микрофон **CABLE Output**.

## Структура

```text
src/VoiSe.Audio       аудиоядро Gate 0
src/VoiSe.Gate0.Cli   консольный прототип для проверки тракта
src/VoiSe.App         заготовка WinUI 3 приложения
scripts               команды bootstrap/build
scripts/bootstrap.ps1 создание sln и восстановление пакетов
docs                  решения и тест-планы
```

## Что важно

Это не финальное приложение, а стартовый код для снятия главного риска: маршрутизация и задержка audio pipeline через VB-CABLE.


## Gate 1: one-shot SoundBoard test

After Gate 0 succeeds, test a sound file mixed into the virtual microphone route:

```powershell
dotnet run --project src/VoiSe.Gate0.Cli -- --input "Микрофон (Fifine Microphone)" --virtual-output "CABLE Input" --monitor "Наушники (Realtek(R) Audio)" --sound-file "C:\Path\To\sound.wav"
```

Runtime keys:

- `S` — play the sound
- `X` — stop the sound
- `Ctrl+C` — exit

For OGG/Vorbis, the prototype uses `NAudio.Vorbis`.
