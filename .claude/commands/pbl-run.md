Kill any running PBLApp instance and launch a fresh one

```powershell
taskkill /IM "PBLApp*" /F 2>$null; dotnet run --project D:\Work\PathBuildLab\PBLApp\PBLApp.csproj
```
