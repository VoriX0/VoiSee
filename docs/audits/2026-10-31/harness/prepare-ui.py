import pathlib, json
root = pathlib.Path(__file__).resolve().parent
app = root / 'ui-baseline/src/VoiSe.App'
profile = str(root / 'isolated-ui-data')
for name in ['SettingsStore.cs', 'App.xaml.cs']:
    p = app / name
    s = p.read_text(encoding='utf-8-sig')
    s = s.replace('"VoiSe"', json.dumps(profile))
    p.write_text(s, encoding='utf-8')
p = app / 'MainWindow.xaml.cs'
s = p.read_text(encoding='utf-8-sig')
s = s.replace('private readonly AudioDeviceCatalog _catalog', 'private DispatcherTimer? _auditExitTimer;\n    private readonly AudioDeviceCatalog _catalog', 1)
s = s.replace('private void AutoStartEngineAfterRestore()\n    {', 'private void AutoStartEngineAfterRestore()\n    {\n        if (Environment.GetEnvironmentVariable("VOISEE_AUDIT") == "1") return;', 1)
s = s.replace('var isolationResult = _discordCableSessionIsolationService.Enable();', 'var isolationResult = Environment.GetEnvironmentVariable("VOISEE_AUDIT") == "1" ? new DiscordCableSessionIsolationResult(0, 0, null) : _discordCableSessionIsolationService.Enable();', 1)
s = s.replace('StartupLog.Write("MainWindow initialized; waiting for first activation.");', '''StartupLog.Write("MainWindow initialized; waiting for first activation.");
        if (Environment.GetEnvironmentVariable("VOISEE_AUDIT") == "1") {
            _auditExitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(35) };
            _auditExitTimer.Tick += (_, _) => { _auditExitTimer.Stop(); _exitRequested = true; Close(); };
            _auditExitTimer.Start();
        }''', 1)
p.write_text(s, encoding='utf-8')
autostart = app / 'AutostartService.cs'
s = autostart.read_text(encoding='utf-8-sig')
s = s.replace('public bool RefreshAndRepairIfRegistered()\n    {', 'public bool RefreshAndRepairIfRegistered()\n    {\n        if (Environment.GetEnvironmentVariable("VOISEE_AUDIT") == "1") return false;', 1)
autostart.write_text(s, encoding='utf-8')
print('Diagnostic copy: isolated data path, no audio autostart, no Discord mute, automatic exit after 35s')
