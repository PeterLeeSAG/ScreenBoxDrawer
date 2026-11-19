# Screen Capture Mode

## Purpose

Screen capture mode lets the user freeze a rectangular part of the desktop and
annotate that frozen image without the underlying applications changing. It is
intended for creating a quick screenshot with visual explanations during
support, demonstrations, and screen recordings.

## User workflow

1. Press the configured **Capture screen** global shortcut to enter capture
   mode. Pressing the same shortcut again exits capture mode.
2. The current virtual desktop is captured once and shown under a dim layer.
3. Hold the left mouse button and drag a rectangle around the area to capture.
   Release the button to finish the selection.
4. The selected rectangle remains visible as a still image from the capture
   moment. The rest of the desktop remains dimmed.
5. Choose a markup tool and drag inside the selected image:
   - **Line** draws a straight line.
   - **Box** draws a rectangular outline.
   - **Circle** draws an ellipse outline.
   The toolbar is placed below the selected rectangle first. If that space is
   too close to the bottom edge, it moves above the rectangle and then to a
   side if necessary.
6. Markup is clipped to the selected rectangle and remains on the frozen
   image.
7. **Copy** places the selected image and its markup on the clipboard.
   **Close** exits capture mode without changing the desktop.

Press **Escape** at any time to cancel capture mode and discard the current
selection and markup.

## Configuration

The capture shortcut is configurable in the notification-area **Settings**
window. The default is `Ctrl+Shift+S`. It must be different from the box,
arrow, and clear shortcuts and must contain at least one modifier key.

## Visual behavior

- The desktop is captured before the capture overlay is displayed.
- The displayed image does not refresh while the user selects or annotates.
- The dimmed area prevents the selection target from being confused with the
  live desktop.
- The capture uses the complete Windows virtual desktop, including monitors
  with negative screen coordinates. Selection cropping is calculated in
  physical screen pixels so a rectangle can cross monitor boundaries.
- Capture geometry uses separate WPF and bitmap coordinate systems. WPF
  device-independent pixels position the overlay, selection border, markup
  canvas, and toolbar. The selected image and red selection border are direct
  children of an absolute `Canvas`, so `Canvas.SetLeft` and `Canvas.SetTop`
  place them at the drag location instead of in the Grid center.
- The crop rectangle is calculated from Win32 physical cursor positions and
  the captured virtual-desktop origin. It is not mapped through WPF mouse
  DPI or a desktop-wide scaling transform.
- Selection and markup positions come directly from WPF mouse
  `GetPosition(RootGrid)` events. This prevents a desktop-wide DPI transform
  from moving the selected area toward a monitor center.
- The toolbar is centered over the selection and constrained to the virtual
  desktop root. It tries below first, then above, then left or right, before
  applying an edge margin.
- The existing box and arrow drawing overlay is suspended while capture mode
  is active.
- Escape, Close, and the capture shortcut restore the normal click-through
  drawing mode.

## Implementation phases

1. Add the persisted capture shortcut and register it with the existing
   `RegisterHotKey` infrastructure.
2. Add a dedicated full-screen WPF capture window that captures and displays a
   static virtual-desktop image.
3. Add rectangle selection and the dimmed/un-dimmed selected-region display.
4. Add line, box, and circle markup tools, clipboard export, cancel behavior,
   and lifecycle cleanup.
5. Build the application, run focused interaction checks, and update the
   project README.
