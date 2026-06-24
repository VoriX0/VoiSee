# Next steps on Windows machine

1. Install .NET SDK 8+.
2. Install VB-CABLE.
3. Open PowerShell in this repository.
4. Run:

```powershell
./scripts/bootstrap.ps1
```

5. List devices:

```powershell
dotnet run --project src/VoiSe.Gate0.Cli -- --list-devices
```

6. Start prototype:

```powershell
dotnet run --project src/VoiSe.Gate0.Cli -- --input "Microphone" --virtual-output "CABLE Input" --monitor "Headphones"
```

7. In Discord/Telegram select `CABLE Output` as microphone.

8. Record test results:

- microphone device name:
- VB-CABLE render endpoint name:
- VB-CABLE capture endpoint name in Discord/Telegram:
- monitor device name:
- audible latency subjective score:
- crackling/dropouts:
- CPU usage:
- notes:
