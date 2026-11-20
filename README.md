# EVE-O Preview for macOS

![EVE-O Preview icon](mac/EveOPreview.Mac/icon.png)

`EVE-O Preview for macOS` is a lightweight companion app for EVE Online multiboxing on macOS.

It helps you keep track of multiple running clients with live preview overlays and quick client switching, while staying focused on a simple, practical workflow.

## Screenshots

Main app window:

![Main app window](app-main-window.png)

EVE Online on macOS:

![EVE Online on macOS](overlay-in-action.png)

## What it does

- detects running EVE Online clients
- shows live thumbnail previews
- lets you click a preview to focus the matching client
- remembers thumbnail positions
- saves local app settings
- ships as a ready-to-test macOS `.app` and `.dmg`

## Built for macOS

This project is a dedicated macOS implementation built with:

- .NET 8
- Avalonia UI
- CoreGraphics
- AppleScript / System Events

## How to use

1. Start one or more EVE Online clients.
2. Launch `EVE-O Preview for macOS`.
3. Allow macOS permissions if prompted.
4. Use the main window to detect clients and manage settings.
5. Click a floating preview to bring the matching client to the front.

## Build

From the repository root:

```bash
./mac/scripts/build-dmg.sh
```

Build output:

- `mac/dist/EVE-O Preview.app`
- `mac/dist/eve-o-preview-mac-osx-arm64.dmg`

Optional Intel build:

```bash
RUNTIME=osx-x64 ./mac/scripts/build-dmg.sh
```

## macOS permissions

Depending on your macOS settings, the app may request access to:

- Accessibility
- Automation
- Screen Recording

These permissions are used for client discovery, activation, and preview capture.

## Project status

This is an early public macOS release focused on the core experience:

- find running EVE clients
- preview them live
- switch between them quickly

## Known limitations

- this is still an early macOS release
- some settings are visible but not implemented yet
- feature parity with the Windows ecosystem is not complete
- Apple Silicon build is the default packaged output

## Credits

- macOS port and current project work by `SihKic`
- inspired by the original `EVE-O Preview` idea and workflow created by the wider EVE multiboxing community

## Support

If this project is useful to you and you would like to support it, ISK donations are welcome in EVE Online.

- in-game character: `SihKic`

## Release

Current packaged build output:

- `mac/dist/eve-o-preview-mac-osx-arm64.dmg`
