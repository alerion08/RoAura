# ROQuestie: owner-approved product direction (2026-10-30)

These explicit owner decisions supersede conflicting parts of the original
ROQuestie_Technical_Specification.md and historical stage-2 UI notes:

1. A large quest window over the game contains catalog/search/filters, active
   quests, history and the full walkthrough. All normal quest actions happen
   here; the player does not need to open the main RoAura window.
2. A separate compact edge tracker shows multiple active quests/current
   instructions with independent Prev/Next, More and navi. It has a separate
   toggle hotkey from the large quest window.
3. The walkthrough gets the large window's primary area, clear current-step
   highlighting, scrolling and direct step selection. Do not restore a cramped
   side editor in the main application.
4. Requirements and rewards share one tab, with labeled sections, spacing,
   item lists and highlighted numeric values. Keep unknown/source wording;
   never infer server conditions, inventory or reward totals.

RoAura's Quests section contains appearance/placement controls and launch
shortcuts only. Both hotkeys are edited in Settings -> Hotkeys, not duplicated.
Current defaults: F6 large quest window, F5 compact tracker. Legacy bindings stay
unchanged; conflicting new bindings are disabled and reported rather than
silently replacing a user's existing key. F9 remains the global visibility mask,
retaining each quest window's independent requested state.

The large window intentionally accepts keyboard focus for search and inputs,
like the existing Database. Closing it returns to the exact selected game HWND.
The tracker must not activate or block keyboard/modifiers. Its text/full-client
HWND is entirely mouse-transparent; tiny nonactivating action HWNDs paint only
buttons. Transparent gaps/corners must pass through to the game process.
Do not make the ordinary OverlayWindow interactive or use cross-process ownership.

More opens the same large window's full-width walkthrough, highlighting the
current instruction. Requirements/rewards use only literal recognized source
labels; original text remains available. Navi chooses between all distinct
usable wiki points and copies a command with an explicit copied message. There
is no game command injection, guessed map, automatic answer or quest completion.

Progress continues to use server + observed CID or an explicitly selected local
profile. Switching clients blocks stale automatic CID attribution until a new
session revision is observed. Explicit manual mode remains labeled and separate.
Do not alter packet parsing/capture, other overlays, histories or coordinates.

Manual acceptance still required: real SpringRO 2K/fullscreen, protected-client
Z-order, multiple clients/relog, long-session input, actual OS mixed-DPI and game
navi paste behavior. CI fake-game input checks are evidence of HWND/input behavior,
not SpringRO gameplay or server quest availability. No release/merge/tag/version
bump is authorized by these design instructions.
