using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MFNVRConfig
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.mfnvr.prototype", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class MFNVRConfigPlugin : BaseUnityPlugin
    {
        private const string PluginGuid = "com.mfnvr.config";
        private const string PluginName = "MFNVR Config";
        private const string PluginVersion = "1.0.0";

        private static MFNVRConfigPlugin instance;
        private static float activeResolutionScale = 1f;
        private static Type coreType;

        private ConfigFile settings;
        private ConfigEntry<float> resolutionScale;
        private ConfigEntry<bool> dynamicResolution;
        private ConfigEntry<float> dynamicMinimum;
        private ConfigEntry<float> dynamicTargetFps;
        private ConfigEntry<bool> dotEnabled;
        private ConfigEntry<float> dotDistance;
        private ConfigEntry<float> dotSize;
        private ConfigEntry<float> hudDistance;
        private ConfigEntry<float> hudScale;
        private ConfigEntry<float> hudHeight;
        private ConfigEntry<float> menuDistance;
        private ConfigEntry<float> menuScale;
        private ConfigEntry<bool> flatScreensOnlyForMainPauseAndFiles;
        private ConfigEntry<bool> interactionCameraMovement;
        private ConfigEntry<bool> menuPointer;
        private ConfigEntry<bool> physicalWeaponSwitching;
        private ConfigEntry<bool> smoothTurning;
        private ConfigEntry<float> snapAngle;
        private ConfigEntry<float> smoothSpeed;

        private MethodInfo applyBridgeSettings;
        private MethodInfo applyUiScreenSettings;
        private MethodInfo applyInteractionCameraSettings;
        private MethodInfo applyMenuPointerSettings;
        private MethodInfo applyPhysicalWeaponSwitchingSettings;
        private DateTime lastConfigWrite;
        private float nextConfigCheck;
        private bool visualSettingsDirty = true;
        private bool snapLatched;
        private readonly FrameTiming[] frameTimings = new FrameTiming[30];
        private float nextDynamicCheck;
        private bool timingWarningLogged;
        private bool timingFallbackLogged;
        private float smoothedFrameMilliseconds;
        private int nextTurningWarningFrame;

        private readonly FieldInfo movementEnabledField = AccessTools.Field(typeof(Player), "movementControlsEnabled");
        private readonly FieldInfo hardDeactivateField = AccessTools.Field(typeof(Player), "hardDeactivate");
        private readonly FieldInfo neckHorizontalField = AccessTools.Field(typeof(Player), "neckHorizonal");
        private readonly FieldInfo rotating180Field = AccessTools.Field(typeof(Player), "rotatingCamera180");
        private readonly FieldInfo rotatingRightField = AccessTools.Field(typeof(Player), "rotatingCameraRight");
        private readonly FieldInfo rotatingLeftField = AccessTools.Field(typeof(Player), "rotatingCameraLeft");

        private void Awake()
        {
            instance = this;
            BindSettings();
            try
            {
                InstallPatches();
                ApplyVisualSettings();
                Logger.LogInfo("MFNVR configuration companion loaded; stable camera core was not modified.");
            }
            catch (Exception exception)
            {
                Logger.LogError($"MFNVR Config disabled itself without affecting VR: {exception}");
                enabled = false;
            }
        }

        private void BindSettings()
        {
            var path = Path.Combine(Paths.ConfigPath, "MFNVR.cfg");
            MigrateLegacyConfigNames(path);
            settings = new ConfigFile(path, true);
            resolutionScale = settings.Bind("Rendering", "ResolutionScale", 1f,
                new ConfigDescription("Fixed eye resolution and dynamic-resolution maximum.",
                    new AcceptableValueRange<float>(0.5f, 1.5f)));
            dynamicResolution = settings.Bind("Rendering", "DynamicResolution", false,
                "Enable automatic GPU-timing-based resolution adjustment.");
            dynamicMinimum = settings.Bind("Rendering", "DynamicResolutionMinScale", 0.7f,
                new ConfigDescription("Minimum automatic resolution scale.",
                    new AcceptableValueRange<float>(0.5f, 1.5f)));
            dynamicTargetFps = settings.Bind("Rendering", "DynamicResolutionTargetFPS", 80f,
                new ConfigDescription("Dynamic-resolution performance target.",
                    new AcceptableValueRange<float>(45f, 144f)));
            dotEnabled = settings.Bind("Crosshair", "Enabled", true, "Show the VR crosshair.");
            dotDistance = settings.Bind("Crosshair", "Distance", 1.08f,
                new ConfigDescription("Crosshair distance in metres.",
                    new AcceptableValueRange<float>(0.25f, 10f)));
            dotSize = settings.Bind("Crosshair", "Size", 0.011f,
                new ConfigDescription("World-space crosshair size.",
                    new AcceptableValueRange<float>(0.002f, 0.05f)));
            hudDistance = settings.Bind("HUD", "Distance", 1000f,
                new ConfigDescription("Virtual HUD distance in metres.",
                    new AcceptableValueRange<float>(0.5f, 1000f)));
            hudScale = settings.Bind("HUD", "Scale", 0.78f,
                new ConfigDescription("HUD angular scale.",
                    new AcceptableValueRange<float>(0.25f, 2f)));
            hudHeight = settings.Bind("HUD", "HeightOffset", 0f,
                new ConfigDescription("HUD vertical offset in metres.",
                    new AcceptableValueRange<float>(-2f, 2f)));
            menuDistance = settings.Bind("MainMenu", "Distance", 10f,
                new ConfigDescription("Fixed main-menu distance in metres.",
                    new AcceptableValueRange<float>(1f, 20f)));
            menuScale = settings.Bind("MainMenu", "Scale", 1f,
                new ConfigDescription("Fixed main-menu size multiplier.",
                    new AcceptableValueRange<float>(0.25f, 2f)));
            flatScreensOnlyForMainPauseAndFiles = settings.Bind("UI", "UIScreens", true,
                "When true, only the main menu, pause menu, and Files menu use a flat VR screen. Other interfaces use MFN's real camera position. Set false to use flat screens for every non-gameplay interface.");
            interactionCameraMovement = settings.Bind("Camera", "InteractionCameraMovement", false,
                "Allow MFN to move the VR camera during interaction menus. Cutscenes and toolbox views always retain their authored camera movement.");
            menuPointer = settings.Bind("UI", "MenuPointer", true,
                "Show a tracked right-hand pointer in inventory, toolboxes, and interaction menus. Right trigger selects or picks up/places items; right-stick click rotates held inventory items.");
            physicalWeaponSwitching = settings.Bind("Controls", "PhysicalWeaponSwitching", true,
                "When true, right grip switches weapons only at physical holsters: right hip cycles Wrench/Punctuation and behind the right shoulder cycles Rolodexer/Novelist/Conclusion. Normal right-grip weapon switching is disabled.");
            smoothTurning = settings.Bind("Turning", "SmoothTurning", false,
                "Enable continuous smooth turning. Set false to use snap turning.");
            snapAngle = settings.Bind("Turning", "SnapAngle", 30f,
                new ConfigDescription("Degrees per snap turn.",
                    new AcceptableValueRange<float>(15f, 90f)));
            smoothSpeed = settings.Bind("Turning", "SmoothTurnSpeed", 90f,
                new ConfigDescription("Smooth turning speed in degrees per second.",
                    new AcceptableValueRange<float>(30f, 360f)));
            activeResolutionScale = resolutionScale.Value;
            settings.Save();
            lastConfigWrite = File.GetLastWriteTimeUtc(path);
        }

        private static void MigrateLegacyConfigNames(string path)
        {
            if (!File.Exists(path))
                return;
            var text = File.ReadAllText(path);
            var migrated = text;
            var hasCrosshair = migrated.IndexOf("[Crosshair]",
                StringComparison.OrdinalIgnoreCase) >= 0;
            var hasLegacyAimingDot = migrated.IndexOf("[AimingDot]",
                StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hasCrosshair && hasLegacyAimingDot)
            {
                // Preserve existing values when upgrading an older configuration.
                migrated = migrated.Replace("[AimingDot]", "[Crosshair]");
            }
            else if (hasCrosshair && hasLegacyAimingDot)
            {
                // The stable patched camera core still creates this legacy section before
                // the config companion loads. Crosshair is the sole public setting now, so
                // remove the duplicate section on every startup without rebuilding the core.
                migrated = Regex.Replace(migrated,
                    @"(?ms)^\[AimingDot\]\s*\r?\n.*?(?=^\[|\z)", string.Empty);
            }
            migrated = migrated.Replace("FlatScreensOnlyForMainPauseAndFiles =",
                "UIScreens =");
            migrated = migrated.Replace("Mode = Smooth", "SmoothTurning = true");
            migrated = migrated.Replace("Mode = Snap", "SmoothTurning = false");
            migrated = migrated.Replace("# Acceptable values: Snap, Smooth\r\n", "");
            migrated = migrated.Replace("# Acceptable values: Snap, Smooth\n", "");
            if (!string.Equals(text, migrated, StringComparison.Ordinal))
                File.WriteAllText(path, migrated);
        }

        private void InstallPatches()
        {
            coreType = Type.GetType("MFNVR.MFNVRPlugin, MFNVR", true);
            var configureEyes = AccessTools.Method(coreType, "ConfigureEyeCameras");
            var updateTouch = AccessTools.Method(coreType, "UpdateTouchGamepad");
            if (configureEyes == null || updateTouch == null)
                throw new MissingMethodException("The installed MFNVR camera core is not the supported stable build.");

            var harmony = new Harmony(PluginGuid);
            harmony.Patch(configureEyes, transpiler: new HarmonyMethod(typeof(MFNVRConfigPlugin),
                nameof(ScaleEyeDimensions)));
            var playerUpdate = AccessTools.Method(typeof(Player), "Update");
            if (playerUpdate == null)
                throw new MissingMethodException("MFN's player update is unavailable.");
            harmony.Patch(playerUpdate,
                prefix: new HarmonyMethod(typeof(MFNVRConfigPlugin),
                    nameof(SuppressVanillaVrTurningPrefix)),
                postfix: new HarmonyMethod(typeof(MFNVRConfigPlugin),
                    nameof(ApplyConfiguredTurningPostfix)));
        }

        private void Update()
        {
            PollConfig();
            ApplyVisualSettings();
            UpdateDynamicResolution();
        }

        private void PollConfig()
        {
            if (Time.realtimeSinceStartup < nextConfigCheck)
                return;
            nextConfigCheck = Time.realtimeSinceStartup + 1f;
            var write = File.GetLastWriteTimeUtc(settings.ConfigFilePath);
            if (write <= lastConfigWrite)
                return;
            lastConfigWrite = write;
            settings.Reload();
            if (!dynamicResolution.Value)
                activeResolutionScale = resolutionScale.Value;
            else
                activeResolutionScale = Mathf.Clamp(activeResolutionScale,
                    Mathf.Min(dynamicMinimum.Value, resolutionScale.Value),
                    resolutionScale.Value);
            visualSettingsDirty = true;
            Logger.LogInfo("Reloaded MFNVR.cfg.");
        }

        private void ApplyVisualSettings()
        {
            if (!visualSettingsDirty)
                return;
            var bridge = Type.GetType("MFNVRBridge.RenderBridge, MFNVRRenderBridge", false);
            applyBridgeSettings = applyBridgeSettings ?? bridge?.GetMethod("ApplyUserSettings",
                BindingFlags.Static | BindingFlags.Public);
            applyUiScreenSettings = applyUiScreenSettings ?? bridge?.GetMethod(
                "ApplyUiScreenSettings", BindingFlags.Static | BindingFlags.Public);
            applyInteractionCameraSettings = applyInteractionCameraSettings ??
                bridge?.GetMethod("ApplyInteractionCameraSettings",
                    BindingFlags.Static | BindingFlags.Public);
            applyMenuPointerSettings = applyMenuPointerSettings ?? bridge?.GetMethod(
                "ApplyMenuPointerSettings", BindingFlags.Static | BindingFlags.Public);
            applyPhysicalWeaponSwitchingSettings = applyPhysicalWeaponSwitchingSettings ??
                bridge?.GetMethod("ApplyPhysicalWeaponSwitchingSettings",
                    BindingFlags.Static | BindingFlags.Public);
            if (applyBridgeSettings == null || applyUiScreenSettings == null ||
                applyInteractionCameraSettings == null ||
                applyMenuPointerSettings == null ||
                applyPhysicalWeaponSwitchingSettings == null)
                return;
            applyBridgeSettings.Invoke(null, new object[]
            {
                dotEnabled.Value, dotDistance.Value, dotSize.Value,
                hudDistance.Value, hudScale.Value, hudHeight.Value,
                menuDistance.Value, menuScale.Value
            });
            applyUiScreenSettings.Invoke(null, new object[]
            {
                flatScreensOnlyForMainPauseAndFiles.Value
            });
            applyInteractionCameraSettings.Invoke(null, new object[]
            {
                interactionCameraMovement.Value
            });
            applyMenuPointerSettings.Invoke(null, new object[] { menuPointer.Value });
            applyPhysicalWeaponSwitchingSettings.Invoke(null, new object[]
            {
                physicalWeaponSwitching.Value
            });
            visualSettingsDirty = false;
        }

        private void UpdateDynamicResolution()
        {
            if (!dynamicResolution.Value)
                return;
            var currentFrameMilliseconds = Time.unscaledDeltaTime * 1000f;
            if (currentFrameMilliseconds > 0.1f && currentFrameMilliseconds < 100f)
            {
                smoothedFrameMilliseconds = smoothedFrameMilliseconds <= 0f
                    ? currentFrameMilliseconds
                    : Mathf.Lerp(smoothedFrameMilliseconds, currentFrameMilliseconds, 0.05f);
            }
            FrameTimingManager.CaptureFrameTimings();
            if (Time.realtimeSinceStartup < nextDynamicCheck)
                return;
            nextDynamicCheck = Time.realtimeSinceStartup + 1.5f;
            double average = 0;
            var samples = 0;
            if (!FrameTimingManager.IsFeatureEnabled())
            {
                if (!timingWarningLogged)
                {
                    timingWarningLogged = true;
                    Logger.LogWarning("GPU frame timing is unavailable; dynamic resolution is using frame time.");
                }
            }
            else
            {
                var count = FrameTimingManager.GetLatestTimings((uint)frameTimings.Length, frameTimings);
                double total = 0;
                for (var index = 0; index < count; index++)
                {
                    var timing = frameTimings[index].gpuFrameTime;
                    if (timing <= 0.01 || double.IsNaN(timing) || double.IsInfinity(timing))
                        continue;
                    total += timing;
                    samples++;
                }
                if (samples > 0)
                    average = total / samples;
            }
            if (samples == 0)
            {
                if (smoothedFrameMilliseconds <= 0f)
                    return;
                average = smoothedFrameMilliseconds;
                if (!timingFallbackLogged)
                {
                    timingFallbackLogged = true;
                    Logger.LogInfo("Dynamic resolution is using smoothed frame time until GPU samples are available.");
                }
            }
            var target = 1000.0 / dynamicTargetFps.Value;
            var maximum = resolutionScale.Value;
            var minimum = Mathf.Min(dynamicMinimum.Value, maximum);
            var adjusted = activeResolutionScale;
            if (average > target * 1.06)
            {
                // A headset held at half refresh needs a meaningful reduction to escape
                // reprojection. Step down promptly, but avoid a large one-frame quality jump.
                var desired = activeResolutionScale * Mathf.Sqrt((float)(target / average)) * 0.96f;
                adjusted = Mathf.Max(desired, activeResolutionScale - 0.1f);
            }
            else if (average < target * 0.78)
                adjusted += 0.025f;
            adjusted = Mathf.Round(Mathf.Clamp(adjusted, minimum, maximum) * 40f) / 40f;
            if (Mathf.Abs(adjusted - activeResolutionScale) < 0.001f)
                return;
            activeResolutionScale = adjusted;
            var timingKind = samples > 0 ? "GPU" : "frame";
            Logger.LogInfo($"Dynamic resolution: {activeResolutionScale:0.###}x ({timingKind} {average:0.0} ms).");
        }

        private static IEnumerable<CodeInstruction> ScaleEyeDimensions(
            IEnumerable<CodeInstruction> instructions)
        {
            var scaledWidth = AccessTools.Method(typeof(MFNVRConfigPlugin), nameof(GetScaledEyeWidth));
            var scaledHeight = AccessTools.Method(typeof(MFNVRConfigPlugin), nameof(GetScaledEyeHeight));
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == coreType)
                {
                    if (method.Name == "MFN_GetEyeWidth")
                        instruction.operand = scaledWidth;
                    else if (method.Name == "MFN_GetEyeHeight")
                        instruction.operand = scaledHeight;
                }
                yield return instruction;
            }
        }

        public static int GetScaledEyeWidth()
        {
            return ScaleDimension(MFN_GetEyeWidth());
        }

        public static int GetScaledEyeHeight()
        {
            return ScaleDimension(MFN_GetEyeHeight());
        }

        private static int ScaleDimension(int recommended)
        {
            if (recommended <= 0)
                return recommended;
            return Math.Max(320, Mathf.RoundToInt(recommended * activeResolutionScale)) & ~1;
        }

        private static void SuppressVanillaVrTurningPrefix(Player __instance)
        {
            if (instance == null || __instance == null || __instance != Player.current ||
                __instance.movementControls == null)
                return;
            // MFN reads this action directly inside Player.Update and otherwise applies
            // continuous camera rotation regardless of MFNVR's selected mode. Disable
            // only horizontal gamepad look; mouse look and every other action remain live.
            var horizontalLook = __instance.movementControls.Movement.LookHorizontalGamepad;
            if (horizontalLook.enabled)
                horizontalLook.Disable();
        }

        private static void ApplyConfiguredTurningPostfix(Player __instance)
        {
            if (instance == null)
                return;
            try
            {
                instance.TickConfiguredTurning(__instance);
            }
            catch (Exception exception)
            {
                if (Time.frameCount >= instance.nextTurningWarningFrame)
                {
                    instance.nextTurningWarningFrame = Time.frameCount + 240;
                    instance.Logger.LogWarning($"Configured turning was skipped safely: {exception.Message}");
                }
            }
        }

        private void TickConfiguredTurning(Player player)
        {
            if (!CanTurn(player))
            {
                snapLatched = false;
                return;
            }
            if (MFN_GetControllerInput(1, out var x, out var y, out _, out _, out _, out _, out _, out _) == 0)
                return;
            var stick = ApplyDeadzone(new Vector2(x, y));
            if (smoothTurning.Value)
            {
                snapLatched = false;
                if (Mathf.Abs(stick.x) > 0.001f)
                    player.RotateCamera(stick.x * smoothSpeed.Value * Time.deltaTime, 0f);
                return;
            }
            if (Mathf.Abs(stick.x) <= 0.35f)
            {
                snapLatched = false;
                return;
            }
            if (!snapLatched && Mathf.Abs(stick.x) >= 0.70f)
            {
                player.RotateCamera(Mathf.Sign(stick.x) * snapAngle.Value, 0f);
                snapLatched = true;
            }
        }

        private bool CanTurn(Player player)
        {
            if (player == null || movementEnabledField == null || hardDeactivateField == null ||
                neckHorizontalField == null || neckHorizontalField.GetValue(player) as Transform == null)
                return false;
            if (!(bool)movementEnabledField.GetValue(player) || (bool)hardDeactivateField.GetValue(player))
                return false;
            return !(rotating180Field != null && (bool)rotating180Field.GetValue(player)) &&
                   !(rotatingRightField != null && (bool)rotatingRightField.GetValue(player)) &&
                   !(rotatingLeftField != null && (bool)rotatingLeftField.GetValue(player));
        }

        private static Vector2 ApplyDeadzone(Vector2 value)
        {
            const float deadzone = 0.18f;
            var magnitude = value.magnitude;
            if (magnitude <= deadzone)
                return Vector2.zero;
            return value.normalized * Mathf.Clamp01((magnitude - deadzone) / (1f - deadzone));
        }

        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_GetEyeWidth();

        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_GetEyeHeight();

        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_GetControllerInput(int hand, out float stickX, out float stickY,
            out float trigger, out float squeeze, out int primary, out int secondary,
            out int stickClick, out int menu);
    }
}
