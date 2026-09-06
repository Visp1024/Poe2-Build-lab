Kill any running PBLApp instance and launch a fresh one

```powershell
taskkill /IM "PBLApp*" /F 2>$null; dotnet run --project PBLApp/PBLApp.csproj
```
