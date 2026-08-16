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
using UnityEngine.InputSystem;
using UnityEngine.UI;

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
        private static bool capturedSettingsMenuOpen;
        private bool capturedSettingsProviderRegistered;
        private int nextCapturedSettingsProviderAttemptFrame;
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
        private ConfigEntry<float> playerHeightOffset;
        private ConfigEntry<bool> autoRecalibrateHeight;
        private ConfigEntry<bool> menuPointer;
        private ConfigEntry<bool> physicalWeaponSwitching;
        private ConfigEntry<bool> smoothTurning;
        private ConfigEntry<float> snapAngle;
        private ConfigEntry<float> smoothSpeed;

        private MethodInfo applyBridgeSettings;
        private MethodInfo applyUiScreenSettings;
        private MethodInfo applyInteractionCameraSettings;
        private MethodInfo applyPlayerHeightSettings;
        private MethodInfo applyHeightCalibrationSettings;
        private MethodInfo applyMenuPointerSettings;
        private MethodInfo applyPhysicalWeaponSwitchingSettings;
        private MethodInfo applyLeftHandedSettings;
        private MethodInfo setSettingsMenuOpen;
        private MethodInfo toggleSettingsMenu;
        private MethodInfo tryGetSettingsMenuTracking;
        private MethodInfo consumeSettingsMenuToggleRequest;
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
        private readonly float[] settingsTracking = new float[15];
        private readonly List<VrSettingsOption> vrSettingsOptions =
            new List<VrSettingsOption>();
        private readonly List<VrSettingsTab> vrSettingsTabs =
            new List<VrSettingsTab>();
        private GameObject vrSettingsRoot;
        private RectTransform vrSettingsPanel;
        private GameObject vrSettingsPointerDot;
        private LineRenderer vrSettingsPointerLine;
        private Material vrSettingsPointerMaterial;
        private Font vrSettingsFont;
        private bool vrSettingsVisible;
        private float localSettingsGestureHoldStarted = -1f;
        private bool localSettingsGestureTriggered;
        private int vrSettingsCategory;
        private VrSettingsOption vrSettingsHoveredOption;
        private VrSettingsTab vrSettingsHoveredTab;
        private VrSettingsOption vrSettingsDraggingOption;
        private float vrSettingsDragValue;
        private RectTransform vrSettingsCloseButton;
        private Image vrSettingsCloseImage;
        private bool vrSettingsCloseHovered;

        private static readonly string[] VrSettingsCategories =
        {
            "Rendering", "Crosshair", "UI", "Camera & Turning", "Controls"
        };

        private readonly FieldInfo movementEnabledField = AccessTools.Field(typeof(Player), "movementControlsEnabled");
        private readonly FieldInfo hardDeactivateField = AccessTools.Field(typeof(Player), "hardDeactivate");
        private readonly FieldInfo neckHorizontalField = AccessTools.Field(typeof(Player), "neckHorizonal");
        private readonly FieldInfo rotating180Field = AccessTools.Field(typeof(Player), "rotatingCamera180");
        private readonly FieldInfo rotatingRightField = AccessTools.Field(typeof(Player), "rotatingCameraRight");
        private readonly FieldInfo rotatingLeftField = AccessTools.Field(typeof(Player), "rotatingCameraLeft");

        private void Awake()
        {
            instance = this;
            Cursor.visible = false;
            BindSettings();
            capturedSettingsProviderRegistered = RegisterCapturedSettingsProvider();
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
            playerHeightOffset = settings.Bind("Player", "HeightOffset", 0f,
                new ConfigDescription("Vertical player-height adjustment in metres. Positive values make the player taller and negative values make the player shorter.",
                    new AcceptableValueRange<float>(-5f, 5f)));
            autoRecalibrateHeight = settings.Bind("Player", "AutoRecalibrateHeight", false,
                "Automatically recalibrate the current headset height once whenever gameplay loads.");
            menuPointer = settings.Bind("UI", "MenuPointer", true,
                "Show a tracked dominant-hand pointer in inventory, toolboxes, and interaction menus. The dominant trigger selects or picks up/places items; dominant-stick click rotates held inventory items.");
            physicalWeaponSwitching = settings.Bind("Controls", "PhysicalWeaponSwitching", true,
                "When true, dominant grip switches weapons only at physical holsters: dominant hip cycles Wrench/Punctuation and behind the dominant shoulder cycles Rolodexer/Novelist/Conclusion. Normal grip weapon switching is disabled.");
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
            var onscreenCursorAwake = AccessTools.Method(typeof(OnscreenCursor),
                nameof(OnscreenCursor.Awake));
            var onscreenCursorVisible = AccessTools.Method(typeof(OnscreenCursor),
                nameof(OnscreenCursor.SetVisible));
            var onscreenCursorLateUpdate = AccessTools.Method(typeof(OnscreenCursor),
                "LateUpdate");
            if (onscreenCursorAwake != null)
                harmony.Patch(onscreenCursorAwake,
                    postfix: new HarmonyMethod(typeof(MFNVRConfigPlugin),
                        nameof(HideOnscreenCursorPostfix)));
            if (onscreenCursorVisible != null)
                harmony.Patch(onscreenCursorVisible,
                    prefix: new HarmonyMethod(typeof(MFNVRConfigPlugin),
                        nameof(SuppressOnscreenCursorPrefix)));
            if (onscreenCursorLateUpdate != null)
                harmony.Patch(onscreenCursorLateUpdate,
                    prefix: new HarmonyMethod(typeof(MFNVRConfigPlugin),
                        nameof(SuppressOnscreenCursorPrefix)));
        }

        private void Update()
        {
            if (!capturedSettingsProviderRegistered &&
                Time.frameCount >= nextCapturedSettingsProviderAttemptFrame)
            {
                nextCapturedSettingsProviderAttemptFrame = Time.frameCount + 30;
                capturedSettingsProviderRegistered = RegisterCapturedSettingsProvider();
            }
            PollConfig();
            ApplyVisualSettings();
            UpdateDynamicResolution();
            UpdateVrSettingsMenu();
        }

        private void OnDestroy()
        {
            if (vrSettingsVisible)
                SetVrSettingsVisible(false);
            if (vrSettingsRoot != null)
                Destroy(vrSettingsRoot);
            if (vrSettingsPointerDot != null)
                Destroy(vrSettingsPointerDot);
            if (vrSettingsPointerLine != null)
                Destroy(vrSettingsPointerLine.gameObject);
            if (vrSettingsPointerMaterial != null)
                Destroy(vrSettingsPointerMaterial);
        }

        private static void HideOnscreenCursorPostfix(OnscreenCursor __instance)
        {
            if (__instance != null)
                __instance.SetInvisible();
        }

        private static bool SuppressOnscreenCursorPrefix()
        {
            return false;
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
            var bridge = FindLoadedType("MFNVRBridge.RenderBridge");
            applyBridgeSettings = applyBridgeSettings ?? bridge?.GetMethod("ApplyUserSettings",
                BindingFlags.Static | BindingFlags.Public);
            applyUiScreenSettings = applyUiScreenSettings ?? bridge?.GetMethod(
                "ApplyUiScreenSettings", BindingFlags.Static | BindingFlags.Public);
            applyInteractionCameraSettings = applyInteractionCameraSettings ??
                bridge?.GetMethod("ApplyInteractionCameraSettings",
                    BindingFlags.Static | BindingFlags.Public);
            applyPlayerHeightSettings = applyPlayerHeightSettings ?? bridge?.GetMethod(
                "ApplyPlayerHeightSettings", BindingFlags.Static | BindingFlags.Public);
            applyHeightCalibrationSettings = applyHeightCalibrationSettings ?? bridge?.GetMethod(
                "ApplyHeightCalibrationSettings", BindingFlags.Static | BindingFlags.Public);
            applyMenuPointerSettings = applyMenuPointerSettings ?? bridge?.GetMethod(
                "ApplyMenuPointerSettings", BindingFlags.Static | BindingFlags.Public);
            applyPhysicalWeaponSwitchingSettings = applyPhysicalWeaponSwitchingSettings ??
                bridge?.GetMethod("ApplyPhysicalWeaponSwitchingSettings",
                    BindingFlags.Static | BindingFlags.Public);
            applyLeftHandedSettings = applyLeftHandedSettings ?? bridge?.GetMethod(
                "ApplyLeftHandedSettings", BindingFlags.Static | BindingFlags.Public);
            if (applyBridgeSettings == null || applyUiScreenSettings == null ||
                applyInteractionCameraSettings == null ||
                applyPlayerHeightSettings == null ||
                applyHeightCalibrationSettings == null ||
                applyMenuPointerSettings == null ||
                applyPhysicalWeaponSwitchingSettings == null ||
                applyLeftHandedSettings == null)
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
            applyPlayerHeightSettings.Invoke(null, new object[] { playerHeightOffset.Value });
            applyHeightCalibrationSettings.Invoke(null, new object[] { autoRecalibrateHeight.Value });
            applyMenuPointerSettings.Invoke(null, new object[] { menuPointer.Value });
            applyPhysicalWeaponSwitchingSettings.Invoke(null, new object[]
            {
                physicalWeaponSwitching.Value
            });
            // The implementation remains in the bridge for a future release, but the
            // unfinished mode is deliberately unavailable to players for now.
            applyLeftHandedSettings.Invoke(null, new object[] { false });
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

        public static bool GetVrSettingsMenuValues(float[] values)
        {
            var plugin = instance;
            if (plugin == null || values == null || values.Length < 21)
                return false;
            // The captured menu is also the user's live config editor. Always refresh
            // from the real BepInEx config file when the menu opens so hand edits made
            // between openings are represented exactly rather than using cached entries.
            plugin.settings.Reload();
            activeResolutionScale = plugin.dynamicResolution.Value
                ? Mathf.Clamp(activeResolutionScale,
                    Mathf.Min(plugin.dynamicMinimum.Value, plugin.resolutionScale.Value),
                    plugin.resolutionScale.Value)
                : plugin.resolutionScale.Value;
            plugin.visualSettingsDirty = true;
            plugin.ApplyVisualSettings();
            values[0] = plugin.resolutionScale.Value;
            values[1] = plugin.dynamicResolution.Value ? 1f : 0f;
            values[2] = plugin.dynamicMinimum.Value;
            values[3] = plugin.dynamicTargetFps.Value;
            values[4] = plugin.dotEnabled.Value ? 1f : 0f;
            values[5] = plugin.dotDistance.Value;
            values[6] = plugin.dotSize.Value;
            values[7] = plugin.hudDistance.Value;
            values[8] = plugin.hudScale.Value;
            values[9] = plugin.hudHeight.Value;
            values[10] = plugin.menuDistance.Value;
            values[11] = plugin.menuScale.Value;
            values[12] = plugin.flatScreensOnlyForMainPauseAndFiles.Value ? 1f : 0f;
            values[13] = plugin.menuPointer.Value ? 1f : 0f;
            values[14] = plugin.interactionCameraMovement.Value ? 1f : 0f;
            values[15] = plugin.smoothTurning.Value ? 1f : 0f;
            values[16] = plugin.snapAngle.Value;
            values[17] = plugin.smoothSpeed.Value;
            values[18] = plugin.physicalWeaponSwitching.Value ? 1f : 0f;
            values[19] = plugin.playerHeightOffset.Value;
            values[20] = plugin.autoRecalibrateHeight.Value ? 1f : 0f;
            return true;
        }

        public static bool SetVrSettingsMenuValue(int index, float value)
        {
            var plugin = instance;
            if (plugin == null)
                return false;
            ConfigEntryBase entry;
            switch (index)
            {
                case 0: entry = plugin.resolutionScale; entry.BoxedValue = Mathf.Clamp(value, 0.5f, 1.5f); break;
                case 1: entry = plugin.dynamicResolution; entry.BoxedValue = value >= 0.5f; break;
                case 2: entry = plugin.dynamicMinimum; entry.BoxedValue = Mathf.Clamp(value, 0.5f, 1.5f); break;
                case 3: entry = plugin.dynamicTargetFps; entry.BoxedValue = Mathf.Clamp(value, 45f, 144f); break;
                case 4: entry = plugin.dotEnabled; entry.BoxedValue = value >= 0.5f; break;
                case 5: entry = plugin.dotDistance; entry.BoxedValue = Mathf.Clamp(value, 0.25f, 10f); break;
                case 6: entry = plugin.dotSize; entry.BoxedValue = Mathf.Clamp(value, 0.002f, 0.05f); break;
                case 7: entry = plugin.hudDistance; entry.BoxedValue = Mathf.Clamp(value, 0.5f, 1000f); break;
                case 8: entry = plugin.hudScale; entry.BoxedValue = Mathf.Clamp(value, 0.25f, 2f); break;
                case 9: entry = plugin.hudHeight; entry.BoxedValue = Mathf.Clamp(value, -2f, 2f); break;
                case 10: entry = plugin.menuDistance; entry.BoxedValue = Mathf.Clamp(value, 1f, 20f); break;
                case 11: entry = plugin.menuScale; entry.BoxedValue = Mathf.Clamp(value, 0.25f, 2f); break;
                case 12: entry = plugin.flatScreensOnlyForMainPauseAndFiles; entry.BoxedValue = value >= 0.5f; break;
                case 13: entry = plugin.menuPointer; entry.BoxedValue = value >= 0.5f; break;
                case 14: entry = plugin.interactionCameraMovement; entry.BoxedValue = value >= 0.5f; break;
                case 15: entry = plugin.smoothTurning; entry.BoxedValue = value >= 0.5f; break;
                case 16: entry = plugin.snapAngle; entry.BoxedValue = Mathf.Clamp(value, 15f, 90f); break;
                case 17: entry = plugin.smoothSpeed; entry.BoxedValue = Mathf.Clamp(value, 30f, 360f); break;
                case 18: entry = plugin.physicalWeaponSwitching; entry.BoxedValue = value >= 0.5f; break;
                case 19: entry = plugin.playerHeightOffset; entry.BoxedValue = Mathf.Clamp(value, -5f, 5f); break;
                case 20: entry = plugin.autoRecalibrateHeight; entry.BoxedValue = value >= 0.5f; break;
                default: return false;
            }
            plugin.CommitExternalSettingsChange(entry);
            return true;
        }

        private bool RegisterCapturedSettingsProvider()
        {
            try
            {
                var bridge = FindLoadedType("MFNVRBridge.RenderBridge");
                var register = bridge?.GetMethod("RegisterSettingsMenuProvider",
                    BindingFlags.Static | BindingFlags.Public);
                if (register == null)
                {
                    return false;
                }
                register.Invoke(null, new object[]
                {
                    new Func<float[], bool>(GetVrSettingsMenuValues),
                    new Func<int, float, bool>(SetVrSettingsMenuValue),
                    new Action<bool>(OnCapturedSettingsVisibilityChanged)
                });
                Logger.LogInfo("Captured settings menu connected to live MFNVR.cfg values.");
                return true;
            }
            catch (Exception exception)
            {
                Logger.LogWarning("Could not connect the captured settings provider: " +
                                  exception.Message);
                return false;
            }
        }

        private static void OnCapturedSettingsVisibilityChanged(bool open)
        {
            capturedSettingsMenuOpen = open;
            if (!open && instance != null)
                instance.snapLatched = false;
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // Ignore assemblies that cannot enumerate types in Unity's loader.
                }
            }
            return null;
        }

        private void CommitExternalSettingsChange(ConfigEntryBase entry)
        {
            if (ReferenceEquals(entry, resolutionScale))
            {
                activeResolutionScale = dynamicResolution.Value
                    ? Mathf.Clamp(activeResolutionScale,
                        Mathf.Min(dynamicMinimum.Value, resolutionScale.Value),
                        resolutionScale.Value)
                    : resolutionScale.Value;
            }
            else if (ReferenceEquals(entry, dynamicResolution) &&
                     !dynamicResolution.Value)
                activeResolutionScale = resolutionScale.Value;
            settings.Save();
            lastConfigWrite = File.GetLastWriteTimeUtc(settings.ConfigFilePath);
            visualSettingsDirty = true;
            ApplyVisualSettings();
        }

        private void UpdateVrSettingsMenu()
        {
            var f4Pressed = Keyboard.current != null &&
                            Keyboard.current.f4Key.wasPressedThisFrame;
            // Some Unity 2021 menu scenes do not expose a Keyboard device through the
            // new Input System even though the legacy keyboard path is still live.
            try
            {
                f4Pressed |= Input.GetKeyDown(KeyCode.F4);
            }
            catch
            {
                // The project can be built with legacy input disabled; the Input System
                // check above remains the primary path in that configuration.
            }
            if (f4Pressed)
            {
                try
                {
                    var bridge = FindLoadedType("MFNVRBridge.RenderBridge");
                    toggleSettingsMenu = toggleSettingsMenu ?? bridge?.GetMethod(
                        "ToggleSettingsMenu", BindingFlags.Static | BindingFlags.Public);
                    toggleSettingsMenu?.Invoke(null, null);
                    Logger.LogInfo("F4 toggled the captured MFNVR settings screen.");
                }
                catch (Exception exception)
                {
                    Logger.LogWarning("F4 could not toggle MFNVR settings: " +
                                      exception.Message);
                }
            }
        }

        private bool UpdateLocalSettingsMenuGesture()
        {
            float x, y, trigger, grip;
            int primary, secondary, stickClick, menu;
            var leftHeld = MFN_GetControllerInput(0, out x, out y, out trigger,
                out grip, out primary, out secondary, out stickClick, out menu) != 0 &&
                stickClick != 0;
            if (!leftHeld)
            {
                foreach (var gamepad in Gamepad.all)
                {
                    if (gamepad != null && gamepad.added &&
                        gamepad.leftStickButton.isPressed)
                    {
                        leftHeld = true;
                        break;
                    }
                }
            }
            if (!leftHeld)
            {
                localSettingsGestureHoldStarted = -1f;
                localSettingsGestureTriggered = false;
                return false;
            }
            if (localSettingsGestureHoldStarted < 0f)
                localSettingsGestureHoldStarted = Time.realtimeSinceStartup;
            if (localSettingsGestureTriggered ||
                Time.realtimeSinceStartup - localSettingsGestureHoldStarted < 2f)
                return false;
            localSettingsGestureTriggered = true;
            Logger.LogInfo("Two-second left-stick VR Settings gesture completed.");
            return true;
        }

        private bool ConsumeBridgeSettingsMenuToggleRequest()
        {
            try
            {
                var bridge = FindLoadedType("MFNVRBridge.RenderBridge");
                consumeSettingsMenuToggleRequest = consumeSettingsMenuToggleRequest ??
                    bridge?.GetMethod("ConsumeSettingsMenuToggleRequest",
                        BindingFlags.Static | BindingFlags.Public);
                return consumeSettingsMenuToggleRequest != null &&
                       consumeSettingsMenuToggleRequest.Invoke(null, null) is bool requested &&
                       requested;
            }
            catch (Exception exception)
            {
                Logger.LogWarning("Could not read MFNVR settings-menu gesture: " +
                                  exception.Message);
                return false;
            }
        }

        private void SetVrSettingsVisible(bool visible)
        {
            if (visible)
            {
                EnsureVrSettingsMenu();
                if (!TryReadSettingsTracking())
                {
                    Logger.LogWarning("MFNVR settings menu needs active VR tracking.");
                    return;
                }
                PositionVrSettingsMenu();
                vrSettingsRoot.SetActive(true);
                SetVrSettingsPointerVisible(true);
                vrSettingsVisible = true;
                RefreshVrSettingsVisibility();
            }
            else
            {
                vrSettingsVisible = false;
                vrSettingsDraggingOption = null;
                vrSettingsHoveredOption = null;
                vrSettingsHoveredTab = null;
                if (vrSettingsRoot != null)
                    vrSettingsRoot.SetActive(false);
                SetVrSettingsPointerVisible(false);
            }
            SetBridgeSettingsMenuOpen(visible && vrSettingsVisible);
        }

        private void SetBridgeSettingsMenuOpen(bool open)
        {
            try
            {
                var bridge = FindLoadedType("MFNVRBridge.RenderBridge");
                setSettingsMenuOpen = setSettingsMenuOpen ?? bridge?.GetMethod(
                    "SetSettingsMenuOpen", BindingFlags.Static | BindingFlags.Public);
                setSettingsMenuOpen?.Invoke(null, new object[] { open });
            }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not change MFNVR settings-menu input state: {exception.Message}");
            }
        }

        private bool TryReadSettingsTracking()
        {
            try
            {
                var bridge = FindLoadedType("MFNVRBridge.RenderBridge");
                tryGetSettingsMenuTracking = tryGetSettingsMenuTracking ?? bridge?.GetMethod(
                    "TryGetSettingsMenuTracking", BindingFlags.Static | BindingFlags.Public);
                return tryGetSettingsMenuTracking != null &&
                       tryGetSettingsMenuTracking.Invoke(null,
                           new object[] { settingsTracking }) is bool valid && valid;
            }
            catch
            {
                return false;
            }
        }

        private void PositionVrSettingsMenu()
        {
            var headPosition = new Vector3(settingsTracking[0], settingsTracking[1],
                settingsTracking[2]);
            var headRotation = new Quaternion(settingsTracking[3], settingsTracking[4],
                settingsTracking[5], settingsTracking[6]);
            var forward = headRotation * Vector3.forward;
            // This opaque world-space canvas is the settings screen. It is fixed once
            // when opened, just like MFNVR's main/pause screen, rather than following
            // the player's head every frame.
            vrSettingsPanel.SetPositionAndRotation(headPosition + forward * 1.8f,
                headRotation);
        }

        private void EnsureVrSettingsMenu()
        {
            if (vrSettingsRoot != null)
                return;
            vrSettingsFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            vrSettingsRoot = new GameObject("MFNVR Settings Menu",
                typeof(RectTransform), typeof(Canvas));
            vrSettingsPanel = vrSettingsRoot.GetComponent<RectTransform>();
            vrSettingsPanel.sizeDelta = new Vector2(1400f, 900f);
            vrSettingsPanel.localScale = Vector3.one * 0.001f;
            var canvas = vrSettingsRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32000;

            CreateVrImage("Background", vrSettingsPanel, Vector2.zero,
                new Vector2(1400f, 900f), new Color(0.035f, 0.045f, 0.065f, 0.98f));
            CreateVrImage("Header", vrSettingsPanel, new Vector2(0f, 402f),
                new Vector2(1400f, 96f), new Color(0.10f, 0.19f, 0.34f, 1f));
            CreateVrText("Title", vrSettingsPanel, "MFNVR SETTINGS",
                new Vector2(-420f, 402f), new Vector2(500f, 70f), 38,
                TextAnchor.MiddleLeft, Color.white);
            CreateVrText("Hint", vrSettingsPanel,
                "Point with the dominant controller  |  Dominant trigger selects  |  Hold left stick click for 2 seconds to close",
                new Vector2(100f, 402f), new Vector2(760f, 58f), 20,
                TextAnchor.MiddleLeft, new Color(0.78f, 0.86f, 1f, 1f));

            vrSettingsCloseButton = CreateVrImage("Close", vrSettingsPanel,
                new Vector2(650f, 402f), new Vector2(58f, 58f),
                new Color(0.35f, 0.12f, 0.14f, 1f)).rectTransform;
            vrSettingsCloseImage = vrSettingsCloseButton.GetComponent<Image>();
            CreateVrText("Close X", vrSettingsCloseButton, "X", Vector2.zero,
                new Vector2(58f, 58f), 28, TextAnchor.MiddleCenter, Color.white);

            var tabWidth = 252f;
            for (var index = 0; index < VrSettingsCategories.Length; index++)
            {
                var x = -520f + index * 260f;
                var image = CreateVrImage("Tab " + VrSettingsCategories[index],
                    vrSettingsPanel, new Vector2(x, 322f), new Vector2(tabWidth, 54f),
                    new Color(0.09f, 0.12f, 0.18f, 1f));
                CreateVrText("Tab Label", image.rectTransform,
                    VrSettingsCategories[index], Vector2.zero,
                    new Vector2(tabWidth - 10f, 50f), 21,
                    TextAnchor.MiddleCenter, Color.white);
                vrSettingsTabs.Add(new VrSettingsTab
                {
                    Category = index,
                    Rect = image.rectTransform,
                    Image = image
                });
            }

            AddVrSlider(0, "Resolution Scale", resolutionScale, 0.5f, 1.5f);
            AddVrToggle(0, "Dynamic Resolution", dynamicResolution);
            AddVrSlider(0, "Dynamic Minimum Scale", dynamicMinimum, 0.5f, 1.5f);
            AddVrSlider(0, "Dynamic Target FPS", dynamicTargetFps, 45f, 144f);

            AddVrToggle(1, "Crosshair Enabled", dotEnabled);
            AddVrSlider(1, "Crosshair Distance", dotDistance, 0.25f, 10f);
            AddVrSlider(1, "Crosshair Size", dotSize, 0.002f, 0.05f);

            AddVrSlider(2, "HUD Distance", hudDistance, 0.5f, 1000f, true);
            AddVrSlider(2, "HUD Scale", hudScale, 0.25f, 2f);
            AddVrSlider(2, "HUD Height Offset", hudHeight, -2f, 2f);
            AddVrSlider(2, "Menu Distance", menuDistance, 1f, 20f);
            AddVrSlider(2, "Menu Scale", menuScale, 0.25f, 2f);
            AddVrToggle(2, "UI Screens", flatScreensOnlyForMainPauseAndFiles);
            AddVrToggle(2, "Menu Pointer", menuPointer);

            AddVrToggle(3, "Interaction Camera Movement", interactionCameraMovement);
            AddVrToggle(3, "Smooth Turning", smoothTurning);
            AddVrSlider(3, "Snap Turn Angle", snapAngle, 15f, 90f);
            AddVrSlider(3, "Smooth Turn Speed", smoothSpeed, 30f, 360f);

            AddVrToggle(4, "Physical Weapon Switching", physicalWeaponSwitching);
            AddVrSlider(4, "Player Height", playerHeightOffset, -5f, 5f);
            AddVrToggle(4, "Auto Recalibrate Height", autoRecalibrateHeight);
            EnsureVrSettingsPointer();
            foreach (var child in vrSettingsRoot.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 31;
            DontDestroyOnLoad(vrSettingsRoot);
            vrSettingsRoot.SetActive(false);
        }

        private void AddVrToggle(int category, string label, ConfigEntry<bool> entry)
        {
            var option = CreateVrOption(category, label, entry, true, 0f, 1f, false);
            option.ToggleBox = CreateVrImage("Checkbox", option.Root,
                new Vector2(500f, 0f), new Vector2(48f, 48f),
                new Color(0.08f, 0.10f, 0.14f, 1f));
            option.ValueText = CreateVrText("Check", option.ToggleBox.rectTransform, "",
                Vector2.zero, new Vector2(44f, 44f), 30,
                TextAnchor.MiddleCenter, Color.white);
            RefreshVrOption(option, Convert.ToBoolean(entry.BoxedValue));
        }

        private void AddVrSlider(int category, string label, ConfigEntry<float> entry,
            float minimum, float maximum, bool logarithmic = false)
        {
            var option = CreateVrOption(category, label, entry, false, minimum, maximum,
                logarithmic);
            option.Slider = CreateVrImage("Slider", option.Root,
                new Vector2(150f, 0f), new Vector2(700f, 30f),
                new Color(0.06f, 0.075f, 0.11f, 1f)).rectTransform;
            option.Fill = CreateVrImage("Fill", option.Slider, Vector2.zero,
                new Vector2(1f, 22f), new Color(0.20f, 0.48f, 0.88f, 1f));
            option.Fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            option.Fill.rectTransform.anchoredPosition = new Vector2(-346f, 0f);
            option.ValueText = CreateVrText("Value", option.Root, "",
                new Vector2(565f, 0f), new Vector2(150f, 54f), 23,
                TextAnchor.MiddleRight, new Color(0.86f, 0.91f, 1f, 1f));
            RefreshVrOption(option, Convert.ToSingle(entry.BoxedValue));
        }

        private VrSettingsOption CreateVrOption(int category, string label,
            ConfigEntryBase entry, bool toggle, float minimum, float maximum,
            bool logarithmic)
        {
            var row = 0;
            foreach (var existing in vrSettingsOptions)
            {
                if (existing.Category == category)
                    row++;
            }
            var image = CreateVrImage("Option " + label, vrSettingsPanel,
                new Vector2(0f, 245f - row * 78f), new Vector2(1280f, 66f),
                new Color(0.055f, 0.068f, 0.095f, 0.96f));
            CreateVrText("Label", image.rectTransform, label,
                new Vector2(-430f, 0f), new Vector2(390f, 56f), 25,
                TextAnchor.MiddleLeft, Color.white);
            var option = new VrSettingsOption
            {
                Category = category,
                Entry = entry,
                IsToggle = toggle,
                Minimum = minimum,
                Maximum = maximum,
                Logarithmic = logarithmic,
                RootObject = image.gameObject,
                Root = image.rectTransform,
                Background = image
            };
            vrSettingsOptions.Add(option);
            return option;
        }

        private Image CreateVrImage(string name, Transform parent, Vector2 position,
            Vector2 size, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = obj.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text CreateVrText(string name, Transform parent, string value,
            Vector2 position, Vector2 size, int fontSize, TextAnchor alignment,
            Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Text));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = obj.GetComponent<Text>();
            text.font = vrSettingsFont;
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private void EnsureVrSettingsPointer()
        {
            if (vrSettingsPointerLine != null)
                return;
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            vrSettingsPointerMaterial = new Material(shader);
            vrSettingsPointerMaterial.color = new Color(0.18f, 0.82f, 1f, 1f);
            var lineObject = new GameObject("MFNVR Settings Pointer Ray");
            lineObject.layer = 31;
            vrSettingsPointerLine = lineObject.AddComponent<LineRenderer>();
            vrSettingsPointerLine.sharedMaterial = vrSettingsPointerMaterial;
            vrSettingsPointerLine.positionCount = 2;
            vrSettingsPointerLine.startWidth = 0.007f;
            vrSettingsPointerLine.endWidth = 0.004f;
            vrSettingsPointerLine.useWorldSpace = true;
            vrSettingsPointerLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            vrSettingsPointerLine.receiveShadows = false;
            vrSettingsPointerDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vrSettingsPointerDot.name = "MFNVR Settings Pointer Dot";
            vrSettingsPointerDot.layer = 31;
            vrSettingsPointerDot.transform.localScale = Vector3.one * 0.018f;
            var collider = vrSettingsPointerDot.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            var renderer = vrSettingsPointerDot.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = vrSettingsPointerMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            DontDestroyOnLoad(lineObject);
            DontDestroyOnLoad(vrSettingsPointerDot);
            SetVrSettingsPointerVisible(false);
        }

        private void SetVrSettingsPointerVisible(bool visible)
        {
            if (vrSettingsPointerLine != null)
                vrSettingsPointerLine.gameObject.SetActive(visible);
            if (vrSettingsPointerDot != null)
                vrSettingsPointerDot.SetActive(visible);
        }

        private void UpdateVrSettingsPointer(bool triggerPressed, bool triggerStarted)
        {
            var origin = new Vector3(settingsTracking[7], settingsTracking[8],
                settingsTracking[9]);
            var rotation = new Quaternion(settingsTracking[10], settingsTracking[11],
                settingsTracking[12], settingsTracking[13]);
            var direction = rotation * Vector3.forward;
            var plane = new Plane(vrSettingsPanel.forward, vrSettingsPanel.position);
            float distance;
            var hitPanel = plane.Raycast(new Ray(origin, direction), out distance) &&
                           distance > 0f && distance <= 5f;
            var point = hitPanel ? origin + direction * distance : origin + direction * 3f;
            vrSettingsPointerLine.SetPosition(0, origin);
            vrSettingsPointerLine.SetPosition(1, point);
            vrSettingsPointerDot.transform.position = point - direction * 0.003f;
            vrSettingsPointerDot.SetActive(hitPanel);

            vrSettingsHoveredOption = null;
            vrSettingsHoveredTab = null;
            vrSettingsCloseHovered = false;
            if (hitPanel)
            {
                foreach (var tab in vrSettingsTabs)
                {
                    if (ContainsWorldPoint(tab.Rect, point))
                    {
                        vrSettingsHoveredTab = tab;
                        break;
                    }
                }
                if (ContainsWorldPoint(vrSettingsCloseButton, point))
                    vrSettingsCloseHovered = true;
                foreach (var option in vrSettingsOptions)
                {
                    if (option.Category == vrSettingsCategory &&
                        option.RootObject.activeSelf && ContainsWorldPoint(option.Root, point))
                    {
                        vrSettingsHoveredOption = option;
                        break;
                    }
                }
            }

            if (triggerStarted)
            {
                if (vrSettingsCloseHovered)
                {
                    SetVrSettingsVisible(false);
                    return;
                }
                if (vrSettingsHoveredTab != null)
                {
                    vrSettingsCategory = vrSettingsHoveredTab.Category;
                    RefreshVrSettingsVisibility();
                }
                else if (vrSettingsHoveredOption != null)
                {
                    if (vrSettingsHoveredOption.IsToggle)
                    {
                        var value = !Convert.ToBoolean(
                            vrSettingsHoveredOption.Entry.BoxedValue);
                        vrSettingsHoveredOption.Entry.BoxedValue = value;
                        CommitVrSetting(vrSettingsHoveredOption);
                    }
                    else
                    {
                        vrSettingsDraggingOption = vrSettingsHoveredOption;
                        vrSettingsDragValue = GetVrSliderValue(
                            vrSettingsDraggingOption, point);
                        RefreshVrOption(vrSettingsDraggingOption, vrSettingsDragValue);
                    }
                }
            }

            if (vrSettingsDraggingOption != null)
            {
                if (triggerPressed && hitPanel)
                {
                    vrSettingsDragValue = GetVrSliderValue(
                        vrSettingsDraggingOption, point);
                    RefreshVrOption(vrSettingsDraggingOption, vrSettingsDragValue);
                }
                else if (!triggerPressed)
                {
                    vrSettingsDraggingOption.Entry.BoxedValue = vrSettingsDragValue;
                    CommitVrSetting(vrSettingsDraggingOption);
                    vrSettingsDraggingOption = null;
                }
            }
            UpdateVrSettingsColors();
        }

        private static bool ContainsWorldPoint(RectTransform rect, Vector3 point)
        {
            if (rect == null)
                return false;
            var local = rect.InverseTransformPoint(point);
            return rect.rect.Contains(new Vector2(local.x, local.y));
        }

        private float GetVrSliderValue(VrSettingsOption option, Vector3 worldPoint)
        {
            var local = option.Slider.InverseTransformPoint(worldPoint);
            var normalized = Mathf.InverseLerp(option.Slider.rect.xMin,
                option.Slider.rect.xMax, local.x);
            if (option.Logarithmic)
            {
                return Mathf.Exp(Mathf.Lerp(Mathf.Log(option.Minimum),
                    Mathf.Log(option.Maximum), normalized));
            }
            return Mathf.Lerp(option.Minimum, option.Maximum, normalized);
        }

        private void CommitVrSetting(VrSettingsOption option)
        {
            if (ReferenceEquals(option.Entry, resolutionScale))
            {
                if (!dynamicResolution.Value)
                    activeResolutionScale = resolutionScale.Value;
                else
                    activeResolutionScale = Mathf.Clamp(activeResolutionScale,
                        Mathf.Min(dynamicMinimum.Value, resolutionScale.Value),
                        resolutionScale.Value);
            }
            else if (ReferenceEquals(option.Entry, dynamicResolution) &&
                     !dynamicResolution.Value)
            {
                activeResolutionScale = resolutionScale.Value;
            }
            settings.Save();
            lastConfigWrite = File.GetLastWriteTimeUtc(settings.ConfigFilePath);
            visualSettingsDirty = true;
            RefreshVrOption(option, option.Entry.BoxedValue);
            ApplyVisualSettings();
        }

        private void RefreshVrSettingsVisibility()
        {
            foreach (var option in vrSettingsOptions)
            {
                option.RootObject.SetActive(option.Category == vrSettingsCategory);
                if (option.Category == vrSettingsCategory)
                    RefreshVrOption(option, option.Entry.BoxedValue);
            }
            UpdateVrSettingsColors();
        }

        private void RefreshVrOption(VrSettingsOption option, object value)
        {
            if (option.IsToggle)
            {
                var enabledValue = Convert.ToBoolean(value);
                option.ToggleBox.color = enabledValue
                    ? new Color(0.18f, 0.56f, 0.92f, 1f)
                    : new Color(0.08f, 0.10f, 0.14f, 1f);
                option.ValueText.text = enabledValue ? "X" : "";
                return;
            }
            var number = Mathf.Clamp(Convert.ToSingle(value), option.Minimum,
                option.Maximum);
            float normalized;
            if (option.Logarithmic)
            {
                normalized = Mathf.InverseLerp(Mathf.Log(option.Minimum),
                    Mathf.Log(option.Maximum), Mathf.Log(Mathf.Max(option.Minimum, number)));
            }
            else
            {
                normalized = Mathf.InverseLerp(option.Minimum, option.Maximum, number);
            }
            option.Fill.rectTransform.sizeDelta = new Vector2(692f * normalized, 22f);
            var span = option.Maximum - option.Minimum;
            option.ValueText.text = span > 50f ? number.ToString("0") :
                span < 0.1f ? number.ToString("0.000") : number.ToString("0.00");
        }

        private void UpdateVrSettingsColors()
        {
            foreach (var tab in vrSettingsTabs)
            {
                tab.Image.color = tab.Category == vrSettingsCategory
                    ? new Color(0.18f, 0.38f, 0.68f, 1f)
                    : ReferenceEquals(tab, vrSettingsHoveredTab)
                        ? new Color(0.15f, 0.22f, 0.34f, 1f)
                        : new Color(0.09f, 0.12f, 0.18f, 1f);
            }
            foreach (var option in vrSettingsOptions)
            {
                option.Background.color = ReferenceEquals(option, vrSettingsHoveredOption)
                    ? new Color(0.11f, 0.17f, 0.27f, 1f)
                    : new Color(0.055f, 0.068f, 0.095f, 0.96f);
            }
            if (vrSettingsCloseImage != null)
            {
                vrSettingsCloseImage.color = vrSettingsCloseHovered
                    ? new Color(0.65f, 0.18f, 0.20f, 1f)
                    : new Color(0.35f, 0.12f, 0.14f, 1f);
            }
        }

        private sealed class VrSettingsOption
        {
            public int Category;
            public ConfigEntryBase Entry;
            public bool IsToggle;
            public float Minimum;
            public float Maximum;
            public bool Logarithmic;
            public GameObject RootObject;
            public RectTransform Root;
            public Image Background;
            public Image ToggleBox;
            public RectTransform Slider;
            public Image Fill;
            public Text ValueText;
        }

        private sealed class VrSettingsTab
        {
            public int Category;
            public RectTransform Rect;
            public Image Image;
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
            if (vrSettingsVisible || capturedSettingsMenuOpen)
                return false;
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
