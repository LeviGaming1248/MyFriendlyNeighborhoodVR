using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System;
using System.Text;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.XR;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MFNVR
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class MFNVRPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mfnvr.prototype";
        public const string PluginName = "MFN VR Prototype";
        public const string PluginVersion = "0.4.1";

        private enum TurningMode
        {
            Snap,
            Smooth
        }

        private const int ReportsToWrite = 8;
        private static MFNVRPlugin instance;
        private ConfigFile userConfig;
        private ConfigEntry<float> resolutionScaleSetting;
        private ConfigEntry<bool> dynamicResolutionSetting;
        private ConfigEntry<float> dynamicResolutionMinScaleSetting;
        private ConfigEntry<float> dynamicResolutionTargetFpsSetting;
        private ConfigEntry<bool> aimingDotEnabledSetting;
        private ConfigEntry<float> aimingDotDistanceSetting;
        private ConfigEntry<float> aimingDotSizeSetting;
        private ConfigEntry<float> hudDistanceSetting;
        private ConfigEntry<float> hudScaleSetting;
        private ConfigEntry<float> hudHeightOffsetSetting;
        private ConfigEntry<float> mainMenuDistanceSetting;
        private ConfigEntry<float> mainMenuScaleSetting;
        private ConfigEntry<TurningMode> turningModeSetting;
        private ConfigEntry<float> snapAngleSetting;
        private ConfigEntry<float> smoothTurnSpeedSetting;
        private float activeResolutionScale = 1f;
        private DateTime configLastWriteUtc;
        private float nextConfigFileCheck;
        private bool bridgeSettingsApplied;
        private MethodInfo bridgeSettingsMethod;
        private bool snapTurnLatched;
        private readonly FrameTiming[] dynamicFrameTimings = new FrameTiming[30];
        private float nextDynamicResolutionCheck;
        private bool dynamicTimingUnavailableReported;
        private bool rendererEntryReported;
        private int nextPrefixErrorFrame;
        private bool nativeRenderEventQueued;
        private bool nativeRendererReported;
        private Camera leftEyeCamera;
        private Camera rightEyeCamera;
        private Camera leftHudCamera;
        private Camera rightHudCamera;
        private Camera leftHandsCamera;
        private Camera rightHandsCamera;
        private RenderTexture leftEyeTexture;
        private RenderTexture rightEyeTexture;
        private Camera gameplayCamera;
        private Camera gameplayHudCamera;
        private Camera captureAttachedTo;
        private CommandBuffer leftWorldCapture;
        private CommandBuffer rightWorldCapture;
        private Quaternion lastHeadRotation;
        private bool hasLastHeadRotation;
        private readonly FieldInfo rotating180Field = AccessTools.Field(typeof(Player), "rotatingCamera180");
        private readonly FieldInfo rotatingRightField = AccessTools.Field(typeof(Player), "rotatingCameraRight");
        private readonly FieldInfo rotatingLeftField = AccessTools.Field(typeof(Player), "rotatingCameraLeft");
        private readonly FieldInfo handsCameraField = AccessTools.Field(typeof(Player), "handsCamera");
        private Vector3 trackingOriginPosition;
        private Quaternion trackingOriginRotation;
        private bool hasTrackingOrigin;
        private bool cameraComponentsReported;
        private int flipDiagnosticFrames;
        private readonly FieldInfo neckHorizontalField = AccessTools.Field(typeof(Player), "neckHorizonal");
        private readonly FieldInfo cameraRotYField = AccessTools.Field(typeof(Player), "cameraRotY");
        private readonly FieldInfo movementControlsEnabledField = AccessTools.Field(typeof(Player), "movementControlsEnabled");
        private readonly FieldInfo hardDeactivateField = AccessTools.Field(typeof(Player), "hardDeactivate");
        private Quaternion movementBaseNeckRotation;
        private bool movementYawApplied;
        private Camera comfortRigSource;
        private Vector3 comfortRigCameraOffset;
        private Vector3 renderRigPosition;
        private Quaternion renderRigRotation;
        private bool useComfortRig;
        private bool hasCachedEyeViews;
        private Vector3 cachedLeftPosition;
        private Vector3 cachedRightPosition;
        private Quaternion cachedLeftRotation;
        private Quaternion cachedRightRotation;
        private Matrix4x4 cachedLeftProjection;
        private Matrix4x4 cachedRightProjection;
        private Gamepad touchGamepad;
        private int lastTouchInputFrame = -1;
        private int nextTouchGamepadInitFrame;
        private bool touchInputReported;
        private readonly FieldInfo equippedPlayerField = AccessTools.Field(typeof(EquippedManager), "myPlayer");
        private readonly FieldInfo modelGoesHereField = AccessTools.Field(typeof(EquippedManager), "modelGoesHere");
        private readonly FieldInfo wrenchHitSoundField = AccessTools.Field(typeof(EquippedManager), "wrenchHit");
        private EquippedManager motionEquippedManager;
        private Player motionPlayer;
        private Transform rightHandAnchor;
        private ItemInHand trackedItem;
        private Vector3 trackedItemGripOffset;
        private Quaternion trackedItemRotationOffset = Quaternion.identity;
        private Transform trackedRightWrist;
        private Transform trackedLeftHandRoot;
        private Transform trackedLeftWrist;
        private Quaternion trackedLeftWristRestRotation = Quaternion.identity;
        private ArmIkRig rightArmRig;
        private ArmIkRig leftArmRig;
        private ProceduralArmRig proceduralRightArm;
        private ProceduralArmRig proceduralLeftArm;
        private Vector3 currentRightGripLocalPosition;
        private Quaternion currentRightAimLocalRotation = Quaternion.identity;
        private Vector3 leftGripWorldPosition;
        private Quaternion leftGripWorldRotation = Quaternion.identity;
        private bool leftGripPoseValid;
        private bool leftGripPressed;
        private bool previousLeftGripPressed;
        private bool twoHandedGrip;
        private Quaternion twoHandRotationCorrection = Quaternion.identity;
        private float leftGripPoseLostTimer;
        private int nextMotionDiagnosticFrame;
        private int lastPrimaryMotionFrame = -1;
        private bool motionManagerReported;
        private Vector3 previousWrenchHeadPosition;
        private Vector3 previousWrenchHeadLocalPosition;
        private bool hasPreviousWrenchHead;
        private readonly HashSet<int> wrenchHitsThisSwing = new HashSet<int>();
        private readonly Dictionary<int, float> wrenchLastHitTimes = new Dictionary<int, float>();
        private float wrenchPoseElapsed;
        private int wrenchFastFrames;
        private bool rightAimValid;
        private Vector3 rightAimWorldPosition;
        private Quaternion rightAimWorldRotation;
        private Camera weaponAimCamera;
        private Vector3 savedWeaponAimCameraPosition;
        private Quaternion savedWeaponAimCameraRotation;
        private int weaponAimOverrideDepth;
        private Quaternion savedEquippedManagerRotation;

        private sealed class ArmIkRig
        {
            public Transform upperArm;
            public Transform lowerArm;
            public Transform hand;
            public float upperLength;
            public float lowerLength;
            public Quaternion handTargetOffset = Quaternion.identity;
            public Vector3 handGripLocal;
        }

        private sealed class ProceduralArmRig
        {
            public GameObject root;
            public Transform upperArm;
            public Transform lowerArm;
            public float upperLength = 0.32f;
            public float lowerLength = 0.30f;
        }

        private void Awake()
        {
            instance = this;
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Unity {Application.unityVersion}.");
            InitializeUserConfig();
            LoadNativeBridge();
            SceneManager.sceneLoaded += OnSceneLoaded;
            Camera.onPreCull += OnCameraPreCull;
            Camera.onPostRender += OnCameraPostRender;
            StartCoroutine(ReportRuntime());
            var playerUpdate = AccessTools.Method(typeof(Player), "Update");
            var prefix = typeof(MFNVRPlugin).GetMethod(nameof(OnPlayerUpdatePrefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            var postfix = typeof(MFNVRPlugin).GetMethod(nameof(OnPlayerUpdate),
                BindingFlags.Static | BindingFlags.NonPublic);
            var harmony = new Harmony(PluginGuid);
            harmony.Patch(playerUpdate, new HarmonyMethod(prefix), new HarmonyMethod(postfix));
            var equippedUpdate = AccessTools.Method(typeof(EquippedManager), "Update");
            var equippedPostfix = typeof(MFNVRPlugin).GetMethod(nameof(OnEquippedManagerUpdate),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (equippedUpdate != null)
                harmony.Patch(equippedUpdate, postfix: new HarmonyMethod(equippedPostfix));
            var weaponPrefix = typeof(MFNVRPlugin).GetMethod(nameof(OnWeaponAimPrefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            var weaponPostfix = typeof(MFNVRPlugin).GetMethod(nameof(OnWeaponAimPostfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var methodName in new[] { "FireProjectile", "FireShotgunProjectile", "FireFinalGunProjectile",
                         "ThrowProjectile", "ThrowGrenadeMiddle" })
            {
                var method = AccessTools.Method(typeof(EquippedManager), methodName);
                if (method != null)
                    harmony.Patch(method, new HarmonyMethod(weaponPrefix), new HarmonyMethod(weaponPostfix));
            }
            var flatMeleePrefix = typeof(MFNVRPlugin).GetMethod(nameof(OnFlatMeleePrefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var methodName in new[] { "StartMeleeCollision", "StartMeleeCollisionHard" })
            {
                var method = AccessTools.Method(typeof(EquippedManager), methodName);
                if (method != null)
                    harmony.Patch(method, prefix: new HarmonyMethod(flatMeleePrefix));
            }
            var wrenchSwing = AccessTools.Method(typeof(EquippedManager), "StartWrenchSwing");
            if (wrenchSwing != null)
                harmony.Patch(wrenchSwing, prefix: new HarmonyMethod(typeof(MFNVRPlugin).GetMethod(
                    nameof(OnFlatWrenchSwingPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
            Debug.Log("MFNVR: explicit Player.Update hook installed.");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Camera.onPreCull -= OnCameraPreCull;
            Camera.onPostRender -= OnCameraPostRender;
            GL.invertCulling = false;
            RemoveWorldCapture();
            if (touchGamepad != null && touchGamepad.added)
                InputSystem.RemoveDevice(touchGamepad);
            if (rightHandAnchor != null)
                Destroy(rightHandAnchor.gameObject);
            DestroyProceduralArm(ref proceduralRightArm);
            DestroyProceduralArm(ref proceduralLeftArm);
        }

        private void InitializeUserConfig()
        {
            var configPath = Path.Combine(Paths.ConfigPath, "MFNVR.cfg");
            userConfig = new ConfigFile(configPath, true);

            resolutionScaleSetting = userConfig.Bind("Rendering", "ResolutionScale", 1.0f,
                new ConfigDescription(
                    "Fixed per-eye resolution multiplier, and the maximum scale when DynamicResolution is enabled. Higher values improve clarity but cost GPU performance. Restart MFN after changing this setting.",
                    new AcceptableValueRange<float>(0.5f, 1.5f)));
            dynamicResolutionSetting = userConfig.Bind("Rendering", "DynamicResolution", false,
                "Automatically lower and restore eye resolution to maintain the target frame rate. Disabled by default.");
            dynamicResolutionMinScaleSetting = userConfig.Bind("Rendering", "DynamicResolutionMinScale", 0.70f,
                new ConfigDescription("Lowest resolution scale dynamic resolution is allowed to use.",
                    new AcceptableValueRange<float>(0.5f, 1.5f)));
            dynamicResolutionTargetFpsSetting = userConfig.Bind("Rendering", "DynamicResolutionTargetFPS", 80.0f,
                new ConfigDescription("Performance target used only when DynamicResolution is enabled.",
                    new AcceptableValueRange<float>(45f, 144f)));

            aimingDotEnabledSetting = userConfig.Bind("AimingDot", "Enabled", true,
                "Show the yellow world-space aiming dot for guns and grenades.");
            aimingDotDistanceSetting = userConfig.Bind("AimingDot", "Distance", 1.08f,
                new ConfigDescription("Distance in metres from the weapon muzzle to the aiming dot.",
                    new AcceptableValueRange<float>(0.25f, 10f)));
            aimingDotSizeSetting = userConfig.Bind("AimingDot", "Size", 0.011f,
                new ConfigDescription("World-space diameter scale of the aiming dot.",
                    new AcceptableValueRange<float>(0.002f, 0.05f)));

            hudDistanceSetting = userConfig.Bind("HUD", "Distance", 1000.0f,
                new ConfigDescription("Virtual HUD distance in metres. Larger values make the HUD feel farther away.",
                    new AcceptableValueRange<float>(0.5f, 1000f)));
            hudScaleSetting = userConfig.Bind("HUD", "Scale", 0.78f,
                new ConfigDescription("HUD angular scale. 0.78 is MFNVR's tested default.",
                    new AcceptableValueRange<float>(0.25f, 2f)));
            hudHeightOffsetSetting = userConfig.Bind("HUD", "HeightOffset", 0.0f,
                new ConfigDescription("Moves the HUD vertically in metres; positive values move it upward.",
                    new AcceptableValueRange<float>(-2f, 2f)));

            mainMenuDistanceSetting = userConfig.Bind("MainMenu", "Distance", 10.0f,
                new ConfigDescription("Distance in metres to the fixed main-menu screen.",
                    new AcceptableValueRange<float>(1f, 20f)));
            mainMenuScaleSetting = userConfig.Bind("MainMenu", "Scale", 1.0f,
                new ConfigDescription("Size multiplier for the fixed main-menu screen.",
                    new AcceptableValueRange<float>(0.25f, 2f)));

            turningModeSetting = userConfig.Bind("Turning", "Mode", TurningMode.Snap,
                "Turning style. Valid values are Snap and Smooth.");
            snapAngleSetting = userConfig.Bind("Turning", "SnapAngle", 30.0f,
                new ConfigDescription("Degrees rotated by each snap turn.",
                    new AcceptableValueRange<float>(15f, 90f)));
            smoothTurnSpeedSetting = userConfig.Bind("Turning", "SmoothTurnSpeed", 90.0f,
                new ConfigDescription("Maximum smooth-turn speed in degrees per second.",
                    new AcceptableValueRange<float>(30f, 360f)));

            activeResolutionScale = resolutionScaleSetting.Value;
            userConfig.Save();
            configLastWriteUtc = File.Exists(configPath)
                ? File.GetLastWriteTimeUtc(configPath)
                : DateTime.MinValue;
            Logger.LogInfo($"MFNVR settings loaded from {configPath}.");
        }

        private void PollUserConfig()
        {
            if (userConfig == null || Time.realtimeSinceStartup < nextConfigFileCheck)
                return;
            nextConfigFileCheck = Time.realtimeSinceStartup + 1f;
            try
            {
                var writeTime = File.GetLastWriteTimeUtc(userConfig.ConfigFilePath);
                if (writeTime <= configLastWriteUtc)
                    return;
                configLastWriteUtc = writeTime;
                userConfig.Reload();
                bridgeSettingsApplied = false;
                Logger.LogInfo("Reloaded MFNVR.cfg visual and turning settings.");
                if (Mathf.Abs(resolutionScaleSetting.Value - activeResolutionScale) > 0.0001f)
                    Logger.LogInfo("ResolutionScale changed; the new value will be used after restarting MFN.");
            }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not reload MFNVR.cfg: {exception.Message}");
            }
        }

        private void PushRenderBridgeSettings()
        {
            if (bridgeSettingsApplied || userConfig == null)
                return;
            try
            {
                if (bridgeSettingsMethod == null)
                {
                    var bridgeType = Type.GetType("MFNVRBridge.RenderBridge, MFNVRRenderBridge", false);
                    bridgeSettingsMethod = bridgeType?.GetMethod("ApplyUserSettings",
                        BindingFlags.Static | BindingFlags.Public);
                }
                if (bridgeSettingsMethod == null)
                    return;
                bridgeSettingsMethod.Invoke(null, new object[]
                {
                    aimingDotEnabledSetting.Value,
                    aimingDotDistanceSetting.Value,
                    aimingDotSizeSetting.Value,
                    hudDistanceSetting.Value,
                    hudScaleSetting.Value,
                    hudHeightOffsetSetting.Value,
                    mainMenuDistanceSetting.Value,
                    mainMenuScaleSetting.Value
                });
                bridgeSettingsApplied = true;
            }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not apply MFNVR visual settings: {exception.Message}");
            }
        }

        private void QueueNativeRenderer(Player player)
        {
            if (!rendererEntryReported)
            {
                rendererEntryReported = true;
                Logger.LogInfo("Player update reached the native VR renderer.");
            }
            try
            {
                ConfigureEyeCameras(player);
            }
            catch (Exception exception)
            {
                Logger.LogError($"VR camera setup failed: {exception}");
                return;
            }
            // Head pose is intentionally not applied until true stereo rendering is stable.

            try
            {
                if (leftEyeTexture != null && rightEyeTexture != null)
                    MFN_SetSourceTextures(leftEyeTexture.GetNativeTexturePtr(), rightEyeTexture.GetNativeTexturePtr());
                GL.IssuePluginEvent(MFN_GetRenderEvent(), 1);
                if (++flipDiagnosticFrames == 180)
                    Logger.LogInfo($"Native GPU flip path (live): {MFN_GetFlipPath()}.");
                if (!nativeRenderEventQueued)
                {
                    nativeRenderEventQueued = true;
                    Debug.Log("MFNVR: queued native OpenXR render event.");
                }
            }
            catch (System.Exception exception)
            {
                nativeRenderEventQueued = true;
                Logger.LogWarning($"Could not queue the native renderer: {exception.GetType().Name}: {exception.Message}");
                return;
            }

            if (!nativeRendererReported)
            {
                nativeRendererReported = true;
                try
                {
                    var message = new StringBuilder(512);
                    MFN_GetStatus(message, message.Capacity);
                    Logger.LogInfo($"Native renderer: {message}");
                    Debug.Log($"MFNVR: native renderer status: {message}");
                }
                catch (System.Exception exception)
                {
                    Logger.LogWarning($"Could not read native renderer status: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        private static void OnPlayerUpdatePrefix(Player __instance)
        {
            if (instance == null)
                return;
            try
            {
                instance.PollUserConfig();
                instance.PushRenderBridgeSettings();
            }
            catch (Exception exception)
            {
                instance.ReportPrefixFailure("configuration", exception);
            }
            try
            {
                instance.UpdateTouchGamepad(__instance);
            }
            catch (Exception exception)
            {
                instance.ReportPrefixFailure("controller/turning", exception);
            }
            try
            {
                instance.ApplyMovementHeadYaw(__instance);
            }
            catch (Exception exception)
            {
                instance.ReportPrefixFailure("head-directed movement", exception);
            }
        }

        private void ReportPrefixFailure(string subsystem, Exception exception)
        {
            if (Time.frameCount < nextPrefixErrorFrame)
                return;
            nextPrefixErrorFrame = Time.frameCount + 240;
            Logger.LogError($"MFNVR {subsystem} prefix failed without blocking VR rendering: {exception}");
        }

        private void UpdateTouchGamepad(Player player)
        {
            EnsureTouchGamepad();
            if (touchGamepad == null || !touchGamepad.added || lastTouchInputFrame == Time.frameCount)
                return;
            lastTouchInputFrame = Time.frameCount;
            if (MFN_GetControllerInput(0, out var leftX, out var leftY, out var leftTrigger,
                    out var leftGrip, out var x, out var y, out var leftStickClick, out var menu) == 0 ||
                MFN_GetControllerInput(1, out var rightX, out var rightY, out var rightTrigger,
                    out var rightGrip, out var a, out var b, out var rightStickClick, out _) == 0)
                return;

            if (!touchInputReported)
            {
                touchInputReported = true;
                Logger.LogInfo("OpenXR Touch input is feeding the virtual Xbox gamepad.");
            }

            var leftStick = ApplyStickDeadzone(new Vector2(leftX, leftY));
            var rightStick = ApplyStickDeadzone(new Vector2(rightX, rightY));
            ApplyConfiguredTurning(player, ref rightStick);
            var state = new GamepadState
            {
                leftStick = leftStick,
                rightStick = rightStick,
                leftTrigger = ApplyTriggerDeadzone(leftTrigger),
                rightTrigger = ApplyTriggerDeadzone(rightTrigger)
            };
            state = state.WithButton(GamepadButton.West, x != 0)
                         .WithButton(GamepadButton.North, y != 0)
                         .WithButton(GamepadButton.South, a != 0)
                         .WithButton(GamepadButton.East, b != 0)
                         .WithButton(GamepadButton.LeftShoulder, false)
                         .WithButton(GamepadButton.RightShoulder, rightGrip > 0.55f)
                         .WithButton(GamepadButton.LeftStick, leftStickClick != 0)
                         .WithButton(GamepadButton.RightStick, rightStickClick != 0)
                         .WithButton(GamepadButton.Start, menu != 0);
            leftGripPressed = leftGrip > 0.55f;
            InputSystem.QueueStateEvent(touchGamepad, state);
        }

        private void ApplyConfiguredTurning(Player player, ref Vector2 rightStick)
        {
            if (player == null || turningModeSetting == null ||
                movementControlsEnabledField == null || hardDeactivateField == null ||
                neckHorizontalField == null || neckHorizontalField.GetValue(player) as Transform == null ||
                !(bool)movementControlsEnabledField.GetValue(player) ||
                (bool)hardDeactivateField.GetValue(player) || IsScriptedCameraMove(player))
            {
                snapTurnLatched = false;
                return;
            }

            var horizontal = rightStick.x;
            rightStick.x = 0f;
            if (turningModeSetting.Value == TurningMode.Smooth)
            {
                snapTurnLatched = false;
                if (Mathf.Abs(horizontal) > 0.001f)
                    player.RotateCamera(horizontal * smoothTurnSpeedSetting.Value * Time.deltaTime, 0f);
                return;
            }

            if (Mathf.Abs(horizontal) <= 0.35f)
            {
                snapTurnLatched = false;
                return;
            }
            if (!snapTurnLatched && Mathf.Abs(horizontal) >= 0.70f)
            {
                player.RotateCamera(Mathf.Sign(horizontal) * snapAngleSetting.Value, 0f);
                snapTurnLatched = true;
            }
        }

        private void EnsureTouchGamepad()
        {
            if (touchGamepad != null && touchGamepad.added)
                return;
            if (Time.frameCount < nextTouchGamepadInitFrame)
                return;
            nextTouchGamepadInitFrame = Time.frameCount + 120;
            try
            {
                // BepInEx Awake runs before InputSystem has registered its built-in layouts in
                // this game. Creating by layout name here, during Player.Update, is reliable.
                touchGamepad = InputSystem.AddDevice("Gamepad") as Gamepad;
                if (touchGamepad != null)
                    Logger.LogInfo($"Virtual Xbox controller created for Rift Touch: {touchGamepad.name}.");
                else
                    Logger.LogWarning("Unity's Gamepad layout is not ready; will retry.");
            }
            catch (System.Exception exception)
            {
                Logger.LogWarning($"Virtual Xbox controller creation will retry: {exception.Message}");
            }
        }

        private static Vector2 ApplyStickDeadzone(Vector2 value)
        {
            const float deadzone = 0.18f;
            var magnitude = value.magnitude;
            if (magnitude <= deadzone)
                return Vector2.zero;
            return value.normalized * Mathf.Clamp01((magnitude - deadzone) / (1f - deadzone));
        }

        private static float ApplyTriggerDeadzone(float value)
        {
            return value < 0.04f ? 0f : Mathf.Clamp01((value - 0.04f) / 0.96f);
        }

        private static void OnPlayerUpdate(Player __instance)
        {
            if (instance == null)
                return;
            instance.RestoreMovementNeck();

            // VR rendering always gets first priority. Motion controls run afterward and are
            // exception-isolated so a bad weapon prefab can never stop headset submission.
            instance.QueueNativeRenderer(__instance);
            instance.motionPlayer = __instance;
            instance.TryTickMotionControls(false);
        }

        private static void OnEquippedManagerUpdate(EquippedManager __instance)
        {
            if (instance != null)
                instance.motionEquippedManager = __instance;
        }

        private void LateUpdate()
        {
            TryTickMotionControls(true);
        }

        private void TryTickMotionControls(bool finalAnimationPass)
        {
            try
            {
                TickMotionControls(finalAnimationPass);
            }
            catch (Exception exception)
            {
                rightAimValid = false;
                if (Time.frameCount >= nextMotionDiagnosticFrame)
                {
                    nextMotionDiagnosticFrame = Time.frameCount + 240;
                    Logger.LogError($"Motion subsystem was isolated from VR rendering: {exception}");
                }
            }
        }

        private void TickMotionControls(bool finalAnimationPass)
        {
            if (!finalAnimationPass && lastPrimaryMotionFrame == Time.frameCount)
                return;
            if (!finalAnimationPass)
                lastPrimaryMotionFrame = Time.frameCount;

            // Reacquire from the known, actively-rendered player rather than relying on
            // EquippedManager.Update, which MFN disables in several weapon/gameplay states.
            var player = motionPlayer != null ? motionPlayer : Player.current;
            if (motionEquippedManager == null && player != null)
            {
                try
                {
                    motionEquippedManager = player.GetEquipManager();
                }
                catch (Exception exception)
                {
                    if (Time.frameCount >= nextMotionDiagnosticFrame)
                    {
                        nextMotionDiagnosticFrame = Time.frameCount + 240;
                        Logger.LogError($"Unable to acquire MFN equipped manager: {exception}");
                    }
                }
            }

            if (motionEquippedManager == null)
            {
                if (Time.frameCount >= nextMotionDiagnosticFrame)
                {
                    nextMotionDiagnosticFrame = Time.frameCount + 240;
                    Logger.LogWarning($"Motion waiting for equipped manager; player={(player == null ? "missing" : "ready")}.");
                }
                return;
            }
            if (!motionManagerReported)
            {
                motionManagerReported = true;
                Logger.LogInfo($"Motion controls bound to {GetPath(motionEquippedManager.transform)}.");
            }

            if (motionEquippedManager != null)
            {
                try
                {
                    ApplyRightHandMotion(motionEquippedManager);
                }
                catch (Exception exception)
                {
                    rightAimValid = false;
                    if (Time.frameCount >= nextMotionDiagnosticFrame)
                    {
                        nextMotionDiagnosticFrame = Time.frameCount + 240;
                        Logger.LogError($"Weapon motion update failed without affecting VR rendering: {exception}");
                    }
                }
            }
            if (trackedItem == null || rightHandAnchor == null || trackedItem.transform.parent != rightHandAnchor)
                return;
            // Weapon prefab animators may contain root curves. The grip itself must remain rigidly
            // owned by the controller; only child transforms are allowed to animate.
            trackedItem.transform.localPosition = trackedItemGripOffset;
            trackedItem.transform.localRotation = trackedItemRotationOffset;
            if (trackedRightWrist != null)
                trackedItem.transform.position += rightHandAnchor.position - trackedRightWrist.position;
            ApplyTrackedArms();
            if (finalAnimationPass && rightAimValid && motionEquippedManager != null)
                UpdatePhysicalWrench(motionEquippedManager, currentRightGripLocalPosition,
                    currentRightAimLocalRotation);
        }

        private static void OnWeaponAimPrefix()
        {
            instance?.BeginWeaponAimOverride();
        }

        private static void OnWeaponAimPostfix(MethodBase __originalMethod)
        {
            if (instance == null)
                return;
            instance.EndWeaponAimOverride();
            if (!instance.rightAimValid || __originalMethod == null)
                return;
            var name = __originalMethod.Name;
            var amplitude = name == "FireShotgunProjectile" ? 0.85f :
                            name == "FireFinalGunProjectile" ? 0.65f :
                            name == "FireProjectile" ? 0.45f : 0.25f;
            var duration = name == "FireShotgunProjectile" ? 0.12f : 0.055f;
            MFN_ApplyControllerHaptic(1, amplitude, duration, 0f);
        }

        private static bool OnFlatMeleePrefix(EquippedManager __instance)
        {
            return instance == null || !instance.rightAimValid ||
                   __instance.GetCurrentItem() != InventoryItem.Wrench;
        }

        private static bool OnFlatWrenchSwingPrefix(EquippedManager __instance)
        {
            return instance == null || !instance.rightAimValid ||
                   __instance.GetCurrentItem() != InventoryItem.Wrench;
        }

        private void ApplyRightHandMotion(EquippedManager equippedManager)
        {
            if (equippedManager == null)
                return;
            rightAimValid = false;
            // Never alter MFN's held-item hierarchy until stereo rendering and the body-relative
            // tracking origin are both live. Doing this from an early component Update can break
            // Player.Update before the native compositor gets its first frame.
            if (!hasTrackingOrigin || !useComfortRig ||
                MFN_GetControllerPose(1, 0, out var gripPx, out var gripPy, out var gripPz,
                    out var gripQx, out var gripQy, out var gripQz, out var gripQw) == 0 ||
                MFN_GetControllerPose(1, 1, out var aimPx, out var aimPy, out var aimPz,
                    out var aimQx, out var aimQy, out var aimQz, out var aimQw) == 0)
            {
                twoHandedGrip = false;
                leftGripPoseValid = false;
                return;
            }

            EnsureRightHandAnchor(equippedManager);
            if (rightHandAnchor == null)
                return;

            var gripPosition = new Vector3(gripPx, gripPy, -gripPz);
            var gripRotation = new Quaternion(-gripQx, -gripQy, gripQz, gripQw);
            var aimPosition = new Vector3(aimPx, aimPy, -aimPz);
            var aimRotation = new Quaternion(-aimQx, -aimQy, aimQz, aimQw);
            var inverseOrigin = Quaternion.Inverse(trackingOriginRotation);
            var gripLocalPosition = inverseOrigin * (gripPosition - trackingOriginPosition);
            var gripLocalRotation = inverseOrigin * gripRotation;
            var aimLocalPosition = inverseOrigin * (aimPosition - trackingOriginPosition);
            var aimLocalRotation = inverseOrigin * aimRotation;
            currentRightGripLocalPosition = gripLocalPosition;
            currentRightAimLocalRotation = aimLocalRotation;
            var gripWorldPosition = renderRigPosition + renderRigRotation * gripLocalPosition;
            rightAimWorldPosition = renderRigPosition + renderRigRotation * aimLocalPosition;
            rightAimWorldRotation = renderRigRotation * aimLocalRotation;
            UpdateLeftGripPose(inverseOrigin);
            rightHandAnchor.SetPositionAndRotation(gripWorldPosition, rightAimWorldRotation);
            ProcessTwoHandToggle(gripWorldPosition);
            if (twoHandedGrip && leftGripPoseValid)
            {
                var supportDirection = leftGripWorldPosition - gripWorldPosition;
                var supportDistance = supportDirection.magnitude;
                if (supportDistance >= 0.12f && supportDistance <= 1.1f)
                {
                    var forward = supportDirection / supportDistance;
                    var stableUp = Vector3.ProjectOnPlane(rightAimWorldRotation * Vector3.up, forward);
                    if (stableUp.sqrMagnitude < 0.001f)
                        stableUp = Vector3.ProjectOnPlane(rightAimWorldRotation * Vector3.right, forward);
                    rightAimWorldRotation = Quaternion.LookRotation(forward, stableUp.normalized) *
                                            twoHandRotationCorrection;
                    rightHandAnchor.rotation = rightAimWorldRotation;
                }
                else
                    ReleaseTwoHandGrip("Left support hand moved outside the stable grip range.");
            }
            if (twoHandedGrip && !leftGripPoseValid)
            {
                leftGripPoseLostTimer += Time.unscaledDeltaTime;
                if (leftGripPoseLostTimer > 0.25f)
                    ReleaseTwoHandGrip("Left controller tracking was lost.");
            }
            else
                leftGripPoseLostTimer = 0f;
            var modelMount = modelGoesHereField?.GetValue(equippedManager) as Transform;
            if (trackedItem != null && modelMount != null &&
                trackedItem.gameObject.activeSelf != modelMount.gameObject.activeInHierarchy)
                trackedItem.gameObject.SetActive(modelMount.gameObject.activeInHierarchy);
            rightAimValid = true;
            if (Time.frameCount >= nextMotionDiagnosticFrame)
            {
                nextMotionDiagnosticFrame = Time.frameCount + 240;
                Logger.LogInfo($"Right Touch 6DoF: localGrip={gripLocalPosition}, localAim={aimLocalPosition}, " +
                               $"anchorWorld={gripWorldPosition}.");
            }
        }

        private void UpdateLeftGripPose(Quaternion inverseOrigin)
        {
            leftGripPoseValid = MFN_GetControllerPose(0, 0, out var px, out var py, out var pz,
                out var qx, out var qy, out var qz, out var qw) != 0;
            if (!leftGripPoseValid)
                return;
            var position = new Vector3(px, py, -pz);
            var rotation = new Quaternion(-qx, -qy, qz, qw);
            var localPosition = inverseOrigin * (position - trackingOriginPosition);
            var localRotation = inverseOrigin * rotation;
            leftGripWorldPosition = renderRigPosition + renderRigRotation * localPosition;
            leftGripWorldRotation = renderRigRotation * localRotation;
        }

        private void ProcessTwoHandToggle(Vector3 rightGripWorldPosition)
        {
            var pressedThisFrame = leftGripPressed && !previousLeftGripPressed;
            previousLeftGripPressed = leftGripPressed;
            if (!pressedThisFrame)
                return;
            if (twoHandedGrip)
            {
                ReleaseTwoHandGrip("Left support hand released weapon.");
                return;
            }
            if (!leftGripPoseValid || trackedItem == null || trackedItem.firePosition == null ||
                motionEquippedManager == null || motionEquippedManager.GetCurrentItem() == InventoryItem.Wrench)
                return;

            var muzzle = trackedItem.firePosition.position;
            var segment = muzzle - rightGripWorldPosition;
            var segmentLengthSquared = segment.sqrMagnitude;
            var amount = segmentLengthSquared > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(leftGripWorldPosition - rightGripWorldPosition, segment) /
                                segmentLengthSquared)
                : 0f;
            var nearest = rightGripWorldPosition + segment * amount;
            if (Vector3.Distance(leftGripWorldPosition, nearest) > 0.22f)
                return;

            var supportDirection = leftGripWorldPosition - rightGripWorldPosition;
            if (supportDirection.sqrMagnitude <= 0.0144f)
                return;
            var attachForward = supportDirection.normalized;
            var attachUp = Vector3.ProjectOnPlane(rightAimWorldRotation * Vector3.up, attachForward);
            if (attachUp.sqrMagnitude < 0.001f)
                attachUp = Vector3.ProjectOnPlane(rightAimWorldRotation * Vector3.right, attachForward);
            var supportBasis = Quaternion.LookRotation(attachForward, attachUp.normalized);
            twoHandRotationCorrection = Quaternion.Inverse(supportBasis) * rightAimWorldRotation;
            twoHandedGrip = true;
            MFN_ApplyControllerHaptic(0, 0.45f, 0.065f, 0f);
            Logger.LogInfo("Left support hand attached to weapon.");
        }

        private void ReleaseTwoHandGrip(string reason)
        {
            if (!twoHandedGrip)
                return;
            twoHandedGrip = false;
            leftGripPoseLostTimer = 0f;
            MFN_ApplyControllerHaptic(0, 0.25f, 0.045f, 0f);
            Logger.LogInfo(reason);
        }

        private void ApplyTrackedArms()
        {
            if (rightArmRig != null && rightAimValid)
            {
                var shoulder = renderRigPosition + renderRigRotation * new Vector3(0.19f, -0.18f, 0.015f);
                var pole = shoulder + renderRigRotation * new Vector3(0.48f, -0.32f, 0.24f);
                SolveArmIk(rightArmRig, shoulder, pole, rightHandAnchor.position,
                    rightHandAnchor.rotation);
            }
            else if (proceduralRightArm != null && rightAimValid)
            {
                var shoulder = renderRigPosition + renderRigRotation * new Vector3(0.19f, -0.18f, 0.015f);
                var pole = shoulder + renderRigRotation * new Vector3(0.48f, -0.32f, 0.24f);
                UpdateProceduralArm(proceduralRightArm, shoulder, pole, rightHandAnchor.position);
            }

            if (!leftGripPoseValid)
                return;
            if (leftArmRig != null)
            {
                var shoulder = renderRigPosition + renderRigRotation * new Vector3(-0.19f, -0.18f, 0.015f);
                var pole = shoulder + renderRigRotation * new Vector3(-0.48f, -0.32f, 0.24f);
                SolveArmIk(leftArmRig, shoulder, pole, leftGripWorldPosition, leftGripWorldRotation);
                return;
            }
            if (proceduralLeftArm != null)
            {
                var shoulder = renderRigPosition + renderRigRotation * new Vector3(-0.19f, -0.18f, 0.015f);
                var pole = shoulder + renderRigRotation * new Vector3(-0.48f, -0.32f, 0.24f);
                UpdateProceduralArm(proceduralLeftArm, shoulder, pole, leftGripWorldPosition);
            }

            // Non-humanoid weapon prefabs still get exact left-controller hand placement.
            if (trackedLeftHandRoot == null)
                return;
            if (trackedLeftWrist != null)
            {
                trackedLeftHandRoot.rotation = leftGripWorldRotation *
                                               Quaternion.Inverse(trackedLeftWristRestRotation);
                trackedLeftHandRoot.position += leftGripWorldPosition - trackedLeftWrist.position;
            }
            else
                trackedLeftHandRoot.SetPositionAndRotation(leftGripWorldPosition, leftGripWorldRotation);
        }

        private static void SolveArmIk(ArmIkRig rig, Vector3 shoulder, Vector3 pole,
            Vector3 handTarget, Quaternion handRotation)
        {
            if (rig == null || rig.upperArm == null || rig.lowerArm == null || rig.hand == null)
                return;

            var desiredHandRotation = handRotation * rig.handTargetOffset;
            var desiredHandPosition = handTarget - desiredHandRotation * rig.handGripLocal;
            var upperLength = Mathf.Max(0.08f, rig.upperLength);
            var lowerLength = Mathf.Max(0.08f, rig.lowerLength);
            var maximumReach = Mathf.Max(0.12f, upperLength + lowerLength - 0.005f);
            var targetVector = desiredHandPosition - shoulder;
            var targetDistance = targetVector.magnitude;
            if (targetDistance < 0.001f)
                return;
            var targetDirection = targetVector / targetDistance;

            // Preserve exact controller contact even beyond the authored arm length by allowing
            // a small, natural shoulder reach instead of leaving the hand floating behind.
            if (targetDistance > maximumReach)
            {
                shoulder += targetDirection * (targetDistance - maximumReach);
                targetVector = desiredHandPosition - shoulder;
                targetDistance = targetVector.magnitude;
                targetDirection = targetVector / Mathf.Max(targetDistance, 0.001f);
            }

            var minimumReach = Mathf.Abs(upperLength - lowerLength) + 0.005f;
            var solvedDistance = Mathf.Clamp(targetDistance, minimumReach, maximumReach);
            var poleDirection = Vector3.ProjectOnPlane(pole - shoulder, targetDirection);
            if (poleDirection.sqrMagnitude < 0.0001f)
                poleDirection = Vector3.ProjectOnPlane(Vector3.down, targetDirection);
            poleDirection.Normalize();

            var along = (upperLength * upperLength - lowerLength * lowerLength +
                         solvedDistance * solvedDistance) / (2f * solvedDistance);
            var bend = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            var elbowTarget = shoulder + targetDirection * along + poleDirection * bend;

            rig.upperArm.position = shoulder;
            var currentUpperDirection = rig.lowerArm.position - rig.upperArm.position;
            var desiredUpperDirection = elbowTarget - shoulder;
            if (currentUpperDirection.sqrMagnitude > 0.000001f &&
                desiredUpperDirection.sqrMagnitude > 0.000001f)
                rig.upperArm.rotation = Quaternion.FromToRotation(currentUpperDirection,
                                            desiredUpperDirection) * rig.upperArm.rotation;

            var currentLowerDirection = rig.hand.position - rig.lowerArm.position;
            var desiredLowerDirection = desiredHandPosition - rig.lowerArm.position;
            if (currentLowerDirection.sqrMagnitude > 0.000001f &&
                desiredLowerDirection.sqrMagnitude > 0.000001f)
                rig.lowerArm.rotation = Quaternion.FromToRotation(currentLowerDirection,
                                            desiredLowerDirection) * rig.lowerArm.rotation;
            rig.hand.rotation = desiredHandRotation;
        }

        private static Vector3 SolveElbowPosition(Vector3 shoulder, Vector3 pole, Vector3 target,
            float upperLength, float lowerLength)
        {
            var toTarget = target - shoulder;
            var distance = toTarget.magnitude;
            if (distance < 0.001f)
                return shoulder + Vector3.down * upperLength;
            var direction = toTarget / distance;
            var maximumReach = upperLength + lowerLength - 0.005f;
            if (distance > maximumReach)
            {
                shoulder += direction * (distance - maximumReach);
                toTarget = target - shoulder;
                distance = toTarget.magnitude;
                direction = toTarget / Mathf.Max(distance, 0.001f);
            }
            var minimumReach = Mathf.Abs(upperLength - lowerLength) + 0.005f;
            distance = Mathf.Clamp(distance, minimumReach, maximumReach);
            var poleDirection = Vector3.ProjectOnPlane(pole - shoulder, direction);
            if (poleDirection.sqrMagnitude < 0.0001f)
                poleDirection = Vector3.ProjectOnPlane(Vector3.down, direction);
            poleDirection.Normalize();
            var along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) /
                        (2f * distance);
            var bend = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            return shoulder + direction * along + poleDirection * bend;
        }

        private static void UpdateProceduralArm(ProceduralArmRig rig, Vector3 shoulder,
            Vector3 pole, Vector3 hand)
        {
            if (rig == null || rig.root == null || rig.upperArm == null || rig.lowerArm == null)
                return;
            var direction = hand - shoulder;
            var maximumReach = rig.upperLength + rig.lowerLength - 0.005f;
            if (direction.magnitude > maximumReach)
                shoulder += direction.normalized * (direction.magnitude - maximumReach);
            var elbow = SolveElbowPosition(shoulder, pole, hand, rig.upperLength, rig.lowerLength);
            PlaceArmSegment(rig.upperArm, shoulder, elbow, 0.052f);
            PlaceArmSegment(rig.lowerArm, elbow, hand, 0.046f);
        }

        private static void PlaceArmSegment(Transform segment, Vector3 start, Vector3 end,
            float radius)
        {
            var delta = end - start;
            var length = delta.magnitude;
            if (length < 0.001f)
            {
                segment.gameObject.SetActive(false);
                return;
            }
            segment.gameObject.SetActive(true);
            segment.position = (start + end) * 0.5f;
            segment.rotation = Quaternion.FromToRotation(Vector3.up, delta / length);
            segment.localScale = new Vector3(radius, Mathf.Max(0.01f, length * 0.5f), radius);
        }

        private void EnsureRightHandAnchor(EquippedManager equippedManager)
        {
            motionEquippedManager = equippedManager;
            if (rightHandAnchor == null)
            {
                var anchorObject = new GameObject("MFN VR Direct Weapon Anchor");
                rightHandAnchor = anchorObject.transform;
                Logger.LogInfo("Created world-space direct weapon anchor.");
            }

            var currentItem = equippedManager.GetItemInHand();
            if (currentItem == trackedItem)
                return;
            trackedItem = currentItem;
            trackedRightWrist = null;
            trackedLeftHandRoot = null;
            trackedLeftWrist = null;
            rightArmRig = null;
            leftArmRig = null;
            DestroyProceduralArm(ref proceduralRightArm);
            DestroyProceduralArm(ref proceduralLeftArm);
            twoHandedGrip = false;
            previousLeftGripPressed = leftGripPressed;
            hasPreviousWrenchHead = false;
            wrenchPoseElapsed = 0f;
            wrenchFastFrames = 0;
            wrenchHitsThisSwing.Clear();
            if (trackedItem == null)
                return;

            // ItemInHand is normally parented to MFN's animated camera-relative model mount.
            // Detaching the actual item gives the Rift grip authoritative six-degree ownership
            // while preserving the item's own reload/fire animation hierarchy.
            trackedItemRotationOffset = GetWeaponRotationOffset(trackedItem, equippedManager.GetCurrentItem());
            trackedRightWrist = FindRightWrist(trackedItem);
            trackedLeftHandRoot = FindHandRoot(trackedItem, "PL_HAND_L");
            trackedLeftWrist = FindWristUnder(trackedLeftHandRoot);
            if (trackedLeftHandRoot != null && trackedLeftWrist != null)
                trackedLeftWristRestRotation = Quaternion.Inverse(trackedLeftHandRoot.rotation) *
                                               trackedLeftWrist.rotation;
            rightArmRig = FindArmRig(trackedItem, false);
            leftArmRig = FindArmRig(trackedItem, true);
            if (rightArmRig == null)
                proceduralRightArm = CreateProceduralArm(trackedItem, trackedLeftHandRoot, false);
            if (leftArmRig == null)
                proceduralLeftArm = CreateProceduralArm(trackedItem, trackedLeftHandRoot, true);
            trackedItemGripOffset = GetWeaponGripOffset(trackedItem, trackedRightWrist,
                trackedItemRotationOffset);
            trackedItem.transform.SetParent(rightHandAnchor, true);
            trackedItem.transform.localPosition = trackedItemGripOffset;
            trackedItem.transform.localRotation = trackedItemRotationOffset;
            Logger.LogInfo($"Directly attached {trackedItem.name} ({equippedManager.GetCurrentItem()}) to Rift grip; " +
                           $"rotationOffset={trackedItemRotationOffset.eulerAngles}.");
            ReportTrackedWeapon(trackedItem);
        }

        private ArmIkRig FindArmRig(ItemInHand item, bool left)
        {
            if (item == null)
                return null;
            var upperBone = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            var lowerBone = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            var handBone = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            foreach (var animator in item.GetComponentsInChildren<Animator>(true))
            {
                try
                {
                    if (!animator.isHuman)
                        continue;
                    var upper = animator.GetBoneTransform(upperBone);
                    var lower = animator.GetBoneTransform(lowerBone);
                    var hand = animator.GetBoneTransform(handBone);
                    var rig = CreateArmRig(upper, lower, hand);
                    if (rig == null)
                        continue;
                    ConfigureArmGrip(rig, item, left ? trackedLeftWrist : trackedRightWrist);
                    Logger.LogInfo($"Using Unity humanoid {(left ? "left" : "right")} arm IK: " +
                                   $"{GetPath(upper)} -> {GetPath(lower)} -> {GetPath(hand)}.");
                    return rig;
                }
                catch (Exception exception)
                {
                    Logger.LogWarning($"Could not read humanoid arm bones from {animator.name}: " +
                                      exception.Message);
                }
            }

            var sideTokens = left ? new[] { "_l", ".l", "left" } : new[] { "_r", ".r", "right" };
            Transform fallbackUpper = null;
            Transform fallbackLower = null;
            Transform fallbackHand = null;
            foreach (var transform in item.GetComponentsInChildren<Transform>(true))
            {
                var name = transform.name.ToLowerInvariant();
                var correctSide = false;
                foreach (var token in sideTokens)
                    correctSide |= name.Contains(token);
                if (!correctSide)
                    continue;
                if (fallbackUpper == null && (name.Contains("upperarm") || name.Contains("upper_arm")))
                    fallbackUpper = transform;
                else if (fallbackLower == null && (name.Contains("forearm") || name.Contains("lowerarm") ||
                                                    name.Contains("lower_arm")))
                    fallbackLower = transform;
                else if (fallbackHand == null && name.Contains("hand"))
                    fallbackHand = transform;
            }
            var fallback = CreateArmRig(fallbackUpper, fallbackLower, fallbackHand);
            if (fallback != null)
            {
                ConfigureArmGrip(fallback, item, left ? trackedLeftWrist : trackedRightWrist);
                Logger.LogInfo($"Using named-bone {(left ? "left" : "right")} arm IK fallback.");
            }
            return fallback;
        }

        private void ConfigureArmGrip(ArmIkRig rig, ItemInHand item, Transform gripPoint)
        {
            if (rig == null || item == null)
                return;
            var handInItem = Quaternion.Inverse(item.transform.rotation) * rig.hand.rotation;
            rig.handTargetOffset = trackedItemRotationOffset * handInItem;
            rig.handGripLocal = gripPoint != null
                ? rig.hand.InverseTransformPoint(gripPoint.position)
                : Vector3.zero;
        }

        private static ArmIkRig CreateArmRig(Transform upper, Transform lower, Transform hand)
        {
            if (upper == null || lower == null || hand == null ||
                !lower.IsChildOf(upper) || !hand.IsChildOf(lower))
                return null;
            var upperLength = Vector3.Distance(upper.position, lower.position);
            var lowerLength = Vector3.Distance(lower.position, hand.position);
            if (upperLength < 0.04f || lowerLength < 0.04f ||
                upperLength > 1f || lowerLength > 1f)
                return null;
            return new ArmIkRig
            {
                upperArm = upper,
                lowerArm = lower,
                hand = hand,
                upperLength = upperLength,
                lowerLength = lowerLength,
                handTargetOffset = Quaternion.identity
            };
        }

        private ProceduralArmRig CreateProceduralArm(ItemInHand item, Transform leftHandRoot,
            bool left)
        {
            if (item == null)
                return null;
            var root = new GameObject(left ? "MFN VR Left Arm IK" : "MFN VR Right Arm IK");
            var upper = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var lower = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            upper.name = left ? "Left Upper Arm" : "Right Upper Arm";
            lower.name = left ? "Left Forearm" : "Right Forearm";
            upper.transform.SetParent(root.transform, true);
            lower.transform.SetParent(root.transform, true);
            DisableArmCollider(upper);
            DisableArmCollider(lower);

            var sourceRoot = left ? leftHandRoot : FindHandRoot(item, "PL_HAND_R");
            var sourceRenderer = sourceRoot != null
                ? sourceRoot.GetComponentInChildren<Renderer>(true)
                : item.GetComponentInChildren<Renderer>(true);
            if (sourceRenderer != null && sourceRenderer.sharedMaterial != null)
            {
                upper.GetComponent<Renderer>().sharedMaterial = sourceRenderer.sharedMaterial;
                lower.GetComponent<Renderer>().sharedMaterial = sourceRenderer.sharedMaterial;
            }
            SetLayerRecursively(root.transform, item.gameObject.layer);
            Logger.LogInfo($"Created procedural {(left ? "left" : "right")} shoulder/elbow arm fallback.");
            return new ProceduralArmRig
            {
                root = root,
                upperArm = upper.transform,
                lowerArm = lower.transform
            };
        }

        private static void DisableArmCollider(GameObject arm)
        {
            var collider = arm.GetComponent<Collider>();
            if (collider == null)
                return;
            collider.enabled = false;
            Destroy(collider);
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null)
                return;
            root.gameObject.layer = layer;
            for (var index = 0; index < root.childCount; index++)
                SetLayerRecursively(root.GetChild(index), layer);
        }

        private static void DestroyProceduralArm(ref ProceduralArmRig rig)
        {
            if (rig != null && rig.root != null)
                Destroy(rig.root);
            rig = null;
        }

        private void ReportTrackedWeapon(ItemInHand item)
        {
            if (item.firePosition != null)
                Logger.LogInfo($"Tracked weapon muzzle: {GetPath(item.firePosition)}, " +
                               $"local={item.transform.InverseTransformPoint(item.firePosition.position)}, " +
                               $"forward={item.transform.InverseTransformDirection(item.firePosition.forward)}.");
            foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
                Logger.LogInfo($"Tracked weapon renderer: {GetPath(renderer.transform)}, bounds={renderer.bounds.size}.");
        }

        private static Transform FindHandRoot(ItemInHand item, string handName)
        {
            if (item == null)
                return null;
            foreach (var transform in item.GetComponentsInChildren<Transform>(true))
                if (string.Equals(transform.name, handName, StringComparison.OrdinalIgnoreCase))
                    return transform;
            return null;
        }

        private static Transform FindWristUnder(Transform handRoot)
        {
            if (handRoot == null)
                return null;
            foreach (var transform in handRoot.GetComponentsInChildren<Transform>(true))
                if (string.Equals(transform.name, "WRIST", StringComparison.OrdinalIgnoreCase))
                    return transform;
            return null;
        }

        private static Transform FindRightWrist(ItemInHand item)
        {
            if (item == null)
                return null;
            foreach (var transform in item.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(transform.name, "WRIST", StringComparison.OrdinalIgnoreCase))
                    continue;
                var cursor = transform.parent;
                while (cursor != null && cursor != item.transform)
                {
                    if (cursor.name.IndexOf("PL_HAND_R", StringComparison.OrdinalIgnoreCase) >= 0)
                        return transform;
                    cursor = cursor.parent;
                }
            }
            return null;
        }

        private static Vector3 GetWeaponGripOffset(ItemInHand item, Transform rightWrist,
            Quaternion rotationOffset)
        {
            if (item == null || rightWrist == null)
                return Vector3.zero;
            var wristInItem = item.transform.InverseTransformPoint(rightWrist.position);
            return -(rotationOffset * wristInItem);
        }

        private static Quaternion GetWeaponRotationOffset(ItemInHand itemInHand, InventoryItem item)
        {
            if (itemInHand != null && itemInHand.firePosition != null)
            {
                var localBarrelForward = itemInHand.transform.InverseTransformDirection(
                    itemInHand.firePosition.forward).normalized;
                if (localBarrelForward.sqrMagnitude > 0.0001f)
                    return Quaternion.FromToRotation(localBarrelForward, Vector3.forward);
            }
            return Quaternion.identity;
        }

        private void UpdatePhysicalWrench(EquippedManager equippedManager, Vector3 gripLocalPosition,
            Quaternion aimLocalRotation)
        {
            if (trackedItem == null || equippedManager.GetCurrentItem() != InventoryItem.Wrench)
            {
                hasPreviousWrenchHead = false;
                wrenchPoseElapsed = 0f;
                wrenchFastFrames = 0;
                wrenchHitsThisSwing.Clear();
                return;
            }

            if (trackedItem.firePosition == null)
                return;
            var headPosition = trackedItem.firePosition.position;
            var headInAnchor = rightHandAnchor.InverseTransformPoint(headPosition);
            var headLocalPosition = gripLocalPosition + aimLocalRotation * headInAnchor;
            wrenchPoseElapsed += Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            if (!hasPreviousWrenchHead)
            {
                previousWrenchHeadPosition = headPosition;
                previousWrenchHeadLocalPosition = headLocalPosition;
                hasPreviousWrenchHead = true;
                wrenchPoseElapsed = 0f;
                return;
            }

            var localDelta = headLocalPosition - previousWrenchHeadLocalPosition;
            if (localDelta.sqrMagnitude < 0.00000025f)
            {
                // The hand did not move in tracking space. Keep the collision origin abreast of
                // player locomotion so walking cannot become a long sweep on the next real swing.
                previousWrenchHeadPosition = headPosition;
                return;
            }
            var worldDelta = headPosition - previousWrenchHeadPosition;
            var elapsed = Mathf.Max(wrenchPoseElapsed, 0.001f);
            wrenchPoseElapsed = 0f;
            if (localDelta.magnitude > 0.35f)
            {
                previousWrenchHeadPosition = headPosition;
                previousWrenchHeadLocalPosition = headLocalPosition;
                wrenchFastFrames = 0;
                wrenchHitsThisSwing.Clear();
                return;
            }
            var speed = localDelta.magnitude / elapsed;
            if (speed < 0.8f)
            {
                wrenchHitsThisSwing.Clear();
                wrenchFastFrames = 0;
            }
            else if (speed >= 1.8f)
                wrenchFastFrames++;
            if (speed >= 1.8f && wrenchFastFrames >= 2 && worldDelta.sqrMagnitude > 0.000001f)
            {
                const float headRadius = 0.065f;
                var hits = Physics.SphereCastAll(previousWrenchHeadPosition, headRadius,
                    worldDelta.normalized, worldDelta.magnitude, ~0, QueryTriggerInteraction.Collide);
                foreach (var hit in hits)
                    DamageFromPhysicalWrench(equippedManager, hit.collider, hit.point, speed);
            }
            previousWrenchHeadPosition = headPosition;
            previousWrenchHeadLocalPosition = headLocalPosition;
        }

        private void DamageFromPhysicalWrench(EquippedManager equippedManager, Collider collider,
            Vector3 hitPoint, float speed)
        {
            if (collider == null)
                return;
            var enemy = collider.GetComponentInParent<EnemyParent>();
            var sender = enemy == null ? collider.GetComponentInParent<EnemyDamageSender>() : null;
            if (enemy == null && sender == null)
                return;
            var hitObject = enemy != null ? enemy.gameObject : sender.gameObject;
            var hitId = hitObject.GetInstanceID();
            if (!wrenchHitsThisSwing.Add(hitId))
                return;

            if (wrenchLastHitTimes.TryGetValue(hitId, out var lastHit) && Time.unscaledTime - lastHit < 0.6f)
                return;
            wrenchLastHitTimes[hitId] = Time.unscaledTime;

            var speedMultiplier = Mathf.Lerp(0.5f, 4f, Mathf.InverseLerp(1.8f, 7.5f, speed));
            var damage = Mathf.Max(0.25f, trackedItem.meleeDamage * speedMultiplier);
            var difficulty = SaveData.GetIntData("Difficulty");
            if (difficulty == 0) damage *= 2f;
            else if (difficulty == 1) damage *= 1.3f;
            var forceStun = speed >= 3f;
            var alwaysStun = speed >= 5.5f;
            var point = hitPoint == Vector3.zero ? collider.ClosestPoint(rightHandAnchor.position) : hitPoint;
            if (enemy != null)
                enemy.Damage(damage, Player.current, point, forceStun, true, false,
                    alwaysStun, true);
            else
                sender.Damage(damage, Player.current, point, forceStun, true, false, alwaysStun);
            var impactSound = wrenchHitSoundField?.GetValue(equippedManager) as AudioLevelAdjuster;
            if (impactSound != null)
            {
                impactSound.SetPitch(UnityEngine.Random.Range(0.96f, 1.04f));
                impactSound.PlayAllSources();
            }
            MFN_ApplyControllerHaptic(1, Mathf.Clamp01(speed / 7.5f), 0.07f, 0f);
            Logger.LogInfo($"Physical wrench hit {hitObject.name}: speed={speed:F2}m/s damage={damage:F2}.");
        }

        private void BeginWeaponAimOverride()
        {
            if (!rightAimValid)
                return;
            if (weaponAimOverrideDepth++ > 0)
                return;
            var player = equippedPlayerField?.GetValue(motionEquippedManager) as Player ?? Player.current;
            weaponAimCamera = player != null ? player.GetMainCamera() : null;
            if (weaponAimCamera == null)
            {
                weaponAimOverrideDepth = 0;
                return;
            }
            savedWeaponAimCameraPosition = weaponAimCamera.transform.position;
            savedWeaponAimCameraRotation = weaponAimCamera.transform.rotation;
            if (motionEquippedManager != null)
            {
                savedEquippedManagerRotation = motionEquippedManager.transform.rotation;
                motionEquippedManager.transform.rotation = rightAimWorldRotation;
            }
            weaponAimCamera.transform.SetPositionAndRotation(rightAimWorldPosition, rightAimWorldRotation);
        }

        private void EndWeaponAimOverride()
        {
            if (weaponAimOverrideDepth <= 0 || --weaponAimOverrideDepth > 0)
                return;
            if (weaponAimCamera != null)
                weaponAimCamera.transform.SetPositionAndRotation(savedWeaponAimCameraPosition,
                    savedWeaponAimCameraRotation);
            if (motionEquippedManager != null)
                motionEquippedManager.transform.rotation = savedEquippedManagerRotation;
            weaponAimCamera = null;
        }
        private void ApplyHeadLook(Player player)
        {
            if (IsScriptedCameraMove(player))
            {
                hasLastHeadRotation = false;
                return;
            }
            if (MFN_GetHeadOrientation(out var x,out var y,out var z,out var w)==0) return;
            var now=new Quaternion(-x,-y,z,w);
            if(!hasLastHeadRotation){lastHeadRotation=now;hasLastHeadRotation=true;return;}
            var delta=now*Quaternion.Inverse(lastHeadRotation); var e=delta.eulerAngles;
            var pitch=e.x>180f?e.x-360f:e.x; var yaw=e.y>180f?e.y-360f:e.y;
            AccessTools.Method(typeof(Player),"RotateCamera")?.Invoke(player,new object[]{pitch,yaw});
            lastHeadRotation=now;
        }
        private bool IsScriptedCameraMove(Player player)
        {
            return (rotating180Field != null && (bool)rotating180Field.GetValue(player)) ||
                   (rotatingRightField != null && (bool)rotatingRightField.GetValue(player)) ||
                   (rotatingLeftField != null && (bool)rotatingLeftField.GetValue(player));
        }

        private void ApplyMovementHeadYaw(Player player)
        {
            movementYawApplied = false;
            var movementEnabled = movementControlsEnabledField != null &&
                                  (bool)movementControlsEnabledField.GetValue(player);
            var hardDeactivated = hardDeactivateField != null && (bool)hardDeactivateField.GetValue(player);
            if (!movementEnabled || hardDeactivated || !hasTrackingOrigin || IsScriptedCameraMove(player) ||
                neckHorizontalField == null ||
                MFN_GetHeadOrientation(out var x, out var y, out var z, out var w) == 0)
                return;

            var neck = neckHorizontalField.GetValue(player) as Transform;
            if (neck == null)
                return;

            var orientation = new Quaternion(-x, -y, z, w);
            var relative = Quaternion.Inverse(trackingOriginRotation) * orientation;
            var headForward = relative * Vector3.forward;
            var headYaw = Mathf.Atan2(headForward.x, headForward.z) * Mathf.Rad2Deg;
            movementBaseNeckRotation = neck.localRotation;
            neck.localRotation = movementBaseNeckRotation * Quaternion.Euler(0f, headYaw, 0f);
            movementYawApplied = true;
        }

        private void RestoreMovementNeck()
        {
            if (!movementYawApplied || neckHorizontalField == null || Player.current == null)
                return;

            var neck = neckHorizontalField.GetValue(Player.current) as Transform;
            if (neck != null)
            {
                // cameraRotY is MFN's authoritative non-VR facing direction. Restoring from it
                // prevents the temporary locomotion basis from feeding back into the next frame.
                if (cameraRotYField != null)
                    neck.localEulerAngles = new Vector3(0f, (float)cameraRotYField.GetValue(Player.current), 0f);
                else
                    neck.localRotation = movementBaseNeckRotation;
            }
            movementYawApplied = false;
        }

        private bool IsVrEyeCamera(Camera camera)
        {
            return camera == leftEyeCamera || camera == rightEyeCamera ||
                   camera == leftHudCamera || camera == rightHudCamera ||
                   camera == leftHandsCamera || camera == rightHandsCamera;
        }

        private void OnCameraPreCull(Camera camera)
        {
            GL.invertCulling = IsVrEyeCamera(camera);
        }

        private void OnCameraPostRender(Camera camera)
        {
            if (IsVrEyeCamera(camera))
                GL.invertCulling = false;
        }

        private void ConfigureEyeCameras(Player player)
        {
            UpdateDynamicResolution();
            var recommendedWidth = MFN_GetEyeWidth();
            var recommendedHeight = MFN_GetEyeHeight();
            var width = Mathf.Max(320, Mathf.RoundToInt(recommendedWidth * activeResolutionScale)) & ~1;
            var height = Mathf.Max(320, Mathf.RoundToInt(recommendedHeight * activeResolutionScale)) & ~1;
            var sourceCamera = player.GetMainCamera();
            if (recommendedWidth <= 0 || recommendedHeight <= 0 || sourceCamera == null)
                return;
            gameplayCamera = sourceCamera;
            ConfigureRenderRig(player, sourceCamera);
            hasCachedEyeViews = TryGetEyeView(0, out cachedLeftPosition, out cachedLeftRotation,
                                    out cachedLeftProjection) &&
                                TryGetEyeView(1, out cachedRightPosition, out cachedRightRotation,
                                    out cachedRightProjection);

            if (leftEyeTexture == null || leftEyeTexture.width != width || leftEyeTexture.height != height)
            {
                if (leftEyeTexture != null) Destroy(leftEyeTexture);
                if (rightEyeTexture != null) Destroy(rightEyeTexture);
                leftEyeTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rightEyeTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                leftEyeTexture.Create();
                rightEyeTexture.Create();
                RemoveWorldCapture();
                Logger.LogInfo($"VR eye render size: {width}x{height} ({activeResolutionScale:0.##}x of {recommendedWidth}x{recommendedHeight}).");
            }

            EnsureStereoCameraPair(sourceCamera, ref leftEyeCamera, ref rightEyeCamera, "World");
            leftEyeCamera.enabled = true;
            rightEyeCamera.enabled = true;
            leftEyeCamera.stereoTargetEye = StereoTargetEyeMask.None;
            rightEyeCamera.stereoTargetEye = StereoTargetEyeMask.None;
            leftEyeCamera.targetTexture = leftEyeTexture;
            rightEyeCamera.targetTexture = rightEyeTexture;
            leftEyeCamera.useOcclusionCulling = false;
            rightEyeCamera.useOcclusionCulling = false;

            var transform = sourceCamera.transform;
            ConfigureTrackedPair(sourceCamera, leftEyeCamera, rightEyeCamera);

            RemoveWorldCapture();

            var hudCamera = player.GetHUDCamera();
            if (hudCamera != null)
            {
                gameplayHudCamera = hudCamera;
                EnsureStereoCameraPair(hudCamera, ref leftHudCamera, ref rightHudCamera, "HUD");
                leftHudCamera.stereoTargetEye = StereoTargetEyeMask.None;
                rightHudCamera.stereoTargetEye = StereoTargetEyeMask.None;
                leftHudCamera.targetTexture = leftEyeTexture;
                rightHudCamera.targetTexture = rightEyeTexture;
                leftHudCamera.useOcclusionCulling = false;
                rightHudCamera.useOcclusionCulling = false;
                leftHudCamera.depth = leftEyeCamera.depth + 1f;
                rightHudCamera.depth = rightEyeCamera.depth + 1f;
                ConfigureTrackedPair(hudCamera, leftHudCamera, rightHudCamera);
            }

            var handsCamera = handsCameraField?.GetValue(player) as Camera;
            if (handsCamera != null)
            {
                EnsureStereoCameraPair(handsCamera, ref leftHandsCamera, ref rightHandsCamera, "Hands");
                ConfigureOverlayPair(handsCamera, leftHandsCamera, rightHandsCamera, transform);
            }
            if (!cameraComponentsReported)
            {
                cameraComponentsReported = true;
                ReportCameraComponents("World", sourceCamera);
                ReportCameraComponents("HUD", hudCamera);
                ReportCameraComponents("Hands", handsCamera);
            }
        }

        private void UpdateDynamicResolution()
        {
            if (dynamicResolutionSetting == null || !dynamicResolutionSetting.Value)
                return;

            FrameTimingManager.CaptureFrameTimings();
            if (Time.realtimeSinceStartup < nextDynamicResolutionCheck)
                return;
            nextDynamicResolutionCheck = Time.realtimeSinceStartup + 1.5f;

            if (!FrameTimingManager.IsFeatureEnabled())
            {
                if (!dynamicTimingUnavailableReported)
                {
                    dynamicTimingUnavailableReported = true;
                    Logger.LogWarning("DynamicResolution is enabled, but Unity GPU frame timing is unavailable; keeping the configured fixed resolution.");
                }
                return;
            }

            var count = FrameTimingManager.GetLatestTimings(
                (uint)dynamicFrameTimings.Length, dynamicFrameTimings);
            if (count == 0)
                return;

            double gpuMilliseconds = 0;
            var validSamples = 0;
            for (var index = 0; index < count; index++)
            {
                var sample = dynamicFrameTimings[index].gpuFrameTime;
                if (sample <= 0.01 || double.IsNaN(sample) || double.IsInfinity(sample))
                    continue;
                gpuMilliseconds += sample;
                validSamples++;
            }
            if (validSamples == 0)
                return;

            gpuMilliseconds /= validSamples;
            var targetMilliseconds = 1000.0 / dynamicResolutionTargetFpsSetting.Value;
            var maximum = resolutionScaleSetting.Value;
            var minimum = Mathf.Min(dynamicResolutionMinScaleSetting.Value, maximum);
            var newScale = activeResolutionScale;
            if (gpuMilliseconds > targetMilliseconds * 1.06)
                newScale -= 0.05f;
            else if (gpuMilliseconds < targetMilliseconds * 0.78)
                newScale += 0.025f;
            newScale = Mathf.Clamp(newScale, minimum, maximum);
            newScale = Mathf.Round(newScale * 40f) / 40f;
            if (Mathf.Abs(newScale - activeResolutionScale) < 0.001f)
                return;

            activeResolutionScale = newScale;
            Logger.LogInfo($"Dynamic resolution adjusted to {activeResolutionScale:0.###}x (GPU {gpuMilliseconds:0.0} ms, target {targetMilliseconds:0.0} ms).");
        }

        private void ConfigureRenderRig(Player player, Camera sourceCamera)
        {
            useComfortRig = false;
            if (neckHorizontalField == null)
                return;

            var movementEnabled = movementControlsEnabledField != null &&
                                  (bool)movementControlsEnabledField.GetValue(player);
            var hardDeactivated = hardDeactivateField != null && (bool)hardDeactivateField.GetValue(player);
            var neck = neckHorizontalField.GetValue(player) as Transform;
            if (!movementEnabled || hardDeactivated || IsScriptedCameraMove(player) || neck == null)
                return;

            if (comfortRigSource != sourceCamera)
            {
                comfortRigSource = sourceCamera;
                comfortRigCameraOffset = Quaternion.Inverse(neck.rotation) *
                                         (sourceCamera.transform.position - neck.position);
                Logger.LogInfo($"Stable VR rig attached at {GetPath(neck)}; camera offset={comfortRigCameraOffset}.");
            }

            // The neck supplies only MFN's world position and body yaw. Camera bob, mouse pitch,
            // recoil and smoothing are deliberately excluded from the HMD's tracking transform.
            renderRigPosition = neck.position + neck.rotation * comfortRigCameraOffset;
            renderRigRotation = neck.rotation;
            useComfortRig = true;
        }

        private void ReportCameraComponents(string label, Camera camera)
        {
            if (camera == null)
            {
                Logger.LogInfo($"{label} camera: <null>");
                return;
            }
            Logger.LogInfo($"{label} camera '{GetPath(camera.transform)}': clear={camera.clearFlags}, " +
                           $"depth={camera.depth}, mask=0x{camera.cullingMask:X8}");
            foreach (var component in camera.GetComponents<Component>())
                Logger.LogInfo($"{label} component: {component.GetType().AssemblyQualifiedName}");
        }

        private void EnsureStereoCameraPair(Camera source, ref Camera left, ref Camera right, string label)
        {
            if (left != null && right != null)
                return;
            left = new GameObject().AddComponent<Camera>();
            right = new GameObject().AddComponent<Camera>();
            left.gameObject.SetActive(false);
            right.gameObject.SetActive(false);
            left.CopyFrom(source);
            right.CopyFrom(source);
            left.name = $"MFN VR Left {label}";
            right.name = $"MFN VR Right {label}";
            left.gameObject.tag = "Untagged";
            right.gameObject.tag = "Untagged";
            left.gameObject.layer = source.gameObject.layer;
            right.gameObject.layer = source.gameObject.layer;
            CopyRenderEffects(source, left.gameObject);
            CopyRenderEffects(source, right.gameObject);
            left.gameObject.SetActive(true);
            right.gameObject.SetActive(true);
        }

        private void CopyRenderEffects(Camera sourceCamera, GameObject destination)
        {
            foreach (var source in sourceCamera.GetComponents<Component>())
            {
                var type = source.GetType();
                var name = type.FullName;
                if (name != "UnityEngine.Rendering.PostProcessing.PostProcessLayer" &&
                    name != "AmplifyOcclusionEffect" && name != "FXAA" && name != "PSGammaCorrection")
                    continue;
                try
                {
                    var copy = destination.AddComponent(type);
                    CopySerializedFields(source, copy, type);
                    if (source is Behaviour sourceBehaviour && copy is Behaviour copyBehaviour)
                        copyBehaviour.enabled = sourceBehaviour.enabled;
                }
                catch (System.Exception exception)
                {
                    Logger.LogWarning($"Could not copy render effect {name}: {exception.Message}");
                }
            }
        }

        private static void CopySerializedFields(Component source, Component destination, Type type)
        {
            while (type != null && type != typeof(Component) && type != typeof(UnityEngine.Object))
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsLiteral)
                        continue;
                    if (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField)))
                        continue;
                    try { field.SetValue(destination, field.GetValue(source)); } catch { }
                }
                type = type.BaseType;
            }
        }

        private void ConfigureOverlayPair(Camera source, Camera left, Camera right, Transform worldTransform)
        {
            left.CopyFrom(source);
            right.CopyFrom(source);
            left.enabled = true;
            right.enabled = true;
            left.stereoTargetEye = StereoTargetEyeMask.None;
            right.stereoTargetEye = StereoTargetEyeMask.None;
            left.targetTexture = leftEyeTexture;
            right.targetTexture = rightEyeTexture;
            left.useOcclusionCulling = false;
            right.useOcclusionCulling = false;
            ConfigureTrackedPair(source, left, right);
        }

        private void ConfigureTrackedPair(Camera source, Camera left, Camera right)
        {
            if (!hasCachedEyeViews)
            {
                var halfIpd = source.transform.right * 0.032f;
                left.transform.SetPositionAndRotation(source.transform.position - halfIpd, source.transform.rotation);
                right.transform.SetPositionAndRotation(source.transform.position + halfIpd, source.transform.rotation);
                left.projectionMatrix = source.projectionMatrix;
                right.projectionMatrix = source.projectionMatrix;
                return;
            }

            var leftPosition = cachedLeftPosition;
            var rightPosition = cachedRightPosition;
            var leftRotation = cachedLeftRotation;
            var leftProjection = cachedLeftProjection;
            var rightProjection = cachedRightProjection;

            var center = (leftPosition + rightPosition) * 0.5f;
            if (!hasTrackingOrigin)
            {
                trackingOriginPosition = center;
                // Recenter only yaw. Pitch/roll belong to the head pose; including them in the
                // tracking-space basis makes positional tracking tilt and wobble while turning.
                var originForward = leftRotation * Vector3.forward;
                originForward.y = 0f;
                trackingOriginRotation = originForward.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(originForward.normalized, Vector3.up)
                    : Quaternion.identity;
                hasTrackingOrigin = true;
            }
            var inverseOrigin = Quaternion.Inverse(trackingOriginRotation);
            var leftLocalPosition = inverseOrigin * (leftPosition - trackingOriginPosition);
            var rightLocalPosition = inverseOrigin * (rightPosition - trackingOriginPosition);
            var headRotation = inverseOrigin * leftRotation;
            var basePosition = useComfortRig ? renderRigPosition : source.transform.position;
            var baseRotation = useComfortRig ? renderRigRotation : source.transform.rotation;
            left.transform.SetPositionAndRotation(basePosition + baseRotation * leftLocalPosition,
                baseRotation * headRotation);
            right.transform.SetPositionAndRotation(basePosition + baseRotation * rightLocalPosition,
                baseRotation * headRotation);
            left.projectionMatrix = leftProjection;
            right.projectionMatrix = rightProjection;
        }

        private bool TryGetEyeView(int eye, out Vector3 position, out Quaternion rotation, out Matrix4x4 projection)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            projection = Matrix4x4.identity;
            if (MFN_GetEyeView(eye, out var px, out var py, out var pz, out var qx, out var qy,
                    out var qz, out var qw, out var angleLeft, out var angleRight,
                    out var angleUp, out var angleDown) == 0)
                return false;
            position = new Vector3(px, py, -pz);
            // OpenXR is right-handed with -Z forward; Unity is left-handed with +Z forward.
            // Reflecting Z maps quaternion (x,y,z,w) to (-x,-y,z,w).
            rotation = new Quaternion(-qx, -qy, qz, qw);
            var near = 0.03f;
            var far = gameplayCamera != null ? gameplayCamera.farClipPlane : 1000f;
            projection = Matrix4x4.Frustum(Mathf.Tan(angleLeft) * near, Mathf.Tan(angleRight) * near,
                Mathf.Tan(angleDown) * near, Mathf.Tan(angleUp) * near, near, far);
            // OpenXR's D3D swapchain and Unity render textures use opposite vertical projection
            // conventions. Flip the projection once at the camera rather than rotating the layer.
            return true;
        }

        private IEnumerator RenderStereoAtEndOfFrame()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                if (gameplayCamera == null || leftEyeTexture == null || rightEyeTexture == null)
                    continue;

                RenderCameraForEyes(gameplayCamera, true);
                if (gameplayHudCamera != null && gameplayHudCamera != gameplayCamera)
                    RenderCameraForEyes(gameplayHudCamera, false);
            }
        }

        private void RenderCameraForEyes(Camera camera, bool offsetForStereo)
        {
            var originalTarget = camera.targetTexture;
            var originalPosition = camera.transform.position;
            var originalRotation = camera.transform.rotation;
            var originalOcclusion = camera.useOcclusionCulling;
            try
            {
                camera.useOcclusionCulling = false;
                var eyeOffset = offsetForStereo ? originalRotation * Vector3.right * 0.032f : Vector3.zero;
                camera.targetTexture = leftEyeTexture;
                camera.transform.SetPositionAndRotation(originalPosition - eyeOffset, originalRotation);
                camera.Render();
                camera.targetTexture = rightEyeTexture;
                camera.transform.SetPositionAndRotation(originalPosition + eyeOffset, originalRotation);
                camera.Render();
            }
            catch (System.Exception exception)
            {
                Logger.LogWarning($"Stereo camera render failed: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                camera.targetTexture = originalTarget;
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                camera.useOcclusionCulling = originalOcclusion;
            }
        }

        private void AttachWorldCapture(Camera camera)
        {
            if (captureAttachedTo == camera || leftEyeTexture == null || rightEyeTexture == null)
                return;

            RemoveWorldCapture();
            leftWorldCapture = new CommandBuffer { name = "MFN VR Left World Capture" };
            rightWorldCapture = new CommandBuffer { name = "MFN VR Right World Capture" };
            leftWorldCapture.Blit(BuiltinRenderTextureType.CameraTarget, leftEyeTexture);
            rightWorldCapture.Blit(BuiltinRenderTextureType.CameraTarget, rightEyeTexture);
            camera.AddCommandBuffer(CameraEvent.AfterEverything, leftWorldCapture);
            camera.AddCommandBuffer(CameraEvent.AfterEverything, rightWorldCapture);
            captureAttachedTo = camera;
        }

        private void RemoveWorldCapture()
        {
            if (captureAttachedTo != null)
            {
                if (leftWorldCapture != null) captureAttachedTo.RemoveCommandBuffer(CameraEvent.AfterEverything, leftWorldCapture);
                if (rightWorldCapture != null) captureAttachedTo.RemoveCommandBuffer(CameraEvent.AfterEverything, rightWorldCapture);
            }

            leftWorldCapture?.Release();
            rightWorldCapture?.Release();
            leftWorldCapture = null;
            rightWorldCapture = null;
            captureAttachedTo = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            DestroyProceduralArm(ref proceduralRightArm);
            DestroyProceduralArm(ref proceduralLeftArm);
            hasTrackingOrigin = false;
            hasCachedEyeViews = false;
            comfortRigSource = null;
            useComfortRig = false;
            rightHandAnchor = null;
            trackedItem = null;
            motionEquippedManager = null;
            motionPlayer = null;
            motionManagerReported = false;
            rightAimValid = false;
            hasPreviousWrenchHead = false;
            leftGripPoseValid = false;
            twoHandedGrip = false;
            previousLeftGripPressed = leftGripPressed;
            trackedLeftHandRoot = null;
            trackedLeftWrist = null;
            trackedRightWrist = null;
            rightArmRig = null;
            leftArmRig = null;
            wrenchPoseElapsed = 0f;
            wrenchFastFrames = 0;
            wrenchHitsThisSwing.Clear();
            wrenchLastHitTimes.Clear();
            Logger.LogInfo($"Scene loaded: {scene.name} ({mode}).");
            ReportCameras();
        }

        private IEnumerator ReportRuntime()
        {
            for (var i = 0; i < ReportsToWrite; i++)
            {
                yield return new WaitForSecondsRealtime(3f);
                ReportXrState();
                ReportCameras();
            }
        }

        private void ReportXrState()
        {
            try { Logger.LogInfo($"Native GPU flip path: {MFN_GetFlipPath()}."); } catch { }
            var devices = new List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevices(devices);
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetInstances(displays);
            Logger.LogInfo($"XR: inputDevices={devices.Count}, displaySubsystems={displays.Count}.");

            foreach (var display in displays)
            {
                Logger.LogInfo($"XR display: id='{display.SubsystemDescriptor.id}' running={display.running}.");
            }

            foreach (var device in devices)
            {
                Logger.LogInfo($"XR device: '{device.name}' manufacturer='{device.manufacturer}' " +
                               $"characteristics={device.characteristics} valid={device.isValid}.");
            }
        }

        private void LoadNativeBridge()
        {
            var nativePath = System.IO.Path.Combine(Application.dataPath, "Plugins", "MFNOpenXR.dll");
            if (LoadLibrary(nativePath) == System.IntPtr.Zero)
                Logger.LogWarning($"Could not load native bridge: {nativePath}");
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern System.IntPtr LoadLibrary(string fileName);

        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern System.IntPtr MFN_GetRenderEvent();

        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern void MFN_GetStatus(StringBuilder message, int messageLength);

        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_GetEyeWidth();

        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_GetEyeHeight();

        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void MFN_SetSourceTextures(System.IntPtr left, System.IntPtr right);
        [DllImport("MFNOpenXR.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int MFN_GetHeadOrientation(out float x,out float y,out float z,out float w);
        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_GetControllerInput(int hand, out float stickX, out float stickY,
            out float trigger, out float squeeze, out int primary, out int secondary,
            out int stickClick, out int menu);
        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_GetControllerPose(int hand, int aim, out float px, out float py,
            out float pz, out float qx, out float qy, out float qz, out float qw);
        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_ApplyControllerHaptic(int hand, float amplitude,
            float durationSeconds, float frequency);
        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_GetEyeView(int eye, out float px, out float py, out float pz,
            out float qx, out float qy, out float qz, out float qw, out float angleLeft,
            out float angleRight, out float angleUp, out float angleDown);
        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_GetFlipPath();



        private void ReportCameras()
        {
            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera == null || !camera.gameObject.scene.IsValid())
                    continue;

                Logger.LogInfo($"Camera: name='{camera.name}', enabled={camera.enabled}, tag='{camera.tag}', " +
                               $"depth={camera.depth}, stereoTargetEye={camera.stereoTargetEye}, " +
                               $"path='{GetPath(camera.transform)}'.");
            }
        }

        private static string GetPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }
    }
}
