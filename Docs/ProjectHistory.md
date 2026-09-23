# Warriors & Wizards - project history (moved out of CLAUDE.md on 2026-09-22)

These sections were the oldest part of `CLAUDE.md`. They are history: every one is marked superseded or describes work that was later replaced. Nothing was edited when they were moved.

## History: the 2026-09-12/13 bug-fixing marathon

Starting point: 25+ commits of feature work (test scaffolding, a `ConditionEffectSet` system,
a repo reorg) had been pushed with **zero manual playtesting** in between. Manual testing
immediately surfaced a chain of severe bugs, all now fixed:

1. **Total black screen (world never rendered)** — `Object.frag`'s glow/outline post-effect
   did per-pixel neighbor-sampling in a loop bounded by a value derived from
   `1.0/length(dFdx(uv))`; degenerate derivatives on this GPU blew that bound up, and the
   effect painted whole sprite quads solid black over everything drawn underneath. Fixed by
   removing the neighbor-sampling glow effect (the separate, unrelated "fading in" alpha<1
   passthrough right next to it was kept).
2. **"Flat colored square instead of the real sprite"** — `Object.vert`'s `OBJECT_OUT`
   interface block had a struct-typed varying (`extra Extra;`) between `BaseUV`/`UV` and
   `Color`, corrupting the interpolated UV coordinates on this driver (see hardware constraints
   above). Fixed by flattening it to a plain `vec4`.
3. **Severe flickering/glitching** — turned out to be screen tearing from VSync being off, not
   a shader bug. Fixed by enabling VSync. (Buffer orphaning was also applied defensively while
   chasing this — didn't turn out to be the cause, but is worth keeping as good practice.)
4. **Tile/entity/model rendering rewritten off GL instancing** entirely — see
   `Alloy.Engine/Graphics/Buffers/InstanceAttributeBuffer.cs` (real per-vertex data, no
   instancing) and `Rendering/Render.Draw.cs`'s `FlushBufferModel` (expands each model
   instance's mesh + instance data into one flat vertex buffer per draw).
5. **Server: every new character spawned with ~25 status effects permanently active**
   (including `Confused`, which scrambles movement keys), and some base stats behaved oddly.
   Root cause: `alloy-server/GameServer/Game/Systems/Inventory/EntityInventory.cs`'s `Tick()`
   built item-slot stat updates using `StatType.Inventory0 + i` for `i` in `0..<_size`
   (`_size=20` for players: 12 regular inventory + 8 backpack slots) — but `Inventory0..11` is
   only a 12-slot range. For backpack slots (i=12..19) this overflowed into
   `Attack/Defense/Speed/Vitality/Wisdom/Dexterity/Condition1/NumStars`, setting them all to
   `-1` (all 32 bits set as `Condition1`, i.e. every base condition effect flagged active)
   since a fresh character's backpack starts empty. Fixed by mapping `i>=12` to
   `StatType.Backpack0 + (i-12)`. **Any future "stats/condition-effects acting weird" report
   should check for this exact class of stat-index-overflow bug before assuming a client bug.**
6. **Inventory drag-and-drop crashed** (`NullReferenceException` in `Sprite.StartDrag()`) —
   `ItemTile.OnBeginDrag()` removed the dragged sprite from its old parent, called
   `StartDrag()` (which reads `Stage.Mouse`, null while detached), and only *then* re-attached
   it to the new parent. Fixed by reordering: attach first, then `StartDrag()`.
7. **Item tooltips never appeared** — `Tooltip`'s base constructor set `Width`/`Height` (which
   are content-scale-derived properties: `Width = ContentWidth * ScaleX`) before any children
   existed, so `GetScale(0, ...)` permanently locked the scale at 0 (invisible), and nothing
   ever reset it. Fixed by only setting `Width`/`Height` in `DrawSprite()`, after content
   exists.

**Known remaining gap (not a bug — feature-incomplete)**: item tooltips show name + description
only, no stat details (weapon damage/range/rate of fire, potion heal amount, equipment stat
boosts). The original author's own code comment says "im going to make tooltips soon" — this
was already unfinished in the upstream source, not something broken by the above fixes.

All of the above was committed in `3a95c77` ("much stuffs fixed") on 2026-09-13.

## GUI customization — status as of 2026-09-15 (SUPERSEDED, see 09-16 section below)

Everything in this section describes a design direction (single centered PLAY button,
`ButtonFrameRed.png` plate, `PixelParchment` as the only secondary font) that was fully replaced
during the 2026-09-16 session below. Left in place as history only — do not treat anything in
this section as current.

## GUI customization — status as of 2026-09-15 (in progress, NOT committed)

**The user has explicitly said not to commit or push any of this yet** — there is much more UI
work planned before that happens. The user stepped away ("gotta go to work, back later") mid-session
on 2026-09-15 — this section is the handoff note for picking back up.

Current design direction for the title screen: **one big centered "PLAY" button only**, low on
the screen, sitting on the cobblestone path in the background art. The earlier
play/servers/legends/editor/exit multi-button menu bar has been fully removed —
servers/legends/editor/exit will be relocated to other, not-yet-designed areas of the UI later
(not on the title screen at all). Character select/creation still just show plain dark grey
(from the 09-13 work, unchanged).

Current uncommitted local state of the title screen (`AlloyClient/AlloyClient/Screens/TitleScreen.cs`):
- Single `MenuBarButton` reading **"PLAY"** (all-caps), no pulse/throb animation (removed per
  request — it looked weird), centered horizontally and positioned low near the bottom edge
  without touching it.
- Background frame behind the button: `AlloyClient/AlloyClient/Content/Ui/Buttons/ButtonFrameRed.png`,
  a nine-sliced plate **cleanly cropped by exact pixel rect (x:6-57, y:234-248) directly out of
  the user's `FreeHorrorUI.PNG`** asset sheet (NOT a Snip-Tool screenshot crop — that was tried
  first and produced a black-outline artifact from anti-aliasing, then redone properly). The
  sheet also has a grey variant of the same empty plate the user hasn't tried yet, plus matching
  PLAY/SETTINGS/EXIT button graphics with text baked in, if that ends up being preferred over
  dynamically-sized nine-slice + separate text.
- Button text now renders in a **second font**, `PixelParchment` (the user's own
  `PixelParchment.ttf`), not the default `MyriadPro` used everywhere else in the UI. This
  required adding real multi-font support to the renderer (previously exactly one global font
  atlas was supported) — see `Alloy.UiLib.Core.FontGroup`, `UiRender.RegisterSecondaryFont`,
  `TextureType.Text2`, and the second texture-unit uniforms in `Ui.frag`. A backup copy of the
  ttf lives in the user's own `Documents/Fonts/`.
- `AlloyClient/AlloyClient/Content/TitleScreen/TitleScreenGraphic.png` (the user's `Background.png`
  poster) and `ScreenBaseGraphic.cs`/`TitleScreenBase.cs` from 09-13 are unchanged.

**Two real engine bugs found and fixed while iterating on this button** (both non-obvious,
worth knowing about for future UI work — see `Ui/Components/Buttons/MenuBarButton.cs` and the
final `TitleScreen.cs` for the fixed patterns):
1. `MenuBarButton`'s pulse animation used to overwrite `Scale` outright every frame, silently
   discarding whatever `Stage.ScreenScale` a screen's `OnResize` had set — so a pulsing button
   never actually scaled correctly with the window (looked undersized/misplaced specifically
   after maximizing). Fixed via a new `BaseScale` property the pulse now multiplies against.
2. Wrapping the button + its background panel in a `Container` anchored `MiddleBottom` (with the
   children themselves anchored `Middle`) double-applied the centering offset — `Container`
   auto-computes its own size from its children's bounds, so giving it a centering anchor too
   stacks two centering offsets and shifts the whole group left/up by about half its own size.
   Fixed by dropping the wrapper `Container` and positioning both sprites directly at the same
   shared center point instead.

**Tried and reverted (09-13, superseded)**: a custom RPG-style corner menu panel cropped from the
user's own `Background.png` border — removed because the user wanted to source a proper asset
pack instead, which they've now started doing (`FreeHorrorUI.PNG`, above).
**Also tried and reverted (09-15)**: a vertical icon-button sidebar banner
(`VerticalButtonBar.png`, rotated 90° from a horizontal source asset) for the left edge of the
screen — abandoned in favor of the single centered PLAY button direction described above.

**Unrelated pre-existing bug found along the way (not yet fixed)**: `dotnet run --project
AlloyClient` (bypassing `WarriorsAndWizards.Client.sln`) always fails with `MSB3073` / a `*Undefined*
...ContentBuilder.exe` path, because `AlloyClient.csproj`'s `PostBuild` target references
`$(SolutionDir)`, which MSBuild only sets when building through the solution. Build via
`dotnet build WarriorsAndWizards.Client.sln` instead, then launch `AlloyClient/bin/Debug/net10.0/AlloyClient.exe`
directly.

**Another font-pipeline gotcha worth knowing**: a font recipe (`Content/Fonts/<Name>/<Name>.msdf`)
with only ONE `<FontPath>` entry makes `msdf-atlas-gen` emit a JSON shape this codebase's reader
can't parse (crashes with `KeyNotFoundException` on `"variants"`). Always list 2+ `<FontPath>`
entries (duplicate the same file under a second weight if there's only one real weight) — see
`PixelParchment.msdf` for the working example.

## GUI customization — status as of 2026-09-16 (in progress, NOT committed)

**Still not committed/pushed** — same as 09-15, there's more UI work planned first. The user
stepped away for work mid-session again ("gotta go to work... gotta close this session out") —
this section is the handoff note for picking back up. This entirely supersedes the 09-15 section
above; the single-PLAY-button/`ButtonFrameRed`/`PixelParchment`-only design was scrapped and
rebuilt several times over this session.

### Title screen (`AlloyClient/AlloyClient/Screens/TitleScreen.cs`) — current design

A list of 5 rows — **PLAY, SERVERS, LEGENDS, ACCOUNT, SETTINGS** — each an icon (left) + word
(right), stacked top-to-bottom under a one-line header message, positioned over a scroll that's
now drawn directly into the background wallpaper art (see below) rather than a separate
foreground frame image. Only **PLAY** is wired to anything (login prompt if logged out, else
straight to character list) — the other four are intentional placeholders (`onClicked: null`)
until account/settings pop-ups and a working servers/legends screen get built later. `OnServers`/
`OnLegends` navigation methods were deleted along with that wiring, not just unhooked.

**CRITICAL, must not regress**: `TitleScreen.FrameWidth`/`FrameHeight` (currently `420`/`540`)
are **permanently locked constants**, by explicit, heated user demand — see
this session's memory notes (`feedback_no_content_driven_sizing`). They used to be computed by measuring the row
buttons' and header's actual rendered text width (`Math.Max` over `TitleMenuButton.ContentWidth`,
`SimpleText.Width`, etc.), which meant *any* unrelated edit — changing a button's font size,
fixing a login-text bug, adding a row — silently resized the whole block and undid prior manual
tuning, repeatedly, across many prompts. **Never reintroduce measuring child content to compute a
container's size in this codebase.** If content needs to change, make it fit within the fixed
size (e.g. `SimpleText.MaxWidth` to force wrapping) — never make the container grow to fit it.
The two numbers are a direct, explicit edit only, and only when the user asks for that specific
change.

Visual details of the current row list:
- All 5 rows render at the same fixed size (`RowFontSize`/`RowIconSize`) — no more "PLAY is
  bigger than the rest" (that was an earlier, since-reverted design).
- Icon sits to the **left** of the word (flipped at least twice over the session — right, then
  left, then confirmed left is where it's staying) and is intentionally sized a bit **larger**
  than the word's cap height, so the icon reads as the visually dominant part of the row.
- Row/header text is **black** fill (was white, then a purple `#5A1E9C`, now solid black) with a
  black outline (so effectively no separate outline color — that was fine).
- Header text ("Click 'PLAY' to begin your adventure.." when logged out, "Grete thee wel,
  {username}" when logged in — deliberately archaic spelling, matches user's request) is small
  (`HeaderFontSize = 14`) specifically so it fits on **one line** within the fixed frame width;
  `MaxWidth` is still set as a wrap safety net for any longer message added later.
- Whole block is positioned via `HorizontalPositionFraction` (currently `0.286`, i.e. 28.6% of
  screen width) and a small `VerticalNudge` off dead-center — both were derived by *directly
  sampling pixel colors* out of the new background PNG to find where its drawn-in scroll actually
  sits (see below), not by eyeballing. If the background image changes again, re-measure the
  same way rather than guessing.

**Bug fixed**: the header used to sometimes show "Welcome back, Guest" even when not actually
logged in. Root cause: the account server's `/char/list` endpoint (`alloy-server/AccountServer/
Systems/Char/List.cs`) falls back to a synthetic `Account.Guest` record whenever verification
fails, and `AppRequests.GetCharList()` runs unconditionally on every app startup — so
`GlobalData.Get<AccountData>()` is *always* non-null, Guest or not. `GetHeaderText()` was using
"`AccountData` present" as its logged-in check; it now checks `GlobalData.Contains<LoginData>()`
instead (the same signal `OnPlay()` already used), which is the real signal.

### Fonts — now 3 font slots (was 2 as of 09-15)

The renderer's dual-font-atlas support from 09-15 (`FontGroup.MyriadPro`/`PixelParchment`,
`TextureType.Text`/`Text2`) was extended to a **third slot** this session (`Text3`,
`RegisterTertiaryFont`, new `PixelRange3`/`TextTextureSize3`/`TextTexture3` uniforms in
`Alloy.UiLib/AdditionalFiles/Ui.frag`, bound to texture unit 8 in `Main.cs`). Current mapping:
- `FontGroup.MyriadPro` — everywhere else in the UI (unchanged default).
- `FontGroup.AntiquityPrint` — the title screen header message only. This *used to be* the
  `PixelParchment` slot literally renamed (font file swapped from `PixelParchment.ttf` to
  `antiquity-print.ttf`, folder/enum/all references renamed) back when there were still only 2
  slots — that rename is why `PixelParchment` briefly didn't exist as a font at all mid-session.
- `FontGroup.CrunchyFont` — the PLAY/SERVERS/LEGENDS/ACCOUNT/SETTINGS row words. This is a
  **new**, genuinely third slot (not a rename) — added specifically so the header and the row
  words could use two *different* fonts simultaneously, which the old 2-slot design couldn't do.
  `PixelParchment` itself was restored from the user's `Documents/Fonts/PixelParchment.ttf`
  backup to fill this need first, then swapped again for the user's `Crunchyfont.ttf`, ending up
  with the `CrunchyFont` name it has now.

All font ttfs live in `Content/Fonts/<Name>/<Name>.msdf` + `.ttf` in the repo, and are backed up
in the user's own `Documents/Fonts/`. Yes, this means **any** font the user hands over becomes a
real, permanently-usable option in the codebase going forward, not a one-off swap.

### Background wallpaper (`Content/TitleScreen/TitleScreenGraphic.png`)

Swapped a few times this session; the **current** version has the "WARRIORS & WIZARDS" logo/
castle scene on the right half and a large blank pixel-art **scroll drawn directly into the
artwork** on the left half (with a red gem at the top roller and purple at the bottom) — this is
intentionally what the title screen's row list/header sits on top of now; there is no longer a
separate scroll/frame image layered on top (that whole approach — first a small hand-drawn plate,
then a large detailed scroll cropped from `freefantasy.png`, nine-sliced and later rendered
unstretched — was tried, fought over at length, and ultimately removed entirely per explicit user
request; only `FrameWidth`/`FrameHeight` remain, as pure invisible layout numbers). The
`HorizontalPositionFraction`/`VerticalNudge` values above were measured directly from this PNG's
pixel data (parchment-colored region bounds) to line the content up with the scroll drawn into it.

### Login/Register frames (`Screens/Components/Containers/LoginContainer.cs`/`RegisterContainer.cs`)

No longer plain dark-grey `ColorRect` boxes. Background is now a nine-sliced wood board
(`Content/Ui/Frames/LoginFrame.png`, `SliceLibrary.LoginFrame`, cut 14/28) cropped directly out of
the user's `freefantasy.png` asset sheet — specifically the *blank* hanging board next to the one
with pre-drawn PLAY/LOAD/EXIT buttons on the same sheet. Sized 480×400 (up from 475×350) to fit
the same inputs/buttons with a bit more room; internal layout (title text, username/password
inputs, bottom buttons) shifted down slightly to match.

**Bug fixed**: these overlays used to hardcode their position off `Settings.DefaultScreenWidth/
Height` (the *design* resolution) at construction time, so they only looked centered if the
actual window happened to match that resolution — otherwise they stayed off-center until some
later, unrelated resize event happened to correct it. `OverlayManager.PrivateSet()` now
positions the overlay immediately (against the real live `Stage.StageWidth/Height`) the moment
it's shown, via a new shared `PositionCurrent()` method also used by the resize handler, instead
of only correcting position reactively on the next resize.

### Icons

All five row icons (Play/Servers/Legends/Account/Settings) were swapped at least twice this
session for new user-provided art; current source files are `Content/Ui/Icons/Title
<Name>ButtonIcon.png`. `Content/Ui/Buttons/TitleButtonBackground.png` and `Buttons/
TitleButtonsFrame.png` (the two abandoned per-button/scroll frame assets, see above) are still
sitting in the repo unused — fine to delete outright if they never come back into play.

**Still to come from the user**: a new sound-toggle icon (the old `MusicButton`/top-left speaker
icon was removed earlier this session along with the rest of the old top-bar UI) — the user plans
to place it either in a corner of the drawn-in scroll on the title screen, or as a toggle inside
the future SETTINGS pop-up; wait for them to actually hand over the icon and specify which before
building either.

### Audio: MP3 support added (`Alloy.Audio`)

The engine only supported `.ogg` (via `StbVorbisSharp`) before this session; it hard-rejected
anything else with a logged "not an '.ogg' file" message. Added a pure-managed `.mp3` decoder
(`NLayer` NuGet package, no native deps) alongside it:
- New `IAudioStream` interface (`Channels`, `SampleRate`, `SongBuffer`, `Decoded`,
  `SubmitBuffer()`, `Restart()`, `IDisposable`) abstracts the chunked-decode shape `StreamTrack`
  needs, so it no longer depends on the concrete Vorbis type directly.
- `VorbisAudioStream` wraps the existing `StbVorbisSharp.Vorbis` streaming decoder;
  `Mp3AudioStream` wraps `NLayer.MpegFile`, converting its float PCM output to the same
  interleaved 16-bit short format on the fly. `Mp3AudioStream.DecodeFully(...)` handles the
  one-shot (static/SFX-style) fully-decoded-to-memory path the same way `StbVorbis.decode_vorbis_
  from_memory` does for OGG.
- `StreamBuffer.Vorbis` was renamed to `StreamBuffer.Audio` (type `IAudioStream`) throughout;
  `InternalAudioEngine`'s `_vorbisBuffer` dict renamed to `_streamBuffers` for the same reason —
  neither name made sense once a second format existed.
- `AudioEngine.CreateStaticTrack`/`CreateStreamTrack` now branch on file extension
  (`SupportedExtensions = [".ogg", ".mp3"]`) to pick the right decoder; everything else
  (fade/loop/volume/pooling logic in `AudioTracks.cs`) is unchanged and works identically for
  both formats since it only ever touches the `IAudioStream` abstraction.

Regression-tested by actually launching the client through to the title screen after the
refactor — the existing `Music/sorc.ogg` startup track still loads and plays with no errors.

**What's still pending**: the user is going to hand over an actual `.mp3` file to replace
`Music/sorc.ogg` (currently the *only* music track in the game — set once in `Main.cs`'s
`LoadContent()` via `Audio.MusicChannel.FadeTo("Music/sorc.ogg", 2f)` and never changed again
anywhere, so it plays continuously through the loading screen, title screen, and the entire game
with no separate in-game track). The user's eventual plan is a laid-back home-screen/character-
select track that switches to something more intense on entering the game world — that
track-switching logic doesn't exist yet and hasn't been discussed further than "eventually I'll
find tracks to switch to."

### Next planned work, in order (SUPERSEDED — see 09-17/09-18 section below for what actually
happened with items 1 and 3)

1. Wire in the user's replacement MP3 for the title screen music (waiting on the file).
2. Polish the login/register frame further (the wood-board background is a first pass, not
   necessarily final).
3. Character selection + character creation screen revamps (still plain dark grey from 09-13,
   completely untouched since).
4. Build real ACCOUNT and SETTINGS destinations (account likely its own screen; settings likely a
   small pop-up, not a full screen) and wire the two now-placeholder buttons to them; fix up
   SERVERS (currently broken/blank list rendering) and LEGENDS (never implemented past an empty
   stub class) properly, likely also pop-ups rather than full screens, per the user — exact shape
   undecided, described as "depends on what we're feeling when we get to it."

## Character Console + Warrior/Wizard rendering fixes — status as of 2026-09-18 (COMMITTED)

**Committed and pushed** as `840ec66` ("literally so much stuff, gui work and tweaks and fixes
mostly") — this section is now history/reference, not a handoff note. Everything below landed in
that one commit, which spans a huge multi-day arc (the character-select redesign started
09-16/09-17, character-rendering bug hunt happened 09-18).

### "The Console" — new character-select screen

`CharacterListScreen.cs` replaced the old plain-dark-grey placeholder (see the superseded "Next
planned work" item 3 above) with **`Screens/Components/CharacterList/CharacterConsole.cs`**, a
handheld-console-styled profile/character-select shell built from the "Pocket Inventory Series #9
Pixel Console" asset pack (grey/gold side panels with a D-pad and ABXY buttons framing a central
"screen"), over a dense animated starfield (`MilkyWayBackground.cs`, from the "LockVenture #5
Milky Way" pack). Old files fully removed: `CharacterRect.cs`, `CharacterWheel.cs`, `ClassInfo.cs`,
`ClassRect.cs`.

Current shape:
- A procedurally-animated entrance (console fades in closed, holds, then slides its two side
  panels open while the screen grows to fill the gap between them — reverse-engineered from the
  pack's own reference "Open" animation frames rather than guessed).
- Three tabs — **PROFILE / CHARACTERS / GRAVEYARD** — PROFILE shows a gold/copper coin-icon
  currency readout (`icon:amount`, no "GOLD"/"FAME" words, per explicit request), CHARACTERS shows
  a grid of character tiles (replacing an older one-at-a-time browse view), GRAVEYARD is a plain
  "offline" placeholder (never implemented anywhere in this codebase, not a new gap).
- **D-pad now drives tab navigation, not character browsing**: Up/Right = next tab, Down/Left =
  previous tab (`NextTab`/`PreviousTab`). Character tiles are mouse-click-select only now — the
  old `ShowPrevious`/`ShowNext` D-pad-browse methods were deleted, not just unhooked.
- The bottom button-legend text (`< / > BROWSE   A ENTER   X FORGE   B BACK`) was removed entirely
  per request ("I know how to navigate it") — more screen space reserved for future content. The
  plan for the ABXY buttons long-term (not built yet, just noted for later): X→a "create character"
  icon or reuse for other pages, and the two currently-unused joystick-adjacent buttons are
  earmarked for ideas like a news/updates popup or a daily-gift/spin feature — nothing beyond
  brainstorming yet.
- Currency icons (`Content/Ui/Console/GoldCoin0.png`/`CopperCoin0.png`, from "Pixel Reward Series
  #1 Coins", 32x32 **Static** variant) are a **single still frame, no animation** — went through
  two earlier passes (14-frame Animated/Shine — too intense a flash at this size — then the
  Static variant's own 8-frame slow-spin — still read as distracting motion) before landing here
  per explicit "no more animationssss" feedback. The Static 8-frame set is also staged unused in
  `Content/Ui/Currency/` for a future in-game (non-console) HUD currency display, which doesn't
  exist yet.
- The grey↔gold panel gap against the screen (`CharacterConsole.PanelOverlap`) was bumped from
  8→20 after directly pixel-sampling a screenshot showed a real ~8-9px sliver of starfield still
  visible through the "closed" gap — the same measure-before-tuning approach used throughout this
  whole redesign arc.

### Fourth font slot: `FontGroup.Occular`

Extended the by-now-established 3-slot font system (see the 09-16 section above) to a 4th slot —
`Text4`/`RegisterQuaternaryFont`/`PixelRange4` etc. mirrored through `Ui.frag`, `UiRender.cs`,
`SimpleText.cs`, `TextInput.cs`, `Main.cs` (bound to texture unit 9) — built from the user's own
`Occular.ttf` (found at `Desktop/Occular.ttf`, also present inside `Desktop/Bagura Font Pack
v1.5/`). Applied to **every** text element inside `CharacterConsole` (tabs, currency amounts,
roster names, empty/graveyard messages) per request ("the entire console font basically").

### Options menu (in-game Escape) was rendering almost entirely off-screen

`Game/Components/Options/OptionsView.cs` lays out its children (title, tabs, continue/reset/home
buttons) assuming its own local origin is the top-left corner of a 1280x720 canvas — but
`OverlayManager.PositionCurrent()` positions every overlay by its own **center** at the stage's
center, and `OptionsView` (unlike `ClassContainer`, which already does this) never called
`SetAnchor(UiAnchor.Middle)` to compensate. So its literal top-left corner landed at screen-center,
pushing almost the entire menu off past the bottom-right edge of the visible window — hitting
Escape in-game opened a menu the user essentially couldn't see or reach. Fixed with the one missing
`SetAnchor(UiAnchor.Middle)` call, matching the pattern every other popup in this codebase already
follows.

### Warrior/Wizard character rendering — size, shadow distance, attack lunge, bullet height

The biggest chunk of this session. `Content/Sheets/Players.png` was rebuilt around higher-res
"Puny Characters"-style art (32x32 cells) for the Warrior/Wizard class cut-down (see
`project_class_system_xml_driven` memory), replacing the original stock 8x8-cell sheet. This
surfaced a chain of rendering bugs that all trace back to `RenderBase.SetTexture` (used by
*every* game object type) baking in hard assumptions that only held for the original 8x8 art:

1. **Characters rendered ~4x too big and floated proportionally far above their own shadow.**
   `SetTexture`'s render-scale formula divides the raw cell pixel size by a hardcoded literal `8`
   — quadrupling the cell size (8→32) quadrupled the output scale, and since the vertical shadow-
   offset is driven by that same scale value, it floated up by the same factor. **Fixed** by
   adding a `protected virtual float TextureBaseUnit => 8f;` hook to `RenderBase`, overridden in
   `TypePlayer` to `32f` (the "players" sheet's real native cell size) — the formula now treats
   32x32 as its *own* baseline instead of comparing it against 8x8. A `RealSize` XML hack was
   tried first and abandoned — `Size` scales the *whole* character uniformly, but width/height and
   the vertical shadow-offset don't scale at the same rate when cell size changes, so no single
   `RealSize` value could ever get both right at once. `TextureBaseUnit` fixes the actual root
   cause instead, so any further `RealSize` tuning now safely affects size and position together.
2. **Even at "correctly" matched cell scale, characters still looked noticeably too small.**
   Measured why directly (Python/PIL `getbbox()`): pulled the *original* stock `Players.png` from
   `github.com/NotTheLegend/AlloyClient` for comparison and confirmed the original character fills
   its 8x8 cell 100% edge-to-edge with zero padding, while this sheet's Wizard/Warrior only fill
   ~38-53% of their 32x32 cell (lots of built-in transparent margin around the character, typical
   of this kind of asset-pack art). `TextureBaseUnit` only matches cell-to-cell scale, not that
   extra margin, so the character was still visibly smaller than the original even once "fixed."
   **Fixed** via measured, per-class `RealSize` values in `Players.xml` — Wizard `190`
   (100/0.531), Warrior `200` (100/0.50) — now a clean, deliberate "bigger than baseline" dial
   rather than fighting the shadow-position bug the way the abandoned earlier `RealSize` attempt
   did.
3. **Still floating slightly above the shadow after the size fix.** Same `getbbox()` approach,
   checked several idle/walk/attack frames: this art consistently leaves an 8-9px transparent gap
   *below the character's feet* within its cell — the original had none. `SetTexture`'s vertical
   shift (`padY`) assumed zero bottom margin (true for the original art only). **Fixed** via a
   second hook, `protected virtual float TextureBottomInset => 0f;` on `RenderBase`, overridden in
   `TypePlayer` to `9f/32f` to shift the sprite down and compensate.
4. **Both classes' shoot/attack animation looked identical and specifically like a bow-draw pose.**
   Confirmed by directly cropping and inspecting every frame of `Players.png`: the double-width
   attack-release frame (frame index 5, the one `Texture.cs`'s `TextureFromFacing` marks
   `attackFrame = true`) contains the *literal same* bow-draw artwork in all 6 rows — both the
   Wizard block and the Warrior block. This is an **asset-content problem in `Players.png` itself,
   not a code bug** — the frame-selection logic is identical and correct for every class; that
   specific frame just needs a distinct melee-swing pose drawn/exported for Warrior. Not fixed
   (can't be, from code) — flagged for whenever the art gets revisited.
5. **Attack animation made the character visibly lunge a full tile sideways when shooting.**
   Same double-width attack frame's *horizontal* centering (`padX` in `SetTexture`) assumes the
   original convention (character in the left quarter of the frame, weapon extending right — a
   hardcoded 0.25 "quarter into content width" bias). Measured this art's actual content center in
   the wide frame via `getbbox()` too: it sits at ~51% either way (idle vs. attack), i.e. already
   centered, no asymmetry to correct for. Applying the old convention's correction shoved the
   character sideways for no reason, and it scaled with the same `RealSize` bump from fix #2,
   turning what would've been a small quirk into an obvious full-tile lunge. **Fixed** via a third
   hook, `protected virtual float AttackFrameBiasFraction => 0.25f;` on `RenderBase`, overridden
   in `TypePlayer` to `0.5f` (dead-center, no bias).
6. **Bullets visually spawned from around head height instead of center-mass.** Unrelated to any
   of the above — `Game/Objects/Projectile.cs`'s `Draw()` hardcodes every projectile's world-Z to
   a flat `0.5f`, completely independent of the shooting entity's own size/position (shared by
   *every* projectile in the game, player or enemy). It only became obvious once the player
   sprite itself was finally sized/positioned correctly — there was nothing correct to judge it
   against before that. Lowered to `0.25f` per direct feedback. Since this is a shared constant,
   a future closer look should include checking how it reads for enemy bullets too, not just the
   player's own.

All six of the above were root-caused via actual pixel measurement (`getbbox()` against real
frame data, and a direct pull of the upstream original `Players.png`/`Players.xml` from
`github.com/NotTheLegend/AlloyClient` for a known-good reference) rather than guessed — repeat
that approach if this art changes again rather than eyeballing new `RealSize`/inset values.

### Found but deliberately deferred: `playerskins`/`playerskins16` atlas packing failure

Editing `Game.atlas`'s XML (for the `players` entry above) forces the content builder to fully
rebuild *every* entry in that file, not just the changed one — normally most are hash-cached and
skipped. This surfaced a pre-existing, unrelated bug: `PlayersSkins.png` is 14072px tall ÷ 8px
cells = 1759 rows, which isn't evenly divisible by the 3-row "Full" grouping
(`AtlasBuilder.ParseAnimated` divides `rowsPerSheet / group` and the remainder breaks the frame
loop), so both `playerskins` and `playerskins16` (used by `Skins.xml`/`EquipSkins.xml` cosmetic
skins) currently fail to pack and sit empty in the atlas — `Failed to add image <playerskins>` at
build time, non-fatal, build still succeeds. This predates today's session; it just never
surfaced before because `Game.atlas` had never been hash-invalidated. **Not fixed yet** — the user
said to come back to it ("dig into that eventually"), it's unrelated to the Warrior/Wizard work,
and equipped cosmetic skins aren't a currently-used feature so nothing visibly regressed.

### Audio: WAV support also added since the 09-16 section above

The 09-16 section documents MP3 support being added; since then a `WavAudioStream.cs` was also
added to `Alloy.Audio` (untracked, alongside `IAudioStream.cs`/`Mp3AudioStream.cs`/
`VorbisAudioStream.cs`) and the actual title-screen track was swapped from the placeholder
`Music/sorc.ogg` (now deleted) to the user's own `Content/Sound/Music/Main_Music.wav` — so item 1
of the superseded "next planned work" list above is done. The planned home-screen↔in-game
track-switching logic still doesn't exist.

### Process hygiene note from this session

Automating the client via PowerShell (`SetForegroundWindow`/`mouse_event`/screenshot loops for
visual verification) leaked stray `powershell.exe` processes across tool calls if not explicitly
closed after each one — the user noticed ~10 accumulating unnoticed. Always close out the
PowerShell process(es) used for automation, not just the client + wrapper, per the existing
`feedback_process_hygiene` convention.

### Next planned work (as of 2026-09-18, user stepped away mid-session — "gotta run")

In roughly this order, though the user explicitly hedged on how far down the list they'll
actually get:
1. Character selection + character creation GUI — the CHARACTERS tab/tile grid inside
   `CharacterConsole` and whatever the character-creation flow (`ClassContainer`/`ClassCard`)
   ends up looking like on/alongside the console, plus more polish on the PROFILE tab.
2. The GRAVEYARD tab (still just an "offline" placeholder) — user said they doubt they'll actually
   get to this one, lowest priority of the console pages.
3. **In-game HUD GUI overhaul** — everything under `Game/Components/Hud/` (bars, inventory,
   minimap, chat, `CharacterDetails`, etc.) is still the original unstyled stock UI, untouched by
   any of the console/title-screen redesign work so far.
4. Stretch goals, explicitly flagged as "if it's not too time consuming": SERVERS frame, LEGENDS
   page, ACCOUNT frame, SETTINGS frame — these were already noted as unbuilt/placeholder in the
   09-16 section above (title screen's SERVERS/LEGENDS/ACCOUNT/SETTINGS row buttons still have
   `onClicked: null`); this is the first time a concrete plan to actually build them has come up.

No visual direction has been given for any of these yet beyond what's already established
(console pixel-art aesthetic, Occular font, dark starfield backdrop) — ask before assuming a
specific look for new screens, same as every prior GUI phase in this project.

## Open performance problems (reported 2026-09-21 - FIXED the same day, see the audit; kept as history)
- Shooting and walking at the same time drops the client to about 1-5 FPS even with no enemies in play. On this old PC (which also hosts the servers) the goal is a steady 60+ FPS.
- Hitting the Nexus DPS dummy makes a huge stack of red text appear (like hundreds of damage numbers at once, oversized) and after 2-3 hits the client freezes.
- Leading suspect, confirmed present in the source but not yet proven to be the cause: `GameScreen.FixedUpdateStep = 1d / 60` is compared against a MILLISECONDS `ElapsedMs`, so the fixed-update loop runs ~1000 times per frame (projectile updates, hit tests, spark effects). Other findings in `Desktop\WARRIORS_PERFORMANCE_OPTIMIZATIONS.md` (comparison with TK-Client-Port, which runs thousands of enemies with no lag): thread pinned to CPU core 0 in `GameWindow`, entity visibility culling commented out in `Entity.UpdateVisibility`, per-frame `GL.BufferData` re-allocation of ~29 MB in `InstanceAttributeBuffer` (careful: orphaning was kept on purpose for this GPU's driver - allocate only the used size or ring-buffer, do not just remove it), a second entity draw pass, 35 visible tile chunks, `TieredCompilation` off. Verify each before changing; the hardware constraints at the top of this file still apply.

# Sessions moved out of CLAUDE.md on 2026-09-22 (evening) - 2026-09-19 to 2026-09-22

## Next planned work (stale since 2026-09-13)

The user wants to **customize the client GUI** next: the title/home screen, character
selection screen, and character creation screen. No specific visual direction has been given
yet — ask what look/theme they want before implementing; this is open-ended creative work, not
a bug fix.

## Releasing a new game version (2026-09-20)
`deploy -SetVersion 0.3.4` sets `Settings.BuildVersion` + `gameServerConfig.xml` `<Version>` together (deploy refuses to run if they differ), then run `deploy -Server -Client -Web`. Anyone still on the old
version (old web tab, old exe) is stopped at the title screen with an "Update required" prompt. Set `<DownloadUrl>` in `appEngineConfig.xml` (server) to give that prompt a Download button. See main CLAUDE.md for the details.

- **The Realm (2026-09-20)**: `Realm.json` + `Realm.jm` (140x140 forest island from `Tools/ForestContent/gen_realm.py`, 100% the new forest tileset - Woodland*/Forest* only, guarded by a test: spawn clearing, a river with two bridges, lake,
  marsh, highlands, dense woods, six outposts; checked reachable from spawn; NO exit portal because the only portal art is stock Lofi - leave with the Escape key; spot reserved at (70,66)) are wired to the Nexus's Realm Portal (`RealmCount` 1). IMPORTANT: this codebase's Realm never had
  monsters - `Realm : World` is 11 lines, `XmlLibrary.TerrainEnemies` is built but nothing reads it, and the old stock maps (`realm*.wmap`, 2048x2048, ~250k objects, in the prune backup zip)
  contain only scenery. Enemies (a spawner + enemy XML/art/behaviours + loot/items) are a separate, still-unbuilt feature. `Common.csproj` lists every world file by name: a new map/config
  must be added there or it never reaches the server package. `PortalData` now resolves a not-yet-created target world lazily (the Realm's exit to the Nexus is created while the Nexus is
  still being built). `Tests/GameServer.Tests/Worlds/RealmWorldTests.cs` runs the real data through `RealmManager.Init()`.
- **Music (2026-09-21)**: `Content/Sound/Music/` holds exactly three files: `Main_Music.wav` (menus; `Settings.MenuMusic`, per client, a settings-page option
  later), `Dreamtune.ogg` (the ONLY jukebox track: `musicConfig.xml` is generated by `Tools/Music/make_music_config.py`, which skips Main_/Menu_ and Realm_ files),
  `Realm_Pondering_The_Cosmos.mp3` (the Realm's fixed track, played locally by the client). `InGameMusic` has three modes (Menu / Playlist / Fixed) chosen by the
  world's music name in MapInfo (`OnWorld`); Nexus / Vault / Guild Hall = the shared jukebox, Realm = fixed. The client's MapInfo read order never matched the
  server's write order (Difficulty read where the seed was written, the music name never read) - fixed the same day. Tests: `InGameMusicTests`, `MusicLibraryTests`.

## Ranks, moderation, rewards, admin dashboard, editor (2026-09-20, from "Bugs and Todo.txt")
- **Ranks**: Player 0 / Moderator 80 / Owner 100 (`Common/Database/Ranks.cs`; `IsAdmin` = Owner). ALWAYS use `Ranks.Of(account)`. `CommandManager` now checks every command's level (it used to check only Admin ones, leaving Moderator commands open to all).
  Owners are made in the database only (SQL in the vault note "Ranks and Moderation"); `/setrank` cannot touch an owner. Moderation logic lives in `Common/Database/AdminDb.cs` (who may act on whom is decided there), reached through RPC methods `Moderate/FindAccount/GetMuteState/SendMail` on `IAccountServerRpc`.
  Commands: Moderator `find kick mute unmute ban unban`; Owner `give setrank mail spawn`; Player `online commands rank`. `CommandArgs` (pure parsing), `ItemSearch` (for /give). Bans/mutes use the existing `bans`/`mutes` tables (expiry now nullable).
- **Inbox / Daily Gift / Daily Spin**: tables `inbox_messages`, `daily_rewards`; `RewardsDb` (atomic claims, UTC day), `RewardRules` (week + wheel), endpoints `/inbox/*` and `/daily/*` in `AccountServer/Systems/Rewards`; client `Data/RewardsData.cs`, `AppRequests`, `Screens/Components/CharacterList/CharacterBook.Rewards.cs` (partial of CharacterBook).
  New accounts get a welcome mail (500 gold). Answers are queued and applied in the book's frame loop (never from the worker thread).
- **Admin dashboard**: `Game/Components/Admin/` (ADMIN tab in `HudTabs`, shown when `AccountData.Rank >= 80`); every button types a chat command; `AdminReplies` shows the server's answers; `TextureData.SheetName` tells new art from original (`ArtRules`).
- **Editor**: `Tools/Editor/` (editor.html, editor.js, generated palette.js, build_palette.py, tests/run_tests.py). Developer tool, not shipped. `ClientPlatform.OpenLocalPage` (+ web shim: returns false) opens it from the dashboard.
- Testing without a browser extension: the editor's tests run in headless Chrome/Edge. DB features were verified with a throwaway harness against the local Postgres (54 checks incl. simultaneous claims), then in the real client.
- Known: `Vault.jm` places an undefined object "Potion Storage". Client is now `127.0.0.1` by default (see folder roles); local play needs the two servers started from `alloy-server/bin/debug/net10.0`.

## Vault, in-game music fix, coin icons (2026-09-20, later)
- **Vault**: the chest system did NOT exist in the source (nor in the developers' current Reference Source: their old server had it commented out "TODO: fix", the new one never got it) - only the portal, a per-account world, the `VaultChest` model and `VaultCount`. Built here:
  `GameServer/Game/Worlds/Logic/Vault.cs` + `VaultRules.cs` (chest spots nearest the map centre get open chests, others closed), `<Persistent />` on `ContainerDesc` (an emptied Vault Chest stays; a loot bag still disappears), `EntityInventory.LoadItems/ItemTypes`,
  targeted save `VaultDb.SaveAsync` via RPC `SaveVaultChests` (writes only `VaultChests` with jsonb_set). Chests: `Vault Chest 0x9f12`, `Closed Vault Chest 0x9f13` in `Containers.xml`, art in ForestProps cells 2/3 from the bought Treasure Chests pack (`Tools/ForestContent/source/vault`, `draw_props.py`).
  Map: `Tools/ForestContent/gen_vault.py` (forest grove, 16 `Vault` spots, no exit portal - leave with Escape). Also fixed the upstream container-swap bug (`containerItem` was read from the player's inventory) and `ClosedVaultChest` no longer maps to EntityType.Container (it has no inventory).
  Tests: `VaultRulesTests`, `VaultWorldTests`, `VaultSwapTests` (real swaps; fail with the old bug), `VaultDbTests`. Verified live: /give -> real InvSwap packet -> chest -> DB -> full server restart -> item still in the chest.
- **Music bug**: `Signal<T>` keeps listeners by WEAK reference to the delegate target; a static method has no target so it is never called. `InGameMusic` used a static listener, so the Nexus never left the menu music and the Jukebox stayed on "Loading". Now an instance listener (`InGameMusic.Init(signal)`; `InGameMusicTests`). Rule: Signal listeners must be instance methods.
- **Icons**: the in-game player plate now uses the same gold coin + copper coin (`Console/GoldCoin0`, `CopperCoin0`) as the Character Book (it used the WaW pack's coin and star). Profile page lifetime gold/fame now also count what the Daily Gift / Spin / Inbox just paid (`_lifetimeGoldEarned`).
- **Text inputs invisible (fixed)**: `TextInput` drew its characters itself and its background box as a child; a sprite draws before its children, so the walnut `WaWSlot` box (opaque middle) covered the text in the chat, Bug Board and admin dashboard. Characters now live in a child `GlyphLayer` added after the box and before the caret (`Alloy.UiLib/BuiltIn/TextInput.cs`). Any box art works now. Also removed the dark name bar on `PlayerPlate`.
- **Levels go to 999 (2026-09-20)**: `Shared/Common.Protocol/LevelRules.cs` (`Common.Structs.LevelRules`) is the ONE place for the max level and the XP curve, used by server (`PlayerExtensions.GetNextLevelXPGoal`, `GameUtils.GetNextLevelXp`) and client. Levels 1-20 keep the original formula; after 20 each level costs 150 more than the last (`ExtraPerLevelAfterOriginal`), because the original curve needs ~3.3 billion XP in total (overflows the int XP field). Whole climb ~77 million. XP to next = 0 at 999. Tests: `LevelRulesTests`.
  BUILT 2026-09-22: XP on kills, level-ups with `LevelIncrease` growth capped by each stat's `max`, fame per 1,000 XP (see the Bugs-and-Todo batch below). HUD: the level bar and the fame bar are BOTH always shown (the original turned the level bar into the fame bar at level 20); `PlayerPlate` has no name any more (bars start at the top, height 156).
- **SimpleText cut-off bug (fixed)**: `SimpleText.Rebuild` only set `OverridePrimCount` when the new text fit the old buffer, so a text longer than any before it (after a shorter one) was drawn with the old smaller count = sentence cut off (seen on the Bug Board rate-limit message). Now always set. The Jukebox no longer shows a "done" confirmation after picking / skipping (errors still show).
- **Placing objects from code (gotcha)**: `Entity.Move` takes WORLD coordinates. Put something on a map tile at `tile + 0.5f` (its middle), never at the raw tile x/y: at the corner the picture is drawn half a tile up-left of the square it blocks (the Vault chests had an invisible wall at their bottom-right; fixed in `Vault.cs`, same fix for the Nexus realm portal). Test: `EveryChestSitsInTheMiddleOfItsTile`.
- **Daily gift / spin = 24-hour cooldown (2026-09-20)**: each can be used again 24 hours after it was last used (`DailyCooldown`/`DailyGiftRules` in `alloy-server/Common/Database/RewardRules.cs`, `RewardsDb`), NOT at a UTC-midnight reset (that let a player use both, then again 2-3 hours later). Gift streak carries if the next claim is 24-48h after the last, else back to day 1. New columns `daily_rewards.last_gift_at/last_spin_at` (Schema.sql migrates itself). Graveyard tab: still only a placeholder line ("You haven't died yet."): nothing records dead characters, `/char/fame` and `/fame/list` are stubs.
- **Flat props (2026-09-20)**: `<FlatOnGround/>` (lies in the ground plane, turns with the terrain, no shadow, drawn behind standing things) is only on the LOGS. Bushes/rocks were tried flat and looked worse, so they are upright billboards again (`Objects.xml`: `<File>forestDecor</File>` + `<BottomInset>`). Do NOT run `Tools/ForestContent/gen_xml.py` against Objects.xml: it rewrites everything after its marker and would cut off the Bug Board, Jukebox and other hand-added objects. Flat only suits art drawn from ABOVE.
- **FPS / memory readout** (the DEV tab / F5) now sits to the LEFT of the minimap, top-aligned with it (`GameScreen.PlaceDebugStats`, `HudView.MinimapLeft/Top`); the chat owns the bottom-right corner.
- Daily rewards migration: at every server start `Schema.sql` stamps a claim made TODAY (UTC) that has no exact time yet with the start moment (one-off for claims from before the 24h rule; new claims always store their exact time, so restarts never move a timer).
- **Real 3D props (2026-09-20)**: the Bug Board and the Jukebox are `<Model>` objects (`<Model>Bug Board</Model>`, `<Model>Jukebox</Model>` in Objects.xml): a code-built mesh (`AlloyClient/Assets/ModelData.Props.cs`, boxes + a picture quad per side made from the art's own pixel layout) drawn by the model pass, so they keep their world facing when the camera turns (you see their sides and back, they go edge-on at 90 degrees, they never swing through a wall). Any prop can get one: add a `PropBuilder` mesh, a `ModelType` entry, a `ParseModel` line in `ModelData.Prebuilt.cs` and the `<Model>` tag. Rules learnt: `Model.frag` now `discard`s alpha < 0.5 (needed for cut-out pictures); the model pass culls back faces and every model has ONE depth value, so overlapping faces inside a mesh are decided by DRAW ORDER (picture quads first, then solid boxes; skip faces another part hides); wind faces like the Wall mesh in `ModelData.Prebuilt.cs`; mesh origin is the object's centre, +y is the front (faces the default camera). The Vault chests stay ordinary upright sprites (user's wish). Trees / bushes / rocks are NOT 3D yet (their art is a single view, so a mesh would need crossed cards).
- **VSync now defaults to ON** (`Settings.VSync`) and was switched on in the user's `%LOCALAPPDATA%\AlloyClient\settings.xml` (backup `settings.xml.bak-before-vsync`): it was False + no FPS cap, which shows as flicker / glitchy GUI (tearing) on the HD 4400. The scratch/test clients share that settings file. The dashboard flicker could NOT be reproduced in captured frames (100+ identical frames) - it was presentation tearing; the small squares at the minimap are the map's own markers for solid objects (Bug Board, Jukebox, trees).
- **Screenshot rig lesson**: capturing frames with `GL.ReadPixels` into a .bmp is bottom-up and PIL already flips it correctly when reading - do NOT flip it again (that made the props look upside down and cost an hour).
- **GUI flicker (2026-09-20) - what happened, what is fixed**:
  1. FIXED and verified: centre-anchored windows followed their children's size. A `SetAnchor(UiAnchor.Middle)` sprite is centred on `ContentWidth/Height`, the union of its CHILDREN (`DisplayContainer.UpdateBounds`), so when a page's contents grew / shrank / were rebuilt the whole window slid and flickered (the dashboard sat ~65 px off-centre and moved per page). Windows now declare `Overlay.FixedSize` (design px) and `OverlayManager.PositionCurrent` centres exactly that box: Admin Dashboard, Jukebox, Bug Board, Options. Do NOT use `SetAnchor(Middle)` on an in-game window with changing content (Login / Register / Class / Book / Dialog still do).
  2. BAD IDEA, removed: orphaning the UI's `IndexBuffer` / `VertexBuffer` and uploading only the used range in `SpriteRender.Flush`. It made the whole client (title screen included) flash BLACK frames, measured at ~22% of frames. The three files are back to the original code. Do not retry without measuring (see 4).
  3. VSync now defaults to ON (`Settings.VSync`) and the user's settings.xml was switched (backup `settings.xml.bak-before-vsync`). The old promoted build with the same settings shows 0% black frames, so VSync is not the problem.
  4. HOW TO MEASURE FLICKER: capture the REAL screen (`CopyFromScreen` of the client window, ~3 captures/s, window in front) for 40 captures after ~22 s and count near-black frames (brightness < 40% of the median): healthy = 0 of 40, bad = 5-40%. Repeat 2-3 times (it is noisy) and compare against the old build in `Warriors-and-Wizards-Game`. `GL.ReadPixels` before the swap CANNOT see this kind of flicker.
  5. BUILD PITFALL that wasted an hour: `Copy-Item` keeps the SOURCE file's old timestamp, so MSBuild thinks the file is unchanged and does NOT recompile it - a "revert" by copying files silently left the changed code in the DLL. After copying / restoring a source file always set `LastWriteTime = Get-Date` (or edit it normally) before building, and check a DLL really changed (e.g. compare strings) before trusting an A/B test.
- **Unused stock objects removed (2026-09-20)**: White Flowers, Pink Flowers, White Wall, Statue of Oryx (+ Base), Arena Column Short, destnex Priest Grave (its whole file `NexusDestroyed.xml` and the `Common.csproj` link are gone) and the Sheep NPC. Object definitions now: 56 in the editor palette (was 64). KEPT on purpose: Pirate (the one test entity), White Fountain, and everything the GUILD HALL maps (Guild0-3.jm) use: Table, the 4 Table Edges, Weapon Rack, Wood Panel Banner / Wall / Window, Red Pillar, Dummy Strong, Target Strong, Guild Chronicle / Board / Register / merchants, Armoire, DPS dummies. Also kept although they LOOK unused in the dashboard library: **Fire Bolt** (the projectile of the Wizard's starting ability item "Old Spell", formerly "Fire Spray Spell", `Players.xml` equipment 0xa2e, and enemy behaviours) and **Invisible** (type 0x0096 is the blank picture the client draws for every EMPTY inventory slot: `TextureHelper.FromGameAtlas(0x0096)`), plus Blade / Grey Missile (the weapons' projectiles). After deleting a data file also delete its stale copies in every `bin` folder (the server loads all `*.xml` it finds). Art no longer referenced by any XML: sheet `lofiChar216x16` (candy: `Content/Objects/CandyColBroken.fbx` is also dead) - not deleted yet.
- **GUI glitches / flicker - ROOT CAUSE FOUND AND FIXED (2026-09-20)**: the UI is drawn in several batches per frame (each top-level layer ends its own batch; a batch is also cut when full) and `SpriteRender.Flush` uploaded EVERY batch into the SAME three GPU buffers. The Intel HD 4400 driver does not wait for a draw still reading a buffer, so when the GPU was busy (loading, lots of UI appearing at once, fast rotation, dashboard page switches) a later batch's upload landed under the earlier batch's draw and pieces of the GUI were drawn from wrong data for a frame: the loading cover drawn with a small black corner and no logo (the world flashing through), tabs / buttons stretching, the dashboard flickering. FIX: `SpriteRender` now keeps a ring of 8 separate buffer sets (instances SSBO, index buffer, vertex buffer, VAO) and uses the next one for every batch; nothing is ever re-allocated. MEASURED: before, a bright frame inside the black loading period in 6 of 8 loading periods; after, 0 of 12 (the only later detector hits were other windows covering the client). The earlier attempt (orphaning = re-allocating the buffers every upload + partial uploads) made the driver flash black frames and was removed - do not do that. The earlier measurement that "found nothing" only looked at steady moments; the glitch needs a busy GPU (loading spike) - measure at load, world switch, opening panels.
- **World switch cover**: `WorldLoadCover.Begin` now cuts to opaque black immediately for a portal / Escape switch (it used to fade in over the old world, which is wiped the instant the Reconnect arrives while the new one starts appearing, so the fade showed an empty / half-built world).
- **HOW TO MEASURE GUI FLICKER PROPERLY**: `pip install dxcam` (desktop duplication, already installed here) captures every DISPLAYED frame (~55/s) of a small region without slowing the game; pin the client window topmost first (`SetWindowPos(h, HWND_TOPMOST)`) or other windows ruin the run; compare each frame's tab faces / plate interior against the phase's median and flag frames > 2% different. `CopyFromScreen` / `ImageGrab` only manage ~6/s and `GL.ReadPixels` serialises the GPU (hides races). The rig was: test account via a small console program using `Common` (`DbClient.RegisterAsync/VerifyAccount/CreateCharacterAsync`), a copy of the client with an auto-login + auto-enter-game edit and a phase driver (IDLE / ROTATE / NEARBOARD written to a phase file), and a Python capture script. The capture script is kept in the scratchpad as `fastcap_keep.py`; the UI batch counters were 6 lines in `SpriteRender.Flush/StartDraw`.
  The loading / switch test that finally reproduced it: a client copy with auto-login, auto-enter-game, and (6 s after the world is up) `UsePortal` on the Realm Portal (type 0x0704) then 14 s later an `Escape` packet; the capture script (kept in the scratchpad as `cap2_keep.py`) records every displayed frame's mean brightness through each loading period and reports a bright frame inside the dark run, and saves the frames around it. The phase marker files are written from `GameScreen` (`LOADWORLD` in the constructor).
- **Starter helmet + spell art (2026-09-20)**: The starter helmet **Old Helmet** (was Broken Helmet, before that Combat Helm, 0xa66) and the starter spell **Old Spell** (was Fire Spray Spell, 0xa2e) use `weapons` sheet cells 10 / 11 (the user's `BrokenHelmet.png` / `OldSpell.png`, kept in `Tools/Weapons/source`, added by `build_weapons_sheet.py`). Both are `<Tier>0</Tier>` (the original developers' tier tag; only swords/staffs have T1-T4 so far). Renamed with scavenged-gear descriptions on request; type ids, numbers and effects unchanged. Older notes below may still say Combat Helm / Fire Spray Spell.

## Art sheets rebuilt (2026-09-20) - read `Tools/Sheets/README.md`
Every old sheet (Lofi*, Forest*, Weapons, invisible, chars8x8rBeach...) is gone from `Content/Sheets/`; only pictures something really uses were kept and regrouped by size
class into: Grasslands_16x16, SmallPlants_24x36, MediumPlants_40x60, SmallTrees_56x84, MediumTrees_80x120, FlatProps_48x48, LargeObjects_80x120, EquipAndConsume_16x16 (one column
per kind, row = tier; cell = tier*12+column), GuildHall_8x8, GuildHallLarge_16x16, Icons_8x8, CaveMonsters_16x16, Npcs_8x8 (+ Players, AlphaTileBlends unchanged). The old sheets +
Game.atlas are archived in `Tools/Sheets/source_old/`; `migration_map_2026-09-20.json` maps old `sheet:cell` -> new. Rules that matter: decoration cells stay 2:3 (sprite scale comes
from the cell), `BottomInset` is a fraction of cell HEIGHT, the atlas name is what XMLs/code use (`grasslands`, `equipAndConsume`, ...). The Broken Helmet is the user's `item209.png`.
Gotchas found doing it: `CaveBattle.cs` (title-screen cave fight) draws monsters/effects from sheets too (now `caveMonsters` + `icons`); `ForestBackdrop`'s decor table
(`BackdropData.g.cs`, generated by `Tools/BookUi/build_backdrop_ground.py`, which now bakes ground from `source_old`) carries a Sheet name per item; `ArtRules` (admin dashboard "NEW / ORIGINAL")
now lists the new sheets and treats the stock Health Potion cell as original. The old sheet-making scripts (build_forest_sheets, draw_props, gen_xml, build_weapons_sheet) are
guarded as obsolete. Still original art: guildHall*, icons, caveMonsters, npcs, the potion. Verified: `python Tools/Sheets/verify_sheets.py`, all test suites, editor tests, web build,
title screen launched (cave battle draws), and the Nexus + Vault were looked at in-game on 2026-09-21 (ground, edges, bushes, rocks, mushrooms, chests, Bug Board, Jukebox, item + slot icons all right). NOT yet looked at: the Realm, guild hall pieces, condition icons, loot bags.

## Session 2026-09-21 (what changed after the sheet rework)
- **Stats overlay** (`StatsPopup`): no header text and no divider line any more (`HudPopup` hides its divider when the title is empty; the Backpack popup keeps its "BACKPACK" title). Rows start at `FirstRowY = 42`.
- **Nearby players panel** (`Game/Components/Hud/NearbyPlayersPanel.cs`): a walnut frame, same size as the interact panel (232x126), ALWAYS visible at the bottom of the HUD right of the gear; the interact panel now sits to its right (`HudView.Layout`). Lists up to 6 closest other players (name left, class right) from `PartyData` (which now really sorts by distance), or "No players nearby!". Clicking a row opens a menu above the frame: class + level, and for staff only "Mute 30m" / "Kick" (the admin dashboard's chat commands). Trade / whisper / friend requests / lock / ignore are NOT offered on purpose: the server has no handlers for them (only the packet ids exist). The user tested it and said it looks good.
- **Art sheets rebuilt** - see the section above. `ArtRules` (admin dashboard NEW / ORIGINAL) uses the new sheet names.
- **Architecture note (ECS)**: the SERVER is ECS-style (`EntityId`s, component structs such as `PlayerChat`, `...Manager` classes on `ManagerBase<T>` with sparse sets, under `GameServer/Game/Systems`; described in `alloy-server/AGENTS.md`). The CLIENT is ordinary object-oriented (`Entity` / `Player` / `Projectile` classes, `Map.Players` / `Map.Entities` dictionaries), no ECS.
- **Process rule from the user**: no automatic test launches (see START HERE). Write the code carefully in one pass, build, run unit tests, report what is untested.

## Engineering audit (2026-09-21) - read `Docs/EngineeringAudit.md`
The full-codebase audit from `Desktop\FablesTasks.txt` lives in `Docs/EngineeringAudit.md` (architecture, findings, prioritized plan,
change log per step, manual test list). Both problems below are FIXED (fixed-update loop ran ~1000 steps per frame because
`1d/60` seconds was compared to a millisecond accumulator; a hit was re-processed every step). Also done since: server loop sleeps,
async logins, graceful shutdown, character autosave, PBKDF2 passwords with silent upgrade, login limits, plausibility logging,
per-chunk static ground meshes, entity culling, one shared `PacketId` enum. Keep that document's change log and manual test list
current when touching engine, network or persistence code.

## The Portal (2026-09-21) - the game's RealmEye
`Portal/site` (static HTML/CSS/JS, same look as the website) + `Portal/vps/setup_portal.sh` + `deploy -SetupPortal` / `deploy -Portal` + the read-only
public API on the AccountServer (`AccountServer/Systems/Public/PublicHandlers.cs`: `/public/player?name`, `/search?q`, `/leaderboard?kind=fame|chars|level|guilds`,
`/guild?name`, `/online`; shaping + SQL in `Common/Database/PublicDb.cs`, tests `PublicProfileTests`). Profiles are public for everyone (user's decision), show the
4 gear slots + MAX stats + fame/exp/level/backpack per living character, class records, stars (fame goals from gameConfig.xml `StarGoals` x classes), guild, created /
last seen, online + world (asked from the game servers over RPC). Never exposed: inventory, credits, potions, e-mail, account id, deleted characters.
`Tools/Portal/build_portal_data.py` crops item icons (`EquipAndConsume_16x16.png`, cell = Texture Index) and class portraits (`Players.png`, 32x32, first frame at
y = index*96) x3 nearest-neighbour into `Portal/site/icons/` and writes `data/items.json` / `classes.json` / `build.json`; `-Portal` runs it. nginx gives pretty URLs
(`/player/Name`, `/guild/Name`, `/top/fame`, `/wiki/items`, `/wiki/item/0a00`, `/wiki/classes`, `/graveyard`); locally (file:// or localhost) the pages use `?name=` etc.
`Program.GetContextIP` trusts `X-Forwarded-For` only for connections from the VPS itself (nginx), so rate limits see the real visitor. NOT built yet: graveyard (the game
records no deaths), per-account "hide my profile", daily fame snapshots / graphs, forum-account linking, skins / pets / dungeon counts (the game has none).
**In-client Portal (2026-09-21, later)**: the title screen's PORTAL row (between SERVERS and SETTINGS) opens `Screens/PortalScreen.cs` -> `Screens/Components/Portal/PortalView*.cs`
(parchment board over the ForestBackdrop, the book's palette, MyriadPro only - the FontGroup enum has ONE value now, the multi-font notes above are history). Same pages as the
website: Home (online count, top 10 x3), player profile (facts, class records, character rows with portrait / 4 gear slots / 8 stat chips, gold = maxed), guild, leaderboards
(4 tabs), items (slot filter chips, cards, detail with "used by"), classes (cards, detail with start/max table, starting gear, usable gear), graveyard. Names / guilds /
classes / items are links; BACK walks the history (Escape too), then leaves to the title. Live data: `AppEngine/PortalRequests.cs` (POST to the same `/public/...` endpoints,
never sets the offline flag) + `Data/PortalData.cs` (System.Text.Json JsonDocument, tests `PortalDataTests`); answers are queued and applied in the frame loop, cached per
screen visit (REFRESH link), one request per key at a time. Wiki data comes from `ObjectLibrary.TypeToItem` / `TypeToObjectProps` (IsPlayer) - the same XML the website's
tool reads. Nothing links out to the website (user's wish: no external links). Layout = fixed design px, long lists page with PREV / NEXT (no scroll container).

## SPAWN LOCATION (code name FastTravel) + server list in the Character Book (2026-09-21, later)
The page is titled SPAWN LOCATION on purpose: "fast travel" is reserved for a future in-game feature. No IP/port is shown on the server row (user's wish).
- The title screen has THREE rows now: PLAY / PORTAL / SETTINGS. SERVERS is gone (one server; `ServersTitleScreen.cs` + `ServerRect.cs` deleted) - the server
  list moved to the book. LEGENDS/ACCOUNT were removed earlier (older sections above that say 5 rows are history).
- The book has a SEVENTH tab, `BookPage.FastTravel` (`CharacterBook.FastTravel.cs`), THIRD from the top (Profile, Characters, Fast Travel, Inbox, Daily Spin, Daily Gift, Graveyard - the
  Graveyard moved last; tab art is per SLOT so only the enum order + `TabIcons` changed): left page = SERVERS (the one server from `ServerListData`, name =
  `gameServerConfig.xml <ServerName>` = "US East" now, players online from `/public/online`), right page = FAST TRAVEL tiles (Nexus / Vault / Guild Hall - always all three, the Guild Hall darkened and
  unclickable without a guild; no captions under the tiles, by request) with a map preview each; the highlighted tile is `Settings.FastTravel` (a `FastTravel.Destinations` key, `Data/FastTravel.cs`)
  and `Client.SendHello` sends its `GameId`. The pack has only 6 tab sprites: Tab7 = Tab6's art one slot lower (`BookFrameData.g.cs` has the frame BY HAND -
  re-running `build_book_assets.py` would drop it), icon + previews come from `Tools/BookUi/build_fast_travel.py` (one pixel per tile from the real .jm maps,
  ground colour = average of the ground art's cell; rerun after map changes). Ui.atlas lists every image explicitly: new PNGs must be added there.
- Server: `Shared/Common.Protocol/WorldIds.cs` (Nexus -1, Test -2, Vault -5, GuildHall -6). `Hello` resolves Vault via `Vault.ForAccount(acc)` and GuildHall via
  `GuildHall.ForAccount(acc)` ("You are not in a guild." otherwise); anything else must already be in `RealmManager.Worlds`. NEW `Worlds/Logic/GuildHall.cs`:
  one world per guild id, `GuildHall.json` `Name` is now "GuildHall" (was "Guild Hall", which no class could ever match - the Nexus Guild Hall Portal logged
  "World logic doesn't exist" at every start; fixed as a side effect). Every guild gets map 0 (Guild0.jm) until guild levels exist. Tests: `GuildHallWorldTests`.
- Wire note: the new negative GameIds are new VALUES on an existing field. An old server answers them with "Invalid target world", so deploy `-Server` before
  `-Client`/`-Web`, or bump the version (`deploy -SetVersion`) and ship all three.
- ATLAS GOTCHA (2026-09-21, cost the title scroll on the live web client): the UI atlas is a fixed 4096x4096 (`Alloy.Common/AtlasConfig.cs`) and was nearly
  full; adding the map previews made the packer drop `Frames/MenuScroll` (340x910, the biggest picture) with a NON-FATAL "Failed to add image" line in the
  build output - the build succeeds and the sprite just vanishes. `AtlasBuilder` now packs the biggest pictures first (sorted by PNG header area), which
  fits everything again with room to spare. After adding any UI art, grep the build output for "Failed to add" (or `grep -c <name>` in the packed
  `bin/.../Content/Ui.atlas`). The builder only repacks when the recipe or a listed PNG changes (hash cache): edit the recipe to force it.
- NOT verified in a window (built + 725 tests green): the seventh tab's placement on the book art, the tiles' look, spawning into the Vault / Guild Hall from PLAY.

## ALL-PACK ART (2026-09-21, later) - read `Tools/Sheets/README.md`
Every sheet in `Game.atlas` now comes from THREE bought packs by one artist on the Desktop: `GrasslandAssets` (Nexus / Vault / Realm: unchanged, already pure pack art),
`DarkDungeonAssets` (new sheets `dungeon`, `dungeonDecor`, `dungeonItems`, `dungeonMonsters`, `hudIcons` (+ a few drawn HUD glyphs), and the `largeObjects` pictures) and
`CharactersAssets` (`players` + new `skins`: all 19 characters in the game's 7x3 block for a future wardrobe; `character_block` in the tool is the measured frame rule).
Retired and moved to `Tools/Sheets/source_old`: GuildHall, GuildHallLarge, Icons, CaveMonsters, Npcs, EquipAndConsume. `Tools/Sheets/build_pack_sheets.py` is the re-runnable
rework (its `MAP` = object id -> new cell; it halves `<Size>` for art that left an 8x8 cell); `verify_sheets.py` proves every reference and every picture. The guild hall maps
keep their names but draw dungeon floors / brick walls / crates / torches; portals = torches / a chest, loot bags = coins, projectiles = arrows / flame, condition icons =
items (`ConditionEffect.IconSheet`), cave battle = the six dungeon monsters (`cell % 6`), Bug Board / Jukebox / Vault chests = door / barrel / chests scaled x5. Items: swords
13/18/18/23/23, staffs 24/21/21/22/22 (repeats are placeholders). 64 objects are PLACEHOLDERS (generated `Game/Components/Admin/ArtPlaceholders.g.cs`, shown so in the admin
dashboard via `ArtRules.IsPlaceholder(objectId)`; "ORIGINAL" now only means a retired sheet name crept back). The editor palette was rebuilt; the Portal data too. NOT looked at
in a window: every placeholder's size / look in the guild hall and Nexus, the HUD icons, the cave battle - the user tests. Client 233 + server 431 tests green.

## NEWS BOARD - a rule for EVERY future session (2026-09-21)
`alloy-server/Common/Resources/News/PatchNotes.txt` is the News Board in the Nexus (object `News Board` 0x9f14 at Nexus tile 37,26, two tiles west of the Bug
Board; the Bug Board's picture and mesh; `Class NewsBoard` -> `NewsBoardPanel` / `NewsBoardView`; endpoint `/news/board` on the AccountServer reads the file,
re-read when it changes, no restart). **At the end of every work session, before the user deploys, add a new entry at the TOP of that file** in the
format the file's header shows (`## <date> - <title>` + `- ` lines written for players). The server package carries it (Common.csproj), so `deploy -Server`
publishes it. Tests: `PatchNotesTests` (format + the shipped file must have entries, newest first).
- Character book: the Characters page shows a NEW tile while a free slot exists and always a BUY SLOT tile with the price in fame (`CharacterListData.
  CharSlotCost`, 1000; the server's `/account/purchaseCharSlot` charges CurrentFame and raises MaxChars). New accounts get `newAccountsConfig.xml` MaxChars = 2.

## Late 2026-09-21 batch (items, view, title map, book)
- ITEMS: only 8 exist now - Old Sword (dungeonItems 0x12), Old Staff (0x16), Old Helmet (0x0a), Old Spell (0x08), Old Armor (0xa67, slot 7, DEF+3, 0x0f), Old Robe
  (0xa68, slot 14, DEF+1 MP+10, 0x0f = the armour's picture, a PLACEHOLDER), Old Ring (0xa69, slot 9, HP+10, 0x00), Health Potion. Both classes start with their full
  set (`Players.xml` Equipment slots 0-3). The tiered Iron/Steel/Bronze/Gold swords and staffs (0xa01-04, 0xa98-9b) were REMOVED until there is art per tier
  (`project_weapon_tiers` memory is history). The server now drops saved items whose definition is gone (PlayerExtensions.InitPlayerInventory reads past their
  bytes; EntityInventory.Init skips them) instead of crashing the character load. Tests that used those ids now use the starters.
- Options: "Player Position" Centered / Lowered (`Settings.LowerPlayerView`, `LowerViewTiles` = 2.5): the camera looks 2.5 tiles up the screen from the character
  (`GameScreen`: focus += (sin a, -cos a) * tiles, the same direction the move-up key walks), so the character sits lower and more is visible ahead.
- Title screen map: `Tools/BookUi/build_title_cave.py` now bakes NO cliff masses (CLIFFS = [], open floor edge to edge) so the cave battle's fighters walk in and out
  at any edge without crossing walls. The pack lives at `Desktop/Extra Assets/RPGW_Caves_v2.1` (moved; the tool path was fixed).
- Book: the Characters tab icon is the user's `Desktop/NewCharacterListIcon.png` (64x64 = 4x pixel art, saved as 16x16 `BookGems/Icon/Characters.png` by hand -
  `build_book_assets.py` would overwrite it with the old one). Hovering any tab shows a parchment tag with the page names (`TabTitles` in CharacterBook: "Profile &
  Last Played", "Characters & Details", "Servers & Spawn Location", ...).
- Sign-in book: the LEFT page of the Register and Sign In forms is `Screens/Components/Containers/BookActor.cs` - the Wizard walks in, faces you and talks
  in the in-game speech-bubble look (`RegisterLines` / `SignInLines` in that file), wanders and flicks the attack frame between lines; restarts each time
  the page is flipped to (Event.AddedToStage). Old Ring is dungeonItems 0x05 (the gold ring). Starter sets apply to NEW characters only (server
  `DbClient` copies Players.xml Equipment at creation); existing characters are not back-filled.
- NOT viewed in a window by me: the book actor, the tab tags, the lowered view direction (sign derived from the movement code), the open title map, the starter set on a NEW character.

## Session end 2026-09-21 (late): delete character, Characters link, title header
- **Delete a character**: the Details page (Characters tab, a character selected) has a small DELETE text link above PLAY. It swaps PLAY for "Delete this level N Class? This cannot be undone." with YES / NO scroll buttons (`CharacterBook.BuildCharacterSummary`, `_confirmDeleteId`); YES calls `AppRequests.DeleteCharacter` -> `/char/delete` (the existing soft delete: `IsDeleted = true`, never listed again), then `ReloadCharactersAsync` fetches `/char/list` again, sorts by fame then id and rebuilds the page on the frame loop; `_deleteNote` shows "Deleted." / the error. Last Played keeps PLAY only.
- **Empty Last Played page**: "No characters yet." + "Visit the [Characters icon] Characters page" + "to create a character." - the icon and the word both call `GoToPage(BookPage.Characters)`, the same flip as clicking the tab (`BuildNoCharactersHint`; pieces laid out from measured text widths around the 32 px icon).
- **Title header size**: `TitleScreen.HeaderFontSize` 24 -> 16. The guest line ("Click PLAY to begin your adventure") is three pieces on ONE line with no wrapping; 24 fit the thin cursive font used before every font went back to MyriadPro, whose wider glyphs pushed it off the scroll's 284 px writable width. 17 is the most that still fits.
- **Deleted characters counted as slots (fixed)**: `DbClient.CreateCharacterAsync` compared `acc.Characters.Count` (every character ever made, soft-deleted ones
  included) with `MaxChars`, so an account that deleted both characters got MaxCharactersReached on every create and was kicked back to the book. Now
  `Common/Database/CharacterSlots.cs` counts only living, not-deleted characters (`CharacterSlotsTests`). The book shows one NEW tile PER free slot (it used to
  show a single tile for all of them, which read as "one slot"). `/char/list` already left deleted / dead characters out, so the client count matches.
- **Power cut mid-session (2026-09-21)**: the laptop switched off while building; two `obj/**/*.cache` files came back zero-filled and `dotnet build` failed with
  "Error loading lock file ... '0x00' is an invalid start of a value". Fix: delete the zero-filled cache files (a Python walk over `obj` folders finds them) and the
  named project's `obj` folder, then build again. Both solutions and all tests (server 447, client 296) were green afterwards.
- **One placeholder picture (2026-09-22)**: every placeholder OBJECT (38: dummies, Pirate, portals, fountain, guild hall furniture, loot bags,
  projectiles Blade / Grey Missile / Fire Bolt, the Old Robe) now draws the Health Potion (dungeonItems 0x13) instead of a random pack picture, the
  user's call so stand-ins are obvious. `build_pack_sheets.py` step 3b `apply_placeholder_picture()` (`PLACEHOLDER_PICTURE`) does it and is re-runnable;
  the 18 placeholder GROUNDS keep their floor / water stand-ins (a floor of potions is no floor). `verify_sheets.py` and `build_palette.py` re-run.
  Give an object real art = point its XML at the art and remove it from `PLACEHOLDERS` in the tool.
- **Default hotkeys (2026-09-22)**: `Settings.cs` Options Escape -> O, Escape-to-Nexus R -> F, HealthPotion F -> C (F was taken), CenterPlayerKey X -> Z,
  ResetCameraAngle Z -> X. The Bug Board / News Board / Jukebox / admin dashboard closed on `Settings.Options.Key` (which WAS Escape); they now close
  on that key OR `Scancode.Escape`. Saved settings keep the player's own keys (only the defaults changed). NOT changed: the options menu itself still
  closes with CONTINUE only (a key close would fight the key-capture boxes, where Escape cancels a capture).
- **Player Position key did nothing (fixed)**: `CenterPlayerKey` had no case in `UserInput.OnKeyDown`; it now flips `Settings.LowerPlayerView` (the real
  camera offset, read live by `GameScreen`). The Graphics tab's "Center On Player" on/off option and `Settings.CenterPlayer` were dead (nothing read them)
  and are gone; "Player Position" (Lowered / Centered, Controls tab) is the one switch. Old settings.xml files with a <CenterPlayer> tag load fine (unknown tags are skipped).
- **Walking jitter in the browser (fixed 2026-09-22)**: `GameScreen.Update` built `_camera` from the player's position BEFORE `Map.Update` moved the player,
  and Draw used that camera: the character sat dt x speed off-centre. Constant (invisible) on the VSynced desktop, but browser frame times vary, so the
  offset changed each frame = a 1-2 px back-and-forth while walking (the user: "skipping / dancing while it slides"). Now `CameraOnPlayer()` runs
  twice: before input (mouse -> world, culling) and again after `Map.Update` for Draw. Same order existed in the Game backup - not caused by the audit.
- **Title screen "tears" (fixed 2026-09-22)**: thin lines the width of each orb / a square round each crystal glow = the EDGES of the glow quads.
  `TitleMap.png` is sampled LINEAR (Main.cs unit 4) and the GL sampler wrap defaulted to REPEAT; the glow strip's cells butt against the map's
  bottom row and the strip's bottom edge wraps to the map's top row, so an edge fragment blended map texels in. Fixes: `Sampler.ClampToEdge()`
  in every constructor (nothing tiles through a sampler), `CaveBackdrop.GlowRegion` reads each cell one texel in (its edge texels are
  transparent), `Region` pulls every region in by half a texel. The baked map itself was checked pixel by pixel round the crystals: clean.
  Same class of bug elsewhere: the game atlas (book backdrop, in-game) is NEAREST + 1 px transparent padding, so it is not affected; the
  TitleGraphic logo sheet is LINEAR - if its pieces ever show edge lines, inset their regions the same way.
- **"Bugs and Todo" batch (2026-09-22)**, all seven lines of `Desktop/Repos/Bugs and Todo.txt`:
  1. Loot bags despawn after 60 s: `LootDrop.HandleLoot` queues `AddTimedAction(ProgressionRules.LootBagLifetimeMs)` per bag (only if still present).
  2. XP / levels / fame were NEVER built. `Systems/Combat/ProgressionRules.cs` (pure: XP = maxHP/10 x XpMult, min 1; `AddXp` walks LevelRules;
     1 fame per 1,000 XP with a carry in `GameInfo.FameXpCarry`; `Grow` per `PlayerDesc.LevelIncreases` capped at the stat max; a level-up heals fully)
     + `Progression.cs` (applies to EntityStats, sends "+N XP" / "Level N!" / "+N Fame" Notifications) called from `EntityCombat.Death` for every
     player in the DamageRecords. `PlayerDesc` now parses `<LevelIncrease>` (`TryStatFromXmlName`: HpRegen = Vitality, MpRegen = Wisdom).
  3. Regeneration: `EntityStats.Regenerate` (players, every tick while HP > 0): HP 1 + 0.12 x VIT per s, MP 0.5 + 0.06 x WIS per s, fractional carries.
  4. Potions: there was NO `UseItem` packet handler on the server. `Systems/Inventory/UseItem.cs` (`Apply` is pure-ish and tested): own inventory
     only, consumables only, `<Activate>` Heal / Magic capped at max, the item is removed. Client keys: C / V = first Health / Magic Potion, 1-8 = that
     slot's consumable (`ItemTile.UseFirst / UseInventorySlot`; the cases in UserInput were empty).
  5. Double-click (`ItemTile.DoubleClick`): consumable = use; gear the class can wear = InvSwap with the gear slot of its SlotType; from a gear slot =
     first free inventory slot. The server validates as always.
  6. Enter submits: `TextInput.OnSubmit` (Key.Return / KeypadEnter), set on both fields of LoginContainer and RegisterContainer.
  7. `NearbyPlayersPanel.PlayerMenu`: DarkAgesParchment plate + ParchmentInk text, the name cut at 14 chars, the close button padded.
  Nexus targets (`Target Strong` 0x1806 / `Dummy Strong` 0x1807, 700 HP, `<Enemy/>`) pay 70 XP and RESPAWN: `World.SpawnFromMap` records every
  map-placed entity's origin (`_mapOrigins`, dropped in RemoveEntity); `EntityCombat.Death` of an `Enemy` with an origin queues
  `SpawnFromMap` after `ProgressionRules.MapEnemyRespawnMs` (5 s). The DPS dummies (10M HP) never die. `<Exp>` tags in NPCs.xml are unused (XP = maxHP/10).
  Tests: `ProgressionRulesTests` (6), `MapEnemyRespawnTests` (1, the real Nexus), `ProgressionWorldTests` (3: a real kill in a Vault world pays XP; a level-up grows within the class rules;
  a potion heals capped and disappears). NOT viewed in a window: the menu look, the floating texts, double-click - the user tests.
- **Game-port connections are counted and capped (2026-09-22)**: a raw-socket stress test left NO trace in the log - `SocketServer` never logged an
  accept or a refusal, its per-IP dictionary deleted the whole count on any disconnect (the cap could never trigger), `MaxClientsPerIP` was 2000,
  and a refused socket was never closed. Now `Network/ConnectionLedger.cs` (pure, thread-safe, `ConnectionLedgerTests` 6) counts open sockets per
  address, refuses over the cap (10, `gameServerConfig.xml`), NEVER caps 127.0.0.1 / ::1 (every browser player arrives from the websocket bridge on
  the same box - nginx limits those), closes refused sockets, logs `[FLOOD] refused a connection from <ip> ...` on the first and every 100th refusal
  per address, and the `[STATS]` line ends with `sockets=<open> accepted=<n> refused=<n>` per 10 s window. `deploy -Logs -Load` includes FLOOD.
- **/spawn Pirate froze the live Nexus (2026-09-22)**: two server bugs, both in `World.cs`. (1) `AddComponents` called `behavior.Load()` BEFORE
  `EntityBehaviors.Add`; Load builds an `EntityView` whose `Behavior` is looked up in that manager, so `State.Enter` got a null component -
  NullReferenceException for every entity with a behaviour (the Pirate is the only one in the data). Now Add first, then `Load()` on the stored ref.
  (2) `HandleTimers` removed a timed action only AFTER a successful run: the /spawn timer threw every tick, the exception aborted the whole Nexus
  tick (`GameLogic` logs "Error ticking world -1" 20x a second) and the world stayed frozen until `deploy -Restart`. A due timer is now taken out
  before it runs and a throwing one is logged (`_timerLog`) and dropped. Tests: `WorldTimerAndSpawnTests` (2). Symptom to recognise: the same stack
  trace repeating 20x a second in `deploy -Logs` = a stuck timer; restart first, then fix.
  Also: `/spawn` name lookup was the exact id only ("/spawn 10 pirates" -> "null object desc"). `Commands/SpawnRules.Resolve` (pure, `SpawnRulesTests`):
  exact id or DisplayId in any case, a typed plural, a unique partial match, else suggestions; players never spawnable. Tiles are TileDescs, not
  ObjectDescs, so they cannot be spawned. A count reuses one Entity value; `EntityManager.Add` assigns a fresh id each time (tested).
- **/give showed nothing in the browser until a world switch (2026-09-22)**: the server side is proven by `GiveItemTests` (TryAdd -> slot 4 -> the ticks
  publish Inventory4 / InventoryData4 under the private mask) and the item WAS there after a relog, so only the live display was lost, browser only. Read
  and cleared: packet pooling (returned after Handle), the tick order, NewTick -> Entity.UpdateStats -> InventoryUpdate -> InventoryGrid, the Signal's weak
  reference (to the grid instance, alive), HUD rebuilt per local player, nothing calls Remove. The exact lost trigger was NOT found. Fix in place: the HUD
  calls `EquippedGrid.Sync()` / `InventoryGrid.Sync()` (+ the backpack popup while open) every frame - a tile whose ItemDesc differs from `Equipment[slot]`
  is redrawn, so the display can never stay out of step for more than a frame. If a cause is ever found, keep the sync anyway (cheap, 12 comparisons).
  NOTE `SignalCallback.Equals` compares only the method, not the target: `Signal.Remove` from one instance would remove another instance's listener.
- **Spawn Location never worked in the BROWSER (fixed 2026-09-22)**: `WebClient/web/shim/WebClient.cs` replaces `Networking/Client.cs` wholesale in the web
  build and its `SendHello` still sent `GameId = -1` (Nexus). It now mirrors the desktop line (`Data.FastTravel.GameIdFor`). RULE: any change to
  `Client.cs` (SendHello, reconnect, packet handling) must be repeated in that shim - see `Docs/EngineeringAudit.md` 1.3 for the list of patched files.
- **Admin dashboard NEW / PLACEHOLDER by the wrong name (fixed)**: the library `Entry.Name` is `DisplayId ?? ObjectId`, and `ArtRules.IsPlaceholder` was given
  that, so objects with a DisplayId (DPS dummies, loot bags, guild hall upgrades) read NEW. `Entry` now carries `Id` (the XML id) and the rules get that.
- **Old Robe is not a placeholder**: it shares the Old Armor's picture (dungeonItems 0x0f) by the user's choice; the placeholder pass had turned it into a
  potion and listed it. Reverted, off the tool's list (55 placeholders now: 37 objects + 18 grounds).
- **Daily Gift note overlapped the timer (fixed)**: `_giftNote` sat at PageBottom - Sz(10) and "Next gift in" at PageTop + Sz(306) - the same 20 px band on a
  518 px page. Now (CharacterBook.Rewards.BuildDailyGiftPage): waiting + note = note at Sz(290), timer at the bottom edge (PageBottom - Sz(4), 20f);
  gift still open + note (an error) = the note replaces the streak line at Sz(80) above the OPEN GIFT button; no note = as before.
- **Daily Gift cycle**: the 7 rewards are `DailyGiftRules.Cycle` in `alloy-server/Common/Database/RewardRules.cs` (code, not config): per-player streak
  (day N = N-th claim in a row, 24-48 h apart), no calendar week. Changing them = edit the array, `deploy -Server`. Not yet an XML config.
- **SimpleText width after a shorter text (fixed)**: `FillData` now clears the vertex slots past the last character. The buffer only grows, so a shorter
  text left the longer text's vertices in place and `SetGraphicsBuffer` (width = largest vertex X) kept the old width: a Middle-anchored label sat left of
  centre until rebuilt - the options' key boxes after picking a key showed it. Every shrinking SimpleText benefits. Not unit-tested (needs the font renderer).
- **Vault chests are real art (2026-09-22)**: the Vault Chest / Closed Vault Chest pictures come from a FOURTH bought pack, `Desktop/Extra Assets/Treasure
  Chests` (style 6; the old `Tools/ForestContent/draw_props.py` composed them, sources in `Tools/ForestContent/source/vault`). The 09-21 sheet tool had
  wrongly pasted the Dark Dungeon chest over largeObjects cells 2 / 3 and listed both as placeholders; now it copies all four cells of
  `source_old/ForestProps.png` unchanged and the chests are off the placeholder list (56 remain). Containers.xml points at largeObjects 0x02 / 0x03 again.
- PatchNotes.txt got its entry for the News Board / slot / delete / book actor / typing batch (top of the file).
- Not viewed in a window by me: the delete flow, the hotlink line's fit, the header at 16 - the user tests. The user then deploys server + client + linux + web + portal and promotes; a Linux tester confirmed the Linux download runs.
- From the next session on, Claude Code is opened in `Desktop\Repos` (see the hub `CLAUDE.md` there); the vault `Desktop\Repos\W&W` was refreshed to this state the same evening.

## Crossed-card props + the honest FPS readout (2026-09-22, later)
- **Crossed cards** (Bugs and Todo line 17, "small props whirl when the camera turns"): `<CrossedCards>4</CrossedCards>` in `Objects.xml` on all four
  trees (Forest Tree / Pine / Oak / Pine Tall - the first pass missed the three whose id has no "tree" in it, the user noticed), Forest Bush,
  Forest Shrub, Forest Bush Wide, Forest Rock Cluster and Forest Small Rock (logs stay `<FlatOnGround />`). The number is how many upright
  copies stand round the vertical axis (2 = a cross, 4 = a star every 45 degrees, the user's choice for a more 3D look; `<CrossedCards />` = 2; max 8). `ObjectProperties.CrossedCards` ->
  `Entity.GetRenderType` -> `Rendering/Types/TypeCrossedCards.cs` (`ModelType.CrossedCards`, no mesh): the sprite is drawn by the MODEL pass as N
  upright pictures spread evenly round the vertical axis, fixed in the world, the same size and foot position the billboard had
  (`RenderBase.SetTexture`'s Scale x Size, bottom = -(0.5 x Scale.Y + Scale.W) x k), both faces of each card because the model pass culls one side.
  `Render.DrawCrossedCards` writes 12 expanded vertices per card straight into the model buffer (`_directVertexCount`; `FlushBufferModel` draws them) with a
  depth PER CORNER = the object's sortId + 0.4 x (corner offset . the camera's depth column, `Render.Depth`), so the two cards interleave per pixel
  where they cross and sort against the billboards around them (a model normally has ONE depth). Shadows: `Map.Draw` walks the CrossedCards
  storage too. The server links the same XML and ignores the tag (it reads with HasElement). Free camera rotation eases in / out over 80 ms
  (`Player.UpdateCameraRotation`, `_spin`). NOT viewed in a window by me: the look at 45 degrees, the tree height, the foot shading (Model.frag
  darkens the lowest 0.6 tiles) - the user judges; a card is one XML line to add or remove per object.
- **FPS readout rework** (line 19): see `Docs/EngineeringAudit.md` section 9 (2026-09-22 later). Facts fixed: FPS was frames / 1.0 s although the window
  ran 1000 ms + the last frame's overshoot (read high at low frame rates); percentiles were over 5 s. Now `Game/FrameStats.cs` (pure, tested),
  `Alloy.Engine/FrameTiming.cs` (work / swap wait / cap sleep / GPU timer query / monitor Hz from `Toolkit.Display.GetRefreshRate`; the web
  GameWindow leaves them 0 / -1), `Alloy.Engine/Graphics/GpuStats.cs` (draw calls, uploaded bytes). Rows: `FPS: 60.0 (VSync 60 Hz)`, frame ms
  avg / P90 / P99 / max over 1 s, CPU work (update / fixed / draw), GPU ms + swap wait + sleep, `Unlimited: ~N FPS` (1000 / max(CPU, GPU) -
  the number to push up on this VSynced PC; friends with VSync off see it as their FPS), draw calls + KB uploaded per frame, memory, world, loop.
  `[FPS] ...` console line every 10 s while the readout is open. NO rendering change was made for speed yet: the user measures with the
  readout first; candidates (entity `InstanceAttributeBuffer` full-capacity orphan every draw = 8.6 MB BufferData per flush, the UI's
  full-array uploads per batch, the second entity pass) all touch the streaming-buffer behaviour that produced black frames / blinking bars
  on this driver before, so each needs the dxcam flicker measurement, not a blind edit.
- Card counts after the user looked: trees 4, bushes / shrub 6, rocks 2 (four cards made the rocks read as grey bushes). `<CrossedCards>N</CrossedCards>` per object.
- **News Board overflow (Bugs and Todo 1, 2026-09-22)**: `NewsBoardView.Layout` placed an entry whole even when it was taller than the 380 px list, so a long
  entry ran off the bottom of the window (the 2026-09-22 entry did). Pages are cut by LINE now: `BuildEntry(entry, width, startLine, maxHeight, firstOnPage,
  out rowHeight, out nextLine)` adds lines while they fit, a cut entry continues on the next page as "<title> (continued)" without its date, the divider only
  follows a finished entry, paging state is `(entry, line)` pairs (`_start/_startLine`, `_next`, `_previousStarts`), the pager reads "3-4 of 12" / "3 of 12".
  The list is also a clip container (MouseEnabled false) so nothing can spill. Not unit-tested (needs the font renderer); not viewed in a window by me.
- **Minimap icon (Bugs and Todo 2)**: the blue arrow (`hudIcons` cell 1, an `ObjectRect` rotated with the camera in `Minimap.cs`, drawn at a size that
  changed while turning) is gone. `MinimapLayer` now draws YOU last, in the middle, from triangles: `Core/MinimapIcon.cs` (enums `MinimapIconShape`
  Square / Circle / Diamond / Triangle and `MinimapIconColor` 9 colours, saved by NAME in settings.xml - append new members, never rename; `MinimapIcon.Get`
  caches each shape's offsets + indices, `HalfSize` 6 px vs 3.25 for the other markers). `Settings.MinimapIconShape / MinimapIconColor` (green square by
  default), two `ChoiceOption`s on the options' **Extra** tab (`OptionTabView.AddExtraOptions`, empty before: "where the experimental options go"). The
  layer's buffers are now vertex / index counted (`AddShape`) instead of quad counted. Emoji icons = a planned third option (a library the client ships).
  Tests: `MinimapIconTests` (3). Client tests 246.
- **Logs with thickness (2026-09-22)**: flat props lost their 3D look next to the card stars. `<Thickness>0.25</Thickness>` on a `<FlatOnGround/>` object
  (both logs) -> `TypeFlatStack` (`ModelType.FlatStack`, model pass): the flat picture drawn as a STACK of layers from the ground to t tiles up
  ("sprite stacking", `Render.DrawFlatStack`, about one layer per 4 screen px, 2-16 layers, both windings, each layer a slightly smaller depth than
  the one below so the depth test lets it draw; flat sort 0.97 like every flat prop, no shadow). The camera's shear lifts each layer up the screen,
  so the lower layers' bottom edges show under the top picture as the object's side; Model.frag's near-ground darkening shades them. World-fixed
  like the flat sprite (the picture's top towards -y; `Entity.Rotation` applied, 0 for the logs). Not viewed in a window by me; the thickness is
  one XML number per object, 0 = the plain flat sprite again. FIRST LOOK (user): the bottom looked "dragged / stretched" - every layer repeated the
  picture's details down the screen. Now only the TOP layer is the picture; the layers under it carry `SideShade` 4 (Model.frag: shade x
  near-ground darkening, so they go nearly black = a dark side wall) and the thickness is 0.15. Still the user's call.
- **"VSync off but still 60 FPS, and no tearing now" (2026-09-22)**: in a WINDOW on Windows 10 the desktop compositor (DWM) paces SwapBuffers at
  the desktop's refresh even with swap interval 0, and composes whole frames (hence no tearing). The readout's limiter now reads "desktop
  compositor: the swap waits" when VSync is off, no cap, and the swap takes more than half the frame (`DebugStats.Describe`, `DebugStatsLimiterTests`).
  So on this PC the FPS row is 60 either way; "Unlimited" (1000 / max(CPU, GPU)) is the real measure. Exclusive fullscreen would bypass DWM (untested).
- Web build: `Sampler.ClampToEdge` uses the literal 0x812F (the WebGL shim's `Enums.g.cs` has no `TextureWrapMode`); the web build is green again.
- Tests: client 240 + protocol 62, server 481 (GameServer 164, Common 255, Protocol 62). verify_sheets OK. Launched servers + client locally for the user's test.

## FPS investigation (2026-09-22, evening) - measured, not guessed
The friend's reports (1200 -> ~1000 FPS walking in an empty Nexus; ~half when a second player joined) were measured on this PC with a
scripted, repeatable rig. Results and what was changed:
- **The rig** (keep it, it is off unless asked for): `Game/DevPerfTest.cs` runs only when the environment variable `ALLOY_PERFTEST`
  names an output file. The title screen presses PLAY, the book plays the last character, then fixed phases of 20 s: alone standing /
  walking back and forth / with 1 and with 10 FAKE remote players (client-only `Player` objects, never on the wire) / alone again.
  Each phase averages `Game/PerfSections.cs` (per-part CPU timers: Net, Hud, Fixed, MapUpdate, StageUpdate, DrawTiles, DrawShadows,
  DrawParticles, DrawModels, DrawEntities, Minimap, Ui), frame / work / swap / GPU time, draw calls, KB uploaded, UI sprites and batches,
  entities drawn and bytes allocated per frame, and writes a table. Launcher used: scratchpad `perfrun.ps1` (start servers if needed,
  set the variable, run, read the file). NOTE it force-closes every AlloyClient first - tell the user before running it.
- **A remote player is cheap**: 1 -> 10 fake players added ~1.3 ms per frame on this laptop (~0.15 ms each; on a fast PC ~0.02 ms).
  Halving 1200 FPS needs +0.8 ms, so the second-player drop is NOT the cost of updating / drawing / listing a player. Not reproduced;
  what the fakes skip is the network side of a real second client (its NewTick / Update traffic). Next step: a real second client
  (needs a test account the user signs in; never type passwords).
- **Walking** adds ~0.3-0.5 ms here: more props come into view (entities drawn 38 -> 60) so the model and sprite passes grow, and
  allocation rises ~490 -> ~800 B per frame. No single culprit.
- **The model pass is the biggest fixed cost** (walls, boards, the card stars; ~0.9-1.2 ms per frame alone, paced). Two alternatives to
  the per-write orphan in `InstanceAttributeBuffer` were MEASURED WORSE on the HD 4400 and reverted: skipping the orphan (model pass
  ~1 -> 6 ms: the driver waits on a buffer the GPU read recently) and a ring of 12 / 4 separate buffers written without the orphan
  (3.6 ms vs ~1.8 ms unpaced). The orphan stays. The sprite-pass buffer is 4000 sprites now (was 10000: 3.4 MB re-allocated per frame
  instead of 8.6 MB; no measurable difference here, kept as the smaller allocation).
- **Measurement caveats**: every section creeps up over a run whatever the phase (this laptop slows as it warms, and the servers share
  it), and the Windows compositor paces a windowed game at 60 until something unpaces it mid-run (frame 16.7 -> ~8-10 ms) - compare
  phases within one run, never small differences across runs.
- **VSync kept coming back on (fixed)**: options were saved only on a clean exit (`Program.OnProcessExit`) or PLAY
  (`CharacterListScreen.OnPlay`), and the rig's forced close of every AlloyClient skipped that save, so the user's VSync-off was lost
  each time. Now `ChoiceBox.SetSelected`, `KeyCodeBox` and the options' CONTINUE / RESET save immediately (`Settings.SaveSettings`).
  Several game windows share one settings.xml and each still writes its whole copy on exit (last one wins) - known, not changed.

## Static props baked into per-area meshes (2026-09-22, night) - the big frame-rate win
- **Measured cause**: the model pass (walls, wall tops, Bug / News Board, Jukebox, card stars, stacked logs) expanded and uploaded every
  prop EVERY frame: 2.5-3.9 ms of a ~9 ms frame on this laptop (unpaced), growing while walking. Players and enemies are NOT the problem:
  10 fake players +0.9 ms, 10 real pirates (server traffic, shooting) +0.2-0.8 ms, network apply < 0.1 ms flat (`DevPerfTest` enemy mode).
- **Fix**: `Game/StaticProps.cs` groups every STATIC entity whose render type implements `IStaticProp` (TypeWall + its TypeWallTop,
  TypeModel3D, TypeCrossedCards, TypeFlatStack) into 16x16-tile areas; each area is ONE `Rendering/StaticPropMesh.cs` (same pattern as
  TileChunkMesh: orphan + upload only when a prop in the area arrives / leaves) drawn with one call in the model pass (`Map.Draw`, after the
  per-type loop) + their shadows in the shadow pass. `Map.AddEntity/RemoveEntity` route qualifying entities there instead of
  `EntityStorage`; `ClearWorldObjects` clears it (GPU objects deleted on the next Draw, Clear may run on the network thread; a lock guards it).
  Areas drawn: centre within `CullRules.Radius + 12` tiles.
- **Depth**: the model pass writes depth = sort value (0.5 + 0.4 x screen y of the ground point + `Entity.Jitter`), which depends on the
  camera. Baked vertices (`Rendering/Render.Baked.cs`) carry a CODE in iExtra.y instead: -10 + jitter x 1000 = ground point is iPosition.xy
  (props; card corners have their ground point written into iPosition), -20 + ... = iPosition.xy + 0.5 (walls / tops stored at the tile
  corner). `Model.vert` decodes it with the new uniform `DepthColumn` = the camera's (M12, M22, M32, M42) (`Render.SetShaderParams`), the
  SAME numbers the CPU / Projectile use. GOTCHA that cost a run: FullMatrix is uploaded TRANSPOSED (`Shader.SetValue` matrix, transpose
  true), so `(pos * FullMatrix).y` in GLSL is NOT the CPU's screen y - never re-derive CPU sort values from FullMatrix in a shader. Depth >= 0
  still means "use as-is" (flat stacks, all per-frame draws).
- **Verified**: A/B screenshots at the same moment (`ALLOY_NO_STATIC_BAKE=1` = old per-frame path) are identical round the character
  behind the Vault tree line. Model pass 2.56 -> 0.09 ms standing, 3.67 -> 0.12 ms walking; CPU work 5.75 -> 3.24 ms; uploads ~1010 -> ~464
  KB/frame. On this laptop the GPU (5.5-6.8 ms) is now the limit. Tests `StaticPropsTests` (5). NOT checked in a window by me: the Realm /
  Nexus walls and boards from every camera angle (the Vault shot covered trees, bushes, rocks, chests are not static-baked), the browser.
- **/god** (owner): `GodCommand` in `CreativeCommands.cs`, `GameInfo.God`, `EntityCombat.Tick` drops the damage. Made for measuring among
  enemies (`ALLOY_PERFTEST_MODE=enemies` sends /god on + /spawn 10 Pirate; restart the local game server afterwards to clear them).
- **Real second player, measured (2026-09-22, night)**: `ALLOY_PERFTEST_MODE=timeline` (enter the Nexus, stand, one line per 5 s with the
  players in the world) while the user walked a second game window ("Riigged") round the measured one ("Claude"). With the second window
  open EVERY section of the measured window slowed ~2x at once (stage update, UI, network apply, sprites - including parts that have
  nothing to do with players) and the GPU time tripled; it recovered the instant that window closed (unpaced alone after: ~150-165 FPS,
  6.1 ms frames, vs ~110-120 before the static-prop bake). That is two game windows sharing this laptop's 2 cores and HD 4400, not a cost
  of the player. Per-player cost measured without sharing (fakes, 10 real pirates) stays small. The friend's halving (two separate PCs)
  is therefore still unexplained - it needs his own readout / `[FPS]` lines. Also fixed on the way: the account server capped
  registrations at 10 per address INCLUDING this PC (`DbClient.RegisterAsync`); loopback is exempt now (same rule as ConnectionLedger).
  Biggest CPU section now: the UI stage update (~1.1 ms, EnterFrame listeners) - the next candidate.

## v0.3.7 release (2026-09-22, late night) - written up on 2026-09-23 (the PC shut off before the session log)
- Bugs and Todo items 1-7: floating XP / level / fame / heal texts (`Notification.Handle` -> `NotificationLayer.AddStatusText`, stacked + capped);
  Portal Last Seen (`logins.last_login_at` on every VerifyAccount) and Created (`Account.CreatedAt`); Portal search (1-2 letters = prefix,
  3+ = contains, exact first); the in-game Portal's X button; "Update required" as soon as a server update kicks you (the Failure is reported
  in `Read`, Disconnect re-fetches `/app/version`).
- Launcher 2.0.0 (`WaWLauncher`, Avalonia, UI in code): first-run folder + shortcuts, progress / speed, PLAY, Open game folder, Release notes,
  Repair; the published game became one `WarriorsAndWizards.exe` next to it (`Content` + `runtimes`); the self-update archives keep the old
  file name for launcher 1.0.0. Website: download-first home page (`js/os.js` highlights the visitor's system), short Download page, Cinzel +
  Inter fonts. Item 7 (a third key not registering) is keyboard ghosting, not the game.
- Deploy order used: `-Launcher` first, then `-Server`, `-Web -SkipWebBuild`, `-Client -Linux`, website push. MISTAKE: `-SkipWebBuild` re-uploaded
  the 21:14 AOT build (`dist/site-aot`, still 0.3.6) because the newer 22:10 build was the interpreted one (`dist/site`); browser players
  got "Update required" until a full `deploy -Web` on 2026-09-23. Before `-SkipWebBuild`, check `site-aot/index.html`'s date against the bump.

## 2026-09-23: Alloy -> WaW rename, website FAQ, Portal tabs, Skill slot, options, camera, HP bars, dialogs, launcher 2.1.0 (v0.3.8)
- **Website** (Website repo): Home tab first, "Web version" -> Web Client, removed the lines the user listed, "Having trouble with the launcher?"
  jumps to a new 8-question FAQ on the Download page (folded `.reqs` boxes, `.faq` styles); the Mac/phone hint line only shows for those visitors.
- **Portal** (`Portal/site/js/portal.js` + css): a boxed "<- Home" button by the logo, tabs Rankings / Guilds / Wiki / Graveyard / Releases /
  Forums (Forums in the orange Play colour, no Play tab); Wiki lights up on Items + Classes, which get an Items | Classes switch; the small nav
  search is left out on the Portal home page (big search there). Careful: `search()` with a null form would grab the big box - guarded.
- **Rename** (`rename_waw.py` in the session scratchpad, dry-run first, run by the user with `!` because the auto-mode classifier blocks big
  move/delete scripts): `AlloyClient` -> `WaW-Client` (project `WaWClient`), `Alloy.*` -> `WaW.*`, `alloy-server` -> `WaW-Server`, 481 files,
  obj folders + generated web files cleared. Kept on purpose: VPS names, the launcher's `OldGameName`, upstream credits, this history.
  Fixed afterwards: `AlloyClient\AlloyClient.csproj` (folder + FILE) had been read as two folders in `deploy.ps1`, the test csproj and the .sln;
  the upstream GitHub links / credits had been renamed too. Settings folder: `Settings.MoveOldLocalFolder` moves `%LocalAppData%\AlloyClient`
  to `WaWClient` once (it already ran on this PC during the client tests). Checked: both solutions, all tests, the web compile,
  `deploy -Client/-Server -NoUpload`; launched locally for the user to try.
- **Todo 1 (dialogs)**: `Dialog` rebuilt in the Options look (WaW panel, gold title, cream wrapped text, ParchmentButtons), fixed 500 x 270
  (it used to size itself from its text).
- **Todo 2 (launcher 2.1.0)**: all-black window + `EmberField` (46 drifting glow dots, 30 fps, not hit-testable), title in Not Jam Signature 21
  (`Assets/Fonts`, `AvaloniaResource`, `avares://WaWLauncher/Assets/Fonts#Not Jam Signature 21`), no "Game launcher", "Size: 150 MB".
  Version bumped in BOTH `Launcher.csproj` and `SelfUpdate.Version` (new test guards it: a mismatch = endless self-update).
  The first-run text "placed right next to this launcher" was misleading: from Downloads/Desktop the launcher proposes
  `%LOCALAPPDATA%\Programs\Warriors & Wizards` and copies itself there on INSTALL, so the game IS next to the installed copy.
- **Todo 3 (options)**: Controls -> General (Movement / Camera / Actions / Interface sections; potions, Escape To Nexus, Show Options, Switch
  Tabs moved in), Hot Keys -> Inventory (slot keys only), Show HP/MP Bars + Allow Camera Rotation -> Extra.
- **Todo 4 (camera)**: `Settings.CameraAngle` was both the option and the live angle, so the last played angle became the next start; new
  `DefaultCameraAngle` (the option; "45" now 7pi/4, it was 7pi = 180 degrees), applied in `Map` when the local player spawns and by the reset key.
- **Todo 5 (HP bars)**: `SetFill` moved the centre by the whole lost width; the quad is centred (corners +-0.5), so it is half now
  (`TypeHpBar` + `TypeBar`, the MP bar had it too).
- **Skill slot (old todo 9)**: `Shared/Common.Protocol/InventoryLayout.cs` (slot 20, SlotType 30); server players have 21 slots, slot 20
  typed Skill; stats `Skill0 = 96` / `SkillData0 = 115` on both sides (the enums differ after 82 - these were free on both);
  `EntityInventory.Save` grows 20-slot arrays; new characters get 21. Client: `Equipment` 21, `SkillBar` above the gear (popups stop above it),
  `ItemTile` greyed "S", skills usable by every class, double-click in / out. Tests: `SkillSlotTests` (4). No Skill items exist yet.
- Version 0.3.8 (`deploy -SetVersion`, local): the Skill stats would desync a 0.3.7 client. Tests: client 254, server 62 + 262 + 168,
  launcher 24. Compile-checked while the user's game ran by building into a scratch `-p:OutDir`.
- Not seen in a window: the new dialogs, the Skill row, the options tabs, the launcher's look (embers / font), the camera start, the HP bars.
