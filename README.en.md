# <img src="docs/images/pin-chalk.png" width="440" alt="Pin">

<a href="README.md"><img src="docs/images/zh.png" width="94" alt="简体中文"></a> <a href="README.en.md"><img src="docs/images/en-active.png" width="94" alt="English"></a>

Pin one window to another so they come forward together. For example, pin Notepad to a browser to keep your notes in view when you return to the browser. You can also pin a window to the desktop for use with **Win+D**.

## Get started

Download **Pin.exe** from the [latest release](https://github.com/Fr33-B1rd/Pin/releases/latest) and double-click it on 64-bit Windows. If you built Pin from source, double-click `publish\Pin.exe`. The executable includes both languages and the .NET desktop runtime. A pin box appears near the bottom-right corner of the screen, and a pin icon appears in the system tray.

1. Place Notepad partly over a browser window.
2. Drag a pin from the box to the area where the windows overlap. The blue outline marks the front window (the **paper**). The amber outline marks the window underneath (the **wall**).
3. Release to attach the pin. Click the browser to bring both windows forward with Notepad above it. Click the pin head to highlight the pair.

![Meeting notes pinned to the project board, with the paper outlined in blue and the wall in amber.](docs/images/pinned-pair.png)

## Language

Right-click either the tray icon or the pin box and open **语言 / Language**. The default, **Follow system**, uses Simplified Chinese for a Chinese Windows display language and English otherwise. Select **简体中文** or **English** to override it. Switching takes effect immediately and keeps your current pins in place. Pin remembers the language the next time you start it.

## Controls

| Action | Result |
| --- | --- |
| Drop a fresh pin on a window with only the desktop underneath | Attach that window to the desktop. |
| Press Esc or right-click while dragging | Cancel the drag. |
| Drop a fresh pin on empty space or back in the box | Return it unused. |
| Drag an attached pin head to another window | Reattach the pin at the new location. |
| Drop an attached pin on empty space or in the box | Remove the pin. |
| Right-click a pin head | Change its color, adjust how the paper follows its wall, or remove the pin. |
| Ctrl+drag the pin box | Move the box. |
| Double-click the tray icon | Show or hide the box. |
| Right-click the tray icon or the box | View pinned pairs, change the language, or exit. |

Under **Pin color**, choose red, green, blue, or yellow. **Use mode color** gives ordinary pins a red head and desktop widgets a blue head. A custom color lasts until you remove the pin or select **Use mode color** again.

![The pin menu offers color, movement, minimize, and removal options.](docs/images/pin-options.png)

## How pinned windows behave

Each paper can have one wall. A wall can hold several papers and can itself be pinned to another window. Activating any window in the group brings the group forward, with each paper above its wall. Pin rejects attachments that would form a loop.

By default, moving the wall moves its paper by the same distance. Maximizing or restoring the wall skips this movement. Minimizing and restoring the wall also minimizes and restores its paper. Right-click the pin head to change **Move with the wall** or **Minimize with the wall** individually. If you minimize the paper on its own, activating the wall can bring it back after a short grace period.

A paper attached to an always-on-top wall inherits that setting until you remove the pin. Closing either window removes its attachment. Exiting Pin releases all pins, so reattach the windows you need after a restart.

### Desktop widget mode

An ordinary desktop pin keeps its window below other apps and brings it into view with **Show desktop**. To use the window as a widget, right-click its pin head and select **Desktop widget mode (hide separate taskbar button)**. This option also appears under the window’s entry in the pin box menu.

You can click and type in the widget on the desktop. Its minimize button remains available, and its separate taskbar button may disappear. Select the option again to return to ordinary desktop pinning. Windows that already have an owner cannot use widget mode.

## Run or build from source

Install the .NET 10 SDK. The scripts first look in `%LOCALAPPDATA%\Programs\Pin-dotnet`, then use the SDK on your PATH. From the project directory, run `run.bat` to start Pin in Release mode or `publish.bat` to build the self-contained executable at `publish\Pin.exe`.

Window relationships and event handling live in `src/PinManager.cs`. For drag and drop, see `src/DragSession.cs`. For the attached pin head, see `src/PinDecal.cs`. The tray and pin box share menus through `src/TrayContext.cs`. Interface text and the language preference live in `src/UiText.cs`.

## Known limitations

- To pin a window running as administrator, start Pin as administrator too.
- Windows may briefly raise a wall before Pin puts its paper back above it, causing a visible flicker.
- Exclusive full-screen games and some UWP windows may not work with Pin.
- After you switch virtual desktops, a pin head may take about half a second to catch up.

## Manual checks

- Pin Notepad to a browser. Move, minimize, and restore the browser. Check that Notepad follows. Minimize Notepad separately, switch apps, then activate the browser again.
- Pin Calculator to Notepad to make a three-window group. Click the browser and check the stacking order. Trying to pin the browser to Calculator should show a red outline and reject the cycle.
- Pin a window to the desktop and press **Win+D**. Check that you can click the window. Repeat in widget mode.
- With a pin attached, choose Chinese from the tray and English from the pin box. Reopen the menu after each change and check the labels and pinned pair. Restart Pin to check the saved language, then reattach any windows you need.
- Move a pinned window between monitors with different scaling and check the pin head’s size and position. Drag the head back to the box to remove it.

## License

[MIT](LICENSE)
