Full UI-verification cycle: rebuild PBLApp, restart the visual client with
IPC enabled, open the test build, capture a screenshot, and read it back.

Use this **after any change to** XAML files in `PBLApp/Views/`, view models in
`PBLApp.Core/`, or anything that affects rendering. It exists specifically so a
"done" claim is backed by a real screenshot, not just a green build.

The MCP `pbl-engine` server must be connected so the deferred `mcp__pbl-engine__visual_*`
tools resolve. If they aren't available, ask the user for `/mcp` reconnect.

Steps:

1. **Kill any running client** (releases locks on Debug DLLs):
   ```powershell
   Get-Process | Where-Object { $_.Name -like "PBLApp*" } | Stop-Process -Force -ErrorAction SilentlyContinue
   Start-Sleep -Seconds 2
   ```

2. **Build**:
   ```powershell
   dotnet build D:\Work\PathBuildLab\PBLApp\PBLApp.csproj --nologo -v quiet
   ```
   Abort and report errors if the build is red.

3. **Launch with IPC**:
   ```powershell
   Start-Process -FilePath "D:\Work\PathBuildLab\PBLApp\bin\Debug\net9.0\PBLApp.exe" `
     -WorkingDirectory "D:\Work\PathBuildLab\PBLApp\bin\Debug\net9.0" `
     -ArgumentList "--enable-ipc"
   Start-Sleep -Seconds 9     # LuaHost init takes ~7-9s
   ```

4. **Navigate to the test build** — `mcp__pbl-engine__visual_open_build` with
   `name="New Build"` (or whatever the user names their test build). Wait
   ~5 s for the page to settle.

5. **(Optional) Drive to the specific tab / slot you changed** — e.g.
   `mcp__pbl-engine__visual_select_tab` `Items`, then
   `mcp__pbl-engine__visual_items_select_slot` `Helmet`.

6. **Screenshot + read**:
   - `mcp__pbl-engine__visual_screenshot` (empty path → auto temp).
   - `Read` the returned PNG so the image lands in the conversation.

7. **Report**: one short sentence summarising what changed visually. Don't
   re-describe the whole tooltip; trust that the user can see the image.

If the screenshot reveals a regression (overlap, missing translation,
broken binding, etc.), fix it before calling the task done — never claim
"done" with the screenshot left silent on a known issue.
