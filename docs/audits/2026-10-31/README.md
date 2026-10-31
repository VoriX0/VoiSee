# Свидетельства аудита VoiSee 12.5.1

Основной документ: `../../ARCHITECTURE_PERFORMANCE_AUDIT_12_5_1.md`.

- `audio-benchmarks.json`: отдельные DSP, allocations микшера, удержание PCM-кеша, воспроизведение незавершения one-shot без monitor и потери старого файла при ошибке ReplaceSoundFile.
- `native-RNNoise.json`, `native-DeepFilterNet.json`: отдельные процессы; синтетический сигнал, 20 ms blocks, 2 s warmup + 5 s paced run. CPU относится к одному ядру; это не CPU всего приложения.
- `ui-idle.json`: 35-секундный запуск диагностической оболочки, 70 samples; steady-state — 10–30 секунд. Пустая библиотека, engine/Discord mute отключены, окно в трее. Startup log зафиксировал обработанный отказ registry access до steady-state. Исходный тест завершился штатно.
- `package-size.json`, `package-groups.json`: инвентаризация portable/install 12.5.1. Опорный размер распаковки берётся из ZIP; каталог publish может содержать дополнительные локальные файлы.
- `harness/`: исходный измерительный стенд и инструменты изоляции диагностической копии UI. Это инструменты аудита, не изменения production-кода.

Машина: Intel Core i7-14650HX, 24 logical CPUs, Windows 10.0.26200, .NET 8.0.28. Не считать результаты гарантией для другого ПК. Короткий CPU-тест ограничен точностью Process.TotalProcessorTime; массивы результатов полезнее для allocations и регрессионных сравнений, чем для ранжирования близких эффектов.

## Воспроизведение компонентного стенда

Рабочий каталог — корень репозитория. В `artifacts/audit-12.5.1` скопировать `harness/Program.cs`, `harness/Audit.csproj`, `harness/AuditStubs.cs`.

Из коммита `b1829bb` извлечь все `src/VoiSe.Audio/*.cs` в `artifacts/audit-12.5.1/baseline/src/VoiSe.Audio/`, а `SoundBoardLibraryStore.cs` и `SoundBoardModels.cs` — в соседний `baseline/src/VoiSe.App/`. Не использовать там source из текущей main.

Стенд ссылается на NAudio, NVorbis и RNNoise.NET DLL из `artifacts/publish/VoiSe`. Для этого аудита использована имеющаяся поставка 12.5.1; не заменять её DLL случайными текущими сборками.

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --project artifacts/audit-12.5.1/Audit.csproj -c Release
```

Выход: `artifacts/audit-12.5.1/bin/Release/net8.0-windows/results.json`.

Для native-тестов скопировать `rnnoise.dll` и `deep_filter_ladspa.dll` из той же поставки рядом с Audit.dll, затем выполнить по отдельности:

```powershell
dotnet artifacts/audit-12.5.1/bin/Release/net8.0-windows/Audit.dll native RNNoise
dotnet artifacts/audit-12.5.1/bin/Release/net8.0-windows/Audit.dll native DeepFilterNet
```

Нативный DF worker живёт до окончания процесса. Раздельные процессы нужны, чтобы не смешивать его фоновые расходы с RNNoise или компонентными тестами.

## Диагностическая копия UI

`prepare-ui.py` предназначен только для свежей копии исходников `b1829bb` в `artifacts/audit-12.5.1/ui-baseline` (App, Audio, Directory.Build.props, global.json, fetch-deepfilternet script). Он заменяет путь данных и startup log на каталог внутри артефактов, отключает автозапуск микрофона, изменение Discord mute и autostart repair, добавляет таймер штатного выхода через 35 секунд. В исходную main эти правки не вносить. Это ограниченный idle-тест диагностической копии, не сравнение полной stock runtime-нагрузки. Запрет autostart repair добавлен после первого измерения; в первом прогоне попытка repair была запрещена sandbox до steady-state и не изменила registry.

После подготовки и Release-сборки `measure-ui.ps1` запускает копию с `--background`, собирает process counters раз в 500 мс и сохраняет `ui-idle.json`. Не использовать этот скрипт для обычной установленной программы: автоматический выход обеспечен только изменениями prepare-ui.py. Свежий UI-restore в этом окружении потребовал локальный источник `--source "$env:USERPROFILE\.nuget\packages"`, поскольку nuget.org недоступен.
