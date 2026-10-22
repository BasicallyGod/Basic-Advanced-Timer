# Integrated dashboard rail prototype

Enable **Settings → Appearance and behaviour → Integrated dashboard rail (experimental)**. Switch it off to return to the original dashboard and compact Notes layouts, including the saved compact-account choice. The alternative is off by default and does not affect the timer pill.

The same 88px rail stays visible across Notes, Timeline, Week overview, Hours table, Entries and Settings. Compared with the existing 60px navigation plus 176px Notes column, it frees 148px of horizontal space. Compared with the 112px compact-account experiments plus navigation, it frees 84px.

- The clock has fixed-width, centred, tabular hours and minutes at 30px, followed by smaller muted seconds. Units are omitted. Existing tracking, idle, break and overtime colours remain; a status dot and accessible description identify the state.
- A primary 42px play/pause button and secondary break button sit vertically below the clock. Both are one-click actions with state-dependent tooltips and accessible names.
- Accounts use 32px rows. The existing collision-safe abbreviation appears inside an account-coloured capsule that fills upwards with budget usage; today's recorded time is always visible beside it. A separate live dot/outline highlights the recording account without replacing its chip colour. A separate warning cap and `!` identify budget overruns; a dash identifies no budget.
- The account area alone scrolls. Navigation sits below it in a two-column grid, with the existing icons, destinations, note badge and active-page indicators. Settings, Start/Finish day and Close timer remain fixed below navigation. Switch to pill remains in the navigation grid.
- Hover shows the full account name, today's recorded time, configured-period budget usage/limit and percentage. Right-click or focus an account and press **Shift+F10** (or the Menu key) to read the same details without hover. Tab reaches tracking controls, every account, navigation and footer; Enter activates focused buttons.

This is one `IntegratedSidebar` attached to `DashboardWindow`, not a set of page-specific copies. The window reparents its existing navigation buttons and restores their original parents when the option is disabled. Tracking and budget calculations remain in `TimerModel`; storage precision, recording behavior, exports and the pill are unchanged. The earlier compact layouts remain available.

Validation renders Light/Dark panes and 100%, 125%, 150% and 200% scaling examples, with many-account scrolling and fixed navigation/footer bounds. These are rendered scale checks; per-monitor Windows DPI transitions still warrant hands-on review on the target displays.
