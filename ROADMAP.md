# Roadmap

Rough order of work. Items are ticked as they land.

## Milestone 1: a shelf that holds things

- [x] WPF project scaffold targeting .NET 9
- [x] Tray icon, no taskbar entry, exit from the tray menu
- [x] Borderless always-on-top shelf window, draggable by its header
- [x] Accept dropped files and hold them in memory
- [x] Show held files as a grid of tiles
- [x] Drag files back out to Explorer and other apps

## Milestone 2: feels like a real utility

- [x] Real shell icons and thumbnails for held files
- [x] Remove a single item, clear the shelf
- [x] Global hotkey to summon and dismiss the shelf
- [x] Shelf follows you across virtual desktops
- [x] Screen-edge trigger so the shelf appears when a drag starts

## Milestone 3: the awkward cases

- [x] Virtual files, so attachments dragged out of Outlook and Gmail work
- [x] Dragged text and images become real files on the shelf
- [x] Dragged links become resolvable items
- [ ] Multiple shelves at once

## Milestone 4: shipping

- [x] Settings as tray menu toggles rather than a window
- [x] Start with Windows
- [x] Persist shelf contents across restarts
- [x] Only one instance runs at a time
- [x] Single-file publish and a release build
- [x] Build and test on every push

## Settled questions

Both of these looked like blockers at the start.

**Pinning a window to all virtual desktops has no supported API.** The interfaces
that would allow it are undocumented and their identifiers change between Windows
builds. Instead the shelf asks whether it is on the desktop being viewed, and if
not, the window is destroyed and rebuilt. A window created now is created here.
The items survive because they live in the model rather than the window.

**Nothing tells an application that a drag has started.** Only a low-level mouse
hook can see it, and a hook cannot tell a file drag from a text selection, since
both are the button held down while moving. So the catcher appears on reaching the
screen edge with the button held, which is deliberate rather than accidental. The
hook callback does nothing but compare two coordinates, which keeps the cursor
responsive across the whole system.

## Not doing

- **Copying files onto the shelf.** The shelf stores paths, so holding a 4GB video
  costs nothing. Copying would be safer against the original being moved, and far
  worse in every other respect.
- **Move as a drag-out effect.** In a file drag the target performs the operation,
  so offering Move would let a target delete the user's original. The shelf holds a
  reference and has no business authorising that.
