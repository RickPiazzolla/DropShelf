# Roadmap

Rough order of work. Items move to Done as they land.

## Milestone 1 — a shelf that holds things

- [x] WPF project scaffold targeting .NET 9
- [x] Tray icon, no taskbar entry, exit from the tray menu
- [x] Borderless always-on-top shelf window, draggable by its header
- [x] Accept dropped files and hold them in memory
- [x] Show held files as a grid of tiles
- [x] Drag files back out to Explorer and other apps

## Milestone 2 — feels like a real utility

- [x] Real shell icons and thumbnails for held files
- [x] Remove a single item, clear the shelf
- [x] Global hotkey to summon and dismiss the shelf
- [x] Shelf follows you across virtual desktops
- [ ] Screen-edge trigger so the shelf appears when a drag starts

## Milestone 3 — the awkward cases

- [ ] Virtual files, so attachments dragged out of Outlook and Gmail work
- [ ] Dragged text and images become real files on the shelf
- [ ] Dragged links become resolvable items
- [ ] Multiple shelves at once

## Milestone 4 — shipping

- [ ] Settings window
- [ ] Start with Windows
- [ ] Persist shelf contents across restarts
- [ ] Single-file publish and a release build

## Known unknowns

- ~~Pinning a window to all virtual desktops has no supported API.~~ Settled. The
  shelf window is destroyed and rebuilt when it is summoned from a desktop it is
  not on. A new window is created on the current desktop, so no private API is
  needed, and the items survive because they live in the model rather than the
  window.
- Detecting that a drag has started anywhere on the system needs a low-level mouse
  hook. Needs care to avoid a laggy cursor.
