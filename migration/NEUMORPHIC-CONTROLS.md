# Neumorphic controls: comparison gallery

Alpha 647 remains the visual baseline; Alpha 653 introduced the first pilot. The expanded gallery includes existing control families and contextual comparisons. Working-page changes are the outlined gold importance star, neutral disabled primary buttons and circular native checkboxes. Palette experiments and other shared button families stay in the gallery pending feedback. Navigation and text fields keep their accepted appearance.

Open **Settings → About → Development → Open controls gallery**. The sample-data demo already has Development enabled. The gallery compares Light and Dark locally without changing the application's selected theme. Fixed reference states are labelled; the live samples below them can be clicked or operated with the keyboard. Samples do not receive the timer model, write preferences or change tracking. Closing the owner closes the gallery, and reopening from the same owner reuses it. Smaller screens can scroll the comparison.

The section buttons change both theme panes together:

- **States:** shared button/toggle reference states and live samples.
- **Existing buttons:** production toolbar/dialog actions, disabled Save, icon buttons, chips, quick choices, joined Undo/Redo and zoom keys, and the exit action.
- **On surfaces:** shared buttons and ticks on flat, raised, inset and striped material, with room for their shadows.
- **Menus & navigation:** dashboard three-way selector, all three two-way selector families, Settings and Notes selection rows, real dropdown and context menus with checked/disabled entries. These change only local sample selection.
- **Ticks & inputs:** circular checkboxes including mixed/disabled states, working-day and long-label examples, note/budget/time fields, dropdown, the application's own calendar and scale slider.
- **Colour experiments:** the supplied Sage, Lavender, Butter, Clay, Coral, Ink, Mint, Blush and Slate palette, each on a solid and soft sample button. These colours are not assigned to production actions yet.

## Material rules

- Lighting remains from the upper left. Raised controls get a highlight above/left and a shadow below/right; pressed controls use an inset.
- Compact and regular controls have separate depth recipes. A 28 px star does not inherit a large text field's six-pixel inset.
- The enabled state and the held-down state are distinct. Importance keeps a stroke star and neutral button; only the icon turns gold. A shallow inset indicates its persistent state, and pressing adds a small depth change.
- Hover brightens the existing material. Keyboard focus is a separate overlay, so showing it does not change layout or move the icon.
- Disabled primary actions use the neutral grey surface and muted words, including the existing toolbar style. They lose their shadow and cannot look like enabled blue actions.
- Native checkboxes use the notes tick's 20 px circle, raised when empty and inset when checked or mixed. Keyboard focus is an overlay; checking, three-state values, disabled behaviour and access keys remain native CheckBox behaviour.
- Text fields retain Alpha 647's neutral focus edges and bounded inset shadows. The gallery's field and segmented choices provide existing-control context.

## Colour roles

| Role | Treatment |
|---|---|
| Ordinary action | Neutral material and readable ink |
| Primary action | Existing accent and its contrasting text colour |
| Important | Gold stroke icon, neutral surface; independent of blue selection |
| Selected filter | Existing accent selection treatment |
| Break | Existing amber family |
| Tracking / overdue | Existing green / red roles, shown as context swatches |

The gold icon is derived to maintain at least 3:1 contrast against its neutral resting, hovered and pressed fills. Experiment buttons use contrasting black/white words on solid palette fills, and dark words on softened fills. The palette remains exploratory; semantic recording, break and warning colours keep their jobs.

## Implementation

`Styles/NeumorphicControls.axaml` owns the isolated Button/ToggleButton themes and shared circular CheckBox theme. `NeumorphicControls` creates standard Avalonia controls using the button themes; native keyboard and toggle semantics remain available. Compact and role classes select appearance. The old generic Button styles exclude these controls so their hover/focus treatment does not accumulate on top of the new template. `NeumorphicGallerySamples` uses existing application styles for the inventory and keeps colour overrides local to experiment buttons.

`ThemeEngine` derives the local `Neo*` resources from each theme's existing colours. They remain outside the shared Core package and follow live theme updates and the diagnostic flat-shadow switch. The existing `ImportantStar` role stays the icon's colour key. The gallery's `previewHover`, `previewPressed` and `previewFocus` classes expose the same state recipes as real interaction; working controls do not use preview classes.

The star covers both note lists and both note composers. Existing model actions, important-note ordering, names used by automation and icon dimensions are preserved. The timeline's pointer guide now reaches the full display's bottom, including when snapped to a reminder pin. No transition scheduling or animation timing is changed.

## Review checks

Compare the neutral star in all states, especially Light's enabled state. Toggle with Space and by clicking, in a row and while composing. Compare grey disabled Save in both button families. Try native checkboxes in Settings/onboarding and check their focus and labels. Switch gallery sections, try the three-way and two-way choices, open both menus, and compare the same controls on all four surfaces. Judge the palette before assigning colours to further working controls. Hover freely over the timeline and near a reminder pin: the dashed line should reach the display's bottom in both cases.
