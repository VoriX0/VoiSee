# VoiSe Gate 5.6 — SoundBoard Build & Scroll Fixes

Gate 5.6 продолжает доведение вкладки SoundBoard до целевого UX после проверки Gate 5.3.

## Что изменено после Gate 5.3

- заголовок окна: `VoiSe Gate 5.6 - SoundBoard Build & Scroll Fixes`;
- блок `Start Engine / Stop Engine` перенесён в общий header окна справа от заголовка VoiSe и виден на всех вкладках;
- общий нижний блок удалён;
- лог приложения теперь находится только во вкладке `Settings`;
- на вкладках `Voice Changer` и `Scenes` снизу больше не показывается статистика SoundBoard;
- транспортный блок `Previous / Next / Stop` оставлен компактным;
- кнопка `Play/Pause` и таймлайн придвинуты ближе к транспортному блоку;
- `Stop` по-прежнему занимает ширину двух верхних кнопок;
- выпадающий список категорий остаётся слева и занимает примерно 25% ширины;
- кнопки `Add Track / Delete Track` перенесены под выбор категории;
- после кнопок треков сразу начинается список треков;
- список треков растягивается на всю оставшуюся высоту вкладки и должен корректно принимать прокрутку колесом мыши по всей области списка.

## Запуск

```powershell
dotnet run --project src/VoiSe.App
```

## Проверка

1. В заголовке окна должно быть `VoiSe Gate 5.6`.
2. `Start Engine / Stop Engine` должны быть справа в верхней части окна и оставаться видимыми на всех вкладках.
3. На SoundBoard кнопка Play/Pause должна быть рядом с транспортным блоком, без большого пустого промежутка.
4. `Previous / Next / Stop` должны образовывать компактный прямоугольный блок.
5. `Add Track / Delete Track` должны находиться под выбором категории.
6. После кнопок треков сразу должен идти список треков.
7. Список треков должен прокручиваться колесом мыши по всей области списка.
8. Лог должен отображаться только на вкладке `Settings`.
9. На вкладках `Voice Changer` и `Scenes` снизу не должно быть статистики SoundBoard.


## Gate 5.6 changes

- Removed the border around Start/Stop Engine controls.
- Moved Play/Pause closer to the transport block.
- Timeline tooltip now displays time instead of raw numeric values.
- Improved SoundBoard track list scroll hit area.
- Restored Settings log scrolling.
