# Neumorphic material study — gallery only

Alpha 662 is the working-app baseline. This study changes the controls gallery only. Production styles, theme resources, navigation, fields, notes and animation code are unchanged.

Open **Settings → About → Development → Open controls gallery**. **Proposed** is the initial view. Switch between **Current** and **Proposed** while staying on the same section and keeping the same sample values. Light and Dark remain side by side. The gallery has no timer model and cannot save preferences or modify tracking.

## One material and one light

Light comes from above and to the left. Raised pieces use an upper-left highlight and lower-right shadow; recesses reverse those directions. Shadows stay neutral and are derived separately for Light and Dark. Dark gets a restrained highlight and a shallower well, rather than a black hole with a bright rim.

Depth follows size. Compact keys, ticks, radio wells and switch knobs share a tight recipe. Regular keys and fields share the next depth. Wider shadows are reserved for cards and menus. A small control never borrows a text field's large inset shadow.

The surface colour carries through to the controls sitting on it. Thin edges only finish the shape; light and shadow do most of the work. No texture or artwork is copied from the references.

## States and families

| Family | Proposed treatment |
|---|---|
| Action key | Raised at rest, a small lift on hover, inset while pressed |
| Icon-only key | Circular, with compact depth; same press behaviour as a word button |
| Persistent toggle | Inset when selected, plus a readable label or icon colour cue |
| Importance | Neutral material with an outlined gold star; no gold button fill |
| Field / dropdown | Recessed well; focus does not change its bounds |
| Checkbox | Circular, raised when empty and inset with a tick or mixed mark |
| Radio | Circular, raised when empty and inset with an accent dot when selected |
| On/off switch | Inset track with a raised knob; knob position and On/Off words identify state |
| Two-/three-way selector | One inset rail containing a raised selected thumb |
| Slider | Inset track and a raised thumb; the value colour keeps its meaning |
| Settings / Notes selection | Quiet rows, with a compact recess for the selected row |
| Context menu | Card depth on the container, a compact recess for the pointed/pressed row |
| Disabled control | Grey neutral material, reduced emphasis, no raised action shadow |
| Keyboard focus | An additional stable outline, without changing the selected value or geometry |

Joined toolbar keys retain one outer group shadow. Their individual halves press inward rather than each adding another resting shadow.

The nine supplied colours remain experiments on action buttons. Recording, break, warning and importance roles are retained. Solid and soft fills use the same depth rules as neutral controls.

## Comparison sections

- **States:** ready, hover, pressed, selected, disabled and keyboard focus, with live examples.
- **Material rules:** concrete examples of raised actions, inset values and raised thumbs in rails.
- **Button families:** action/dialog, toolbar, icon, chip, quick-choice, joined-key and exit examples.
- **On surfaces:** the same controls on flat, raised, inset and striped backgrounds.
- **Menus & navigation:** dashboard and two-way selectors, Settings and Notes rows, real dropdown and context menus.
- **Ticks & inputs:** checks, radios, on/off switches, fields, dropdown, application calendar and scale slider.
- **Colour experiments:** the supplied nine-colour palette on solid and soft buttons.

## Isolation and verification

`GalleryMaterialStudy` owns window-local resource dictionaries and the local style include. Proposed mode adds local aliases for the gallery's existing depth tokens; Current removes those aliases. `Styles/GalleryMaterialStyles.axaml` is loaded by the gallery and its example menus, not by `App.axaml`. The study does not change `ThemeEngine`, the production control dictionary or production views.

Native RadioButton, ToggleSwitch, CheckBox and Slider behaviour is retained. Tests exercise selection, Space-key activation, knob movement, disabled focus, slider keyboard input, both material modes, geometry and production-resource isolation. Rendered sections and popup menus are reviewed in both themes. No new idle animation, layout animation or application-wide transition is introduced.

## Reference guidance

The user's attached examples and these pages informed the lighting, raised/inset relationship and control grouping. Layouts, artwork and code are not reproduced:

- [Radio example](https://www.aicssanimations.com/directory/Radio-Buttons/neumorphic-radio)
- [Button example](https://www.aicssanimations.com/directory/Buttons/neumorphic)
- [Switch example](https://www.aicssanimations.com/directory/Toggle-Switches/neumorphic)
- [Checkbox example](https://www.aicssanimations.com/directory/Checkboxes/neumorphic-checkbox)

The study keeps hover as a modest lift and reserves the inset for pressing/selection. It also keeps readable text and explicit focus cues rather than adopting the very low contrast of some inspiration images. Working-page adoption waits for feedback on the gallery.
