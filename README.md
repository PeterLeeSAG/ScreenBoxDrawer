# ScreenBoxDrawer

ScreenBoxDrawer is a lightweight Windows desktop overlay for highlighting
content during presentations, demonstrations, screen recordings, and remote
support sessions. Draw temporary boxes and arrows over any application without
changing the underlying screen.

The application runs in the Windows notification area and uses configurable
global shortcuts, so it can be used without bringing a control window to the
foreground.

## Features

- Draw rounded boxes over the desktop.
- Draw arrows from a tail point to a tip point.
- Capture and freeze a rectangular area of the virtual desktop.
- Annotate a frozen capture with lines, arrows, boxes, circles, brushes, and
  text.
- Undo the most recent captured markup.
- Choose drawing thickness and text font size from the capture toolbar.
- Choose markup colors from the capture toolbar.
- Copy an annotated capture to the clipboard.
- Display multiple drawings at the same time.
- Automatically remove drawings after a configurable duration.
- Keep drawings until they are cleared by setting the duration to `0`.
- Limit the number of visible drawings; the oldest drawing is removed first
  when the limit is reached.
- Enable or disable moving dashed outline effects.
- Use a configurable global shortcut for each drawing and clearing action.
- Optionally draw directly with `Ctrl+Shift+drag`.
- Ignore a shortcut followed by a click without movement, so accidental clicks
  do not create zero-size drawings.
- Control the overlay from the notification-area menu.
- Persist settings under `%LOCALAPPDATA%\ScreenBoxDrawer\settings.json`.

## Quick start

### Requirements

- Windows
- .NET 8 SDK

### Build and run

From the repository root:

```powershell
dotnet build .\ScreenBoxDrawer\ScreenBoxDrawer.sln
dotnet run --project .\ScreenBoxDrawer\ScreenBoxDrawer.csproj
```

The application is a notification-area utility, so it does not open a normal
main window at startup. Look for the **ScreenBoxDrawer** icon in the Windows
notification area.

## Default shortcuts

| Action | Shortcut | Behavior |
| --- | --- | --- |
| Draw a box | `Ctrl+Shift+D` | Arms the box tool for the next left-button drag. |
| Draw an arrow | `Ctrl+Shift+A` | Arms the arrow tool for the next left-button drag. |
| Clear all drawings | `Ctrl+Shift+X` | Removes every visible box and arrow. |
| Capture screen | `Ctrl+Shift+S` | Opens a dimmed virtual-desktop capture mode. |
| Direct drawing | `Ctrl+Shift+drag` | Draws a box without first pressing the box shortcut. |

After pressing a drawing shortcut, hold the left mouse button and drag across
the screen. Releasing the button completes the drawing. A click without
movement is discarded.

The direct `Ctrl+Shift+drag` gesture is enabled by default and can be disabled
in **Settings**.

### Capture screen

Press the capture shortcut, then hold the left mouse button and drag a
rectangle around the area to freeze. The selected area remains as a still image
while the rest of the virtual desktop stays dimmed. A toolbar appears around
the selection, below it when there is room and above or beside it when the
selection is near an edge.

Choose **Line**, **Arrow**, **Box**, **Circle**, **Brush**, or **Text**,
adjust drawing thickness and text size, and select a color in the toolbar.
Drag inside the frozen image to draw. For text, click inside the capture and
type, then press Enter or move focus away to place the text. Use **Undo** to
remove the latest markup. Use **Copy** or the app icon to place the annotated
capture on the clipboard. Press **Escape** or choose **Close** to leave capture
mode.

## Configure the application

Right-click the notification-area icon and choose **Settings...**. The
settings panel provides:

- **Shortcuts** - Capture new combinations for drawing boxes, drawing arrows,
  clearing all drawings, and capture mode. Each shortcut must include at least
  one modifier key (`Ctrl`, `Alt`, `Shift`, or `Win`), and the four shortcuts
  must be different.
- **Maximum visible boxes** - Choose from 1 to 100 visible drawings. Boxes and
  arrows share this limit. When a new drawing would exceed the limit, the
  oldest drawing is removed first.
- **Visible duration** - Set the number of seconds a drawing remains visible.
  A value of `0` means unlimited duration. A positive duration includes the
  fade-out period.
- **Effects** - Enable or disable moving dashed outlines. Disabled drawings use
  a solid outline.
- **Direct gesture** - Enable or disable `Ctrl+Shift+drag`.

The notification-area menu also includes **Clear all boxes** and **Exit**.

## How it works

ScreenBoxDrawer places a transparent, click-through WPF overlay across the
Windows virtual desktop. The overlay becomes interactive only while a drawing
drag is in progress, then returns to click-through mode when the drawing is
complete. Existing applications remain usable underneath the overlay.

Global keyboard input is handled through Windows registered hotkeys, and mouse
dragging is captured through a low-level mouse hook. If another application
already owns a selected shortcut, ScreenBoxDrawer reports the registration
error so a different shortcut can be chosen.

## Development

The project is a .NET 8 WPF application with Windows Forms integration for the
notification-area icon.

```powershell
# Build the solution
dotnet build .\ScreenBoxDrawer\ScreenBoxDrawer.sln

# Build an optimized release
dotnet build .\ScreenBoxDrawer\ScreenBoxDrawer.sln -c Release
```

The main application code is in
[`ScreenBoxDrawer/`](ScreenBoxDrawer/), with the
overlay lifecycle implemented in
[`MainWindow.xaml.cs`](ScreenBoxDrawer/MainWindow.xaml.cs)
and persisted settings implemented in
[`AppSettings.cs`](ScreenBoxDrawer/AppSettings.cs).
