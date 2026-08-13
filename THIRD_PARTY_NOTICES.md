# Third-party notices

The installable MFNVR release includes or relies on the following third-party components. Their original licenses and copyright notices continue to apply.

| Component | Release used | Project and license information |
| --- | --- | --- |
| BepInEx | 5.4.23.5 | [BepInEx](https://github.com/BepInEx/BepInEx), GNU Lesser General Public License 2.1 |
| Unity Doorstop | 4.5.0 | [UnityDoorstop](https://github.com/NeighTools/UnityDoorstop), GNU Lesser General Public License 2.1 |
| OpenXR loader, headers, and import library | 1.1.59 | [Khronos OpenXR-SDK-Source](https://github.com/KhronosGroup/OpenXR-SDK-Source), Apache License 2.0 |
| HarmonyX / Harmony interoperability | Bundled with BepInEx | [HarmonyX](https://github.com/BepInEx/HarmonyX) |
| Mono.Cecil | Bundled with BepInEx and included as a source-build tool dependency | [Mono.Cecil](https://github.com/jbevain/cecil), MIT License |
| MonoMod RuntimeDetour and Utils | Bundled with BepInEx | [MonoMod](https://github.com/MonoMod/MonoMod) |
| Unity Native Plugin API headers | Source build dependency | Copyright Unity Technologies; Unity Companion License as stated in the header files |

The BepInEx and Unity Doorstop LGPL 2.1 texts are included in the `licenses` directory. The complete Apache License 2.0 text supplied with the OpenXR SDK is included under `third_party/openxr/share/doc/openxr/LICENSE` in source distributions and under `licenses/OpenXR-SDK-Source-LICENSE.txt` in binary distributions.

MFNVR does not include *My Friendly Neighborhood*, Unity engine binaries, SteamVR, or Meta/Oculus runtime software. Game and runtime assemblies are referenced only from the user's local installation during development or at runtime.

This notice is informational and is not a replacement for the license texts supplied by the upstream projects.
