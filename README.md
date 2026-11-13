# ScreenBoxDrawer

ScreenBoxDrawer is a Windows WPF overlay for drawing temporary boxes on screen.

## Use

- Find the ScreenBoxDrawer icon in the taskbar notification area and choose **Settings...** to open the control panel.
- The default draw shortcut is **Ctrl+Shift+D**. Press it, then drag with the left mouse button to draw a box.
- The default clear shortcut is **Ctrl+Shift+X**. It removes all visible boxes.
- **Ctrl+Shift+drag** also remains available as a direct drawing gesture.
- Choose the maximum number of simultaneously visible boxes (1–100). When the limit is reached, the oldest box is removed.
- Set the total time a box remains visible after drawing, including its fade-out; **0** keeps it until manually cleared or removed by the box limit. The default is 1.5 seconds.
- Disable dashed and moving effects to use solid outlines.
- Use the notification-area menu or settings panel to clear boxes, and choose **Exit** to close the app.

Settings are stored in `%LOCALAPPDATA%\ScreenBoxDrawer\settings.json`.

## Build

Build `ScreenBoxDrawer\ScreenBoxDrawer.sln` with the .NET 8 SDK on Windows.
