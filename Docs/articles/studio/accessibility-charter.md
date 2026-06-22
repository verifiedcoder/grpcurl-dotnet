# Accessibility charter (manual pass)

GrpCurl.Net Studio targets **WCAG 2.1 AA** (SPEC-020 §6). Most of it is enforced automatically on every
CI run — `ThemeContrastTests` audits colour contrast in both themes, and `AccessibilityTests` asserts every
interactive control across the shell and all document/pane views carries an accessible name. This charter
covers the parts a machine can't judge: the lived experience with a screen reader and a keyboard.

Run it **once per release**, on a real desktop, in **both Light and Dark** themes. Record the date, OS,
assistive technology + version, and any defects. A row may only be ticked with evidence.

## Scope per platform (SPEC-060 NFR-A2–A4)

| Platform | Screen reader | Bar |
| --- | --- | --- |
| Windows | Narrator **and** NVDA | Must |
| macOS | VoiceOver | Should |
| Linux | Orca | Could (best-effort) |

## Keyboard-only pass (no mouse — NFR-A1)

Unplug or ignore the mouse for this section.

- [ ] **Reach everything.** `Tab` / `Shift+Tab` reaches every interactive control on each screen; focus is
      always visible (a 2 px accent outline) and never trapped.
- [ ] **Zones.** `F6` / `Shift+F6` cycles focus across sidebar → document → inspector → console → status bar.
- [ ] **Full round-trip.** Add a connection, browse services, open a method, edit the request, and invoke —
      entirely from the keyboard.
- [ ] **Shortcut map (SPEC-020 §5)** — each works, including while an editor holds focus:
  - [ ] `Ctrl+Enter` invoke / start / execute · `Ctrl+.` cancel
  - [ ] `Ctrl+T` new request tab (from the selected method) · `Ctrl+W` close tab
  - [ ] `Ctrl+Tab` / `Ctrl+Shift+Tab` and `Ctrl+PgDn` / `Ctrl+PgUp` cycle tabs
  - [ ] `Ctrl+S` save · `Ctrl+Shift+F` format the JSON body/variables · `F5` refresh descriptors
  - [ ] `Ctrl+L` focus the explorer filter (second press restores focus) · `Ctrl+E` environment switcher
  - [ ] `Ctrl+K` command palette · `Ctrl+B` / `Ctrl+J` / `Ctrl+I` toggle panes · `Ctrl+H` history · `Ctrl+,` settings
- [ ] **Esc order.** `Esc` resolves innermost-first (completion popup → editor selection → flyout →
      cell-edit) and never closes a tab or cancels an in-flight RPC.
- [ ] **Dialogs.** Opening any dialog (command palette, connection/environment/TLS editor, prompts) lands
      focus on its primary input or default button; `Esc` cancels; the default button responds to `Enter`.

## Screen-reader pass (NFR-A2/A3/A4)

- [ ] **Shell landmarks** are announced as you move between zones.
- [ ] **Names + roles + state** are read for every control (the `AutomationProperties.Name` / `HelpText`
      from SPEC-020 §6) — spot-check the explorer tree, the request editor, the headers grid, the Invoke
      button, the response tabs, and the status-bar environment switcher.
- [ ] **Live regions** announce async completions ("Call completed: …") and batched stream counts, and a
      toast is announced once on show.
- [ ] **Reveal-secret** controls read sensibly and never expose a stored secret value.

## Visual pass (both themes — NFR-A5)

- [ ] **Contrast** of text and indicators looks comfortable in Light and Dark (the automated audit is the
      gate; this is a sanity spot-check, including status pills and badge letters).
- [ ] **No colour-only state.** Status pills carry their status name + numeric code, shape badges carry
      letters (U/SS/CS/BD), connection dots are backed by a state word in the tooltip / automation name, and
      error rows carry an icon or text, not just red.
- [ ] **Reduced motion / text scaling.** With the OS "reduce motion" setting on, nothing distracting
      animates; at 200 % OS text scaling, rows grow and no control clips. (The app ships no decorative
      motion today, so reduce-motion is satisfied by construction — confirm nothing regressed.)

## Result

| Field | Value |
| --- | --- |
| Date | |
| OS / version | |
| Assistive tech + version | |
| Themes exercised | Light / Dark |
| Defects filed | |
