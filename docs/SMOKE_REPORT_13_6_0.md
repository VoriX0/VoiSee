# VoiSee 13.6.1 — static smoke report

## Scope

Gate 13.6 Optimize Voice preparation.

## Static checks

- MainWindow.xaml: XML parse PASS
- App.xaml: XML parse PASS
- VoiSe.App.csproj: XML parse PASS
- app.manifest: XML parse PASS
- MainWindow.xaml x:Name values: 292 unique, 0 duplicates
- XAML event handlers: 99 referenced, 0 missing
- PerMonitorV2: present
- Optimize handler placeholder from 13.5: removed
- `PrepareOptimizedVoice`: present
- optimized canonical audio entry: present
- optimized profile entry: present
- Quick profile removal on optimization: present
- foreground optimization guard: present
- bin/obj: absent

## Runtime boundary

A full WinUI build and Windows audio execution cannot be performed in this Linux container. Windows compilation/runtime smoke remains required.

## Manual Windows smoke

1. Create Reference voice from >= 10 s audio.
2. Select Reference → Optimize.
3. Confirm foreground prompt.
4. Confirm voice becomes Optimized and the same preset name/ID remains selected.
5. Export `.vsee` and verify it reimports as Optimized.
6. Repeat Reference → Quick → Optimize and verify Quick is replaced, not duplicated.
7. Activate Quick before Optimize; after Optimize, confirm the same voice remains the active preset state.
8. Try a mostly silent/too-short reference and confirm Optimize refuses it with an explanatory error.
