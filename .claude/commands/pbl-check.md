Snapshot the running PBLApp visual client and read the screenshot back.

Use this any time you want to inspect the **current** UI state without
restarting the client (no rebuild). Caller must have launched PBLApp with
`--enable-ipc` first (see `/pbl-run` or `/pbl-verify`).

The MCP `pbl-engine` server must be connected (the deferred `mcp__pbl-engine__visual_*`
tools must be available). If they are not, ask the user to `/mcp` reconnect.

Steps:

1. **Ensure the client is live** — call `mcp__pbl-engine__visual_is_client_running`.
   If not running, abort with a clear instruction to run `/pbl-verify` (which
   launches a fresh build).

2. **Capture a screenshot** — `mcp__pbl-engine__visual_screenshot` (empty path =
   auto-name in `%TEMP%\pob-screenshots`).

3. **Read the PNG** — use the `Read` tool on the returned path so the image
   shows up in the conversation. The user (and you) can then evaluate the
   layout / translation / colours visually.

4. **Don't leave the client in a weird page state.** If the screenshot was taken
   from BuildList, mention to the user that they may want to open a build
   (`mcp__pbl-engine__visual_open_build`) for a more useful view next time.

Output a single short sentence summarising what the screenshot shows; don't
narrate every pixel — the user can see the image too.
