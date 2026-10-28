# Character Sessions development

Branch: `feature/character-statistics`, based on the released 3.0.0 main commit.
No merge, tag, installer or public release is authorized.

## Stage gates
1. Isolate development runtime data, logs, IPC and updater. Build and test a DEV portable package.
2. Observe character-select/map-login packets without changing current game statistics.
   Confirm Character ID on SpringRO and RTM; packet IDs and offsets require live validation.
3. Implement per-TCP-flow session identity and transitions Unknown → Connecting → Active → Suspended.
   Do not use PacketPlayerAid as a verified Character ID.
4. Separate per-character Base/Job EXP, Zeny, Gold Goblin, time and persistent sessions.
5. Reuse existing overlay visuals and settings; switch only recorded/displayed statistics.
6. Add RU/EN identity status and safe manual fallback; never switch presets automatically.
7. Verify multiple Character Select cycles, late launch, relog, VPN, corrupt data and full regressions.

## Rules
Only confirmed character sessions may receive character-specific statistics.
No previous-character attribution after disconnect or uncertain identity.
No logging of raw packet payloads, account credentials or authentication tokens.
Keep release settings/presets unmodified; use RoAura-Dev for experimental data.
Zeny balance is baselined afresh at each new login; do not record the baseline as earnings.
Store session and lifetime figures separately; inactive-character time is excluded from rate denominators.
Multi-client support is deferred until flow-to-process association can be verified.
