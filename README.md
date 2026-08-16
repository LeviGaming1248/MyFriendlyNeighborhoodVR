# MFNVR

MFNVR is a VR mod for **My Friendly Neighborhood** with full 6DOF head tracking, room-scale movement, tracked motion controllers, and motion-controlled weapons.

<img width="518" height="222" alt="MFNVR" src="https://github.com/user-attachments/assets/d5604352-5471-4290-8fdf-664fc0fa065d" />

## Features

| Feature |
| --- |
| Full 6DOF head tracking and room-scale gameplay |
| Tracked motion controllers and floating VR hands |
| Motion-controlled firearms with weapon-aligned shots and crosshair |
| Two-handed weapon gripping |
| Physical wrench melee driven by real swing speed and distance |
| Physical hip and shoulder weapon switching |
| VR-compatible HUD, notes, inventory, toolboxes, and interaction menus |
| Motion-controller pointers for menus and item management |
| In-game VR settings menu |
| Snap and smooth turning |
| Fixed and optional dynamic resolution scaling |
| Meta/Oculus and SteamVR OpenXR runtime support |
| Full game and Neighborhorde support |

Left-handed mode is not available in v1.0.0. Its experimental implementation remains in the source for future development.

## Requirements

- *My Friendly Neighborhood* on Steam
- Windows 10 or 11
- An OpenXR-compatible PC VR headset and motion controllers
- Meta Quest Link/Oculus software or SteamVR

The mod was developed and tested primarily with an Oculus Rift S. Other OpenXR hardware may require additional testing or controller-binding work.

## Installation

1. Download `MFNVR-v1.0.0.zip` from the GitHub release.
2. In Steam, right-click *My Friendly Neighborhood* and select **Manage > Browse local files**.
3. Close the game, SteamVR, and Meta Quest Link/Oculus before installing.
4. Extract the ZIP directly into the game's root folder, beside `My Friendly Neighborhood.exe`.
5. Allow Windows to merge folders and replace older MFNVR files if prompted.
6. Select an OpenXR runtime:
   - **Meta/Oculus:** Set Meta Quest Link as the active OpenXR runtime in the Meta PC app.
   - **SteamVR:** Open SteamVR settings, select **OpenXR**, and set SteamVR as the active runtime.
7. Start the selected VR runtime first, wake the headset and controllers, and then launch the game through Steam.

Setting the game's FOV to approximately 100 is recommended.

## VR settings

Open **VR Settings** from the main or pause menu. You can also press `F4` or hold the left-stick click (`L3`) for two seconds.

Settings are saved to:

```text
BepInEx\config\MFNVR.cfg
```

Available settings include resolution scaling, optional dynamic resolution, crosshair distance and size, HUD placement, menu placement, player height, UI screens, menu pointer behavior, interaction-camera movement, snap or smooth turning, and physical weapon switching. Player height can be recalibrated on demand to MFN's vanilla character-camera height or automatically whenever gameplay loads. Options marked **restart required** take effect the next time the game starts.

## Building from source

The source archive includes the managed plugins, native OpenXR bridge, required patching tool, and build scripts. From a **Developer PowerShell for Visual Studio 2022** prompt:

```powershell
.\scripts\build.ps1 -GameDir "E:\SteamLibrary\steamapps\common\My Friendly Neighborhood" -Configuration Release
```

To create a mod-only archive:

```powershell
.\scripts\package.ps1 -GameDir "E:\SteamLibrary\steamapps\common\My Friendly Neighborhood" -Version "1.0.0"
```

Visual Studio 2022 with C++ desktop tools, CMake, and the .NET SDK are required. Game and BepInEx assemblies are referenced from the supplied `GameDir` and are not redistributed in the source archive.

## Disclaimer

This project was developed entirely using AI-assisted code generation with Codex. None of the source code has been manually written or reviewed by a human. The code may contain bugs, security issues, performance problems, compatibility problems, or other unintended behavior.

Use this project at your own risk. Code reviews, bug reports, testing feedback, and pull requests are welcome.

## License

MFNVR source code is released under the [MIT License](LICENSE). Bundled third-party components retain their respective licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the `licenses` directory.

<img width="700" height="540" alt="MFNVR gameplay" src="https://github.com/user-attachments/assets/6b563b0d-3c79-45c3-a753-315a75af1b34" />

<img width="700" height="540" alt="MFNVR motion controls" src="https://github.com/user-attachments/assets/82c65604-8a9f-4d29-ba6f-d8af8edc7e9a" />

<img width="700" height="540" alt="MFNVR interface" src="https://github.com/user-attachments/assets/ad328172-ae66-4069-8aac-f88d4f8c5fd9" />
