---
name: app-style
description: The AI Interface visual design system, derived from sagiv440.github.io/sagiv-reuben. Use whenever building or restyling UI in this app (windows, controls, bubbles, dialogs) so new screens match the established look — Poppins type, deep-navy dark base, teal→violet signature gradient, purple accent, generous rounding, glassy surfaces. Apply the existing design tokens and style classes rather than hard-coding colors.
---

# AI Interface design system (sagiv-reuben look)

The app's look is adapted from **sagiv440.github.io/sagiv-reuben**: a dark, modern portfolio with
Poppins type, a teal→violet hero gradient, purple accents on a near-black navy base, and softly
rounded, glassy surfaces. When building UI, **use the design tokens and style classes below** — do not
hard-code hex values or fonts in views.

## Where things live
- **Tokens** (brushes, gradient, font, per-variant colors): `src/AI_Interface/App.axaml`
  (`Application.Resources` + `ThemeDictionaries` for Dark/Light).
- **Reusable control styles + classes**: `src/AI_Interface/Styles/ControlStyles.axaml`
  (included from `App.axaml`).
- **User-themeable defaults + swatch palette**: `src/AI_Interface/Models/ThemeDefaults.cs`.
- **Runtime application** (variant + custom colors): `src/AI_Interface/Services/ThemeService.cs`.

## Palette (from the site)
| Role | Hex |
|------|-----|
| Accent (primary) | `#804DEE` purple |
| Gradient start | `#00CEA8` teal |
| Gradient end | `#BF61FF` violet |
| Supporting | `#22D3EE` cyan, `#3B82F6` blue |
| Dark base | `#04090F` → `#0D0922` (navy gradient) |
| Status OK / error | `#00CEA8` / `#E0573A` |

The **signature gradient** is teal→violet, 90°. Use it sparingly for emphasis (brand title, primary
CTA) — not on large fills.

## Design tokens — reference these, don't inline colors
- Font: `{DynamicResource AppFont}` (defaults to Poppins, embedded in `Assets/Fonts`) + base size
  `{DynamicResource AppFontSize}`. Both are **user-selectable** (Settings → Theme → Typography) and
  overridden live by `ThemeService`, so reference them dynamically — don't hard-code a font or size.
  FontFamily/FontSize inherit, so the `Window` style already sets them app-wide.
- Accent: `{DynamicResource AppAccentBrush}` (user-themeable) · gradient: `{DynamicResource AccentGradientBrush}`.
- Chat bubbles: `{DynamicResource UserBubbleBrush}` / `{DynamicResource AssistantBubbleBrush}` (user-themeable).
- Structural, **theme-variant-aware** (auto light/dark): `AppWindowBackground`, `AppSurfaceBrush`,
  `AppSurfaceBorderBrush`, `AppInputBackground`, `AppTextPrimary`, `AppTextSecondary`. Always use
  `{DynamicResource ...}` for these so they swap with night mode.

## Style classes — prefer these over ad-hoc setters
- `Button.cta` — primary action: the teal→violet gradient, white SemiBold text, 12px radius.
- `Button.ghost` — secondary/toolbar: transparent with a hairline border.
- `Border.card` — glassy surface panel: `AppSurfaceBrush` + 1px `AppSurfaceBorderBrush`, 16px radius.
- `TextBlock.brand` — gradient-filled Poppins SemiBold title.
- `TextBlock.muted` — secondary 12px text in `AppTextSecondary`.
- Class-toggled state from data: `Classes.user="{Binding IsUser}"`, `Classes.online="{Binding IsConnected}"`.

## Conventions
- **Rounding:** inputs/buttons 10–12px, cards/bubbles 16–18px, pills 9999 where appropriate. Avalonia
  base styles already round `TextBox`/`Button`/`ComboBox` to 10.
- **Depth:** soft shadows on raised surfaces, e.g. `BoxShadow="0 6 18 0 #2A000000"`. Keep them subtle.
- **Type:** Poppins only. Weights: Regular/Medium/SemiBold/Bold are embedded. Never reintroduce Inter,
  Arial, or system-ui (the site deliberately avoids them).
- **Light + dark must both work.** Night mode is real (`ThemeMode.Dark` via `Application.RequestedThemeVariant`).
  Never hard-code a background/foreground pair that only reads in one variant — use the variant-aware
  tokens above. Test both.
- **Customization stays intact.** The Settings → Theme tab lets users override accent + bubble colors and
  pick light/dark; those flow through `ThemeService` into the themeable brush keys. Don't bypass them.

## Composer toggles & dialogs
- **Per-prompt toolbar toggles** (Web search, Thinking) are `ToggleButton`s with the local `toolToggle`
  pill style defined in `MainWindow.axaml` — the `:checked` state tints with the accent. Add new
  composer toggles the same way so they read as a set.
- **Dialogs** (Settings, Project, tool approval, Model Config) are plain `Window`s whose content is
  wrapped in a `Border.card`, using `Button.cta` for the primary action and `Button.ghost` for
  Cancel/secondary. Tabbed dialogs use a `TabControl` inside that card (see `SettingsWindow` /
  `ProjectWindow` / `ModelConfigWindow`). Match this for any new dialog instead of styling a bare window.
- **Sidebar tabs** (Chat Log / Files, shown only with a project) use the local `Button.tab` /
  `Button.tab.active` style in `MainWindow.axaml`; the Files tab hosts a `TreeView.files`. Reuse those
  classes for any new sidebar tab rather than restyling from scratch.

## Gradient CTA button — the one tricky bit
The Fluent `Button` template paints its background on `ContentPresenter#PART_ContentPresenter`, so a
gradient must be set there (and re-asserted for `:pointerover` / `:pressed`), not just on the button.
`Button.cta` in `ControlStyles.axaml` already does this — reuse it instead of re-deriving.
