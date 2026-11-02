# Neumorphic controls: first pilot

Alpha 647 is the visual baseline. This pilot adds a Development controls gallery and revises the importance star. Other new button families are gallery samples pending visual feedback; existing working-page buttons, navigation and text fields keep their accepted appearance.

Open **Settings → About → Development → Open controls gallery**. The sample-data demo already has Development enabled. The gallery compares Light and Dark locally without changing the application's selected theme. Fixed reference states are labelled; the live samples below them can be clicked or operated with the keyboard. Samples do not receive the timer model, write preferences or change tracking. Closing the owner closes the gallery, and reopening from the same owner reuses it. Smaller screens can scroll the comparison.

## Material rules

- Lighting remains from the upper left. Raised controls get a highlight above/left and a shadow below/right; pressed controls use an inset.
- Compact and regular controls have separate depth recipes. A 28 px star does not inherit a large text field's six-pixel inset.
- The enabled state and the held-down state are distinct. Importance uses a filled gold star and a warm surface with a shallow inset; holding it down adds a small depth/fill change.
- Hover brightens the existing material. Keyboard focus is a separate overlay, so showing it does not change layout or move the icon.
- Disabled samples lose their shadow and become quieter while preserving the role's recognisable colour treatment.
- Text fields retain Alpha 647's neutral focus edges and bounded inset shadows. The gallery's field and segmented choices provide existing-control context.

## Colour roles

| Role | Treatment |
|---|---|
| Ordinary action | Neutral material and readable ink |
| Primary action | Existing accent and its contrasting text colour |
| Important | Gold icon, warm enabled fill; independent of blue selection |
| Selected filter | Existing accent selection treatment |
| Break | Existing amber family |
| Tracking / overdue | Existing green / red roles, shown as context swatches |

The first brighter-colour change is the enabled star's warm fill. The icon colour is derived to maintain at least 3:1 contrast against its resting and active fills. This is an icon contrast check, not an application-wide accessibility certification. Broader palette changes should follow feedback on this gallery.

## Implementation

`Styles/NeumorphicControls.axaml` owns the isolated Button/ToggleButton themes. `NeumorphicControls` creates standard Avalonia controls using those themes; native keyboard and toggle semantics remain available. Compact and role classes select appearance. The old generic Button styles exclude these controls so their hover/focus treatment does not accumulate on top of the new template.

`ThemeEngine` derives the local `Neo*` resources from each theme's existing colours. They remain outside the shared Core package and follow live theme updates and the diagnostic flat-shadow switch. The existing `ImportantStar` role stays the icon's colour key. The gallery's `previewHover`, `previewPressed` and `previewFocus` classes expose the same state recipes as real interaction; working controls do not use preview classes.

The pilot covers both note lists and both note composers. Existing model actions, important-note ordering, names used by automation and icon dimensions are preserved. No transition scheduling or animation timing is changed.

## Review checks

Compare the star in Ready, Hover, Pressed, On, Disabled and Focus, especially Light's enabled state. Try toggling it with Space and by clicking, in a note row and while composing. Compare the saved note after returning to the page. In the gallery, try the live controls and confirm that the timer's theme, tracking and notes stay unchanged. Judge whether the warm fill, gold, shallow inset, icon size and edge strength feel consistent before migrating another family.
