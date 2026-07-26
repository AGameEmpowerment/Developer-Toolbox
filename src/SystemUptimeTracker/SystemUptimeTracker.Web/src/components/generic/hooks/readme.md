# Generic Hooks

This directory contains framework-neutral React hooks retained as starter
utilities.

- `use-countdown` builds a non-repeating countdown on `use-interval`.
- `use-dictionary` updates individual entries in dictionary-shaped state.
- `use-feature-flag` reads flags through the configured feature-flag service.
- `use-hover-delay` delays a callback until the hover duration is met.
- `use-interval` provides a declarative interval with cleanup.
- `use-outside-click` detects pointer interaction outside a referenced element.
- `use-status` tracks loading, success, and error states.
- `use-switch` exposes explicit true/false setters.
- `use-toggle` toggles boolean state.

The feature-flag hook uses the application service abstraction and does not
assume a specific third-party feature-management provider.
