---
name: app-style
description: The AI Interface visual design system — a flat "IDE" look modelled on Visual Studio Code / Photoshop. Use whenever building or restyling UI in this app (windows, controls, bubbles, dialogs) so new screens match the established look — neutral dark-gray surfaces, a red-orange accent, the system UI font, sharp corners, hairline borders, flat (no glass/shadows). Apply the existing design tokens and style classes rather than hard-coding colors.
---

# AI Interface design system (flat IDE look)

The app's look is a flat, dense **developer-tool** aesthetic modelled on **VS Code / Photoshop**: neutral
dark-gray surfaces, a single red-orange accent, the system UI font, sharp 3–5px corners, hairline 1px
borders, and flat surfaces (no gradients / glass / drop-shadows). When building UI, **use the design
tokens and style classes below** — do not hard-code hex values or fonts in views.

## Where things live
- **Tokens** (brushes, accent, font, per-variant colors): `src/AI_Interface/App.axaml`
  (`Application.Resources` + `ThemeDictionaries` for Dark/Light).
- **Reusable control styles + classes**: `src/AI_Interface/Styles/ControlStyles.axaml` (included from `App.axaml`).
- **User-themeable defaults + swatch palette**: `src/AI_Interface/Models/ThemeDefaults.cs`.
- **Runtime application** (variant + custom colors + font): `src/AI_Interface/Services/ThemeService.cs`.

## Palette
| Role | Hex |
|------|-----|
| Accent (primary) | `#F2542D` red-orange |
| Accent hover / pressed | `#FF6A45` / `#D8431F` |
| Dark — window (editor) | `#1E1E1E` |
| Dark — surface (panels / sidebar) | `#252526` |
| Dark — border (hairline) | `#3C3C3C` |
| Dark — text primary / secondary | `#D4D4D4` / `#858585` |
| Light — window / surface | `#FFFFFF` / `#F3F3F3` |
| Light — border | `#D0D0D0` |
| Light — text primary / secondary | `#1F1F1F` / `#6E6E6E` |
| Status ok / error / idle | `#3FB950` / `#E5534B` / `#6E6E6E` |

There is **no signature gradient** — the accent is a flat solid. `AccentGradientBrush` still exists (a
near-flat red-orange) only for backward compatibility; prefer the solid `AppAccentBrush`.

## Design tokens — reference these, don't inline colors
- Font: `{DynamicResource AppFont}` (defaults to the system UI font, "Segoe UI") + base size
  `{DynamicResource AppFontSize}` (defaults to 13). Both are **user-selectable** (Settings → Theme →
  Typography) and overridden live by `ThemeService`, so reference them dynamically — never hard-code a
  font or size. FontFamily/FontSize inherit, so the `Window` style already sets them app-wide. The
  embedded **Poppins** font is still available — select it by name; `ThemeService.ResolveFont` maps
  "Poppins" → the embedded `avares://…#Poppins` URI, anything else to a system family.
- Accent: `{DynamicResource AppAccentBrush}` (user-themeable).
- Chat bubbles: `{DynamicResource UserBubbleBrush}` / `{DynamicResource AssistantBubbleBrush}` (user-themeable).
- Structural, **theme-variant-aware** (auto light/dark): `AppWindowBackground`, `AppSurfaceBrush`,
  `AppSurfaceBorderBrush`, `AppInputBackground`, `AppTextPrimary`, `AppTextSecondary`. Always use
  `{DynamicResource ...}` for these so they swap with night mode.

## Style classes — prefer these over ad-hoc setters
- `Button.cta` — primary action: **flat solid** `AppAccentBrush`, white SemiBold text, 3px radius (hover/
  pressed dim via Opacity).
- `Button.ghost` — secondary / toolbar: transparent with a hairline border; faint white hover.
- `Border.card` — flat surface panel: `AppSurfaceBrush` + 1px `AppSurfaceBorderBrush`, 4px radius, no shadow.
- `TextBlock.brand` — the product/title text: `AppTextPrimary`, SemiBold (plain, not gradient-filled).
- `TextBlock.muted` — secondary 12px text in `AppTextSecondary`.
- `Button.nav` / `Button.nav.active` — left-rail entries (shared by the sidebar chat log **and** the
  Settings category nav); `.active` gets a translucent-accent fill. `TextBlock.navheader` — the muted
  group headers above them. Reuse these for any left-rail list.
- `Button.suggestion` — proactive next-step chips below an assistant answer (rounded ~13px accent-outlined
  pill, accent text, translucent-accent hover); defined locally in `MainWindow.axaml`.
- `Border.codeBubble` / `Border.codeHeader` — a code/command block in a reply: `AppInputBackground` body +
  `AppSurfaceBrush` header bar (with the language label + a `Button.copy` 📋), 5px radius, hairline border;
  the body is a monospace `SelectableTextBlock` (`Cascadia Code,Consolas,monospace`) in a horizontal
  `ScrollViewer`. Defined locally in `MainWindow.axaml`; reuse for any code/preformatted block.
- Class-toggled state from data: `Classes.user="{Binding IsUser}"`, `Classes.online="{Binding IsConnected}"`.

## Conventions
- **Rounding:** sharp. Inputs / buttons / toggles / chips 3px, cards / panels 4px, bubbles 5px. Avalonia
  base styles in `ControlStyles.axaml` already set `TextBox` / `Button` / `ComboBox` to 3.
- **Borders & depth:** 1px hairline borders in `AppSurfaceBorderBrush`. **No drop-shadows, no glass** —
  the look is flat. Separate layers with the surface-vs-window tone, not shadows.
- **Density:** compact. Buttons pad ~`11,5`; base font 13. Favour tight, IDE-like spacing.
- **Type:** the system UI font by default — neutral, not decorative. Don't reintroduce a brand display font.
- **Light + dark must both work.** Dark is the default (`ThemeMode.Dark` via `Application.RequestedThemeVariant`).
  Never hard-code a background/foreground pair that only reads in one variant — use the variant-aware
  tokens above. Test both.
- **Customization stays intact.** Settings → Theme lets users override accent + bubble colors, font +
  size, and light/dark; those flow through `ThemeService` into the themeable keys. Don't bypass them.
  `SettingsService` migrates the legacy purple/Poppins defaults to the new ones on load (only values still
  sitting at the old default are changed).

## Composer toggles & dialogs
- **Per-prompt toolbar toggles** (🧠 Thinking, 🔊 Auto-read) are `ToggleButton`s with the local `toolToggle`
  pill style in `MainWindow.axaml` — the `:checked` state tints with a translucent accent (`#33F2542D`) +
  accent border. Add new composer toggles the same way so they read as a set. (Search scope — Local/Web/Deep
  — is a `ComboBox` beside them, not a toggle. **🔊 Auto-read** is shown only when a voice is configured,
  via `IsVisible="{Binding IsVoiceConfigured}"`.)
- **Dialogs** (Settings, Project, tool approval, Model Config) are plain `Window`s whose content is
  wrapped in a `Border.card`, using `Button.cta` for the primary action and `Button.ghost` for
  Cancel/secondary. Match this for any new dialog instead of styling a bare window.
- **Settings layout — grouped left nav + content host.** `SettingsWindow` is **not** a `TabControl` (a
  flat one can't show non-clickable group headers). It's a left rail of muted `TextBlock.navheader` group
  headers (**EDITOR FEATURES** / **AI FEATURES**) with `Button.nav` entries under each, beside a right
  `Panel` whose category panels toggle by `IsVisible` bound to per-category bools on `SettingsViewModel`
  (`SelectCategoryCommand` + the `SettingsCategory` enum). The selected entry uses `Button.nav.active`.
  Reuse this grouped-nav shape (not a TabControl) for any dialog that needs section headers; the *Models*
  category nests a small inner `TabControl` only for its Local AI / Web Models sub-tabs.
- **Sidebar tabs** (Chat Log / Files, shown only with a project) use the local `Button.tab` /
  `Button.tab.active` style in `MainWindow.axaml`; the Files tab hosts a `TreeView.files`. Reuse those
  classes for any new sidebar tab rather than restyling from scratch.

## Flat CTA button — the one tricky bit
The Fluent `Button` template paints its background on `ContentPresenter#PART_ContentPresenter`, so the
accent fill must be set there (and re-asserted for `:pointerover` / `:pressed`), not just on the button.
`Button.cta` in `ControlStyles.axaml` already does this with the solid `AppAccentBrush` (hover/pressed dim
via Opacity) — reuse it instead of re-deriving.
