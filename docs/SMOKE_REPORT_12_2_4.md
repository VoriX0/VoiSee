# Static smoke report — VoiSee 12.2.4 Modular Rack

The Linux workspace does not contain the Windows .NET / WinUI SDK, so a real WinUI compile was not executed here. The source archive was checked structurally before packaging.

## Result

**35 / 35 PASS**

## Checks

1. XAML parses as XML.
2. `VERSION.txt` is 12.2.4.
3. csproj version metadata is synchronized.
4. Inno Setup version is synchronized.
5. visible application version text is synchronized.
6. Voice Changer has a three-column root.
7. Noise Suppression is in the left rail.
8. Presets are in the left rail.
9. Processing Chain is the center column.
10. Effect Library is the right column.
11. `EffectChainCardsPanel` is vertical.
12. Processing Chain has its own vertical ScrollViewer.
13. Presets have their own ScrollViewer.
14. Effect Library has its own ScrollViewer.
15. low-level wheel routing recognizes all three Voice Changer panes.
16. drag/reorder insertion uses Y-axis hit testing.
17. old horizontal-chain wheel handler is absent.
18. Noise Suppression uses three visible segmented controls.
19. the original hidden ComboBox contract is retained for existing settings logic.
20. active chain cards show an order badge.
21. active cards retain slider, numeric input, bypass and delete controls.
22. Effect Library is categorized.
23. all 15 effect types have explicit `Add` buttons.
24. category dropdown is connected.
25. preset search is preserved.
26. effect search is preserved.
27. `Bypass All` is preserved.
28. `Clear Chain` is preserved.
29. Voice Monitor is preserved.
30. ordered DSP persistence code is preserved.
31. no duplicate XAML `x:Name` values.
32. every XAML event handler resolves to a C# method.
33. C# braces are balanced.
34. all 15 legacy/library effect slider names required by existing code are retained.
35. no user settings/presets/scenes/sound-library payloads are included.

## Windows verification still required

Run on Windows:

```powershell
dotnet run --project .\src\VoiSe.App
```

Manual checks should focus on:

- visual proportions at the user's normal window size;
- drag from right library → center chain;
- reorder existing stages vertically;
- independent wheel scrolling in Presets / Chain / Library;
- selecting RNNoise / DeepFilterNet using the segmented controls;
- loading schema 1 and schema 2 presets;
- scene-driven voice preset restore;
- per-effect bypass and Bypass All.
