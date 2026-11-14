# E-ink display: build plan

Status: planned, nothing built. The design is settled in the mockup (private artifact, owner's link: https://claude.ai/artifact/Y4ErorCYowrV74UHvNkNXF). This plan turns it into stages. It assumes `feature/vfd-polish` (the polished reschedule drag, the sidebar display rows, the brighter light display well, the sidebar scroll cells and the wider clock display) has gone to `experimental` first, because this work builds on it.

## What it is

A second look for every display in the app, chosen in Settings, called **E-ink (accessible)**. The existing look stays and is called **Phosphor**.

- Normal type, not segment letters: Inter, sentence case, never under 12 pixels (11 only in the tall pill's dense rows), tabular figures, smooth edges, with the e-ink character carried by speckle inside the strokes. Nothing is posterised, pixelated or dithered (all tried in the mockup and rejected).
- Grain on the paper and on filled cells and blocks, none on empty cells. Light is dark ink on near-white paper (`#F3F3EF`); Dark is the same screen inverted.
- A strength slider, 0 to 100 per cent, default 60. It scales the paper grain, the ink grain and the letter speckle together. 0 is clean.
- Calm motion, always: no glow, flicker, breathing ticks, blinking colon or sweeps. Changes refresh in three coarse steps (about 7 a second). Reduced motion stops everything that moves. The account name scroller and the ticker keep their stepped scrolling.
- Colour is never the only cue: each account has a texture (plain, diagonal, dots, lines, other diagonal, cross-hatch), drawn on blocks and swatches. Status is always a word in a box.
- Contrast measured in the mockup: ink 13.9 to 1, quiet text 8 to 1, status colours 5.6 or better on Light paper; 8.2 or better on Dark paper.

## Decisions already made

| Area | Decision |
| --- | --- |
| Setting | Display style (Phosphor or E-ink) and Effect strength. Two appearance lines, for example `displaystyle=ink` and `inkstrength=60`. E-ink implies calm motion. |
| Name scroller | E-ink draws it as a dot matrix, 16 dots tall, from GNU Unifont (8 by 16), in bold (each stroke doubled by a dot). Pitch 1.7 pixels, so the name is about as tall as the clock. Phosphor keeps its segment letters. |
| Pill, full | Clock bigger (30 pixel type), alert column and name window moved left so there is no dead space, name window 106 wide, keys 44 by 20 with 4 between (Break is a word with room round it), state ring round the card as the Phosphor pill has it. |
| Alert count | Always two fixed cells, each digit centred in its cell, zero-padded (05, and 00 quiet with nothing waiting), no ghost digits in e-ink. |
| Pill, compact | Gets Play/Pause and Break in both looks, stacked at the right, centred on the clock's line. Wider than today's 230: about 288 in Phosphor and 284 in E-ink. Play, Break and the account choice also stay in the right-click menu. |
| Tall pill | Budgets display is removed in both looks. Bat is Phosphor only. E-ink cycles Strip, Next, Week, Strip and next. Changing display steps: old fades in two steps, a ghost under the new for one step, then settles (about 0.6 seconds, no flash). |
| Sidebar | Display rows and scroll cells already exist; E-ink draws the same rows with textured swatches. No coloured edge bar on the recording row. |
| Timeline | Same structure; blocks carry textures; words sit on a small paper plate and only where they fit whole; reminder tags, meeting outlines, ring on the selection, green outline on the block being recorded. |
| Alerts card | Same structure and behaviour, drawn in the ink style. |
| Fonts | Inter for text. Unifont (OFL, needs a licence check before shipping) for the dot matrix. Chakra Petch stays for plain clock digits elsewhere. |

## Decisions still open (please answer before the stage that needs them)

1. **Week page.** The mockup's "one instrument" layout (schedule row, day strips, account rows with pace badges, Week/Timesheet switch) replaces the current Week overview in both looks? It moves Chart/Table and the table's Copy and Export keys. (Needed for stage 1.)
2. **Real recording row.** Change the sidebar's flat full-width wash to the mockup's inset rounded highlight, in both looks? (Stage 2.)
3. **Phosphor alert count.** Zero-pad it too (05) and drop the leading blank, or leave Phosphor as is? (Stage 3.)
4. **Non-Latin account names** in the dot matrix: bundle more of Unifont, or fall back to a plain text scroller for those names? (Stage 2.)
5. **Account swatches elsewhere** (Notes chips, Details, dialogs): add textures there in E-ink, or only on the displays? (Stage 5.)
6. **Seasonal decorations** in E-ink: switch off, or draw static? Proposed: off.
7. **Display timeline setting** (today an opt-in for the Phosphor timeline and week): proposed to stay Phosphor-only, with E-ink always using the display form.

## Architecture: one seam, shared by every display

Every display already draws through a few shared pieces, which is what makes this tractable: `Honeycomb` (about 9 call sites), `PhosphorBloom` (about 9), `SegmentText` / `SegmentRun` / `SegmentShapes` (about 60), `VfdGlyph` and its subclasses, `SteppedControl`, plus `VfdLook`, `VfdWeek`, `LowerDisplay`, `AccountDisplayRow` and `ScrollCells`.

Add a small `DisplayInk` layer beside them and make those pieces ask the display style what to draw:

- `Honeycomb.Meshed*` becomes a **grain mask** in E-ink (a tiled noise brush, scaled by the strength), applied to filled cells and blocks only.
- `PhosphorBloom` draws nothing in E-ink.
- Segment digits and words become an **InkType** run: Inter at a role size (the roles table from the mockup: caption 12, name 15, figure 16, week hours 30, clock 30), tabular figures drawn digit by digit in fixed cells so nothing shifts, with the speckle mask over the glyphs.
- `SegmentName` becomes a **DotName** (Unifont grid, stepped three or four columns per tick, three blank cells between repeats).
- `SteppedControl` keeps its timer but E-ink caps it at the e-ink rate and honours reduced motion.
- Textures are a small `AccountTexture` helper (`Plain`, `Diagonal`, `Dots`, `Lines`, `Backslash`, `Cross`) drawn over a block or swatch clip.
- `ThemeEngine.Write` writes the ink resources per theme (paper, ink, quiet, ghost, accent tones, status colours) so the controls follow theme changes as the rest do.

The one technical risk is the **letter speckle**. The mockup draws text into a cached bitmap and speckles it. In Avalonia the cleaner route is to draw the text normally and push an opacity mask (a noise tile) over it, as Honeycomb already does. That gives the speckle inside the strokes but not the slightly eroded edges. Stage 0 starts with that spike on real Windows at 100, 125, 150 and 200 per cent; if it does not look like the mockup, fall back to cached per-string bitmaps.

## Stages

Each stage is a set of commits on one branch, `feature/eink-display`, cut from `experimental` once `feature/vfd-polish` has merged. Open the first pull request after stage 2 (the dashboard is usable in E-ink), the second after stage 4.

**Stage 0: setting and foundations (medium).**
Files: `TimerModel.cs`, `ThemeEngine.cs`, `SettingsView.cs`, `SettingsPictures.cs`, a new `DisplayInk.cs`, `Motion` and `SteppedControl` in `DisplayDots.cs`.
- `TimerModel.DisplayStyle`, `InkStrength` with the appearance lines, notifications that repaint every display.
- Settings, Look: two tiles (thumbnails drawn by `SettingsPictures`) and the Effect slider, disabled under Phosphor.
- Ink theme resources; the `DisplayInk` facade; the grain mask, InkType, DotName, AccountTexture; the speckle spike.
- Account texture field: assigned at creation, saved with the account, existing accounts numbered by their current order (stable afterwards, so reordering never changes it).
- Tests: setting round trip, ink contrast (ink on paper 7 to 1, quiet 4.5 to 1, statuses 4.5 to 1, both themes), strength 0 draws no grain, tabular digits stay put, texture numbers are stable.

**Stage 1: Week page and timeline (large).**
Files: `VfdWeek.cs`, `WeekOverview.cs`, `VfdLook.cs`, `ReviewViews.cs`, `ReviewViews.Stacks.cs`, `TimelineGuide.cs`.
- Week: if open decision 1 is yes, the new layout in both looks (the figures stay plain data in `WeekOverview.Figures`, so tests keep reading the same numbers); otherwise the current page drawn in ink.
- Timeline: ink blocks with textures, paper plates for words, tags, meetings, rings, now line and chip; stacks and the recording lane keep their rules.
- Tests: the existing `VfdTimelineTests` run for both styles; pictures into `TIMER_PREVIEW_DIR`; the Round25 no-overlap test in E-ink.

**Stage 2: sidebar (medium).**
Files: `Sidebar.cs`, `StatusFace.cs` (sidebar mode), `Controls.cs` (`AccountDisplayRow`), `VfdLook.cs` (`ScrollCells`), `SegmentClock.cs`.
- Display face: LED, 30 pixel clock with full-size quiet seconds, alert column, dot-matrix name, keys 44 by 20 stacked on the second row.
- Account rows: textured swatch, ink figures and cells; inset highlight if decision 2 is yes.
- Tests: `SidebarTests` for both styles, the display and the accounts display stay the same width (existing test), name window stays inside its cells.

**Stage 3: pill, full and compact (large).**
Files: `PillWindow.cs`, `StatusFace.cs`, `DisplayDots.cs` (`TransportGlyph`, `BreakBadgeGlyph`, `AlertBellGlyph`), `Ui.cs` shadow and ring helpers.
- Full pill in E-ink: layout as the mockup, ring, keys, dot-matrix name, alert column.
- Compact pill in both looks gets the keys; `PillWindow.CompactWidth`, `CardWidth`, `ApplyShape`, `WindowWidthNow` and the morph's starting card follow the new width. The right-click menu keeps Play, Break and Track.
- Tests: `HorizontalPillTests` and `Round14Tests` for both styles, compact width, the screen-change rescue still sizes the pill correctly at the new compact width, every part stays inside the card at 80 and 120 per cent pill size.

**Stage 4: tall pill (medium).**
Files: `LowerDisplay.cs`, `StatusFace.cs`, `TimerModel.cs` (`PillLower`).
- Remove Budgets in both looks; saved `pilllower=budgets` falls back to Strip.
- Bat stays Phosphor only; in E-ink the cycle skips it, and a saved `pilllower=bat` falls back to Strip.
- E-ink strip, ticker (stepped), week and the stepped change between displays.
- Tests: `TallPillTests` for both styles, the cycle skips what the style lacks, the card grows for Strip and next.

**Stage 5: the rest and the checks (small to medium).**
- Alerts card colours and the same-width digit cells; textures on account swatches elsewhere if decision 5 is yes; Seasonal off in E-ink.
- `CLAUDE.md` section, `CHANGELOG.md`, `docs/testing/performance-test-campaign.md` additions.
- Windows checks, by hand: speckle and dot matrix at 100, 125, 150 and 200 per cent, Windows high contrast, the frame monitor on the Week and Timeline pages in E-ink against Phosphor, reduced motion, the compact pill on a screen change.

## Testing and tooling

- Every display gets a picture test in both styles and both themes, written to `TIMER_PREVIEW_DIR`, so the owner can compare against the mockup.
- Contrast and layout audits (`Stage8Tests`, `LayoutAudit`, `ShadowClipTests`, `AccentTextTests`) run for both styles.
- The dot-matrix glyph table gets a unit test (every Basic Latin character has 16 rows, widths are 8 columns).
- A golden check that strength 0 equals the clean draw for a few strings.

## Risks

- **Letter speckle in Avalonia** (above). Mitigation: the stage 0 spike, with cached bitmaps as the fallback.
- **Cost of grain and text masks** on the Week and Timeline pages. Mitigation: cache the noise brush per strength step (11 steps), draw grain only on filled shapes, check the frame monitor in stage 5.
- **Pill width changes** (compact pill) touch the morph, the screen rescue and the pill's placement maths. Mitigation: stage 3 has its own tests for each.
- **Unifont licence.** Offered under the SIL Open Font License as well as the GPL with a font exception; confirm before shipping, and ship the glyph table with its notice.
- **Real Windows look.** None of this has been seen on Windows. Every stage ends with a build the owner can try.

## Out of scope

The CRT account colours for Phosphor Dark (shown in an earlier mockup) are a separate decision and are not part of this plan. Pop-ups and cards that are ordinary controls (Details, Notes, dialogs) change only through the theme resources, apart from decision 5.
