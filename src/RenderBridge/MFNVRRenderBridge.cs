using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MFNVRBridge
{
    public static class RenderBridge
    {
        private const int MenuLayer = 31;
        private const int HandsLayer = 12;
        private const float MenuHorizontalFov = 60f;
        private static bool aimingDotEnabled = true;
        private static float aimingDotDistance = 1.08f;
        private static float aimingDotSize = 0.011f;
        private static float hudDistance = 1000f;
        private static float hudScale = 0.78f;
        private static float hudHeightOffset;
        private static float menuDistance = 10f;
        private static float menuScale = 1f;
        private static bool flatScreensOnlyForMainPauseAndFiles = true;
        private static bool interactionCameraMovement;
        private static bool physicalWeaponSwitching = true;
        private static bool menuPointerEnabled = true;
        private static int settingsRevision;
        private static int menuSettingsRevision = -1;

        private static Camera menuSource;
        private static RenderTexture menuCapture;
        private static GameObject menuScreen;
        private static Material menuMaterial;
        private static int menuCaptureFrame = -1;
        private static bool inventoryCaptureWarningLogged;
        private static MirrorBlitEffect mirrorEffect;
        private static bool haveWorldEyeData;
        private static Matrix4x4 worldLeftProjection;
        private static Matrix4x4 worldRightProjection;
        private static Matrix4x4 worldCenterProjection;
        private static Vector3 worldLeftEyeOffset;
        private static Vector3 worldRightEyeOffset;
        private static Vector3 worldLeftEyePosition;
        private static Vector3 worldRightEyePosition;
        private static Quaternion worldLeftEyeRotation;
        private static Quaternion worldRightEyeRotation;
        private static readonly HashSet<int> filteredItems = new HashSet<int>();
        private static readonly Dictionary<int, Camera> camerasWithRenderEffectsEnabled =
            new Dictionary<int, Camera>();

        private static readonly FieldInfo currentlyHoldingField = typeof(EquippedManager).GetField(
            "currentlyHolding", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo reticleRendererField = typeof(ReticleManager).GetField(
            "myRender", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo neckVerticalField = typeof(Player).GetField(
            "neckVertical", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo hoveringInteractableField = typeof(Player).GetField(
            "hoveringInteractable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo readoutGameplayHoverCountField =
            AccessTools.Field(typeof(InteractReadoutManager), "nonInvestigateHovering");
        private static readonly FieldInfo readoutGameplayVisibleField =
            AccessTools.Field(typeof(InteractReadoutManager), "isVisible");
        private static readonly FieldInfo doorTransitioningField = typeof(Player).GetField(
            "isDoorTransitioning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo directorAnimation2Field = typeof(Player).GetField(
            "inDirectorAnimation2", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo inventoryCursorCameraField = typeof(Player).GetField(
            "inventoryCursorCamera", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo inventoryControlsEnabledField = AccessTools.Field(
            typeof(Player), "inventoryControlsEnabled");
        private static readonly FieldInfo menuControlsEnabledField = AccessTools.Field(
            typeof(Player), "menuControlsEnabled");
        private static readonly FieldInfo mapControlsEnabledField = AccessTools.Field(
            typeof(Player), "mapControlsEnabled");
        private static readonly FieldInfo pauseMenuEnabledField = AccessTools.Field(
            typeof(Player), "pauseMenuEnabled");
        private static readonly FieldInfo investigateControlsEnabledField = AccessTools.Field(
            typeof(Player), "investigateControlsEnabled");
        private static readonly FieldInfo isOnMainMenuField = AccessTools.Field(
            typeof(Player), "isOnMainMenu");
        private static readonly FieldInfo isBackToMenuSceneField = AccessTools.Field(
            typeof(Player), "isBackToMenuScene");
        private static readonly FieldInfo isIntroSceneField = AccessTools.Field(
            typeof(Player), "isIntroScene");
        private static readonly FieldInfo inventoryRowsField = typeof(InventoryInWorld).GetField(
            "rows", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo inventoryCursorField = typeof(InventoryInWorld).GetField(
            "myCursor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo inventoryCurrentlyHoveringField =
            typeof(InventoryInWorld).GetField("currentlyHovering",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo inventoryCurrentSquareField =
            typeof(InventoryInWorld).GetField("currentSquare",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo inventoryClosestToField =
            typeof(InventoryInWorld).GetField("closestTo",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo inventoryPickingUpSquareField =
            typeof(InventoryInWorld).GetField("pickingUpSquare",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo inventorySquareHoverBackgroundField =
            typeof(InventorySquare).GetField("hoverBackground",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo itemInventoryField = typeof(ItemInInventory).GetField(
            "myInventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo dropdownTextsField =
            typeof(NewIntentoryDropdown).GetField("texts",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo dropdownVisibleField =
            typeof(NewIntentoryDropdown).GetField("isVisible",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo confirmTextsField =
            typeof(NewInventoryConfirm).GetField("texts",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo inventoryPutDownMethod = typeof(InventoryInWorld).GetMethod(
            "PutDownItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo inventoryRotateMethod = typeof(InventoryInWorld).GetMethod(
            "Rotate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo inventoryActivateObjectsMethod = typeof(InventoryInWorld).GetMethod(
            "ActivateObjs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo itemBoxSectionIsDrawerField = AccessTools.Field(
            typeof(ItemBoxSetToInventory), "isDrawer");
        private static readonly FieldInfo itemBoxSectionParentField = AccessTools.Field(
            typeof(ItemBoxSetToInventory), "myItemParent");
        private static readonly MethodInfo getReachDistanceMethod = typeof(Player).GetMethod(
            "GetReachDistance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static EquippedManager motionManager;
        private static ItemInHand motionItem;
        private static Transform motionAnchor;
        private static Transform rightWrist;
        private static Transform leftHandRoot;
        private static Transform leftWrist;
        private static Transform leftHandAnchor;
        private static Transform originalLeftHandParent;
        private static int originalLeftHandSibling;
        private static Vector3 originalLeftHandLocalPosition;
        private static Quaternion originalLeftHandLocalRotation;
        private static Vector3 originalLeftHandLocalScale;
        private static Vector3 trackedLeftHandLocalScale;
        private static bool originalLeftHandActive;
        private static bool leftHandDetached;
        private static bool haveOriginalLeftHandTransform;
        private static Transform leftHandVisualRoot;
        private static GameObject leftHandVisualObject;
        private static Mesh leftHandVisualMesh;
        private static bool usingBakedLeftHand;
        private static Renderer[] leftHandRenderers = new Renderer[0];
        private static Transform originalItemParent;
        private static int originalItemSibling;
        private static Vector3 originalItemLocalPosition;
        private static Quaternion originalItemLocalRotation;
        private static Vector3 originalItemLocalScale;
        private static Quaternion rightGripToWristRotation;
        private static Quaternion leftGripToWristRotation;
        private static Quaternion rightGripToItemRotation;
        private static bool haveRightCalibration;
        private static bool haveLeftCalibration;
        private static bool haveItemCalibration;
        private static bool motionPoseValid;
        private static bool leftPoseValid;
        private static bool twoHanded;
        private static bool previousLeftGripPressed;
        private static float leftCalibrationHoldStarted = -1f;
        private static bool leftCalibrationHoldTriggered;
        private static bool leftCalibrationLoaded;
        private static bool haveUserLeftHandCalibration;
        private static Quaternion userLeftGripToHandRotation;
        private static string leftHandCalibrationFilePath;
        private static Vector3 lockedLeftHandItemLocalPosition;
        private static Quaternion lockedLeftHandItemLocalRotation;
        private static string lockedSupportGripName;
        private static bool lockedSupportGripSteersWeapon;
        private static float lockedSupportGripReleaseDistance;
        private static Quaternion twoHandControllerCorrection;
        private static Vector3 rightGripWorldPosition;
        private static Quaternion rightGripWorldRotation;
        private static Vector3 rightAimWorldPosition;
        private static Quaternion rightAimWorldRotation;
        private static Vector3 leftGripWorldPosition;
        private static Quaternion leftGripWorldRotation;
        private static Vector3 leftAimWorldPosition;
        private static Quaternion leftAimWorldRotation;
        private static Vector3 rightGripLocalPosition;
        private static Quaternion rightGripLocalRotation;
        private static Vector3 rightAimLocalPosition;
        private static Quaternion rightAimLocalRotation;
        private static Vector3 motionOriginPosition;
        private static Quaternion motionOriginRotation;
        private static Vector3 motionRigPosition;
        private static Quaternion motionRigRotation;
        private static bool motionContextValid;
        private static Vector3 lastGameplayRigPosition;
        private static Quaternion lastGameplayRigRotation = Quaternion.identity;
        private static bool haveLastGameplayRig;
        private static bool interactionRigLocked;
        private static Vector3 lockedInteractionRigPosition;
        private static Quaternion lockedInteractionRigRotation = Quaternion.identity;
        private static int motionDiagnosticFrame;
        private static bool gameplayPatchesInstalled;
        private static FieldInfo coreTouchGamepadField;
        private static bool leftHandDpadMode;
        private static bool behindHeadInputHookInstalled;
        private static Gamepad behindHeadGamepad;
        private static bool behindHeadFilesUpLatched;
        private static bool previousPhysicalWeaponGripPressed;
        private static bool suppressNormalRightGripWeaponSwitch;
        private static readonly MethodInfo openFilesGamepadMethod = AccessTools.Method(
            typeof(Player), "OpenMenuGamepad");
        private static Vector3 gunRayOrigin;
        private static Vector3 gunRayDirection;
        private static Vector3 gunRayTarget;
        // The OpenXR Touch aim pose sits slightly left of MFN's authored barrel line.
        // Correct the one shared ray so the sight, hitscan target, and spawned projectiles
        // all remain coincident instead of applying separate per-weapon visual fixes.
        private const float GunAimYawCorrectionDegrees = 6.0f;
        private static GameObject muzzleSight;
        private static Material muzzleSightMaterial;
        private static readonly FieldInfo wrenchHitSoundField = typeof(EquippedManager).GetField(
            "wrenchHit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static bool wrenchSampleValid;
        private const float PhysicalWrenchReach = 0.36f;
        private const float PhysicalWrenchHeadRadius = 0.045f;
        private static Vector3 wrenchTipOffsetInAimSpace;
        private static Vector3 previousWrenchPhysicalTipLocal;
        private static Vector3 previousWrenchGripLocal;
        private static float previousWrenchSampleTime;
        private static bool wrenchSwingActive;
        private static Vector3 wrenchSwingStartTipLocal;
        private static Vector3 wrenchSwingStartGripLocal;
        private static float wrenchSwingStartTime;
        private static float wrenchSwingDistance;
        private static float wrenchGripDistance;
        private static float wrenchSwingPeakSpeed;
        private static float wrenchGripPeakSpeed;
        private static float lastWrenchDamageTime = -999f;
        private static int lastWrenchPhysicsFrame = -1;
        private static InventoryInWorld physicalInventory;
        private static InventoryRowOfSquares[] physicalInventoryRows;
        private static ItemInInventory physicalInventoryHeldItem;
        private static bool physicalInventoryPositioned;
        private static bool previousInventoryGripPressed;
        private static Vector3 physicalItemOriginalLocalPosition;
        private static Quaternion physicalItemOriginalLocalRotation;
        private static Vector3 physicalGrabPositionOffset;
        private static Quaternion physicalGrabRotationOffset;
        private static Quaternion physicalGrabControllerRotation;
        private static int physicalItemOriginalX;
        private static int physicalItemOriginalY;
        private static int physicalItemOriginalRotation;
        private static bool previousInventoryTriggerPressed;
        private static bool previousInventoryPrimaryPressed;
        private static bool previousMenuPointerLeftTriggerPressed;
        private static bool previousInventoryRotatePressed;
        private static bool toolboxSectionChoiceActive;
        private static int inventoryPointerFrame = -1;
        private static GameObject inventoryPointerDot;
        private static Material inventoryPointerMaterial;
        private static LineRenderer inventoryPointerLine;
        private static Transform menuRightHandVisualRoot;
        private static GameObject menuRightHandVisualObject;
        private static Mesh menuRightHandVisualMesh;
        private static Quaternion rightGripToMenuHandRotation = Quaternion.identity;
        private static bool menuPointerInputActive;
        private static Interactable menuPointerHoveredInteractable;
        private static Interactable flatMenuPointerHoveredInteractable;
        private static bool flatMenuPointerActive;
        private static bool previousFlatMenuTriggerPressed;
        private static bool previousFlatMenuPrimaryPressed;
        private static int flatMenuPointerFrame = -1;
        private static int flatMenuPointerMode = -1;
        private static ItemInInventory menuPointerHoveredItem;
        private static InventoryInWorld menuPointerInventory;
        private static InventorySquare menuPointerSquare;
        private static ItemInInventory menuPointerHighlightItem;
        private static InventoryInWorld menuPointerHighlightInventory;
        private static Camera interactionPointerCamera;
        private static bool interactionPointerCameraActive;
        private static bool interactionPointerUsesStableRig;
        private static Vector3 interactionPointerStablePosition;
        private static Quaternion interactionPointerStableRotation = Quaternion.identity;
        private static readonly Dictionary<GameObject, int> inventoryDropdownOriginalLayers =
            new Dictionary<GameObject, int>();
        private static readonly Dictionary<Transform, Vector3> inventoryDropdownOriginalScales =
            new Dictionary<Transform, Vector3>();
        private static readonly Dictionary<Transform, Vector3> inventoryDropdownOriginalPositions =
            new Dictionary<Transform, Vector3>();
        private static readonly Dictionary<Transform, float> inventoryDropdownFirstSeenTimes =
            new Dictionary<Transform, float>();
        private static InventoryInWorld offsetToolboxDrawer;
        private static Vector3 offsetToolboxDrawerLocalPosition;
        private static bool haveOffsetToolboxDrawerPosition;
        private static bool cutscenePositionFollowActive;
        private static Camera cutscenePositionFollowSource;
        private static Vector3 cutsceneSourceToRigStart;

        public static void ApplyUserSettings(bool dotEnabled, float dotDistance, float dotSize,
            float configuredHudDistance, float configuredHudScale, float configuredHudHeight,
            float configuredMenuDistance, float configuredMenuScale)
        {
            aimingDotEnabled = dotEnabled;
            aimingDotDistance = Mathf.Clamp(dotDistance, 0.25f, 10f);
            aimingDotSize = Mathf.Clamp(dotSize, 0.002f, 0.05f);
            hudDistance = Mathf.Clamp(configuredHudDistance, 0.5f, 1000f);
            hudScale = Mathf.Clamp(configuredHudScale, 0.25f, 2f);
            hudHeightOffset = Mathf.Clamp(configuredHudHeight, -2f, 2f);
            menuDistance = Mathf.Clamp(configuredMenuDistance, 1f, 20f);
            menuScale = Mathf.Clamp(configuredMenuScale, 0.25f, 2f);
            settingsRevision++;
            if (!aimingDotEnabled && muzzleSight != null)
                muzzleSight.SetActive(false);
        }

        public static void ApplyUiScreenSettings(bool onlyMainPauseAndFiles)
        {
            flatScreensOnlyForMainPauseAndFiles = onlyMainPauseAndFiles;
        }

        public static void ApplyInteractionCameraSettings(bool allowMovement)
        {
            interactionCameraMovement = allowMovement;
            if (allowMovement)
                interactionRigLocked = false;
        }

        internal static void BeginInteractionCameraLock()
        {
            if (interactionCameraMovement || interactionRigLocked)
                return;
            lockedInteractionRigPosition = haveLastGameplayRig
                ? lastGameplayRigPosition
                : motionRigPosition;
            lockedInteractionRigRotation = haveLastGameplayRig
                ? lastGameplayRigRotation
                : motionRigRotation;
            interactionRigLocked = true;
        }

        internal static void EndInteractionCameraLock()
        {
            interactionRigLocked = false;
        }

        public static void ApplyPhysicalWeaponSwitchingSettings(bool enabled)
        {
            physicalWeaponSwitching = enabled;
            previousPhysicalWeaponGripPressed = false;
            suppressNormalRightGripWeaponSwitch = enabled;
        }

        public static void ApplyMenuPointerSettings(bool enabled)
        {
            menuPointerEnabled = enabled;
            previousInventoryTriggerPressed = false;
            previousInventoryPrimaryPressed = false;
            previousMenuPointerLeftTriggerPressed = false;
            previousInventoryRotatePressed = false;
            if (!enabled)
            {
                ResetMenuPointerInteraction();
                ResetFlatMenuPointerInteraction();
            }
        }

        public static void ConfigureTrackedPairPost(Camera source, Camera left, Camera right,
            RenderTexture leftTexture, RenderTexture rightTexture,
            bool isWorld, bool isHud, bool gameplay)
        {
            if (source == null || left == null || right == null ||
                leftTexture == null || rightTexture == null)
                return;

            var physicalInventoryActive = IsPhysicalInventoryActive();
            var cutsceneActive = IsCutsceneActive();
            if (isWorld && !physicalInventoryActive)
                RestoreToolboxDrawerPosition();
            if (isWorld)
            {
                interactionPointerCameraActive = false;
                interactionPointerUsesStableRig = false;
                SetStereoOutlineActive(left, right, source, false);
                if (physicalInventoryActive)
                    ConfigurePhysicalInventoryWorld(source, left, right,
                        leftTexture, rightTexture);
                else if (gameplay || cutsceneActive)
                    ConfigureGameplayWorld(source, left, right, leftTexture, rightTexture,
                        gameplay);
                else if (ShouldUseFlatUiScreen())
                    ConfigureMenuScreen(source, left, right, leftTexture, rightTexture);
                else
                    ConfigureWorldAttachedUi(source, left, right, leftTexture, rightTexture);
            }
            else if (isHud)
            {
                ConfigureHud(source, left, right, leftTexture, rightTexture,
                    gameplay || physicalInventoryActive || cutsceneActive);
            }

            // MFN's original cameras still have to stay alive because they drive camera
            // effects and the desktop mirror.  Once the mirror is active, however, drawing
            // their scene geometry to the backbuffer is pure duplicate work: the two eye
            // cameras have already rendered it and MirrorBlitEffect replaces the result.
            // Keep the camera lifecycle intact while making that redundant pass empty.
            EnsureSourceBackbufferOptimizer(source);
        }

        public static void ConfigureHands(Camera source, Camera left, Camera right,
            RenderTexture leftTexture, RenderTexture rightTexture, bool motionActive)
        {
            if (source == null || left == null || right == null ||
                leftTexture == null || rightTexture == null)
                return;

            EnsureSourceBackbufferOptimizer(source);

            // Re-sample immediately before the hands cameras render. This is a late pose update,
            // so controller motion is not a frame behind the headset and animation cannot pull
            // the wrists away from their physical Touch controller positions.
            if (motionActive && motionContextValid)
            {
                RefreshControllerPoses();
                if (IsPhysicalInventoryActive())
                {
                    if (menuPointerEnabled)
                        UpdateMenuPointerInteraction(Player.current, GetPhysicalInventory());
                }
                else
                {
                    ApplyTrackedTransforms();
                }
            }

            CopyOverlayCamera(source, left, leftTexture);
            CopyOverlayCamera(source, right, rightTexture);
            if (motionActive && haveWorldEyeData)
            {
                left.transform.SetPositionAndRotation(worldLeftEyePosition, worldLeftEyeRotation);
                right.transform.SetPositionAndRotation(worldRightEyePosition, worldRightEyeRotation);
                left.projectionMatrix = worldLeftProjection;
                right.projectionMatrix = worldRightProjection;
            }
            else
            {
                var position = source.transform.position;
                var rotation = source.transform.rotation;
                left.transform.SetPositionAndRotation(position, rotation);
                right.transform.SetPositionAndRotation(position, rotation);
                var projection = haveWorldEyeData
                    ? worldCenterProjection
                    : Matrix4x4.Perspective(source.fieldOfView,
                        leftTexture.width / (float)Mathf.Max(1, leftTexture.height),
                        source.nearClipPlane, source.farClipPlane);
                left.projectionMatrix = projection;
                right.projectionMatrix = projection;
            }

            // UI modes must not have the first-person weapon, authored arms, or floating
            // VR hands composited over them. Disabling only the cloned Hands cameras
            // leaves gameplay/equipment state untouched, and CopyOverlayCamera restores
            // everything automatically on the first frame back in gameplay.
            if (IsUiModeActive())
            {
                left.enabled = false;
                right.enabled = false;
            }
        }

        public static bool TickNativeHands(Player player, Vector3 originPosition,
            Quaternion originRotation, Vector3 rigPosition, Quaternion rigRotation,
            bool hasOrigin, bool useRig)
        {
            var inventory = GetPhysicalInventory(player);
            var physicalInventoryActive = inventory != null;
            if (Player.current != null && player != Player.current)
                return motionPoseValid && (motionItem != null || physicalInventoryActive);
            motionOriginPosition = originPosition;
            motionOriginRotation = originRotation;
            // Keep a genuinely stable gameplay base for the normal Y inventory. MFN
            // animates rigPosition onto its inventory camera after Y is pressed; saving
            // that animated value made the VR view move with the flat-camera animation.
            if (!physicalInventoryActive && !IsUiModeActive() && !IsCutsceneActive() &&
                hasOrigin && useRig)
            {
                lastGameplayRigPosition = rigPosition;
                lastGameplayRigRotation = rigRotation;
                haveLastGameplayRig = true;
            }
            motionRigPosition = rigPosition;
            motionRigRotation = rigRotation;
            var menuPointerContext = menuPointerEnabled && IsUiModeActive();
            motionContextValid = player != null && hasOrigin &&
                (useRig || physicalInventoryActive || menuPointerContext);
            if (!motionContextValid)
            {
                motionPoseValid = false;
                return false;
            }

            try
            {
                EnsureGameplayPatches();
                RefreshControllerPoses();
                UpdatePhysicalWeaponSwitching(player, physicalInventoryActive);
                var menuPointerActive = menuPointerEnabled && motionPoseValid &&
                    (physicalInventoryActive ||
                     (IsUiModeActive() && !ShouldUseFlatUiScreen()));
                if (menuPointerActive)
                {
                    motionManager = player.GetEquipManager();
                    BindHeldItem(GetHeldItem(motionManager));
                    UpdateMenuPointerInteraction(player, inventory);
                    return false;
                }

                ResetMenuPointerInteraction();
                if (physicalInventoryActive)
                {
                    EnsurePhysicalInventoryState(inventory);
                    return false;
                }

                ResetPhysicalInventoryStateIfClosed();
                motionManager = player.GetEquipManager();
                BindHeldItem(GetHeldItem(motionManager));
                UpdateTwoHandGrip();
                ApplyTrackedTransforms();
                UpdatePhysicalWrenchDamage();
                return motionPoseValid && motionItem != null;
            }
            catch (Exception exception)
            {
                motionPoseValid = false;
                if (Time.frameCount >= motionDiagnosticFrame)
                {
                    motionDiagnosticFrame = Time.frameCount + 240;
                    Debug.LogWarning("MFNVR floating-hand update failed: " + exception);
                }
                return false;
            }
        }

        private static void EnsureGameplayPatches()
        {
            if (gameplayPatchesInstalled)
                return;
            gameplayPatchesInstalled = true;
            try
            {
                var harmony = new Harmony("com.mfnvr.direct-gun-ray");
                harmony.PatchAll(typeof(RenderBridge).Assembly);
                var flatWrenchPrefix = new HarmonyMethod(typeof(RenderBridge).GetMethod(
                    nameof(SuppressFlatWrenchPrefix), BindingFlags.Static | BindingFlags.NonPublic));
                foreach (var methodName in new[]
                {
                    "StartWrenchSwing", "StartMeleeCollision", "StartMeleeCollisionHard"
                })
                {
                    var method = AccessTools.Method(typeof(EquippedManager), methodName);
                    if (method != null)
                        harmony.Patch(method, prefix: flatWrenchPrefix);
                }
                // Keep the stable MFNVR camera core installed. Patch only the end of its
                // OpenXR-to-virtual-Xbox translation so this gesture works identically
                // through Oculus OpenXR and SteamVR OpenXR.
                var coreType = AccessTools.TypeByName("MFNVR.MFNVRPlugin");
                var legacyWrenchUpdate = coreType != null
                    ? AccessTools.Method(coreType, "UpdatePhysicalWrench")
                    : null;
                var legacyWrenchPrefix = new HarmonyMethod(typeof(RenderBridge).GetMethod(
                    nameof(SuppressLegacyPhysicalWrenchPrefix), BindingFlags.Static |
                    BindingFlags.NonPublic));
                if (legacyWrenchUpdate != null && legacyWrenchPrefix.method != null)
                {
                    harmony.Patch(legacyWrenchUpdate, prefix: legacyWrenchPrefix);
                    Debug.Log("MFNVR: disabled the legacy duplicate physical-wrench detector.");
                }
                var updateTouchGamepad = coreType != null
                    ? AccessTools.Method(coreType, "UpdateTouchGamepad")
                    : null;
                var dpadPostfix = new HarmonyMethod(typeof(RenderBridge).GetMethod(
                    nameof(ApplyBehindHeadDpadPostfix), BindingFlags.Static |
                    BindingFlags.NonPublic));
                if (updateTouchGamepad != null && dpadPostfix.method != null)
                {
                    coreTouchGamepadField = AccessTools.Field(coreType, "touchGamepad");
                    harmony.Patch(updateTouchGamepad, postfix: dpadPostfix);
                    if (!behindHeadInputHookInstalled)
                    {
                        InputSystem.onBeforeUpdate += SuppressBehindHeadWalkingBeforeInputUpdate;
                        behindHeadInputHookInstalled = true;
                    }
                    Debug.Log("MFNVR: behind-head stick-up Files gesture installed.");
                }
                var swapWeaponRight = AccessTools.Method(typeof(Player),
                    nameof(Player.SwapWeaponRight));
                var physicalSwitchPrefix = new HarmonyMethod(typeof(RenderBridge).GetMethod(
                    nameof(SuppressVirtualRightGripWeaponSwitchPrefix),
                    BindingFlags.Static | BindingFlags.NonPublic));
                if (swapWeaponRight != null && physicalSwitchPrefix.method != null)
                    harmony.Patch(swapWeaponRight, prefix: physicalSwitchPrefix);
                Debug.Log("MFNVR: direct projectile, reticle, and physical-wrench patches installed.");
            }
            catch (Exception exception)
            {
                Debug.LogError("MFNVR: could not install direct gun-ray patches: " + exception);
            }
        }

        private static void ApplyBehindHeadDpadPostfix(object __instance)
        {
            try
            {
                var gamepad = coreTouchGamepadField?.GetValue(__instance) as Gamepad;
                if (gamepad == null || !gamepad.added)
                    return;
                behindHeadGamepad = gamepad;

                var behindHead = IsLeftHandBehindHead();
                if (behindHead != leftHandDpadMode)
                {
                    leftHandDpadMode = behindHead;
                    Debug.Log(behindHead
                        ? "MFNVR: behind-head Files gesture active."
                        : "MFNVR: left stick returned to movement.");
                }
                if (!behindHead)
                {
                    behindHeadFilesUpLatched = false;
                    return;
                }

                float stickX, stickY, trigger, grip;
                int primary, secondary, stickClick, menu;
                if (MFN_GetControllerInput(0, out stickX, out stickY, out trigger,
                        out grip, out primary, out secondary, out stickClick,
                        out menu) == 0)
                    return;

                // This deliberately calls MFN's genuine gamepad Files handler instead
                // of impersonating a D-pad. Trigger once per upward stick push and make
                // the player return the stick below the release threshold before it can
                // open again.
                if (stickY <= 0.35f)
                    behindHeadFilesUpLatched = false;
                if (stickY >= 0.70f && !behindHeadFilesUpLatched)
                {
                    behindHeadFilesUpLatched = true;
                    var player = Player.current;
                    if (player != null && openFilesGamepadMethod != null)
                    {
                        openFilesGamepadMethod.Invoke(player,
                            new object[] { default(InputAction.CallbackContext) });
                        Debug.Log("MFNVR: behind-head stick-up opened Files.");
                    }
                }
            }
            catch (Exception exception)
            {
                leftHandDpadMode = false;
                if (Time.frameCount >= motionDiagnosticFrame)
                {
                    motionDiagnosticFrame = Time.frameCount + 240;
                    Debug.LogWarning("MFNVR behind-head Files gesture failed: " + exception);
                }
            }
        }

        private static void SuppressBehindHeadWalkingBeforeInputUpdate()
        {
            // The stable camera core queues its complete virtual-gamepad state from
            // Player.Update. Unity processes that state at the next InputSystem update.
            // Override locomotion in onBeforeUpdate while the gesture is active. Files
            // itself is opened explicitly above and no D-pad state is synthesized.
            var gamepad = behindHeadGamepad;
            if (gamepad == null || !gamepad.added)
                return;
            if (leftHandDpadMode)
                InputSystem.QueueDeltaStateEvent(gamepad.leftStick, Vector2.zero);
            // The menu pointer reads raw OpenXR input before MFN's virtual Xbox layer.
            // Suppress the translated copies while it is active so one trigger pull cannot
            // both click the pointed target and activate the old gamepad selection.
            if (menuPointerInputActive)
            {
                InputSystem.QueueDeltaStateEvent(gamepad.leftTrigger, 0f);
                InputSystem.QueueDeltaStateEvent(gamepad.rightTrigger, 0f);
                InputSystem.QueueDeltaStateEvent(gamepad.rightStickButton, 0f);
                // A is handled directly against the tracked pointer target. Do not
                // also let MFN activate the node last selected by its joystick cursor.
                InputSystem.QueueDeltaStateEvent(gamepad.buttonSouth, 0f);
            }
            // In physical-switch mode MFN's ordinary right-shoulder weapon-cycle
            // binding is always disabled. The raw OpenXR grip is handled exclusively
            // by the two tracked holster zones below.
            if (suppressNormalRightGripWeaponSwitch)
                InputSystem.QueueDeltaStateEvent(gamepad.rightShoulder, 0f);
        }

        private static bool SuppressVirtualRightGripWeaponSwitchPrefix(
            InputAction.CallbackContext context)
        {
            if (!physicalWeaponSwitching)
                return true;
            var control = context.control;
            // Direct calls made by MFN for the mouse wheel carry a default context and
            // must keep working. Block only callbacks originating from our OpenXR-backed
            // virtual gamepad; physical holsters call EquipWeapon# directly.
            return control == null || control.device != behindHeadGamepad;
        }

        private static void UpdatePhysicalWeaponSwitching(Player player,
            bool physicalInventoryActive)
        {
            suppressNormalRightGripWeaponSwitch = physicalWeaponSwitching;
            if (!physicalWeaponSwitching)
            {
                previousPhysicalWeaponGripPressed = false;
                return;
            }

            float stickX, stickY, trigger, squeeze;
            int primary, secondary, stickClick, menu;
            var haveInput = MFN_GetControllerInput(1, out stickX, out stickY,
                out trigger, out squeeze, out primary, out secondary,
                out stickClick, out menu) != 0;
            var gripPressed = haveInput && squeeze >= 0.72f;
            var gripStarted = gripPressed && !previousPhysicalWeaponGripPressed;
            previousPhysicalWeaponGripPressed = gripPressed;
            if (!gripStarted || player == null || !motionPoseValid ||
                physicalInventoryActive || IsUiModeActive() || IsCutsceneActive())
                return;

            var headPosition = (worldLeftEyePosition + worldRightEyePosition) * 0.5f;
            var headRotation = Quaternion.Slerp(worldLeftEyeRotation,
                worldRightEyeRotation, 0.5f);
            var forward = headRotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = motionRigRotation * Vector3.forward;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward).normalized;

            // Head-relative body anchors make the zones follow room-scale movement and
            // the player's current facing direction without depending on MFN's flat
            // camera yaw. The radii are deliberately generous enough for Rift Touch.
            var hip = headPosition + right * 0.25f - Vector3.up * 0.56f - forward * 0.03f;
            var shoulder = headPosition + right * 0.20f - Vector3.up * 0.10f - forward * 0.25f;
            var hipDistance = Vector3.Distance(rightGripWorldPosition, hip);
            var shoulderDistance = Vector3.Distance(rightGripWorldPosition, shoulder);

            if (hipDistance <= 0.30f)
            {
                CyclePhysicalWeapon(player, new[]
                {
                    InventoryItem.Wrench,
                    InventoryItem.LetterGrenade
                }, "right hip");
            }
            else if (shoulderDistance <= 0.34f)
            {
                CyclePhysicalWeapon(player, new[]
                {
                    InventoryItem.BoxingGloveGun,
                    InventoryItem.BoxingGloveShotgun,
                    InventoryItem.FinalGun
                }, "right shoulder");
            }
        }

        private static void CyclePhysicalWeapon(Player player,
            InventoryItem[] cycle, string holsterName)
        {
            var manager = player.GetEquipManager();
            if (manager == null || !manager.CanSwitch())
                return;
            var current = manager.GetCurrentItem();
            var currentIndex = Array.IndexOf(cycle, current);
            for (var offset = currentIndex >= 0 ? 1 : 0; offset < cycle.Length; offset++)
            {
                var index = currentIndex >= 0
                    ? (currentIndex + offset) % cycle.Length
                    : offset;
                var candidate = cycle[index];
                if (!PhysicalWeaponIsAvailable(candidate))
                    continue;
                EquipPhysicalWeapon(player, candidate);
                Debug.Log("MFNVR: " + holsterName + " selected " + candidate + ".");
                return;
            }
        }

        private static bool PhysicalWeaponIsAvailable(InventoryItem item)
        {
            if (item == InventoryItem.LetterGrenade &&
                SaveData.GetPermanentData("InfiniteAmmo") == 1)
                return true;
            return InventoryManager.GetItemAmount(item, 0, 0).total > 0;
        }

        private static void EquipPhysicalWeapon(Player player, InventoryItem item)
        {
            var context = default(InputAction.CallbackContext);
            switch (item)
            {
                case InventoryItem.Wrench:
                    player.EquipWeapon1(context);
                    break;
                case InventoryItem.BoxingGloveGun:
                    player.EquipWeapon2(context);
                    break;
                case InventoryItem.BoxingGloveShotgun:
                    player.EquipWeapon3(context);
                    break;
                case InventoryItem.LetterGrenade:
                    player.EquipWeapon4(context);
                    break;
                case InventoryItem.FinalGun:
                    player.EquipWeapon5(context);
                    break;
            }
        }

        private static bool IsLeftHandBehindHead()
        {
            if (!motionContextValid || !haveWorldEyeData)
            {
                leftHandDpadMode = false;
                return false;
            }

            Vector3 rawGripPosition;
            Quaternion unusedRotation;
            if (!TryGetControllerPose(0, false, out rawGripPosition,
                    out unusedRotation))
            {
                leftHandDpadMode = false;
                return false;
            }

            var gripLocalPosition = Quaternion.Inverse(motionOriginRotation) *
                (rawGripPosition - motionOriginPosition);
            var handPosition = motionRigPosition +
                motionRigRotation * gripLocalPosition;
            var headPosition = (worldLeftEyePosition + worldRightEyePosition) * 0.5f;
            var headRotation = Quaternion.Slerp(worldLeftEyeRotation,
                worldRightEyeRotation, 0.5f);
            var headForward = headRotation * Vector3.forward;
            headForward.y = 0f;
            if (headForward.sqrMagnitude < 0.001f)
                headForward = motionRigRotation * Vector3.forward;
            headForward.Normalize();

            var fromHead = handPosition - headPosition;
            var distance = fromHead.magnitude;
            var behindDistance = -Vector3.Dot(fromHead, headForward);
            var vertical = fromHead.y;

            // Separate enter/exit thresholds prevent mode flicker at the shoulder line.
            if (leftHandDpadMode)
                return behindDistance > 0.025f && distance < 0.90f &&
                       vertical > -0.50f && vertical < 0.45f;
            return behindDistance > 0.10f && distance < 0.75f &&
                   vertical > -0.38f && vertical < 0.38f;
        }

        public static Vector3 GetMotionAimPosition()
        {
            return rightAimWorldPosition;
        }

        public static Quaternion GetMotionAimRotation()
        {
            return rightAimWorldRotation;
        }

        public static Vector3 GetMotionGripLocalPosition()
        {
            return rightGripLocalPosition;
        }

        public static Quaternion GetMotionAimLocalRotation()
        {
            return rightAimLocalRotation;
        }

        private static ItemInHand GetHeldItem(EquippedManager manager)
        {
            return manager != null && currentlyHoldingField != null
                ? currentlyHoldingField.GetValue(manager) as ItemInHand
                : null;
        }

        private static void BindHeldItem(ItemInHand item)
        {
            if (item == motionItem)
                return;

            RestorePreviousItem();
            motionItem = item;
            ResetPhysicalWrenchSample();
            rightWrist = null;
            leftHandRoot = null;
            leftWrist = null;
            haveRightCalibration = false;
            haveLeftCalibration = false;
            haveItemCalibration = false;
            twoHanded = false;
            lockedSupportGripName = null;
            lockedSupportGripSteersWeapon = false;
            lockedSupportGripReleaseDistance = 0f;
            if (motionItem == null)
                return;

            if (motionAnchor == null)
            {
                var anchorObject = new GameObject("MFN VR Native Floating Hands");
                motionAnchor = anchorObject.transform;
            }

            var itemTransform = motionItem.transform;
            originalItemParent = itemTransform.parent;
            originalItemSibling = itemTransform.GetSiblingIndex();
            originalItemLocalPosition = itemTransform.localPosition;
            originalItemLocalRotation = itemTransform.localRotation;
            originalItemLocalScale = itemTransform.localScale;
            rightWrist = FindWrist(motionItem, "PL_HAND_R");
            leftHandRoot = FindNamedTransform(motionItem, "PL_HAND_L");
            leftWrist = FindWristUnder(leftHandRoot);
            itemTransform.SetParent(motionAnchor, true);
            LoadLeftHandCalibration();
            NormalizeHandScale(itemTransform, rightWrist);
            ShrinkWrenchAssembly(motionItem);
            PrepareFloatingHands(motionItem);
            CacheLeftHandRenderers();
            CaptureOriginalLeftHandTransform();
            usingBakedLeftHand = leftHandVisualRoot != null &&
                leftHandVisualObject != null && leftHandVisualMesh != null;
            if (!usingBakedLeftHand)
                usingBakedLeftHand = CreateIndependentLeftHandVisual();
            if (!usingBakedLeftHand)
                DetachLeftHandFromWeaponAnimator();
            if (menuPointerEnabled && menuRightHandVisualRoot == null)
                CreateMenuRightHandVisual();
            EnsureLeftHandVisible();
            Debug.Log("MFNVR: bound " + motionItem.name +
                " to direct floating-hand tracking; rightWrist=" + (rightWrist != null) +
                ", leftWrist=" + (leftWrist != null) +
                ", independentLeftHand=" + (usingBakedLeftHand || leftHandDetached) +
                ", bakedLeftHand=" + usingBakedLeftHand + ".");
        }

        private static void RestorePreviousItem()
        {
            if (motionItem == null)
                return;

            RestoreLeftHandToWeapon();
            var itemTransform = motionItem.transform;
            if (itemTransform != null && itemTransform.parent == motionAnchor)
            {
                itemTransform.SetParent(originalItemParent, false);
                itemTransform.localPosition = originalItemLocalPosition;
                itemTransform.localRotation = originalItemLocalRotation;
                itemTransform.localScale = originalItemLocalScale;
                if (originalItemParent != null)
                    itemTransform.SetSiblingIndex(Mathf.Clamp(originalItemSibling, 0,
                        Mathf.Max(0, originalItemParent.childCount - 1)));
            }
            motionItem = null;
            leftHandRenderers = new Renderer[0];
        }

        private static void CaptureOriginalLeftHandTransform()
        {
            if (leftHandRoot == null)
                return;
            originalLeftHandParent = leftHandRoot.parent;
            originalLeftHandSibling = leftHandRoot.GetSiblingIndex();
            originalLeftHandLocalPosition = leftHandRoot.localPosition;
            originalLeftHandLocalRotation = leftHandRoot.localRotation;
            originalLeftHandLocalScale = leftHandRoot.localScale;
            originalLeftHandActive = leftHandRoot.gameObject.activeSelf;
            haveOriginalLeftHandTransform = true;
        }

        private static void CacheLeftHandRenderers()
        {
            if (motionItem == null || leftHandRoot == null)
            {
                leftHandRenderers = new Renderer[0];
                return;
            }

            var renderers = new List<Renderer>();
            foreach (var renderer in leftHandRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null && !renderers.Contains(renderer))
                    renderers.Add(renderer);
            }
            foreach (var renderer in motionItem.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null || renderer.bones == null)
                    continue;
                foreach (var bone in renderer.bones)
                {
                    if (bone != null && (bone == leftHandRoot || bone.IsChildOf(leftHandRoot)))
                    {
                        if (!renderers.Contains(renderer))
                            renderers.Add(renderer);
                        break;
                    }
                }
            }
            leftHandRenderers = renderers.ToArray();
        }

        private static void DetachLeftHandFromWeaponAnimator()
        {
            if (leftHandRoot == null || leftHandDetached)
                return;

            if (leftHandAnchor == null)
            {
                var anchorObject = new GameObject("MFN VR Independent Left Hand");
                leftHandAnchor = anchorObject.transform;
            }

            if (!haveOriginalLeftHandTransform)
                CaptureOriginalLeftHandTransform();
            leftHandRoot.SetParent(leftHandAnchor, true);
            trackedLeftHandLocalScale = leftHandRoot.localScale;
            leftHandDetached = true;
        }

        private static void RestoreLeftHandToWeapon()
        {
            if (leftHandRoot == null)
            {
                leftHandDetached = false;
                haveOriginalLeftHandTransform = false;
                return;
            }

            if (haveOriginalLeftHandTransform && originalLeftHandParent != null)
            {
                leftHandRoot.SetParent(originalLeftHandParent, false);
                leftHandRoot.localPosition = originalLeftHandLocalPosition;
                leftHandRoot.localRotation = originalLeftHandLocalRotation;
                leftHandRoot.localScale = originalLeftHandLocalScale;
                leftHandRoot.SetSiblingIndex(Mathf.Clamp(originalLeftHandSibling, 0,
                    Mathf.Max(0, originalLeftHandParent.childCount - 1)));
                leftHandRoot.gameObject.SetActive(originalLeftHandActive);
            }
            leftHandDetached = false;
            haveOriginalLeftHandTransform = false;
        }

        private static bool CreateIndependentLeftHandVisual()
        {
            if (motionItem == null || leftHandRoot == null || leftWrist == null)
                return false;
            try
            {
                if (leftHandAnchor == null)
                {
                    var anchorObject = new GameObject("MFN VR Independent Left Hand");
                    leftHandAnchor = anchorObject.transform;
                }

                foreach (var source in motionItem.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (source == null || source.sharedMesh == null ||
                        !RendererUsesBoneTree(source, leftHandRoot))
                        continue;

                    var baked = new Mesh();
                    baked.name = source.sharedMesh.name + " (MFN VR Gripped Left Hand Only)";
                    var openHandDirection = FindHandDirection(leftWrist);
                    var openPalmNormal = FindPalmNormal(leftWrist, openHandDirection);
                    var savedFingerRotations = ApplyGrippedFingerPose(leftWrist,
                        openPalmNormal);
                    try
                    {
                        source.BakeMesh(baked);
                    }
                    finally
                    {
                        RestoreFingerPose(savedFingerRotations);
                    }
                    var vertices = baked.vertices;
                    if (vertices == null || vertices.Length == 0)
                    {
                        UnityEngine.Object.Destroy(baked);
                        continue;
                    }

                    var wristPosition = leftWrist.position;
                    var handDirection = FindHandDirection(leftWrist);
                    var palmNormal = FindPalmNormal(leftWrist, handDirection);
                    var boneReach = FindHandBoneReach(leftWrist, handDirection);
                    if (boneReach < 0.025f)
                    {
                        UnityEngine.Object.Destroy(baked);
                        continue;
                    }
                    var canonicalRotation = StableLookRotation(handDirection, palmNormal);
                    var inverseCanonical = Quaternion.Inverse(canonicalRotation);
                    var handVertex = new bool[vertices.Length];
                    var worldDeltas = new Vector3[vertices.Length];
                    var maximumReach = 0f;
                    for (var vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        var delta = source.transform.TransformPoint(vertices[vertex]) - wristPosition;
                        worldDeltas[vertex] = delta;
                        var along = Vector3.Dot(delta, handDirection);
                        var lateral = delta - handDirection * along;
                        handVertex[vertex] = along >= -boneReach * 0.10f &&
                            along <= boneReach * 1.35f &&
                            lateral.sqrMagnitude <= boneReach * boneReach * 0.56f;
                        if (handVertex[vertex])
                            maximumReach = Mathf.Max(maximumReach,
                                Vector3.Dot(delta, handDirection));
                    }
                    if (maximumReach < 0.025f)
                    {
                        UnityEngine.Object.Destroy(baked);
                        continue;
                    }
                    var targetReach = 0.155f;
                    if (rightWrist != null)
                    {
                        var rightDirection = FindHandDirection(rightWrist);
                        var rightBoneReach = FindHandBoneReach(rightWrist, rightDirection);
                        var rightMaximumReach = 0f;
                        for (var vertex = 0; vertex < vertices.Length; vertex++)
                        {
                            var rightDelta = source.transform.TransformPoint(vertices[vertex]) -
                                rightWrist.position;
                            var rightAlong = Vector3.Dot(rightDelta, rightDirection);
                            var rightLateral = rightDelta - rightDirection * rightAlong;
                            if (rightBoneReach < 0.025f ||
                                rightAlong < -rightBoneReach * 0.10f ||
                                rightAlong > rightBoneReach * 1.35f ||
                                rightLateral.sqrMagnitude > rightBoneReach * rightBoneReach * 0.56f)
                                continue;
                            rightMaximumReach = Mathf.Max(rightMaximumReach,
                                Vector3.Dot(rightDelta, rightDirection));
                        }
                        if (rightMaximumReach >= 0.025f)
                            targetReach = rightMaximumReach;
                    }
                    var handScale = targetReach / maximumReach;

                    var bakedNormals = baked.normals;
                    var haveBakedNormals = bakedNormals != null &&
                        bakedNormals.Length == vertices.Length;
                    var canonicalVertices = new Vector3[vertices.Length];
                    var canonicalNormals = new Vector3[vertices.Length];
                    var normalMatrix = source.transform.localToWorldMatrix.inverse.transpose;
                    for (var vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        canonicalVertices[vertex] = inverseCanonical *
                            worldDeltas[vertex] * handScale;
                        if (haveBakedNormals)
                        {
                            var worldNormal = normalMatrix.MultiplyVector(
                                bakedNormals[vertex]).normalized;
                            canonicalNormals[vertex] =
                                (inverseCanonical * worldNormal).normalized;
                        }
                    }

                    var keptTriangles = 0;
                    for (var subMesh = 0; subMesh < baked.subMeshCount; subMesh++)
                    {
                        var triangles = baked.GetTriangles(subMesh);
                        var kept = new List<int>(triangles.Length / 4);
                        for (var triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                        {
                            var a = triangles[triangle];
                            var b = triangles[triangle + 1];
                            var c = triangles[triangle + 2];
                            var handCount = (handVertex[a] ? 1 : 0) +
                                (handVertex[b] ? 1 : 0) + (handVertex[c] ? 1 : 0);
                            // Never keep a boundary triangle that still owns an arm vertex.
                            // A single distant forearm vertex turns into the long inverted
                            // spikes seen in VR after the mesh is rebased around the wrist.
                            if (handCount != 3)
                                continue;
                            var outputB = b;
                            var outputC = c;
                            if (haveBakedNormals)
                            {
                                var faceNormal = Vector3.Cross(
                                    canonicalVertices[b] - canonicalVertices[a],
                                    canonicalVertices[c] - canonicalVertices[a]);
                                var expectedNormal = canonicalNormals[a] +
                                    canonicalNormals[b] + canonicalNormals[c];
                                if (Vector3.Dot(faceNormal, expectedNormal) < 0f)
                                {
                                    outputB = c;
                                    outputC = b;
                                }
                            }
                            kept.Add(a);
                            kept.Add(outputB);
                            kept.Add(outputC);
                            keptTriangles++;
                        }
                        baked.SetTriangles(kept, subMesh, false);
                    }
                    if (keptTriangles < 20)
                    {
                        UnityEngine.Object.Destroy(baked);
                        continue;
                    }

                    baked.vertices = canonicalVertices;
                    if (haveBakedNormals)
                        baked.normals = canonicalNormals;
                    else
                        baked.RecalculateNormals();
                    baked.RecalculateTangents();
                    baked.bounds = new Bounds(Vector3.forward * 0.065f,
                        new Vector3(0.36f, 0.36f, 0.36f));
                    var rootObject = new GameObject("MFN VR Tracked Left Hand Root");
                    leftHandVisualRoot = rootObject.transform;
                    leftHandVisualRoot.SetPositionAndRotation(leftWrist.position, canonicalRotation);
                    leftHandVisualRoot.SetParent(leftHandAnchor, true);

                    leftHandVisualObject = new GameObject("MFN VR Tracked Left Hand Mesh");
                    leftHandVisualObject.layer = source.gameObject.layer;
                    var visualTransform = leftHandVisualObject.transform;
                    visualTransform.SetParent(leftHandVisualRoot, false);
                    visualTransform.localPosition = Vector3.zero;
                    visualTransform.localRotation = Quaternion.identity;
                    visualTransform.localScale = Vector3.one;
                    leftHandVisualObject.AddComponent<MeshFilter>().sharedMesh = baked;
                    var visualRenderer = leftHandVisualObject.AddComponent<MeshRenderer>();
                    visualRenderer.sharedMaterials = CreateTwoSidedHandMaterials(
                        source.sharedMaterials);
                    visualRenderer.shadowCastingMode = source.shadowCastingMode;
                    visualRenderer.receiveShadows = source.receiveShadows;
                    leftHandVisualMesh = baked;
                    Debug.Log("MFNVR: created independent baked left hand with " +
                        keptTriangles + " triangles from " + source.name +
                        " at fixed " + targetReach.ToString("F3") +
                        "m reach matching the right hand.");
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: could not create independent left hand: " + exception);
            }
            return false;
        }

        private static bool CreateMenuRightHandVisual()
        {
            if (motionItem == null || rightWrist == null)
                return false;
            var rightRoot = FindNamedTransform(motionItem, "PL_HAND_R");
            if (rightRoot == null)
                return false;
            try
            {
                foreach (var source in motionItem.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (source == null || source.sharedMesh == null ||
                        !RendererUsesBoneTree(source, rightRoot))
                        continue;

                    var handDirection = FindHandDirection(rightWrist);
                    var palmNormal = FindPalmNormal(rightWrist, handDirection);
                    var savedFingerRotations = ApplyMenuPointerFingerPose(rightWrist,
                        handDirection, palmNormal);
                    var baked = new Mesh
                    {
                        name = source.sharedMesh.name + " (MFN VR Menu Right Hand Only)"
                    };
                    try
                    {
                        source.BakeMesh(baked);
                    }
                    finally
                    {
                        RestoreFingerPose(savedFingerRotations);
                    }

                    var vertices = baked.vertices;
                    if (vertices == null || vertices.Length == 0)
                    {
                        UnityEngine.Object.Destroy(baked);
                        continue;
                    }

                    var wristPosition = rightWrist.position;
                    var boneReach = FindHandBoneReach(rightWrist, handDirection);
                    if (boneReach < 0.025f)
                    {
                        UnityEngine.Object.Destroy(baked);
                        continue;
                    }
                    var canonicalRotation = StableLookRotation(handDirection, palmNormal);
                    var inverseCanonical = Quaternion.Inverse(canonicalRotation);
                    var handVertex = new bool[vertices.Length];
                    var worldDeltas = new Vector3[vertices.Length];
                    var maximumReach = 0f;
                    for (var vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        var delta = source.transform.TransformPoint(vertices[vertex]) -
                            wristPosition;
                        worldDeltas[vertex] = delta;
                        var along = Vector3.Dot(delta, handDirection);
                        var lateral = delta - handDirection * along;
                        handVertex[vertex] = along >= -boneReach * 0.10f &&
                            along <= boneReach * 1.35f &&
                            lateral.sqrMagnitude <= boneReach * boneReach * 0.56f;
                        if (handVertex[vertex])
                            maximumReach = Mathf.Max(maximumReach, along);
                    }
                    if (maximumReach < 0.025f)
                    {
                        UnityEngine.Object.Destroy(baked);
                        continue;
                    }

                    var scale = 0.155f / maximumReach;
                    var bakedNormals = baked.normals;
                    var haveNormals = bakedNormals != null &&
                        bakedNormals.Length == vertices.Length;
                    var canonicalVertices = new Vector3[vertices.Length];
                    var canonicalNormals = new Vector3[vertices.Length];
                    var normalMatrix = source.transform.localToWorldMatrix.inverse.transpose;
                    for (var vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        canonicalVertices[vertex] = inverseCanonical *
                            worldDeltas[vertex] * scale;
                        if (haveNormals)
                        {
                            var worldNormal = normalMatrix.MultiplyVector(
                                bakedNormals[vertex]).normalized;
                            canonicalNormals[vertex] =
                                (inverseCanonical * worldNormal).normalized;
                        }
                    }

                    var keptTriangles = 0;
                    for (var subMesh = 0; subMesh < baked.subMeshCount; subMesh++)
                    {
                        var triangles = baked.GetTriangles(subMesh);
                        var kept = new List<int>(triangles.Length / 4);
                        for (var triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                        {
                            var a = triangles[triangle];
                            var b = triangles[triangle + 1];
                            var c = triangles[triangle + 2];
                            if (!handVertex[a] || !handVertex[b] || !handVertex[c])
                                continue;
                            var outputB = b;
                            var outputC = c;
                            if (haveNormals)
                            {
                                var faceNormal = Vector3.Cross(
                                    canonicalVertices[b] - canonicalVertices[a],
                                    canonicalVertices[c] - canonicalVertices[a]);
                                var expected = canonicalNormals[a] + canonicalNormals[b] +
                                    canonicalNormals[c];
                                if (Vector3.Dot(faceNormal, expected) < 0f)
                                {
                                    outputB = c;
                                    outputC = b;
                                }
                            }
                            kept.Add(a);
                            kept.Add(outputB);
                            kept.Add(outputC);
                            keptTriangles++;
                        }
                        baked.SetTriangles(kept, subMesh, false);
                    }
                    if (keptTriangles < 20)
                    {
                        UnityEngine.Object.Destroy(baked);
                        continue;
                    }

                    baked.vertices = canonicalVertices;
                    if (haveNormals)
                        baked.normals = canonicalNormals;
                    else
                        baked.RecalculateNormals();
                    baked.RecalculateTangents();
                    baked.bounds = new Bounds(Vector3.forward * 0.065f,
                        new Vector3(0.36f, 0.36f, 0.36f));

                    var rootObject = new GameObject("MFN VR Menu Right Hand Root");
                    menuRightHandVisualRoot = rootObject.transform;
                    rightGripToMenuHandRotation = Quaternion.Inverse(rightGripWorldRotation) *
                        canonicalRotation;
                    menuRightHandVisualRoot.SetPositionAndRotation(rightGripWorldPosition,
                        canonicalRotation);

                    menuRightHandVisualObject = new GameObject("MFN VR Menu Right Hand Mesh");
                    var defaultLayer = LayerMask.NameToLayer("Default");
                    menuRightHandVisualObject.layer = defaultLayer >= 0 ? defaultLayer : 0;
                    menuRightHandVisualObject.transform.SetParent(menuRightHandVisualRoot, false);
                    menuRightHandVisualObject.AddComponent<MeshFilter>().sharedMesh = baked;
                    var visualRenderer = menuRightHandVisualObject.AddComponent<MeshRenderer>();
                    visualRenderer.sharedMaterials = CreateTwoSidedHandMaterials(
                        source.sharedMaterials);
                    visualRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    visualRenderer.receiveShadows = false;
                    menuRightHandVisualMesh = baked;
                    rootObject.SetActive(false);
                    Debug.Log("MFNVR: created independent right-hand menu pointer with " +
                        keptTriangles + " triangles.");
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: could not create right-hand menu pointer: " +
                    exception);
            }
            return false;
        }

        private static List<KeyValuePair<Transform, Quaternion>> ApplyMenuPointerFingerPose(
            Transform wrist, Vector3 handDirection, Vector3 palmNormal)
        {
            var saved = new List<KeyValuePair<Transform, Quaternion>>();
            foreach (var bone in wrist.GetComponentsInChildren<Transform>(true))
            {
                if (bone == wrist || bone.childCount == 0)
                    continue;
                var name = bone.name.ToLowerInvariant();
                var isIndex = name.Contains("index");
                var isFinger = isIndex || name.Contains("middle") || name.Contains("ring") ||
                    name.Contains("pinky") || name.Contains("little") || name.Contains("thumb");
                if (!isFinger)
                    continue;
                var child = bone.GetChild(0);
                var direction = child.position - bone.position;
                if (direction.sqrMagnitude < 0.000001f)
                    continue;
                saved.Add(new KeyValuePair<Transform, Quaternion>(bone, bone.localRotation));
                var targetDirection = isIndex
                    ? handDirection
                    : Vector3.Slerp(direction.normalized, -palmNormal,
                        name.Contains("thumb") ? 0.38f : 0.78f).normalized;
                bone.rotation = Quaternion.FromToRotation(direction.normalized,
                    targetDirection) * bone.rotation;
            }
            return saved;
        }

        private static bool RendererUsesBoneTree(SkinnedMeshRenderer renderer, Transform root)
        {
            if (renderer == null || root == null || renderer.bones == null)
                return false;
            foreach (var bone in renderer.bones)
            {
                if (bone != null && (bone == root || bone.IsChildOf(root)))
                    return true;
            }
            return false;
        }

        private static Vector3 FindHandDirection(Transform wrist)
        {
            var direction = wrist.forward;
            var farthest = 0f;
            foreach (var child in wrist.GetComponentsInChildren<Transform>(true))
            {
                if (child == wrist)
                    continue;
                if (child.name.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                var delta = child.position - wrist.position;
                if (delta.sqrMagnitude > farthest)
                {
                    farthest = delta.sqrMagnitude;
                    direction = delta.normalized;
                }
            }
            return direction.sqrMagnitude > 0.5f ? direction.normalized : wrist.forward;
        }

        private static Vector3 FindPalmNormal(Transform wrist, Vector3 handDirection)
        {
            var thumbDirection = Vector3.zero;
            var farthestThumb = 0f;
            foreach (var child in wrist.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var delta = child.position - wrist.position;
                if (delta.sqrMagnitude > farthestThumb)
                {
                    farthestThumb = delta.sqrMagnitude;
                    thumbDirection = delta.normalized;
                }
            }
            var normal = Vector3.Cross(thumbDirection, handDirection);
            if (normal.sqrMagnitude < 0.25f)
                normal = wrist.up;
            return normal.normalized;
        }

        private static float FindHandBoneReach(Transform wrist, Vector3 handDirection)
        {
            var reach = 0f;
            foreach (var child in wrist.GetComponentsInChildren<Transform>(true))
            {
                if (child == wrist ||
                    child.name.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                reach = Mathf.Max(reach,
                    Vector3.Dot(child.position - wrist.position, handDirection));
            }
            return reach;
        }

        private static List<KeyValuePair<Transform, Quaternion>> ApplyGrippedFingerPose(
            Transform wrist, Vector3 palmNormal)
        {
            var saved = new List<KeyValuePair<Transform, Quaternion>>();
            foreach (var bone in wrist.GetComponentsInChildren<Transform>(true))
            {
                var name = bone.name.ToLowerInvariant();
                var isThumb = name.Contains("thumb");
                if (bone == wrist || bone.childCount == 0 || name.Contains("wrist"))
                    continue;
                var child = bone.GetChild(0);
                var direction = child.position - bone.position;
                if (direction.sqrMagnitude < 0.000001f)
                    continue;
                saved.Add(new KeyValuePair<Transform, Quaternion>(bone, bone.localRotation));
                var depth = 0;
                var cursor = bone.parent;
                while (cursor != null && cursor != wrist)
                {
                    depth++;
                    cursor = cursor.parent;
                }
                var curlAmount = isThumb
                    ? (depth == 0 ? 0.28f : 0.58f)
                    : (depth == 0 ? 0.38f : (depth == 1 ? 0.72f : 0.84f));
                var curledDirection = Vector3.Slerp(direction.normalized,
                    -palmNormal, curlAmount).normalized;
                bone.rotation = Quaternion.FromToRotation(direction.normalized,
                    curledDirection) * bone.rotation;
            }
            return saved;
        }

        private static Material[] CreateTwoSidedHandMaterials(Material[] sourceMaterials)
        {
            if (sourceMaterials == null)
                return new Material[0];
            var result = new Material[sourceMaterials.Length];
            for (var index = 0; index < sourceMaterials.Length; index++)
            {
                var source = sourceMaterials[index];
                if (source == null)
                    continue;
                var material = new Material(source);
                material.name = source.name + " (MFN VR Outward Hand)";
                material.SetInt("_Cull", 0);
                material.SetInt("_CullMode", 0);
                material.SetInt("_CullModeForward", 0);
                result[index] = material;
            }
            return result;
        }

        private static void RestoreFingerPose(
            List<KeyValuePair<Transform, Quaternion>> savedRotations)
        {
            for (var index = 0; index < savedRotations.Count; index++)
            {
                var entry = savedRotations[index];
                if (entry.Key != null)
                    entry.Key.localRotation = entry.Value;
            }
        }

        private static void EnsureLeftHandVisible()
        {
            if (usingBakedLeftHand && leftHandVisualRoot != null)
            {
                var showAuthoredSupportHand = ShouldShowAuthoredSupportHand();
                if (leftHandVisualRoot.gameObject.activeSelf == showAuthoredSupportHand)
                    leftHandVisualRoot.gameObject.SetActive(!showAuthoredSupportHand);
                if (leftHandRoot != null)
                {
                    if (showAuthoredSupportHand)
                    {
                        // Restore the animator-owned shotgun/Conclusion support hand at
                        // its authored weapon-local transform. Its weapon animation can
                        // continue, while the independent tracked visual remains hidden.
                        if (!leftHandRoot.gameObject.activeSelf)
                            leftHandRoot.gameObject.SetActive(true);
                        leftHandRoot.localPosition = originalLeftHandLocalPosition;
                        leftHandRoot.localRotation = originalLeftHandLocalRotation;
                        leftHandRoot.localScale = originalLeftHandLocalScale;
                    }
                    else
                    {
                        // Keep the original combined arm/hand skin out of view while the
                        // independent controller-tracked hand is displayed.
                        leftHandRoot.localScale = Vector3.zero;
                        leftHandRoot.position = motionItem.transform.position +
                            Vector3.down * 100f;
                    }
                }
                foreach (var renderer in leftHandRenderers)
                    if (renderer != null)
                        renderer.enabled = true;
                return;
            }
            if (leftHandRoot != null)
            {
                if (!leftHandRoot.gameObject.activeSelf)
                    leftHandRoot.gameObject.SetActive(true);
                // The support hand is a real, persistent controller-tracked hand. Gripping
                // changes weapon support only; it never creates, hides, or head-locks the hand.
                leftHandRoot.localScale = trackedLeftHandLocalScale;
            }
            foreach (var renderer in leftHandRenderers)
            {
                if (renderer != null)
                    renderer.enabled = true;
            }
        }

        private static bool ShouldShowAuthoredSupportHand()
        {
            if (!twoHanded || !lockedSupportGripSteersWeapon || motionManager == null)
                return false;
            var item = motionManager.GetCurrentItem();
            return item == InventoryItem.BoxingGloveShotgun ||
                   item == InventoryItem.FinalGun;
        }

        private static void RefreshControllerPoses()
        {
            if (!motionContextValid)
            {
                motionPoseValid = false;
                leftPoseValid = false;
                return;
            }

            Vector3 rawRightGrip;
            Quaternion rawRightGripRotation;
            var rawRightAim = Vector3.zero;
            var rawRightAimRotation = Quaternion.identity;
            motionPoseValid = TryGetControllerPose(1, false, out rawRightGrip,
                out rawRightGripRotation) && TryGetControllerPose(1, true,
                out rawRightAim, out rawRightAimRotation);
            if (!motionPoseValid)
            {
                leftPoseValid = false;
                return;
            }

            var inverseOrigin = Quaternion.Inverse(motionOriginRotation);
            rightGripLocalPosition = inverseOrigin * (rawRightGrip - motionOriginPosition);
            rightGripLocalRotation = inverseOrigin * rawRightGripRotation;
            rightAimLocalRotation = inverseOrigin * rawRightAimRotation;
            rightAimLocalPosition = inverseOrigin * (rawRightAim - motionOriginPosition);
            rightGripWorldPosition = motionRigPosition + motionRigRotation * rightGripLocalPosition;
            rightGripWorldRotation = motionRigRotation * rightGripLocalRotation;
            rightAimWorldPosition = motionRigPosition + motionRigRotation * rightAimLocalPosition;
            rightAimWorldRotation = motionRigRotation * rightAimLocalRotation;

            Vector3 rawLeftGrip;
            Quaternion rawLeftGripRotation;
            Vector3 rawLeftAim;
            Quaternion rawLeftAimRotation;
            leftPoseValid = TryGetControllerPose(0, false, out rawLeftGrip,
                out rawLeftGripRotation);
            var leftAimValid = TryGetControllerPose(0, true, out rawLeftAim,
                out rawLeftAimRotation);
            if (leftPoseValid)
            {
                var leftLocalPosition = inverseOrigin * (rawLeftGrip - motionOriginPosition);
                var leftLocalRotation = inverseOrigin * rawLeftGripRotation;
                leftGripWorldPosition = motionRigPosition + motionRigRotation * leftLocalPosition;
                leftGripWorldRotation = motionRigRotation * leftLocalRotation;
                if (leftAimValid)
                {
                    var leftAimLocalPosition = inverseOrigin *
                        (rawLeftAim - motionOriginPosition);
                    leftAimWorldPosition = motionRigPosition +
                        motionRigRotation * leftAimLocalPosition;
                }
                else
                {
                    leftAimWorldPosition = leftGripWorldPosition;
                }
                leftAimWorldRotation = leftAimValid
                    ? motionRigRotation * (inverseOrigin * rawLeftAimRotation)
                    : leftGripWorldRotation;
            }
        }

        private static bool TryGetControllerPose(int hand, bool aim, out Vector3 position,
            out Quaternion rotation)
        {
            float px, py, pz, qx, qy, qz, qw;
            var valid = MFN_GetControllerPose(hand, aim ? 1 : 0, out px, out py, out pz,
                out qx, out qy, out qz, out qw) != 0;
            position = new Vector3(px, py, -pz);
            rotation = new Quaternion(-qx, -qy, qz, qw);
            return valid;
        }

        private static bool TryGetEyePose(int eye, out Vector3 position,
            out Quaternion rotation)
        {
            float px, py, pz, qx, qy, qz, qw;
            float angleLeft, angleRight, angleUp, angleDown;
            var valid = MFN_GetEyeView(eye, out px, out py, out pz,
                out qx, out qy, out qz, out qw, out angleLeft, out angleRight,
                out angleUp, out angleDown) != 0;
            position = new Vector3(px, py, -pz);
            rotation = new Quaternion(-qx, -qy, qz, qw);
            return valid;
        }

        private static bool TryMapRightControllerToFlatMenu(Camera left, Camera right)
        {
            Vector3 rawLeftEyePosition, rawRightEyePosition;
            Vector3 rawAimPosition, rawGripPosition;
            Quaternion rawLeftEyeRotation, rawRightEyeRotation;
            Quaternion rawAimRotation, rawGripRotation;
            if (left == null || right == null ||
                !TryGetEyePose(0, out rawLeftEyePosition, out rawLeftEyeRotation) ||
                !TryGetEyePose(1, out rawRightEyePosition, out rawRightEyeRotation) ||
                !TryGetControllerPose(1, true, out rawAimPosition, out rawAimRotation) ||
                !TryGetControllerPose(1, false, out rawGripPosition, out rawGripRotation))
                return false;

            // Eye and controller poses originate in the same OpenXR local space. Map
            // both through the displayed menu-head pose so title-screen tracking is
            // true 6DoF and does not depend on the gameplay motion rig existing.
            var rawHeadPosition = (rawLeftEyePosition + rawRightEyePosition) * 0.5f;
            var rawHeadRotation = Quaternion.Slerp(rawLeftEyeRotation,
                rawRightEyeRotation, 0.5f);
            var displayHeadPosition = (left.transform.position +
                right.transform.position) * 0.5f;
            var displayHeadRotation = Quaternion.Slerp(left.transform.rotation,
                right.transform.rotation, 0.5f);
            var trackingToMenuRotation = displayHeadRotation *
                Quaternion.Inverse(rawHeadRotation);

            rightAimWorldPosition = displayHeadPosition + trackingToMenuRotation *
                (rawAimPosition - rawHeadPosition);
            rightAimWorldRotation = trackingToMenuRotation * rawAimRotation;
            rightGripWorldPosition = displayHeadPosition + trackingToMenuRotation *
                (rawGripPosition - rawHeadPosition);
            rightGripWorldRotation = trackingToMenuRotation * rawGripRotation;
            return true;
        }

        private static void UpdateTwoHandGrip()
        {
            float stickX, stickY, trigger, squeeze;
            int primary, secondary, stickClick, menu;
            var haveInput = MFN_GetControllerInput(0, out stickX, out stickY,
                out trigger, out squeeze, out primary, out secondary,
                out stickClick, out menu) != 0;
            UpdateLeftHandNeutralCalibrationHold(haveInput && stickClick != 0);
            var gripPressed = haveInput && squeeze >= 0.72f;
            if (gripPressed && !previousLeftGripPressed && leftPoseValid && motionPoseValid)
            {
                if (twoHanded)
                {
                    ReleaseSupportGrip(false);
                }
                else
                {
                    Vector3 supportPosition;
                    float grabRadius;
                    string supportName;
                    bool steersWeapon;
                    float releaseDistance;
                    if (TryGetSupportGripTarget(out supportPosition, out grabRadius,
                        out supportName, out steersWeapon, out releaseDistance) &&
                        Vector3.Distance(leftGripWorldPosition, supportPosition) <= grabRadius)
                    {
                        var itemTransform = motionItem.transform;
                        lockedLeftHandItemLocalPosition =
                            itemTransform.InverseTransformPoint(supportPosition);
                        lockedLeftHandItemLocalRotation =
                            Quaternion.Inverse(itemTransform.rotation) *
                            GetTrackedLeftHandVisualRotation();
                        lockedSupportGripName = supportName;
                        lockedSupportGripSteersWeapon = steersWeapon;
                        lockedSupportGripReleaseDistance = releaseDistance;
                        if (steersWeapon)
                        {
                            var handDelta = leftGripWorldPosition - rightGripWorldPosition;
                            if (handDelta.sqrMagnitude > 0.0064f)
                            {
                                var basis = StableLookRotation(handDelta.normalized,
                                    rightGripWorldRotation * Vector3.up);
                                twoHandControllerCorrection = Quaternion.Inverse(basis) *
                                    rightAimWorldRotation;
                            }
                            else
                            {
                                lockedSupportGripSteersWeapon = false;
                            }
                        }
                        twoHanded = true;
                        MFN_ApplyControllerHaptic(0, 0.42f, 0.065f, 0f);
                        Debug.Log("MFNVR: left hand snapped and locked to " + supportName +
                            "; press left grip again to release.");
                    }
                }
            }
            previousLeftGripPressed = gripPressed;

            if (twoHanded && leftPoseValid && motionItem != null)
            {
                var socketPosition = motionItem.transform.TransformPoint(
                    lockedLeftHandItemLocalPosition);
                if (Vector3.Distance(leftGripWorldPosition, socketPosition) >
                    lockedSupportGripReleaseDistance)
                    ReleaseSupportGrip(true);
            }
        }

        private static void UpdateLeftHandNeutralCalibrationHold(bool held)
        {
            if (!held)
            {
                leftCalibrationHoldStarted = -1f;
                leftCalibrationHoldTriggered = false;
                return;
            }

            if (leftCalibrationHoldStarted < 0f)
                leftCalibrationHoldStarted = Time.realtimeSinceStartup;
            if (leftCalibrationHoldTriggered || !leftPoseValid || twoHanded ||
                Time.realtimeSinceStartup - leftCalibrationHoldStarted < 3f)
                return;

            CalibrateLeftHandToNeutralReference();
            leftCalibrationHoldTriggered = true;
        }

        private static void ReleaseSupportGrip(bool movedTooFar)
        {
            if (!twoHanded)
                return;
            var releasedName = lockedSupportGripName;
            twoHanded = false;
            lockedSupportGripName = null;
            lockedSupportGripSteersWeapon = false;
            lockedSupportGripReleaseDistance = 0f;
            MFN_ApplyControllerHaptic(0, movedTooFar ? 0.30f : 0.18f,
                movedTooFar ? 0.055f : 0.045f, 0f);
            if (movedTooFar)
                Debug.Log("MFNVR: automatically released " + releasedName +
                    " because the physical left hand moved beyond its grip tether.");
        }

        private static void LoadLeftHandCalibration()
        {
            if (leftCalibrationLoaded)
                return;
            leftCalibrationLoaded = true;
            if (PlayerPrefs.GetInt("MFNVR.LeftHandCalibration.Valid", 0) != 0)
            {
                userLeftGripToHandRotation = new Quaternion(
                    PlayerPrefs.GetFloat("MFNVR.LeftHandCalibration.X", 0f),
                    PlayerPrefs.GetFloat("MFNVR.LeftHandCalibration.Y", 0f),
                    PlayerPrefs.GetFloat("MFNVR.LeftHandCalibration.Z", 0f),
                    PlayerPrefs.GetFloat("MFNVR.LeftHandCalibration.W", 1f));
                haveUserLeftHandCalibration = true;
                Debug.Log("MFNVR: loaded persistent base left-hand controller alignment.");
            }
            LoadLeftHandCalibrationFile();
        }

        private static void LoadLeftHandCalibrationFile()
        {
            leftHandCalibrationFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "BepInEx", "config", "MFNVR.LeftHandCalibration.cfg");
            try
            {
                if (!File.Exists(leftHandCalibrationFilePath))
                    return;

                var values = new Dictionary<string, float>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(leftHandCalibrationFilePath))
                {
                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                        continue;
                    float parsed;
                    if (float.TryParse(line.Substring(separator + 1).Trim(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                        values[line.Substring(0, separator).Trim()] = parsed;
                }
                float x, y, z, w;
                if (!values.TryGetValue("RotationX", out x) ||
                    !values.TryGetValue("RotationY", out y) ||
                    !values.TryGetValue("RotationZ", out z) ||
                    !values.TryGetValue("RotationW", out w))
                    return;
                var loaded = new Quaternion(x, y, z, w);
                var magnitude = Mathf.Sqrt(x * x + y * y + z * z + w * w);
                if (magnitude < 0.5f)
                    return;
                userLeftGripToHandRotation = new Quaternion(x / magnitude, y / magnitude,
                    z / magnitude, w / magnitude);
                haveUserLeftHandCalibration = true;
                Debug.Log("MFNVR: loaded permanent neutral left-hand calibration from " +
                    leftHandCalibrationFilePath + ".");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: could not load left-hand calibration file: " +
                    exception.Message);
            }
        }

        private static void CalibrateLeftHandToNeutralReference()
        {
            var headRotation = haveWorldEyeData
                ? Quaternion.Slerp(worldLeftEyeRotation, worldRightEyeRotation, 0.5f)
                : motionRigRotation;
            var headForward = Vector3.ProjectOnPlane(headRotation * Vector3.forward,
                Vector3.up).normalized;
            var headRight = Vector3.ProjectOnPlane(headRotation * Vector3.right,
                Vector3.up).normalized;
            if (headForward.sqrMagnitude < 0.5f)
                headForward = motionRigRotation * Vector3.forward;
            if (headRight.sqrMagnitude < 0.5f)
                headRight = motionRigRotation * Vector3.right;

            // Reference pose based on the Oculus dashboard hand shown by the user:
            // fingers reach forward, slightly upward and inward, while the visible hand
            // surface turns back toward the player's eyes. The wrist position remains the
            // exact OpenXR grip position; only controller-to-hand orientation is calibrated.
            var desiredFingerDirection = (headForward * 0.86f + Vector3.up * 0.42f +
                headRight * 0.12f).normalized;
            var desiredPalmNormal = (-headForward * 0.90f + Vector3.up * 0.24f +
                headRight * 0.16f).normalized;
            var desiredRotation = StableLookRotation(desiredFingerDirection,
                desiredPalmNormal);
            userLeftGripToHandRotation = Quaternion.Inverse(leftGripWorldRotation) *
                desiredRotation;
            haveUserLeftHandCalibration = true;
            SaveLeftHandCalibrationFile();
            MFN_ApplyControllerHaptic(0, 0.58f, 0.11f, 0f);
        }

        private static void SaveLeftHandCalibrationFile()
        {
            try
            {
                if (string.IsNullOrEmpty(leftHandCalibrationFilePath))
                {
                    leftHandCalibrationFilePath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "BepInEx", "config",
                        "MFNVR.LeftHandCalibration.cfg");
                }
                var directory = Path.GetDirectoryName(leftHandCalibrationFilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(leftHandCalibrationFilePath,
                    "# MFNVR permanent neutral left-hand calibration\r\n" +
                    "# Recalibrate by holding the left stick button for 3 seconds.\r\n" +
                    "ReferencePose=OculusNeutral\r\n" +
                    "RotationX=" + userLeftGripToHandRotation.x.ToString("R",
                        CultureInfo.InvariantCulture) + "\r\n" +
                    "RotationY=" + userLeftGripToHandRotation.y.ToString("R",
                        CultureInfo.InvariantCulture) + "\r\n" +
                    "RotationZ=" + userLeftGripToHandRotation.z.ToString("R",
                        CultureInfo.InvariantCulture) + "\r\n" +
                    "RotationW=" + userLeftGripToHandRotation.w.ToString("R",
                        CultureInfo.InvariantCulture) + "\r\n");
                Debug.Log("MFNVR: calibrated the tracked left hand to the neutral " +
                    "reference pose and saved it permanently to " +
                    leftHandCalibrationFilePath + ".");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: could not save left-hand calibration file: " +
                    exception.Message);
            }
        }

        private static Quaternion GetTrackedLeftHandVisualRotation()
        {
            var baseRotation = haveUserLeftHandCalibration
                ? leftGripWorldRotation * userLeftGripToHandRotation
                : StableLookRotation(leftAimWorldRotation * Vector3.forward,
                    leftAimWorldRotation * Vector3.right);
            return baseRotation;
        }

        private static bool TryGetSupportGripTarget(out Vector3 position,
            out float grabRadius, out string supportName, out bool steersWeapon,
            out float releaseDistance)
        {
            position = Vector3.zero;
            grabRadius = 0f;
            supportName = null;
            steersWeapon = false;
            releaseDistance = 0f;
            if (motionItem == null || motionManager == null ||
                motionItem.firePosition == null)
                return false;

            var currentItem = motionManager.GetCurrentItem();
            var rear = rightWrist != null ? rightWrist.position : rightGripWorldPosition;
            var muzzle = motionItem.firePosition;
            if (currentItem == InventoryItem.BoxingGloveGun)
            {
                // The pistol support socket belongs beside the firing hand on the pistol
                // grip, never out on the barrel. The small leftward offset keeps the two
                // baked hands from occupying exactly the same space.
                position = rear - muzzle.right * 0.045f + muzzle.forward * 0.018f;
                grabRadius = 0.16f;
                supportName = "pistol grip";
                releaseDistance = 0.28f;
                return true;
            }

            if (currentItem == InventoryItem.BoxingGloveShotgun)
            {
                // The shotgun socket sits on the forward barrel/fore-end. It is stored in
                // weapon-local space at grab time, so reload animation and controller motion
                // cannot pull the locked hand away from it.
                position = Vector3.Lerp(rear, muzzle.position, 0.58f);
                grabRadius = 0.18f;
                supportName = "shotgun barrel";
                steersWeapon = true;
                releaseDistance = 0.38f;
                return true;
            }

            if (currentItem == InventoryItem.FinalGun)
            {
                // The Conclusion has a transverse wrapped rod/handle protruding from its
                // left side. Locate that actual piece of rendered geometry so the grip
                // remains correct after MFN's viewmodel scaling and animation. The fallback
                // is expressed in the live muzzle basis for unusual prefab variants.
                string rodRendererName;
                if (!TryFindConclusionLeftRod(rear, muzzle, out position,
                    out rodRendererName))
                {
                    position = Vector3.Lerp(rear, muzzle.position, 0.32f) -
                        muzzle.right * 0.235f - muzzle.up * 0.105f;
                    rodRendererName = "basis fallback";
                }
                // Fine-tuned from the in-headset reference: center the palm on the wrapped
                // section rather than above the handle at its inner mounting collar.
                position -= muzzle.up * 0.040f;
                position -= muzzle.right * 0.055f;
                position -= muzzle.forward * 0.030f;
                grabRadius = 0.20f;
                supportName = "Conclusion left rod (" + rodRendererName + ")";
                steersWeapon = true;
                releaseDistance = 0.42f;
                return true;
            }

            return false;
        }

        private static bool TryFindConclusionLeftRod(Vector3 rear, Transform muzzle,
            out Vector3 position, out string rendererName)
        {
            position = Vector3.zero;
            rendererName = null;
            if (motionItem == null || muzzle == null)
                return false;

            var left = -muzzle.right.normalized;
            var up = muzzle.up.normalized;
            var forward = muzzle.forward.normalized;
            var weaponBounds = new Bounds();
            var haveWeaponBounds = false;
            foreach (var renderer in motionItem.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsConclusionGripGeometry(renderer))
                    continue;
                if (!haveWeaponBounds)
                {
                    weaponBounds = renderer.bounds;
                    haveWeaponBounds = true;
                }
                else
                {
                    weaponBounds.Encapsulate(renderer.bounds);
                }
            }
            if (!haveWeaponBounds)
                return false;

            Renderer best = null;
            var bestScore = float.NegativeInfinity;
            foreach (var renderer in motionItem.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsConclusionGripGeometry(renderer))
                    continue;

                var lowerName = renderer.name.ToLowerInvariant();
                var bounds = renderer.bounds;
                var lateralHalf = ProjectedBoundsHalfExtent(bounds.extents, left);
                var verticalHalf = ProjectedBoundsHalfExtent(bounds.extents, up);
                var forwardHalf = ProjectedBoundsHalfExtent(bounds.extents, forward);
                var lateralSpan = lateralHalf * 2f;
                var thickness = Mathf.Max(0.001f,
                    Mathf.Max(verticalHalf * 2f, forwardHalf * 2f));
                var aspect = lateralSpan / thickness;
                var delta = bounds.center - rear;
                var lateralCenter = Vector3.Dot(delta, left);
                var lateralTip = lateralCenter + lateralHalf;
                var verticalFromBody = Vector3.Dot(bounds.center - weaponBounds.center, up);
                var namedLikeHandle = ContainsAny(lowerName, "handle", "grip", "rod",
                    "bar", "lever", "wrap", "wrapped");

                // The requested grip is the wrapped horizontal handle circled by the user:
                // it is left of the body, below the weapon's center, and long primarily on
                // the muzzle-right axis. These strict constraints reject the high ring/rod,
                // drum, main frame and animated typing components.
                if (lateralTip < 0.075f || lateralSpan < 0.045f ||
                    verticalFromBody > 0.015f ||
                    (!namedLikeHandle && aspect < 1.80f) ||
                    (namedLikeHandle && aspect < 1.20f) ||
                    lateralSpan > 0.55f || thickness > 0.24f)
                    continue;

                var forwardOffset = Mathf.Abs(Vector3.Dot(delta, forward));
                var score = lateralTip * 7f + Mathf.Min(aspect, 6f) * 0.20f +
                    (namedLikeHandle ? 2.5f : 0f) - forwardOffset * 0.12f -
                    verticalFromBody * 2.5f;
                if (score <= bestScore)
                    continue;
                best = renderer;
                bestScore = score;
            }

            if (best == null)
                return false;

            // The wrapped portion is centered on this thin renderer. Snapping to its center
            // puts the palm around the actual handle instead of on either end cap.
            position = best.bounds.center;
            rendererName = best.name;
            return true;
        }

        private static bool IsConclusionGripGeometry(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled ||
                !renderer.gameObject.activeInHierarchy || IsUnderHand(renderer.transform) ||
                renderer.bounds.extents.sqrMagnitude < 0.000004f)
                return false;
            var lowerName = renderer.name.ToLowerInvariant();
            return !ContainsAny(lowerName, "hand", "arm", "muzzle", "flash", "smoke",
                "particle", "shell", "letter");
        }

        private static float ProjectedBoundsHalfExtent(Vector3 extents, Vector3 axis)
        {
            return Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y +
                Mathf.Abs(axis.z) * extents.z;
        }

        private static void ApplyLeftHandVisualPose(Vector3 position, Quaternion rotation)
        {
            if (usingBakedLeftHand && leftHandVisualRoot != null)
            {
                leftHandVisualRoot.SetPositionAndRotation(position, rotation);
                EnsureLeftHandVisible();
                return;
            }

            if (leftHandRoot == null)
                return;
            if (leftWrist != null)
            {
                var wristRelativeRotation = Quaternion.Inverse(leftHandRoot.rotation) *
                    leftWrist.rotation;
                leftHandRoot.rotation = rotation * Quaternion.Inverse(wristRelativeRotation);
                leftHandRoot.position += position - leftWrist.position;
            }
            else
            {
                leftHandRoot.SetPositionAndRotation(position, rotation);
            }
            EnsureLeftHandVisible();
        }

        private static void ApplyTrackedTransforms()
        {
            if (motionItem == null)
                return;

            // The shipped weapon animators key the authored support arm during reloads.
            // The VR hand is detached from that hierarchy and made visible again here,
            // after animation and immediately before the hands cameras render.
            EnsureLeftHandVisible();
            if (!motionPoseValid)
                return;

            var effectiveAimRotation = rightAimWorldRotation;
            if (twoHanded && lockedSupportGripSteersWeapon && leftPoseValid)
            {
                var direction = leftGripWorldPosition - rightGripWorldPosition;
                if (direction.sqrMagnitude > 0.0064f)
                {
                    // Shotgun-only two-hand steering pivots around the firing hand. The
                    // correction captured at grab time prevents the muzzle from jumping when
                    // the support hand first snaps onto the barrel.
                    effectiveAimRotation = StableLookRotation(direction.normalized,
                        rightGripWorldRotation * Vector3.up) * twoHandControllerCorrection;
                }
            }

            var itemTransform = motionItem.transform;
            if (motionItem.firePosition != null)
            {
                // Rotate the whole authored weapon/hand assembly until its real muzzle exactly
                // matches the OpenXR aim pose. This removes all first-person viewmodel roll and
                // item-specific sideways offsets without guessing at the wrist bone's axes.
                var muzzleDelta = effectiveAimRotation *
                    Quaternion.Inverse(motionItem.firePosition.rotation);
                itemTransform.rotation = muzzleDelta * itemTransform.rotation;
            }
            else if (rightWrist != null)
            {
                if (!haveRightCalibration)
                {
                    rightGripToWristRotation = Quaternion.Inverse(rightGripWorldRotation) *
                        rightWrist.rotation;
                    haveRightCalibration = true;
                }
                var wristRelativeRotation = Quaternion.Inverse(itemTransform.rotation) *
                    rightWrist.rotation;
                itemTransform.rotation = rightGripWorldRotation * rightGripToWristRotation *
                    Quaternion.Inverse(wristRelativeRotation);
            }
            else
            {
                if (!haveItemCalibration)
                {
                    rightGripToItemRotation = Quaternion.Inverse(rightGripWorldRotation) *
                        itemTransform.rotation;
                    haveItemCalibration = true;
                }
                itemTransform.SetPositionAndRotation(rightGripWorldPosition,
                    rightGripWorldRotation * rightGripToItemRotation);
            }

            if (rightWrist != null)
                itemTransform.position += rightGripWorldPosition - rightWrist.position;

            if (twoHanded)
            {
                var lockedPosition = itemTransform.TransformPoint(
                    lockedLeftHandItemLocalPosition);
                var lockedRotation = itemTransform.rotation *
                    lockedLeftHandItemLocalRotation;
                ApplyLeftHandVisualPose(lockedPosition, lockedRotation);
            }
            else if (leftPoseValid && usingBakedLeftHand && leftHandVisualRoot != null)
            {
                // Default to the prior aim-based mapping, which was closer on Rift S.
                // A saved user calibration replaces it with an exact, persistent
                // controller-to-hand rotation measured in the user's neutral pose.
                var handRotation = GetTrackedLeftHandVisualRotation();
                leftHandVisualRoot.SetPositionAndRotation(leftGripWorldPosition,
                    handRotation);
                EnsureLeftHandVisible();
            }
            else if (leftPoseValid && leftHandRoot != null)
            {
                if (leftWrist != null)
                {
                    if (!haveLeftCalibration)
                    {
                        leftGripToWristRotation = Quaternion.Inverse(leftGripWorldRotation) *
                            leftWrist.rotation;
                        haveLeftCalibration = true;
                    }
                    var wristRelativeRotation = Quaternion.Inverse(leftHandRoot.rotation) *
                        leftWrist.rotation;
                    leftHandRoot.rotation = leftGripWorldRotation * leftGripToWristRotation *
                        Quaternion.Inverse(wristRelativeRotation);
                    leftHandRoot.position += leftGripWorldPosition - leftWrist.position;
                }
                else
                {
                    leftHandRoot.SetPositionAndRotation(leftGripWorldPosition,
                        leftGripWorldRotation);
                }
            }

            // Use the physical muzzle after solving the weapon. This makes projectiles follow
            // the actual barrel in both one- and two-handed poses.
            if (motionItem.firePosition != null)
            {
                rightAimWorldPosition = motionItem.firePosition.position;
                rightAimWorldRotation = motionItem.firePosition.rotation;
            }
            UpdateGunRay();
            UpdateMuzzleSight();
        }

        private static void UpdateGunRay()
        {
            gunRayOrigin = rightAimWorldPosition;
            var aimUp = rightAimWorldRotation * Vector3.up;
            gunRayDirection = Quaternion.AngleAxis(GunAimYawCorrectionDegrees, aimUp) *
                (rightAimWorldRotation * Vector3.forward);
            if (gunRayDirection.sqrMagnitude < 0.5f)
                gunRayDirection = Vector3.forward;
            gunRayDirection.Normalize();

            var mask = LayerMask.GetMask("Enemy", "Level", "Default",
                "DontInteractWithPlayer");
            RaycastHit hit;
            var rayStart = gunRayOrigin + gunRayDirection * 0.035f;
            if (Physics.Raycast(rayStart, gunRayDirection, out hit, 1000f, mask,
                QueryTriggerInteraction.Ignore))
                gunRayTarget = hit.point;
            else
                gunRayTarget = gunRayOrigin + gunRayDirection * 1000f;
        }

        private static bool IsGunEquipped()
        {
            if (motionManager == null || motionItem == null)
                return false;
            var itemName = motionManager.GetCurrentItem().ToString();
            return itemName.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                itemName.IndexOf("Shotgun", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldShowVrAimSight()
        {
            return IsGunEquipped() || (motionManager != null && motionItem != null &&
                motionManager.GetCurrentItem() == InventoryItem.LetterGrenade);
        }

        private static bool IsWrenchEquipped()
        {
            if (motionManager == null || motionItem == null)
                return false;
            return motionManager.GetCurrentItem().ToString().IndexOf("Wrench",
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool SuppressFlatWrenchPrefix(EquippedManager __instance)
        {
            // A Touch trigger/button must never create a desktop melee trace. In VR the
            // wrench can damage something only through UpdatePhysicalWrenchDamage below.
            return !(motionPoseValid && __instance != null && __instance == motionManager &&
                IsWrenchEquipped());
        }

        private static bool SuppressLegacyPhysicalWrenchPrefix()
        {
            // The stable patched camera core contains an older physical-melee detector with
            // permissive 1.8 m/s thresholds. The render bridge owns the sole authoritative
            // wrench sweep now, so never let that duplicate detector apply damage or stun.
            return false;
        }

        private static void ResetPhysicalWrenchSample()
        {
            wrenchSampleValid = false;
            previousWrenchSampleTime = 0f;
            lastWrenchPhysicsFrame = -1;
            ResetPhysicalWrenchSwing();
        }

        private static void ResetPhysicalWrenchSwing()
        {
            wrenchSwingActive = false;
            wrenchSwingStartTime = 0f;
            wrenchSwingDistance = 0f;
            wrenchGripDistance = 0f;
            wrenchSwingPeakSpeed = 0f;
            wrenchGripPeakSpeed = 0f;
        }

        private static void UpdatePhysicalWrenchDamage()
        {
            // TickNativeHands is the one gameplay update for this system. The tracked item is
            // also solved again just before eye rendering, but those late render poses must not
            // run collision or deal duplicate damage.
            if (lastWrenchPhysicsFrame == Time.frameCount)
                return;
            lastWrenchPhysicsFrame = Time.frameCount;

            if (!motionPoseValid || !IsWrenchEquipped())
            {
                wrenchSampleValid = false;
                ResetPhysicalWrenchSwing();
                return;
            }

            var now = Time.realtimeSinceStartup;
            var controllerAimWorldRotation = motionRigRotation * rightAimLocalRotation;
            if (!wrenchSampleValid)
            {
                // MFN's wrench has no dependable muzzle/fire marker. Determine which way its
                // visible geometry extends from the physical hand, then create a short 36 cm
                // collision reach along that axis. Store it in controller aim space so it keeps
                // following the real Touch pose without inheriting player locomotion.
                var reachDirectionWorld = FindWrenchReachDirectionWorld(
                    controllerAimWorldRotation);
                wrenchTipOffsetInAimSpace = Quaternion.Inverse(controllerAimWorldRotation) *
                    reachDirectionWorld * PhysicalWrenchReach;
                previousWrenchPhysicalTipLocal = rightGripLocalPosition +
                    rightAimLocalRotation * wrenchTipOffsetInAimSpace;
                previousWrenchGripLocal = rightGripLocalPosition;
                previousWrenchSampleTime = now;
                wrenchSampleValid = true;
                ResetPhysicalWrenchSwing();
                Debug.Log("MFNVR: physical wrench armed with a 0.36m head-only reach.");
                return;
            }

            var elapsed = now - previousWrenchSampleTime;
            var physicalTipLocal = rightGripLocalPosition +
                rightAimLocalRotation * wrenchTipOffsetInAimSpace;
            var previousTipLocal = previousWrenchPhysicalTipLocal;
            var previousGripLocal = previousWrenchGripLocal;
            var physicalDeltaLocal = physicalTipLocal - previousTipLocal;
            var gripDeltaLocal = rightGripLocalPosition - previousGripLocal;
            previousWrenchPhysicalTipLocal = physicalTipLocal;
            previousWrenchGripLocal = rightGripLocalPosition;
            previousWrenchSampleTime = now;

            // A long frame or a large discontinuity is a tracking reset, not a swing.
            if (elapsed < 0.002f || elapsed > 0.12f || physicalDeltaLocal.magnitude > 0.24f ||
                gripDeltaLocal.magnitude > 0.20f)
            {
                ResetPhysicalWrenchSwing();
                return;
            }

            var segmentDistance = physicalDeltaLocal.magnitude;
            var speed = segmentDistance / elapsed;
            var gripSegmentDistance = gripDeltaLocal.magnitude;
            var gripSpeed = gripSegmentDistance / elapsed;
            // Sub-threshold motion ends the candidate swing. This prevents slow hand
            // drift and controller jitter from accumulating until it eventually damages
            // something that merely happens to be touching the wrench.
            const float swingMotionFloor = 1.25f;
            if (speed < swingMotionFloor || segmentDistance < 0.006f)
            {
                ResetPhysicalWrenchSwing();
                return;
            }

            if (!wrenchSwingActive || now - wrenchSwingStartTime > 0.55f)
            {
                wrenchSwingActive = true;
                wrenchSwingStartTipLocal = previousTipLocal;
                wrenchSwingStartGripLocal = previousGripLocal;
                wrenchSwingStartTime = now - elapsed;
                wrenchSwingDistance = 0f;
                wrenchGripDistance = 0f;
                wrenchSwingPeakSpeed = 0f;
                wrenchGripPeakSpeed = 0f;
            }
            wrenchSwingDistance += segmentDistance;
            wrenchGripDistance += gripSegmentDistance;
            wrenchSwingPeakSpeed = Mathf.Max(wrenchSwingPeakSpeed, speed);
            wrenchGripPeakSpeed = Mathf.Max(wrenchGripPeakSpeed, gripSpeed);
            var netSwingDistance = Vector3.Distance(physicalTipLocal,
                wrenchSwingStartTipLocal);
            var netGripDistance = Vector3.Distance(rightGripLocalPosition,
                wrenchSwingStartGripLocal);
            var swingDuration = Mathf.Max(now - wrenchSwingStartTime, 0.001f);
            var averageSwingSpeed = wrenchSwingDistance / swingDuration;

            // A real strike must cover meaningful space, not just report one noisy
            // high-speed frame. Path length accepts natural curved swings while net
            // displacement rejects rapid back-and-forth tracking jitter.
            const float minimumSwingDistance = 0.20f;
            const float minimumNetSwingDistance = 0.16f;
            const float minimumGripDistance = 0.10f;
            const float minimumNetGripDistance = 0.08f;
            const float minimumGripPeakSpeed = 1.20f;
            const float minimumImpactSpeed = 3.00f;
            if (wrenchSwingDistance < minimumSwingDistance ||
                netSwingDistance < minimumNetSwingDistance ||
                wrenchGripDistance < minimumGripDistance ||
                netGripDistance < minimumNetGripDistance ||
                wrenchGripPeakSpeed < minimumGripPeakSpeed ||
                wrenchSwingPeakSpeed < minimumImpactSpeed || speed < minimumImpactSpeed ||
                averageSwingSpeed < 2.50f ||
                now - lastWrenchDamageTime < 0.30f)
                return;

            // Build the sweep from tracking-space displacement and anchor it at the currently
            // rendered wrench head. This deliberately excludes artificial world/player motion.
            var physicalDeltaWorld = motionRigRotation * physicalDeltaLocal;
            var sweepDistance = physicalDeltaWorld.magnitude;
            if (sweepDistance < 0.012f || sweepDistance > 0.28f)
                return;
            var reachDirectionWorldNow = (controllerAimWorldRotation *
                wrenchTipOffsetInAimSpace).normalized;
            var headWorldPosition = rightGripWorldPosition +
                reachDirectionWorldNow * PhysicalWrenchReach;
            var sweepStart = headWorldPosition - physicalDeltaWorld;
            var hits = Physics.SphereCastAll(sweepStart, PhysicalWrenchHeadRadius,
                physicalDeltaWorld / sweepDistance, sweepDistance, ~0,
                QueryTriggerInteraction.Collide);

            // Only the wrench head and its actual swept arc can hit. The previous full-shaft
            // capsule was the source of stationary proximity hits shown in the recording.
            const float maximumReach = PhysicalWrenchReach + PhysicalWrenchHeadRadius;
            // Never use an isolated peak sample for damage/stun. Capping it by the
            // current and average swing speed prevents a tracking spike from becoming
            // a high-power hit after the hand has nearly stopped.
            var impactSpeed = Mathf.Min(wrenchSwingPeakSpeed,
                Mathf.Min(speed, averageSwingSpeed * 1.20f));
            foreach (var hit in hits)
            {
                var point = hit.point == Vector3.zero && hit.collider != null
                    ? hit.collider.ClosestPoint(headWorldPosition)
                    : hit.point;
                if (Vector3.Distance(point, rightGripWorldPosition) > maximumReach)
                    continue;
                if (DamageFromPhysicalWrench(hit.collider, point, impactSpeed,
                    wrenchSwingDistance))
                {
                    lastWrenchDamageTime = now;
                    ResetPhysicalWrenchSwing();
                    break;
                }
            }
            if (now - lastWrenchDamageTime < 0.30f)
                return;

            // SphereCast does not report a collider when the sweep begins inside it.
            // A final head-only overlap covers that case, with all distance/speed gates
            // above still required. It never checks the shaft or the space near the hand.
            var colliders = Physics.OverlapSphere(headWorldPosition,
                PhysicalWrenchHeadRadius, ~0,
                QueryTriggerInteraction.Collide);
            foreach (var collider in colliders)
            {
                if (collider == null)
                    continue;
                var point = collider.ClosestPoint(headWorldPosition);
                if (Vector3.Distance(point, rightGripWorldPosition) > maximumReach)
                    continue;
                if (DamageFromPhysicalWrench(collider, point, impactSpeed,
                    wrenchSwingDistance))
                {
                    lastWrenchDamageTime = now;
                    ResetPhysicalWrenchSwing();
                    break;
                }
            }
        }

        private static Vector3 FindWrenchReachDirectionWorld(
            Quaternion controllerAimWorldRotation)
        {
            var bestVector = Vector3.zero;
            var bestDistanceSquared = 0f;
            if (motionItem != null)
            {
                foreach (var renderer in motionItem.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer == null || !renderer.enabled || IsUnderHand(renderer.transform))
                        continue;
                    var candidate = renderer.bounds.center - rightGripWorldPosition;
                    if (candidate.sqrMagnitude > bestDistanceSquared)
                    {
                        bestVector = candidate;
                        bestDistanceSquared = candidate.sqrMagnitude;
                    }
                }
                if (bestDistanceSquared < 0.0036f && motionItem.firePosition != null)
                {
                    var candidate = motionItem.firePosition.position - rightGripWorldPosition;
                    if (candidate.sqrMagnitude > bestDistanceSquared)
                    {
                        bestVector = candidate;
                        bestDistanceSquared = candidate.sqrMagnitude;
                    }
                }
            }
            return bestDistanceSquared >= 0.0036f
                ? bestVector.normalized
                : controllerAimWorldRotation * Vector3.forward;
        }

        private static bool DamageFromPhysicalWrench(Collider collider, Vector3 hitPoint,
            float speed, float swingDistance)
        {
            if (collider == null || motionItem == null || Player.current == null)
                return false;
            var enemy = collider.GetComponentInParent<EnemyParent>();
            var sender = enemy == null
                ? collider.GetComponentInParent<EnemyDamageSender>()
                : null;
            if (enemy == null && sender == null)
                return false;

            // Only a deliberate, genuinely fast arm swing can hit. Damage then rises gently
            // instead of turning ordinary VR wrist motion into a one-hit attack.
            var speedMultiplier = Mathf.Lerp(0.50f, 1.75f,
                Mathf.InverseLerp(3.00f, 10.0f, speed));
            var distanceMultiplier = Mathf.Lerp(0.70f, 1.20f,
                Mathf.InverseLerp(0.18f, 0.55f, swingDistance));
            var damage = Mathf.Max(0.25f, motionItem.meleeDamage * speedMultiplier *
                distanceMultiplier);
            var difficulty = SaveData.GetIntData("Difficulty");
            if (difficulty == 0)
                damage *= 2f;
            else if (difficulty == 1)
                damage *= 1.3f;
            var forceStun = speed >= 4.0f;
            var alwaysStun = speed >= 5.0f;
            if (enemy != null)
                enemy.Damage(damage, Player.current, hitPoint, forceStun, true, false,
                    alwaysStun, true);
            else
                sender.Damage(damage, Player.current, hitPoint, forceStun, true, false,
                    alwaysStun);

            var impactSound = wrenchHitSoundField != null && motionManager != null
                ? wrenchHitSoundField.GetValue(motionManager) as AudioLevelAdjuster
                : null;
            if (impactSound != null)
            {
                impactSound.SetPitch(UnityEngine.Random.Range(0.96f, 1.04f));
                impactSound.PlayAllSources();
            }
            try
            {
                MFN_ApplyControllerHaptic(1, Mathf.Clamp01(speed / 10.0f), 0.07f, 0f);
            }
            catch (EntryPointNotFoundException)
            {
                // Damage remains functional with an older native runtime lacking haptics.
            }
            Debug.Log("MFNVR: physical wrench hit at " + speed.ToString("F2") +
                " m/s after " + swingDistance.ToString("F2") + "m of travel for " +
                damage.ToString("F2") + " damage.");
            return true;
        }

        private static void UpdateMuzzleSight()
        {
            if (!aimingDotEnabled || IsPhysicalInventoryActive() || !motionPoseValid || !ShouldShowVrAimSight())
            {
                if (muzzleSight != null)
                    muzzleSight.SetActive(false);
                return;
            }
            EnsureMuzzleSight();
            muzzleSight.SetActive(true);
            muzzleSight.transform.position = gunRayOrigin + gunRayDirection * aimingDotDistance;
            muzzleSight.transform.localScale = Vector3.one * aimingDotSize;
        }

        private static void EnsureMuzzleSight()
        {
            if (muzzleSight != null)
                return;
            muzzleSight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            muzzleSight.name = "MFN VR World-Space Muzzle Sight";
            var collider = muzzleSight.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.Destroy(collider);
            var renderer = muzzleSight.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            muzzleSightMaterial = new Material(shader);
            muzzleSightMaterial.color = new Color(1f, 0.82f, 0.02f, 1f);
            renderer.material = muzzleSightMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        internal static void OverridePlayerProjectile(ref Vector3 direction,
            Player fromPlayer, ref Vector3 collisionPoint)
        {
            if (!motionPoseValid || motionItem == null || fromPlayer == null ||
                fromPlayer != Player.current)
                return;

            UpdateGunRay();
            var shotDirection = gunRayDirection;
            var itemName = motionManager != null
                ? motionManager.GetCurrentItem().ToString()
                : string.Empty;
            if (itemName.IndexOf("Shotgun", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                shotDirection = Quaternion.AngleAxis(UnityEngine.Random.Range(-8f, 8f),
                    rightAimWorldRotation * Vector3.up) *
                    Quaternion.AngleAxis(UnityEngine.Random.Range(-8f, 8f),
                    rightAimWorldRotation * Vector3.right) * shotDirection;
            }
            else if (itemName.IndexOf("FinalGun", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                shotDirection = Quaternion.AngleAxis(UnityEngine.Random.Range(-5f, 5f),
                    rightAimWorldRotation * Vector3.up) *
                    Quaternion.AngleAxis(UnityEngine.Random.Range(-5f, 5f),
                    rightAimWorldRotation * Vector3.right) * shotDirection;
            }
            direction = shotDirection.normalized;
            collisionPoint = gunRayTarget;
        }

        internal static void PositionReticle(ReticleManager manager)
        {
            if (manager == null || reticleRendererField == null || Player.current == null)
                return;
            var renderer = reticleRendererField.GetValue(manager) as SpriteRenderer;
            if (renderer == null)
                return;

            // MFN's desktop reticle is head/camera locked and must never be visible in VR,
            // including while holding the wrench, grenades, ordinary items, or no weapon.
            renderer.enabled = false;
            if (!motionPoseValid || !ShouldShowVrAimSight())
            {
                if (muzzleSight != null)
                    muzzleSight.SetActive(false);
                return;
            }
            UpdateGunRay();
            UpdateMuzzleSight();
        }

        internal static bool TryAssistInteraction(Player player, ref Interactable result)
        {
            if (player == null || !haveWorldEyeData || neckVerticalField == null ||
                hoveringInteractableField == null)
                return false;
            // MFN normally stops its forward gameplay interaction ray while an
            // inspection/inventory/menu camera owns input. Keep that separation even
            // when the VR pointer is active: otherwise the original movement ray can
            // still ring bells, open doors, or enter another toolbox behind the menu.
            if (IsUiModeActive())
            {
                ResetGameplayInteractionReadout(player);
                result = null;
                return true;
            }
            if (doorTransitioningField != null &&
                doorTransitioningField.GetValue(player) is bool transitioning && transitioning)
            {
                ClearGameplayInteractionHover(player);
                result = null;
                return true;
            }

            var neck = neckVerticalField.GetValue(player) as Transform;
            if (neck == null)
                return false;
            var mask = (1 << LayerMask.NameToLayer("Default")) |
                (1 << LayerMask.NameToLayer("Level")) |
                (1 << LayerMask.NameToLayer("DefaultHover")) |
                (1 << LayerMask.NameToLayer("LevelProjectilePassthrough"));
            var reach = 3f;
            if (getReachDistanceMethod != null)
            {
                var value = getReachDistanceMethod.Invoke(player, null);
                if (value is float configuredReach && configuredReach > 0.1f)
                    reach = configuredReach;
            }

            var origin = (worldLeftEyePosition + worldRightEyePosition) * 0.5f;
            var rotation = Quaternion.Slerp(worldLeftEyeRotation, worldRightEyeRotation, 0.5f);
            var direction = rotation * Vector3.forward;
            direction.Normalize();

            // Do not let the widened cast select an object well behind the first visible
            // surface. The 25 cm allowance is enough for a small bell sitting on a desk edge.
            var maximumCandidateDistance = reach;
            RaycastHit vrBlock;
            if (Physics.Raycast(origin, direction, out vrBlock, reach, mask,
                QueryTriggerInteraction.Collide))
                maximumCandidateDistance = Mathf.Min(reach, vrBlock.distance + 0.25f);

            const float assistRadius = 0.10f;
            var hits = Physics.SphereCastAll(origin, assistRadius, direction, reach, mask,
                QueryTriggerInteraction.Collide);
            Interactable candidate = null;
            var candidateDistance = float.PositiveInfinity;
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.distance > maximumCandidateDistance)
                    continue;
                var interactable = hit.collider.GetComponent<Interactable>();
                if (interactable != null && hit.distance < candidateDistance)
                {
                    candidate = interactable;
                    candidateDistance = hit.distance;
                }
            }
            if (candidate == null)
            {
                // We own CheckForInteraction whenever the ordinary camera ray did not
                // already find a valid target. Returning to the original method here
                // lets its differently-oriented desktop ray leave our previous VR
                // target latched, so prompts such as INSPECT and OPEN DOOR can remain
                // visible after the headset ray has moved away. Mirror MFN's native
                // miss path explicitly and return a definitive null result.
                // A null result is authoritative: no gameplay interaction prompt may
                // remain visible. MFN reference-counts prompt enters/exits, and UI
                // transitions can leave that count above one; a single DidExit is then
                // insufficient. Reset the gameplay channel to its actual state.
                ResetGameplayInteractionReadout(player);
                result = null;
                return true;
            }

            var previous = hoveringInteractableField.GetValue(player) as Interactable;
            if (!ReferenceEquals(previous, candidate))
            {
                candidate.DidEnter(player);
                if (previous != null)
                    previous.DidExit(player);
                hoveringInteractableField.SetValue(player, candidate);
            }
            result = candidate;
            return true;
        }

        internal static void ClearGameplayInteractionHover(Player player)
        {
            if (player == null || hoveringInteractableField == null)
                return;
            var previous = hoveringInteractableField.GetValue(player) as Interactable;
            if (previous == null)
                return;
            try { previous.DidExit(player); }
            catch { }
            hoveringInteractableField.SetValue(player, null);
        }

        internal static void ResetGameplayInteractionReadout(Player player)
        {
            if (player == null)
                return;

            // The game's prompt manager is reference-counted. Entering an inspection
            // changes PointAndClickNode.DidExit to the investigate counter, which can
            // leave the gameplay counter above zero even after every actual hover is
            // gone. Reset only that gameplay channel as the interaction camera returns;
            // the normal VR ray will freshly enter a real target on the following frame.
            ClearGameplayInteractionHover(player);
            try
            {
                var readout = player.GetReadoutManager();
                if (readout == null)
                    return;
                readoutGameplayHoverCountField?.SetValue(readout, 0);
                readoutGameplayVisibleField?.SetValue(readout, false);
            }
            catch { }
        }

        private static Quaternion StableLookRotation(Vector3 forward, Vector3 suggestedUp)
        {
            var up = Vector3.ProjectOnPlane(suggestedUp, forward);
            if (up.sqrMagnitude < 0.001f)
                up = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (up.sqrMagnitude < 0.001f)
                up = Vector3.ProjectOnPlane(Vector3.right, forward);
            return Quaternion.LookRotation(forward, up.normalized);
        }

        private static void NormalizeHandScale(Transform itemTransform, Transform wrist)
        {
            if (itemTransform == null || wrist == null)
                return;
            var maximumFingerReach = 0f;
            foreach (var transform in wrist.GetComponentsInChildren<Transform>(true))
            {
                var name = transform.name.ToLowerInvariant();
                if (!ContainsAny(name, "finger", "thumb", "index", "middle", "ring", "pinky"))
                    continue;
                maximumFingerReach = Mathf.Max(maximumFingerReach,
                    Vector3.Distance(wrist.position, transform.position));
            }
            if (maximumFingerReach < 0.01f)
                return;

            // An adult wrist-to-fingertip reach is about 18 cm. MFN's desktop viewmodels are
            // intentionally oversized because a narrow-FOV camera normally makes them appear
            // smaller; world-space VR must undo that exaggeration.
            var factor = Mathf.Clamp(0.155f / maximumFingerReach, 0.15f, 1.15f);
            itemTransform.localScale = Vector3.Scale(itemTransform.localScale,
                new Vector3(factor, factor, factor));
            Debug.Log("MFNVR: normalized viewmodel hand reach from " +
                maximumFingerReach.ToString("F3") + "m with scale factor " +
                factor.ToString("F3") + ".");
        }

        private static void ShrinkWrenchAssembly(ItemInHand item)
        {
            if (item == null ||
                item.name.IndexOf("Wrench", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            // MFN's wrench viewmodel was authored at a much larger desktop-camera scale
            // than the pistol. The old correction reduced only the rigid wrench meshes,
            // leaving its skinned hands and any surviving arm geometry oversized. Apply the
            // same correction once at the common item root so every renderer, bone, hand and
            // grip offset stays in proportion. ApplyTrackedTransforms subsequently snaps the
            // wrist back to the exact controller position, so this does not change tracking.
            const float wrenchScale = 0.68f;
            item.transform.localScale = Vector3.Scale(item.transform.localScale,
                Vector3.one * wrenchScale);
            Debug.Log("MFNVR: normalized the complete wrench, hand, and arm assembly to " +
                wrenchScale.ToString("P0") + " of its desktop-viewmodel size.");
        }

        private static bool IsUnderHand(Transform transform)
        {
            var cursor = transform;
            while (cursor != null)
            {
                if (cursor.name.IndexOf("PL_HAND", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                cursor = cursor.parent;
            }
            return false;
        }

        private static Transform FindNamedTransform(ItemInHand item, string name)
        {
            if (item == null)
                return null;
            foreach (var transform in item.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(transform.name, name, StringComparison.OrdinalIgnoreCase))
                    return transform;
            }
            return null;
        }

        private static Transform FindWrist(ItemInHand item, string handRootName)
        {
            return FindWristUnder(FindNamedTransform(item, handRootName));
        }

        private static Transform FindWristUnder(Transform handRoot)
        {
            if (handRoot == null)
                return null;
            foreach (var transform in handRoot.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(transform.name, "WRIST", StringComparison.OrdinalIgnoreCase))
                    return transform;
            }
            return null;
        }

        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_GetControllerPose(int hand, int aim,
            out float px, out float py, out float pz,
            out float qx, out float qy, out float qz, out float qw);

        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_GetEyeView(int eye,
            out float px, out float py, out float pz,
            out float qx, out float qy, out float qz, out float qw,
            out float angleLeft, out float angleRight,
            out float angleUp, out float angleDown);

        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_GetControllerInput(int hand,
            out float stickX, out float stickY, out float trigger, out float squeeze,
            out int primary, out int secondary, out int stickClick, out int menu);

        [DllImport("MFNOpenXR.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int MFN_ApplyControllerHaptic(int hand, float amplitude,
            float durationSeconds, float frequency);

        public static void PrepareFloatingHands(ItemInHand item)
        {
            if (item == null)
                return;
            var id = item.GetInstanceID();
            if (!filteredItems.Add(id))
                return;

            foreach (var renderer in item.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer != null && IsArmOnlyPath(renderer.transform))
                    renderer.enabled = false;
            }

            var leftRoot = FindNamedTransform(item, "PL_HAND_L");
            var rightRoot = FindNamedTransform(item, "PL_HAND_R");
            var itemLeftWrist = FindWristUnder(leftRoot);
            var itemRightWrist = FindWristUnder(rightRoot);
            foreach (var renderer in item.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                RemoveArmTriangles(renderer, leftRoot, itemLeftWrist, rightRoot, itemRightWrist);
        }

        private static void RemoveArmTriangles(SkinnedMeshRenderer renderer,
            Transform itemLeftRoot, Transform itemLeftWrist,
            Transform itemRightRoot, Transform itemRightWrist)
        {
            if (renderer == null || renderer.sharedMesh == null)
                return;
            try
            {
                var sourceMesh = renderer.sharedMesh;
                var weights = sourceMesh.boneWeights;
                var bones = renderer.bones;
                if (weights == null || weights.Length != sourceMesh.vertexCount || bones == null)
                    return;

                var armBones = new bool[bones.Length];
                var handBones = new bool[bones.Length];
                var foundArm = false;
                for (var index = 0; index < bones.Length; index++)
                {
                    var name = bones[index] != null ? bones[index].name.ToLowerInvariant() : string.Empty;
                    var bone = bones[index];
                    handBones[index] = IsAtOrBelow(bone, itemLeftWrist) ||
                        IsAtOrBelow(bone, itemRightWrist) ||
                        ContainsAny(name, "hand", "wrist", "finger", "thumb", "index",
                            "middle", "ring", "pinky");
                    armBones[index] = !handBones[index] &&
                        (IsBetweenRootAndWrist(bone, itemLeftRoot, itemLeftWrist) ||
                         IsBetweenRootAndWrist(bone, itemRightRoot, itemRightWrist) ||
                         IsArmBoneName(name));
                    foundArm |= armBones[index];
                }
                if (!foundArm)
                    return;

                var armOnlyVertex = new bool[weights.Length];
                var armWeights = new float[weights.Length];
                var handWeights = new float[weights.Length];
                for (var vertex = 0; vertex < weights.Length; vertex++)
                {
                    var weight = weights[vertex];
                    var armWeight = BoneContribution(weight, armBones);
                    var handWeight = BoneContribution(weight, handBones);
                    armWeights[vertex] = armWeight;
                    handWeights[vertex] = handWeight;
                    armOnlyVertex[vertex] = armWeight > 0.10f && armWeight > handWeight * 1.10f;
                }

                var clone = CreateReadableMeshCopy(sourceMesh);
                clone.name = sourceMesh.name + " (MFN VR Floating Hands)";
                var removed = 0;
                for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
                {
                    var triangles = sourceMesh.GetTriangles(subMesh);
                    var kept = new List<int>(triangles.Length);
                    for (var triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                    {
                        var a = triangles[triangle];
                        var b = triangles[triangle + 1];
                        var c = triangles[triangle + 2];
                        var armVertices = (armOnlyVertex[a] ? 1 : 0) +
                            (armOnlyVertex[b] ? 1 : 0) + (armOnlyVertex[c] ? 1 : 0);
                        var totalArm = armWeights[a] + armWeights[b] + armWeights[c];
                        var totalHand = handWeights[a] + handWeights[b] + handWeights[c];
                        if (armVertices == 3 ||
                            (armVertices >= 2 && totalArm > totalHand * 1.25f))
                        {
                            removed++;
                            continue;
                        }
                        kept.Add(a);
                        kept.Add(b);
                        kept.Add(c);
                    }
                    clone.SetTriangles(kept, subMesh, false);
                }
                if (removed > 0)
                {
                    renderer.sharedMesh = clone;
                    Debug.Log("MFNVR: removed " + removed +
                        " authored arm triangles from " + sourceMesh.name + ".");
                }
                else
                    UnityEngine.Object.Destroy(clone);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: could not filter authored arm mesh " +
                    renderer.name + ": " + exception.Message);
            }
        }

        private static bool IsAtOrBelow(Transform bone, Transform wrist)
        {
            return bone != null && wrist != null && (bone == wrist || bone.IsChildOf(wrist));
        }

        private static bool IsBetweenRootAndWrist(Transform bone, Transform root, Transform wrist)
        {
            if (bone == null || root == null || wrist == null || bone == root)
                return false;
            var cursor = wrist.parent;
            while (cursor != null && cursor != root)
            {
                if (cursor == bone)
                    return true;
                cursor = cursor.parent;
            }
            return false;
        }

        private static bool IsArmBoneName(string name)
        {
            if (ContainsAny(name, "upperarm", "upper_arm", "forearm", "lowerarm",
                "lower_arm", "shoulder", "elbow", "bicep", "tricep", "clavicle", "sleeve"))
                return true;
            return name == "arm" || name.StartsWith("arm_") || name.EndsWith("_arm") ||
                name.IndexOf("_arm_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Mesh CreateReadableMeshCopy(Mesh source)
        {
            var readData = Mesh.AcquireReadOnlyMeshData(source);
            var writeData = Mesh.AllocateWritableMeshData(1);
            var applied = false;
            try
            {
                var input = readData[0];
                var output = writeData[0];
                output.SetVertexBufferParams(input.vertexCount, source.GetVertexAttributes());
                for (var stream = 0; stream < input.vertexBufferCount; stream++)
                    output.GetVertexData<byte>(stream).CopyFrom(input.GetVertexData<byte>(stream));

                if (input.indexFormat == IndexFormat.UInt16)
                {
                    var indices = input.GetIndexData<ushort>();
                    output.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);
                    output.GetIndexData<ushort>().CopyFrom(indices);
                }
                else
                {
                    var indices = input.GetIndexData<uint>();
                    output.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
                    output.GetIndexData<uint>().CopyFrom(indices);
                }

                output.subMeshCount = input.subMeshCount;
                for (var subMesh = 0; subMesh < input.subMeshCount; subMesh++)
                {
                    output.SetSubMesh(subMesh, input.GetSubMesh(subMesh),
                        MeshUpdateFlags.DontRecalculateBounds |
                        MeshUpdateFlags.DontValidateIndices);
                }

                var clone = new Mesh();
                Mesh.ApplyAndDisposeWritableMeshData(writeData, clone,
                    MeshUpdateFlags.DontRecalculateBounds |
                    MeshUpdateFlags.DontValidateIndices);
                applied = true;
                clone.bounds = source.bounds;
                clone.bindposes = source.bindposes;
                return clone;
            }
            finally
            {
                readData.Dispose();
                if (!applied)
                    writeData.Dispose();
            }
        }

        private static float BoneContribution(BoneWeight weight, bool[] mask)
        {
            var result = 0f;
            if (weight.boneIndex0 >= 0 && weight.boneIndex0 < mask.Length && mask[weight.boneIndex0]) result += weight.weight0;
            if (weight.boneIndex1 >= 0 && weight.boneIndex1 < mask.Length && mask[weight.boneIndex1]) result += weight.weight1;
            if (weight.boneIndex2 >= 0 && weight.boneIndex2 < mask.Length && mask[weight.boneIndex2]) result += weight.weight2;
            if (weight.boneIndex3 >= 0 && weight.boneIndex3 < mask.Length && mask[weight.boneIndex3]) result += weight.weight3;
            return result;
        }

        private static bool IsArmOnlyPath(Transform transform)
        {
            var cursor = transform;
            while (cursor != null)
            {
                var name = cursor.name.ToLowerInvariant();
                if (ContainsAny(name, "hand", "wrist", "finger", "weapon", "gun", "wrench"))
                    return false;
                if (IsArmBoneName(name) ||
                    ((name.Contains("arms") || name.Contains("sleeve")) && !name.Contains("firearms")))
                    return true;
                cursor = cursor.parent;
            }
            return false;
        }

        private static bool ContainsAny(string value, params string[] tokens)
        {
            foreach (var token in tokens)
            {
                if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public static void EnsureMirror(Camera hudCamera, Camera worldCamera,
            RenderTexture leftTexture, bool gameplay)
        {
            gameplay = gameplay || IsPhysicalInventoryActive();
            var target = hudCamera != null ? hudCamera : worldCamera;
            if (!gameplay || target == null || leftTexture == null)
            {
                if (mirrorEffect != null)
                    mirrorEffect.enabled = false;
                return;
            }

            if (mirrorEffect == null || mirrorEffect.gameObject != target.gameObject)
            {
                if (mirrorEffect != null)
                    UnityEngine.Object.Destroy(mirrorEffect);
                mirrorEffect = target.gameObject.AddComponent<MirrorBlitEffect>();
            }
            mirrorEffect.Texture = leftTexture;
            mirrorEffect.enabled = true;
        }

        internal static bool ShouldSkipSourceBackbuffer(Camera camera)
        {
            return camera != null && camera.targetTexture == null &&
                   mirrorEffect != null && mirrorEffect.enabled &&
                   mirrorEffect.Texture != null;
        }

        internal static bool ShouldSuppressLegacyPointerAction(
            InputAction.CallbackContext context)
        {
            // The raw OpenXR A button is consumed by UpdateMenuPointerInteraction and
            // applied to the ray target. Block only its virtual-gamepad copy here so
            // keyboard/mouse bindings and non-pointer gameplay remain untouched.
            return menuPointerEnabled && menuPointerInputActive &&
                   context.control != null && context.control.device is Gamepad;
        }

        private static void EnsureSourceBackbufferOptimizer(Camera source)
        {
            if (source == null)
                return;
            if (source.GetComponent<SourceBackbufferOptimizer>() == null)
                source.gameObject.AddComponent<SourceBackbufferOptimizer>();
        }

        private static InventoryInWorld GetPhysicalInventory(Player player = null)
        {
            try
            {
                if (player == null)
                    player = Player.current;
                if (player == null)
                    return null;
                var inventory = player.GetInventory();
                // Box zero is the player's carried inventory; nonzero boxes are the
                // storage/toolbox inventories placed throughout MFN. They share the same
                // InventoryInWorld cameras and controller logic, so all active boxes must
                // use the VR inventory render path instead of being mistaken for a menu.
                if (inventory == null || !inventory.gameObject.activeInHierarchy)
                    return null;
                return inventory;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPhysicalInventoryActive()
        {
            return GetPhysicalInventory() != null;
        }

        private static bool IsUiModeActive()
        {
            if (IsPhysicalInventoryActive())
                return true;
            var player = Player.current;
            if (player == null)
                return false;
            return ReadBooleanField(inventoryControlsEnabledField, player) ||
                   ReadBooleanField(menuControlsEnabledField, player) ||
                   ReadBooleanField(mapControlsEnabledField, player) ||
                   ReadBooleanField(pauseMenuEnabledField, player) ||
                   ReadBooleanField(investigateControlsEnabledField, player);
        }

        private static bool ShouldUseFlatUiScreen()
        {
            // Compatibility mode preserves the old behavior where every state outside
            // the gameplay comfort rig was captured to a flat screen.
            if (!flatScreensOnlyForMainPauseAndFiles)
                return true;
            var player = Player.current;
            if (player == null)
                return true;

            // Files uses MFN's Menu controls. Pause has its own flag, and the title scene
            // reports isOnMainMenu. Every other UI keeps the real MFN camera pose so its
            // authored world-space interface is rendered directly in stereo.
            return ReadBooleanField(isOnMainMenuField, player) ||
                   ReadBooleanField(pauseMenuEnabledField, player) ||
                   ReadBooleanField(menuControlsEnabledField, player);
        }

        private static void ConfigureWorldAttachedUi(Camera source, Camera left,
            Camera right, RenderTexture leftTexture, RenderTexture rightTexture)
        {
            // Core eye cameras use this authored inspection-camera transform as their
            // tracking-space base. Keep the pointer on the identical base; otherwise the
            // eyes move to the interaction while the controller ray remains at the player.
            interactionPointerCamera = source;
            interactionPointerCameraActive = source != null;
            interactionPointerUsesStableRig = false;
            if (!interactionCameraMovement && motionContextValid &&
                haveLastGameplayRig && source != null)
            {
                RemapEyesToStableGameplayRig(source, left, right);
                interactionPointerUsesStableRig = true;
                interactionPointerStablePosition = interactionRigLocked
                    ? lockedInteractionRigPosition
                    : lastGameplayRigPosition;
                interactionPointerStableRotation = interactionRigLocked
                    ? lockedInteractionRigRotation
                    : lastGameplayRigRotation;
            }
            ConfigureGameplayWorld(source, left, right, leftTexture, rightTexture, false);

            // PointAndClickNode.DidEnter moves MFN's selected visual onto one of these
            // layers. The old flat capture camera included them, while the ordinary world
            // camera intentionally does not. Add them only for real-camera UI states;
            // toolbox inventories use their separate path and remain outline-free.
            var hoverMask = 0;
            AddNamedLayer(ref hoverMask, "DefaultHover");
            AddNamedLayer(ref hoverMask, "ExamineHover");
            AddNamedLayer(ref hoverMask, "InvisibleHover");
            left.cullingMask |= hoverMask;
            right.cullingMask |= hoverMask;
            SetStereoOutlineActive(left, right, source, true);
        }

        private static void SetStereoOutlineActive(Camera left, Camera right,
            Camera source, bool active)
        {
            var sourceEffect = active && source != null
                ? source.GetComponent<PostProcessExample>()
                : null;
            active = active && sourceEffect != null && sourceEffect.enabled &&
                     sourceEffect.PostProcessMat != null &&
                     sourceEffect.GetOutlineCamera() != null;
            ConfigureStereoOutline(left, sourceEffect, active);
            ConfigureStereoOutline(right, sourceEffect, active);
        }

        private static void ConfigureStereoOutline(Camera eye,
            PostProcessExample sourceEffect, bool active)
        {
            if (eye == null)
                return;
            var effect = eye.GetComponent<VrStereoOutlineEffect>();
            if (!active)
            {
                if (effect != null)
                    effect.enabled = false;
                return;
            }
            if (effect == null)
                effect = eye.gameObject.AddComponent<VrStereoOutlineEffect>();
            effect.Configure(sourceEffect);
            effect.enabled = true;
        }

        private static void AddNamedLayer(ref int layerMask, string layerName)
        {
            var layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0 && layer < 32)
                layerMask |= 1 << layer;
        }

        private static bool ReadBooleanField(FieldInfo field, object instance)
        {
            try
            {
                return field != null && field.GetValue(instance) is bool value && value;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsToolboxInventory(InventoryInWorld inventory)
        {
            if (inventory == null)
                return false;
            if (inventory.GetComponentInParent<ItemBoxParent>() != null)
                return true;

            // ItemBoxParent.current is static and can survive briefly after the toolbox
            // closes. Only accept it when the active inventory is actually one of that
            // box's two live grids, otherwise the normal Y inventory is misclassified.
            var itemBox = ItemBoxParent.current;
            if (itemBox == null)
                return false;
            return itemBox.GetInventory() == inventory ||
                   itemBox.GetInventoryInWorld() == inventory;
        }

        private static bool IsCutsceneActive()
        {
            var player = Player.current;
            if (player == null)
                return false;
            try
            {
                if (player.GetIsInDirector() || player.GetTheatricalAnimation())
                    return true;
                return directorAnimation2Field != null &&
                    directorAnimation2Field.GetValue(player) is bool pendingDirector &&
                    pendingDirector;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyCutscenePositionFollow(Camera source, Camera left,
            Camera right, bool coreUsingComfortRig)
        {
            if (!IsCutsceneActive() || source == null || left == null || right == null)
            {
                cutscenePositionFollowActive = false;
                cutscenePositionFollowSource = null;
                return;
            }

            if (!cutscenePositionFollowActive || cutscenePositionFollowSource != source)
            {
                cutscenePositionFollowActive = true;
                cutscenePositionFollowSource = source;
                cutsceneSourceToRigStart = source.transform.position - motionRigPosition;
                Debug.Log("MFNVR: cutscene X/Y/Z camera following started.");
                return;
            }

            // If the core is already rendering directly from the normal camera it already
            // contains this translation. With the comfort rig active, add only the normal
            // camera's motion relative to that rig. Player-root motion shared by both is not
            // counted twice, and physical headset movement remains untouched.
            if (!coreUsingComfortRig)
                return;
            var sourceToRig = source.transform.position - motionRigPosition;
            var relativeDelta = sourceToRig - cutsceneSourceToRigStart;
            left.transform.position += relativeDelta;
            right.transform.position += relativeDelta;
        }

        private static void ConfigurePhysicalInventoryWorld(Camera source, Camera left,
            Camera right, RenderTexture leftTexture, RenderTexture rightTexture)
        {
            var inventory = GetPhysicalInventory();
            if (inventory == null)
            {
                ConfigureMenuScreen(source, left, right, leftTexture, rightTexture);
                return;
            }
            var toolboxInventory = IsToolboxInventory(inventory);
            if (toolboxInventory)
            {
                // Toolbox views use MFN's authored cameraTransform just like other
                // inspection interfaces. The pointer must use that same tracking base.
                interactionPointerCamera = source;
                interactionPointerCameraActive = source != null;
            }

            // The core deliberately stops attaching the normal camera to the player's body
            // while MFN animates into a flat inventory view. Recover each OpenXR eye's local
            // pose from that animated source camera, then remap it onto the last stable room-
            // scale rig. This keeps normal 6DOF head movement instead of inheriting MFN's
            // neck/inventory animation.
            // Standalone inventory is detached from MFN's animated flat camera and placed
            // in room scale. Toolbox grids are authored around ItemBoxParent's dedicated
            // cameraTransform, so retain that camera base or the lower black drawer grid
            // ends up outside the eye frusta.
            if (motionContextValid && !toolboxInventory)
            {
                RemapEyesToStableGameplayRig(source, left, right);
            }

            ConfigureGameplayWorld(source, left, right, leftTexture, rightTexture, false);
            PreparePhysicalInventory(inventory, left, right);

            // MFN intentionally excludes these layers from its normal world camera because
            // the desktop inventory has two dedicated cameras. Include their exact authored
            // masks in both real eye renders so the board, item meshes, slot highlights and
            // dropdown objects are genuine stereo world geometry.
            var inventoryMask = 0;
            var player = Player.current;
            var inventoryCamera = player != null ? player.GetInventoryCamera() : null;
            var cursorCamera = player != null && inventoryCursorCameraField != null
                ? inventoryCursorCameraField.GetValue(player) as Camera
                : null;
            if (inventoryCamera != null)
                inventoryMask |= inventoryCamera.cullingMask;
            if (cursorCamera != null)
                inventoryMask |= cursorCamera.cullingMask;
            AddNamedLayer(ref inventoryMask, "InventoryCursor");

            // ItemBoxParent creates two separate InventoryInWorld objects.  The lower
            // black drawer grid is normally revealed by animation events (EnableSquares
            // and the later item activation pass).  Those events are unreliable once the
            // original flat inventory cameras are replaced by the stereo render path, so
            // make the already-created toolbox contents visible explicitly.  This does not
            // create, move, or relink anything; MFN still owns all inventory navigation and
            // placement state.
            if (toolboxInventory)
            {
                inventoryMask |= PrepareToolboxInventoryVisuals();
                PositionToolboxDrawerForStereo(source);
            }
            else
            {
                RestoreToolboxDrawerPosition();
            }

            left.cullingMask |= inventoryMask;
            right.cullingMask |= inventoryMask;
            // Both the carried inventory and toolbox cameras can include MFN's Hands
            // layer. Strip it from the world eyes as well as disabling the separate
            // Hands overlay pair for every physical inventory UI.
            left.cullingMask &= ~(1 << HandsLayer);
            right.cullingMask &= ~(1 << HandsLayer);

            if (!menuPointerEnabled)
                SetInventoryPointerVisible(false);
            if (menuScreen != null)
                menuScreen.SetActive(false);
            ResetFlatMenuPointerInteraction();
        }

        private static int PrepareToolboxInventoryVisuals()
        {
            var itemBox = ItemBoxParent.current;
            if (itemBox == null)
                return 0;

            var layerMask = 0;
            AddToolboxInventoryVisuals(itemBox.GetInventory(), ref layerMask);
            AddToolboxInventoryVisuals(itemBox.GetInventoryInWorld(), ref layerMask);
            return layerMask;
        }

        private static void PositionToolboxDrawerForStereo(Camera source)
        {
            var itemBox = ItemBoxParent.current;
            var drawer = itemBox != null ? itemBox.GetInventoryInWorld() : null;
            if (drawer == null || source == null)
            {
                RestoreToolboxDrawerPosition();
                return;
            }

            if (offsetToolboxDrawer != drawer || !haveOffsetToolboxDrawerPosition)
            {
                RestoreToolboxDrawerPosition();
                offsetToolboxDrawer = drawer;
                offsetToolboxDrawerLocalPosition = drawer.transform.localPosition;
                haveOffsetToolboxDrawerPosition = true;
                Debug.Log("MFNVR: lower toolbox grid stereo depth correction applied.");
            }

            var transform = drawer.transform;
            var parent = transform.parent;
            var authoredWorldPosition = parent != null
                ? parent.TransformPoint(offsetToolboxDrawerLocalPosition)
                : offsetToolboxDrawerLocalPosition;
            var towardCamera = source.transform.position - authoredWorldPosition;
            if (towardCamera.sqrMagnitude < 0.0001f)
                return;

            // Two centimetres is enough to put the coplanar grid/item geometry in front
            // of the black drawer surface without visibly separating it from the toolbox.
            var worldOffset = towardCamera.normalized * 0.02f;
            var localOffset = parent != null
                ? parent.InverseTransformVector(worldOffset)
                : worldOffset;
            transform.localPosition = offsetToolboxDrawerLocalPosition + localOffset;
        }

        private static void RestoreToolboxDrawerPosition()
        {
            if (offsetToolboxDrawer != null && haveOffsetToolboxDrawerPosition)
                offsetToolboxDrawer.transform.localPosition = offsetToolboxDrawerLocalPosition;
            offsetToolboxDrawer = null;
            haveOffsetToolboxDrawerPosition = false;
        }

        private static void AddToolboxInventoryVisuals(InventoryInWorld inventory,
            ref int layerMask)
        {
            if (inventory == null)
                return;

            if (!inventory.gameObject.activeSelf)
                inventory.gameObject.SetActive(true);

            // Enables every authored slot/grid tile, including the lower black drawer.
            inventory.EnableSquares();

            // This is MFN's own final presentation step. In the unmodified game it is
            // reached through the toolbox animation timeline; invoke it directly because
            // the VR camera path can enter the toolbox before that timeline callback.
            try
            {
                inventoryActivateObjectsMethod?.Invoke(inventory, null);
            }
            catch
            {
                // The explicit activation below remains a safe fallback across game builds.
            }

            // Setup() deliberately leaves instantiated item meshes inactive until MFN's
            // presentation animation completes.  They already represent the real stored
            // contents, so activating them here is equivalent to that vanilla completion
            // step and preserves the game's controller-based inventory logic.
            foreach (var item in inventory.GetComponentsInChildren<ItemInInventory>(true))
            {
                if (item != null && !item.gameObject.activeSelf)
                    item.gameObject.SetActive(true);
            }

            // Do not guess which layers the two toolbox prefabs use.  Include the exact
            // union of their live hierarchy in both OpenXR eyes so grids, items, outlines,
            // and dropdown indicators cannot be silently culled.
            foreach (var child in inventory.GetComponentsInChildren<Transform>(true))
            {
                if (child != null)
                    layerMask |= 1 << child.gameObject.layer;
            }
        }

        private static void PreparePhysicalInventory(InventoryInWorld inventory,
            Camera leftEye, Camera rightEye)
        {
            if (inventory == null)
                return;
            EnsurePhysicalInventoryState(inventory);

            // Toolboxes author two linked grids directly on the chest: the player's
            // carried inventory on the upper panel and storage inside the drawer. Moving
            // either root like the standalone Y-button inventory stacks the grids on top
            // of one another and breaks MFN's gamepad node navigation. Leave both toolbox
            // inventories exactly where ItemBoxParent placed them; the stereo cameras add
            // their layers below while MFN keeps full ownership of interaction mechanics.
            if (inventory.GetComponentInParent<ItemBoxParent>() != null)
            {
                physicalInventoryPositioned = true;
                return;
            }

            if (physicalInventoryPositioned || physicalInventoryRows == null ||
                physicalInventoryRows.Length == 0 || physicalInventoryRows[0] == null ||
                physicalInventoryRows[0].row == null ||
                physicalInventoryRows[0].row.Length == 0)
                return;

            var lastRow = physicalInventoryRows[physicalInventoryRows.Length - 1];
            if (lastRow == null || lastRow.row == null || lastRow.row.Length == 0)
                return;
            var topLeft = physicalInventoryRows[0].row[0];
            var bottomRight = lastRow.row[lastRow.row.Length - 1];
            if (topLeft == null || bottomRight == null)
                return;

            var headPosition = (leftEye.transform.position + rightEye.transform.position) * 0.5f;
            var headRotation = Quaternion.Slerp(leftEye.transform.rotation,
                rightEye.transform.rotation, 0.5f);
            var forward = headRotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
                forward = motionRigRotation * Vector3.forward;
            forward.y = 0f;
            forward.Normalize();

            var floorY = headPosition.y - 1.45f;
            RaycastHit floorHit;
            var floorMask = LayerMask.GetMask("Default", "Level", "DontInteractWithPlayer");
            if (Physics.Raycast(headPosition + Vector3.up * 0.1f, Vector3.down,
                out floorHit, 3.5f, floorMask, QueryTriggerInteraction.Ignore))
                floorY = floorHit.point.y;

            var root = inventory.transform;
            root.rotation = Quaternion.LookRotation(forward, Vector3.up);
            var gridCenterLocal = (topLeft.transform.localPosition +
                bottomRight.transform.localPosition) * 0.5f;
            var desiredGridCenter = headPosition + forward * 1.05f;
            desiredGridCenter.y = floorY + 0.035f;
            root.position = desiredGridCenter - root.TransformVector(gridCenterLocal);
            physicalInventoryPositioned = true;
            Debug.Log("MFNVR: placed physical inventory grid on the floor " +
                Vector3.Distance(headPosition, desiredGridCenter).ToString("F2") +
                "m from the headset.");
        }

        private static void EnsurePhysicalInventoryState(InventoryInWorld inventory)
        {
            if (inventory == null || physicalInventory == inventory)
                return;
            physicalInventory = inventory;
            physicalInventoryRows = inventoryRowsField != null
                ? inventoryRowsField.GetValue(inventory) as InventoryRowOfSquares[]
                : null;
            physicalInventoryHeldItem = null;
            physicalInventoryPositioned = false;
            previousInventoryGripPressed = false;
            previousInventoryTriggerPressed = false;
            previousInventoryPrimaryPressed = false;
            previousInventoryRotatePressed = false;
            inventoryPointerFrame = -1;
            Debug.Log("MFNVR: right-hand native menu pointer opened.");
        }

        private static void ResetPhysicalInventoryStateIfClosed()
        {
            if (physicalInventory == null && physicalInventoryHeldItem == null)
                return;
            physicalInventory = null;
            physicalInventoryRows = null;
            physicalInventoryHeldItem = null;
            physicalInventoryPositioned = false;
            previousInventoryGripPressed = false;
            previousInventoryTriggerPressed = false;
            previousInventoryPrimaryPressed = false;
            previousInventoryRotatePressed = false;
            inventoryPointerFrame = -1;
            SetInventoryPointerVisible(false);
        }

        private static void UpdateMenuPointerInteraction(Player player,
            InventoryInWorld inventory)
        {
            if (inventoryPointerFrame == Time.frameCount)
                return;
            inventoryPointerFrame = Time.frameCount;
            if (!menuPointerEnabled || player == null || !motionPoseValid)
            {
                ResetMenuPointerInteraction();
                return;
            }

            // Drop any gameplay-world hover that was left over when this UI opened.
            // Trigger input in a menu must never retain a bell/door/toolbox as the
            // Player's ordinary interaction target.
            if (hoveringInteractableField != null)
            {
                var gameplayHover = hoveringInteractableField.GetValue(player) as
                    Interactable;
                if (gameplayHover != null)
                {
                    try { gameplayHover.DidExit(player); }
                    catch { }
                    hoveringInteractableField.SetValue(player, null);
                }
            }

            menuPointerInputActive = true;
            DisableInventorySquareHighlights(inventory);
            if (inventory == null || IsToolboxInventory(inventory))
                ApplyInteractionPointerCameraSpace();
            PoseMenuPointerHand();
            SetMenuPointerVisualLayer(LayerMask.NameToLayer("Default"));
            float stickX, stickY, trigger, squeeze;
            int primary, secondary, stickClick, menu;
            var haveInput = MFN_GetControllerInput(1, out stickX, out stickY,
                out trigger, out squeeze, out primary, out secondary,
                out stickClick, out menu) != 0;
            var triggerPressed = haveInput && trigger >= 0.72f;
            var primaryPressed = haveInput && primary != 0;
            var rotatePressed = haveInput && stickClick != 0;
            var triggerStarted = triggerPressed && !previousInventoryTriggerPressed;
            var primaryStarted = primaryPressed && !previousInventoryPrimaryPressed;
            var pointerSelectStarted = triggerStarted || primaryStarted;
            var rotateStarted = rotatePressed && !previousInventoryRotatePressed;
            float leftStickX, leftStickY, leftTrigger, leftSqueeze;
            int leftPrimary, leftSecondary, leftStickClick, leftMenu;
            var haveLeftInput = MFN_GetControllerInput(0, out leftStickX, out leftStickY,
                out leftTrigger, out leftSqueeze, out leftPrimary, out leftSecondary,
                out leftStickClick, out leftMenu) != 0;
            var leftTriggerPressed = haveLeftInput && leftTrigger >= 0.72f;
            var leftTriggerStarted = leftTriggerPressed &&
                !previousMenuPointerLeftTriggerPressed;
            var ray = new Ray(rightAimWorldPosition,
                rightAimWorldRotation * Vector3.forward);
            var point = ray.GetPoint(3f);
            var validTarget = false;

            // ItemBoxParent creates both inventory grids as soon as the toolbox opens,
            // before the player chooses either section. During that choice screen MFN
            // is still in Investigate controls, so route the pointer to the upper/lower
            // section nodes. Only switch to item/slot manipulation after a section has
            // enabled Inventory controls.
            var toolboxSectionChoice = inventory != null &&
                IsToolboxInventory(inventory) &&
                !ReadBooleanField(inventoryControlsEnabledField, player);
            if (toolboxSectionChoice && !toolboxSectionChoiceActive)
            {
                toolboxSectionChoiceActive = true;
                // Consume the input which opened the toolbox. The held-state
                // latches below then require a release and a fresh press before
                // either toolbox section can be selected.
                pointerSelectStarted = false;
            }
            else if (!toolboxSectionChoice)
            {
                toolboxSectionChoiceActive = false;
            }
            if (inventory != null && !toolboxSectionChoice)
            {
                validTarget = UpdateNativeInventoryPointer(player, inventory, ray,
                    pointerSelectStarted, leftTriggerStarted, rotateStarted, out point);
            }
            else
            {
                validTarget = UpdateNativeInteractablePointer(player, ray,
                    pointerSelectStarted, out point);
            }

            EnsureInventoryPointerVisual();
            SetInventoryPointerVisible(true);
            inventoryPointerDot.transform.position = point;
            inventoryPointerLine.SetPosition(0, rightAimWorldPosition);
            inventoryPointerLine.SetPosition(1, point);
            SetInventoryPointerColor(validTarget
                ? new Color(0.20f, 1f, 0.35f, 1f)
                : new Color(1f, 0.78f, 0.05f, 1f));
            previousInventoryTriggerPressed = triggerPressed;
            previousInventoryPrimaryPressed = primaryPressed;
            previousMenuPointerLeftTriggerPressed = leftTriggerPressed;
            previousInventoryRotatePressed = rotatePressed;
        }

        private static void ApplyInteractionPointerCameraSpace()
        {
            if (!interactionPointerCameraActive || interactionPointerCamera == null)
                return;
            var cameraPosition = interactionPointerUsesStableRig
                ? interactionPointerStablePosition
                : interactionPointerCamera.transform.position;
            var cameraRotation = interactionPointerUsesStableRig
                ? interactionPointerStableRotation
                : interactionPointerCamera.transform.rotation;
            rightGripWorldPosition = cameraPosition +
                cameraRotation * rightGripLocalPosition;
            rightGripWorldRotation = cameraRotation * rightGripLocalRotation;
            rightAimWorldPosition = cameraPosition +
                cameraRotation * rightAimLocalPosition;
            rightAimWorldRotation = cameraRotation * rightAimLocalRotation;
        }

        private static void RemapEyesToStableGameplayRig(Camera source, Camera left,
            Camera right)
        {
            if (source == null || left == null || right == null)
                return;
            var inverseSource = Quaternion.Inverse(source.transform.rotation);
            var leftLocalPosition = inverseSource *
                (left.transform.position - source.transform.position);
            var rightLocalPosition = inverseSource *
                (right.transform.position - source.transform.position);
            var leftLocalRotation = inverseSource * left.transform.rotation;
            var rightLocalRotation = inverseSource * right.transform.rotation;
            var stablePosition = interactionRigLocked
                ? lockedInteractionRigPosition
                : (haveLastGameplayRig ? lastGameplayRigPosition : motionRigPosition);
            var stableRotation = interactionRigLocked
                ? lockedInteractionRigRotation
                : (haveLastGameplayRig ? lastGameplayRigRotation : motionRigRotation);
            left.transform.SetPositionAndRotation(
                stablePosition + stableRotation * leftLocalPosition,
                stableRotation * leftLocalRotation);
            right.transform.SetPositionAndRotation(
                stablePosition + stableRotation * rightLocalPosition,
                stableRotation * rightLocalRotation);
        }


        private static bool UpdateNativeInventoryPointer(Player player,
            InventoryInWorld initialInventory, Ray ray, bool triggerStarted,
            bool openItemMenuStarted, bool rotateStarted, out Vector3 point)
        {
            if (menuPointerHoveredInteractable != null)
            {
                try { menuPointerHoveredInteractable.DidExit(player); }
                catch { }
                menuPointerHoveredInteractable = null;
            }
            point = ray.GetPoint(3f);
            var inventories = GetPointerInventories(initialInventory);
            var dropdownInventory = FindOpenDropdownInventory(inventories);
            if (dropdownInventory != null)
            {
                var openDropdown = ResolveOpenInventoryDropdown(dropdownInventory,
                    inventories);
                PrepareInventoryDropdownForStereo(openDropdown);
                dropdownInventory.DisableCursor();
                player.SetCurrentInventory(dropdownInventory);
                menuPointerInventory = dropdownInventory;
                return UpdateInventoryDropdownPointer(player, dropdownInventory,
                    openDropdown, ray, triggerStarted, out point);
            }
            RestoreInventoryDropdownLayers();
            var heldInventory = FindInventoryHoldingItem(inventories);
            var heldItem = heldInventory != null
                ? heldInventory.GetCurrentlyHoldingItem()
                : null;

            InventoryInWorld targetInventory;
            ItemInInventory pointedItem;
            InventorySquare pointedSquare;
            if (heldItem == null)
            {
                pointedItem = FindPointerInventoryItem(ray, inventories,
                    out targetInventory, out point);
                pointedSquare = FindPointerInventorySquare(ray, inventories,
                    out targetInventory, ref point);
                if (pointedItem != null)
                {
                    targetInventory = GetItemInventory(pointedItem) ?? targetInventory;
                    point = GetClosestItemPointerPoint(ray, pointedItem, point);
                }
            }
            else
            {
                targetInventory = heldInventory;
                pointedItem = null;
                pointedSquare = FindPointerInventorySquare(ray,
                    new List<InventoryInWorld> { heldInventory }, out targetInventory,
                    ref point);
            }

            var activeInventory = heldInventory ?? targetInventory ?? initialInventory;
            if (activeInventory != null)
            {
                activeInventory.DisableCursor();
                player.SetCurrentInventory(activeInventory);
                PositionNativeInventoryCursor(activeInventory, point, pointedSquare);
            }
            SetNativeInventoryHover(player, activeInventory, pointedItem);
            SetNativeInventorySquare(activeInventory, heldItem, pointedSquare);

            if (activeInventory != null && openItemMenuStarted && heldItem == null &&
                pointedItem != null && !activeInventory.GetIsInDropdown() &&
                !activeInventory.IsClosing())
            {
                player.bufferAFrameForInventory = -1f;
                player.SetCurrentInventory(activeInventory);
                activeInventory.CheckOpenDropdown();
                if (activeInventory.GetIsInDropdown())
                    MFN_ApplyControllerHaptic(0, 0.28f, 0.045f, 0f);
            }

            if (activeInventory != null && rotateStarted && heldItem != null &&
                !activeInventory.GetIsInDropdown())
            {
                InvokeInventoryRotate(activeInventory);
                MFN_ApplyControllerHaptic(1, 0.22f, 0.035f, 0f);
            }

            if (activeInventory != null && triggerStarted &&
                !activeInventory.IsClosing())
            {
                if (activeInventory.GetIsInDropdown())
                {
                    activeInventory.Interact();
                }
                else if (heldItem == null && pointedItem != null)
                {
                    player.bufferAFrameForInventory = -1f;
                    player.SetCurrentInventory(activeInventory);
                    player.PickUpInventoryItem(pointedItem);
                    physicalInventoryHeldItem = activeInventory.GetCurrentlyHoldingItem();
                    if (physicalInventoryHeldItem != null)
                        MFN_ApplyControllerHaptic(1, 0.35f, 0.055f, 0f);
                }
                else if (heldItem != null && pointedSquare != null)
                {
                    // Clear the exact footprint before MFN nulls its held-item field.
                    // Otherwise the old white placement squares cannot be identified
                    // on the following frame and remain stuck on the grid.
                    ClearNativeInventorySquareHighlight();
                    inventoryClosestToField?.SetValue(activeInventory, pointedSquare);
                    inventoryCurrentSquareField?.SetValue(activeInventory, pointedSquare);
                    var placed = InvokeInventoryPutDown(player, activeInventory);
                    physicalInventoryHeldItem = activeInventory.GetCurrentlyHoldingItem();
                    MFN_ApplyControllerHaptic(1, placed ? 0.30f : 0.12f,
                        placed ? 0.050f : 0.030f, 0f);
                }
            }

            physicalInventory = activeInventory;
            physicalInventoryHeldItem = activeInventory != null
                ? activeInventory.GetCurrentlyHoldingItem()
                : null;
            return pointedItem != null || pointedSquare != null;
        }

        private static List<InventoryInWorld> GetPointerInventories(
            InventoryInWorld initialInventory)
        {
            var result = new List<InventoryInWorld>(3);
            AddPointerInventory(result, initialInventory);
            var itemBox = ItemBoxParent.current;
            if (itemBox != null)
            {
                AddPointerInventory(result, itemBox.GetInventory());
                AddPointerInventory(result, itemBox.GetInventoryInWorld());
            }
            return result;
        }

        private static void AddPointerInventory(List<InventoryInWorld> inventories,
            InventoryInWorld inventory)
        {
            if (inventory != null && inventory.gameObject.activeInHierarchy &&
                !inventories.Contains(inventory))
                inventories.Add(inventory);
        }

        private static void DisableInventorySquareHighlights(
            InventoryInWorld initialInventory)
        {
            var inventories = GetPointerInventories(initialInventory);
            foreach (var inventory in inventories)
            {
                var rows = inventoryRowsField?.GetValue(inventory) as
                    InventoryRowOfSquares[];
                if (rows == null)
                    continue;
                foreach (var row in rows)
                {
                    if (row == null || row.row == null)
                        continue;
                    foreach (var square in row.row)
                    {
                        if (square == null)
                            continue;
                        // Completely remove the filled white placement/hover tile.
                        // Grid lines use a separate renderer and remain visible.
                        try { square.SnapToOff(); }
                        catch { }
                        var hoverRenderer = inventorySquareHoverBackgroundField?
                            .GetValue(square) as MeshRenderer;
                        if (hoverRenderer != null && hoverRenderer.enabled)
                            hoverRenderer.enabled = false;
                    }
                }
            }
        }

        private static InventoryInWorld FindInventoryHoldingItem(
            List<InventoryInWorld> inventories)
        {
            foreach (var inventory in inventories)
            {
                if (inventory != null && inventory.GetCurrentlyHoldingItem() != null)
                    return inventory;
            }
            return null;
        }

        private static InventoryInWorld FindOpenDropdownInventory(
            List<InventoryInWorld> inventories)
        {
            foreach (var inventory in inventories)
            {
                if (inventory != null && inventory.GetIsInDropdown())
                    return inventory;
            }
            return null;
        }

        private static NewIntentoryDropdown ResolveOpenInventoryDropdown(
            InventoryInWorld owner, List<InventoryInWorld> inventories)
        {
            var direct = owner != null ? owner.GetDropdown() : null;
            if (IsInventoryDropdownVisible(direct))
                return direct;

            // A toolbox owns two linked InventoryInWorld instances. MFN records the
            // open flag on the item owner, but GetMyInventoryDropdown can resolve via
            // whichever linked inventory is currently assigned to Player.inventory.
            // Locate the actual visible dropdown instead of assuming both references
            // always point at the same object.
            if (inventories != null)
            {
                foreach (var inventory in inventories)
                {
                    var candidate = inventory != null ? inventory.GetDropdown() : null;
                    if (IsInventoryDropdownVisible(candidate))
                        return candidate;
                }
            }
            try
            {
                var playerDropdown = Player.current != null
                    ? Player.current.GetMyInventoryDropdown()
                    : null;
                if (IsInventoryDropdownVisible(playerDropdown))
                    return playerDropdown;
            }
            catch { }
            return direct;
        }

        private static bool IsInventoryDropdownVisible(NewIntentoryDropdown dropdown)
        {
            if (dropdown == null || !dropdown.gameObject.activeInHierarchy)
                return false;
            try
            {
                return dropdownVisibleField != null &&
                       dropdownVisibleField.GetValue(dropdown) is bool visible && visible;
            }
            catch
            {
                return false;
            }
        }

        private static void PrepareInventoryDropdownForStereo(
            NewIntentoryDropdown dropdown)
        {
            if (dropdown == null)
                return;
            var targetLayer = LayerMask.NameToLayer("InventoryCursor");
            if (targetLayer < 0)
                targetLayer = LayerMask.NameToLayer("Inventory");
            if (targetLayer < 0)
                targetLayer = 0;
            MoveDropdownHierarchyToLayer(dropdown.transform, targetLayer);
            ScaleInventoryDropdownForVr(dropdown.transform);
            MoveInventoryDropdownInFrontOfGrid(dropdown.transform);
            var confirmer = dropdown.GetConfirmer();
            if (confirmer != null)
            {
                MoveDropdownHierarchyToLayer(confirmer.transform, targetLayer);
                ScaleInventoryDropdownForVr(confirmer.transform);
                if (!confirmer.transform.IsChildOf(dropdown.transform))
                    MoveInventoryDropdownInFrontOfGrid(confirmer.transform);
            }
        }

        private static void MoveInventoryDropdownInFrontOfGrid(Transform root)
        {
            if (root == null || interactionPointerCamera == null)
                return;
            Vector3 originalPosition;
            if (!inventoryDropdownOriginalPositions.TryGetValue(root,
                out originalPosition))
            {
                originalPosition = root.position;
                inventoryDropdownOriginalPositions.Add(root, originalPosition);
            }
            var towardCamera = interactionPointerCamera.transform.position -
                originalPosition;
            if (towardCamera.sqrMagnitude < 0.000001f)
                return;
            // Keep it anchored to the selected item while placing it in front of
            // the toolbox/inventory surfaces so their depth cannot hide the menu.
            root.position = originalPosition + towardCamera.normalized * 0.12f;
        }

        private static void ScaleInventoryDropdownForVr(Transform root)
        {
            if (root == null)
                return;
            Vector3 originalScale;
            if (inventoryDropdownOriginalScales.TryGetValue(root, out originalScale))
            {
                root.localScale = originalScale * 2f;
                return;
            }

            float firstSeen;
            if (!inventoryDropdownFirstSeenTimes.TryGetValue(root, out firstSeen))
            {
                inventoryDropdownFirstSeenTimes.Add(root, Time.unscaledTime);
                return;
            }

            // Toolbox dropdowns can begin their opening animation at zero scale.
            // Capturing and reapplying that first-frame value prevents the animation
            // from ever becoming visible. Let the native animation settle first.
            if (Time.unscaledTime - firstSeen < 0.18f)
                return;
            originalScale = root.localScale;
            if (Mathf.Abs(originalScale.x) < 0.0001f ||
                Mathf.Abs(originalScale.y) < 0.0001f ||
                Mathf.Abs(originalScale.z) < 0.0001f)
                return;
            inventoryDropdownOriginalScales.Add(root, originalScale);
            root.localScale = originalScale * 2f;
        }

        private static void MoveDropdownHierarchyToLayer(Transform root, int layer)
        {
            if (root == null)
                return;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == null)
                    continue;
                var gameObject = child.gameObject;
                if (!inventoryDropdownOriginalLayers.ContainsKey(gameObject))
                    inventoryDropdownOriginalLayers.Add(gameObject, gameObject.layer);
                gameObject.layer = layer;
            }
        }

        private static void RestoreInventoryDropdownLayers()
        {
            if (inventoryDropdownOriginalLayers.Count == 0 &&
                inventoryDropdownOriginalScales.Count == 0 &&
                inventoryDropdownOriginalPositions.Count == 0 &&
                inventoryDropdownFirstSeenTimes.Count == 0)
                return;
            foreach (var pair in inventoryDropdownOriginalLayers)
            {
                if (pair.Key != null)
                    pair.Key.layer = pair.Value;
            }
            inventoryDropdownOriginalLayers.Clear();
            foreach (var pair in inventoryDropdownOriginalScales)
            {
                if (pair.Key != null)
                    pair.Key.localScale = pair.Value;
            }
            inventoryDropdownOriginalScales.Clear();
            foreach (var pair in inventoryDropdownOriginalPositions)
            {
                if (pair.Key != null)
                    pair.Key.position = pair.Value;
            }
            inventoryDropdownOriginalPositions.Clear();
            inventoryDropdownFirstSeenTimes.Clear();
        }

        private static bool UpdateInventoryDropdownPointer(Player player,
            InventoryInWorld inventory, NewIntentoryDropdown dropdown, Ray ray,
            bool triggerStarted, out Vector3 point)
        {
            point = ray.GetPoint(3f);
            if (dropdown == null)
                return false;

            if (dropdown.GetIsInConfirm())
            {
                var confirmer = dropdown.GetConfirmer();
                var nodes = confirmTextsField?.GetValue(confirmer) as InventoryConfirmNode[];
                var index = FindDropdownNodeIndex(ray, inventory, nodes, out point);
                if (index < 0)
                    return false;
                nodes[index].DidEnter(player);
                if (triggerStarted)
                {
                    Debug.Log("MFNVR: Selected inventory confirmation option " +
                        nodes[index].GetText() + " with the right-hand pointer.");
                    confirmer.DoAction();
                    MFN_ApplyControllerHaptic(1, 0.28f, 0.045f, 0f);
                }
                return true;
            }

            var optionNodes = dropdownTextsField?.GetValue(dropdown) as
                InventoryDropdownNode[];
            var optionIndex = FindDropdownNodeIndex(ray, inventory, optionNodes,
                out point);
            if (optionIndex < 0)
                return false;
            optionNodes[optionIndex].DidEnter(player);
            if (triggerStarted)
            {
                Debug.Log("MFNVR: Selected inventory dropdown option " +
                    optionNodes[optionIndex].GetText() +
                    " with the right-hand pointer.");
                dropdown.CheckForConfirm();
                MFN_ApplyControllerHaptic(1, 0.28f, 0.045f, 0f);
            }
            return true;
        }

        private static int FindDropdownNodeIndex<T>(Ray ray,
            InventoryInWorld inventory, T[] nodes, out Vector3 point)
            where T : Component
        {
            point = ray.GetPoint(3f);
            if (inventory == null || nodes == null)
                return -1;
            var activeNodes = new List<KeyValuePair<int, Transform>>();
            for (var index = 0; index < nodes.Length; index++)
            {
                var node = nodes[index];
                if (node != null && node.gameObject.activeInHierarchy)
                    activeNodes.Add(new KeyValuePair<int, Transform>(index,
                        node.transform));
            }
            if (activeNodes.Count == 0)
                return -1;

            // The vanilla free-cursor path targets these exact colliders. Using
            // them first keeps the VR pointer's hover and selection identical to
            // the game's own mouse/gamepad dropdown behavior.
            var cursorLayer = LayerMask.NameToLayer("InventoryCursor");
            if (cursorLayer >= 0)
            {
                var hits = Physics.RaycastAll(ray, 6f, 1 << cursorLayer,
                    QueryTriggerInteraction.Collide);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var hit in hits)
                {
                    var hitNode = hit.collider != null
                        ? hit.collider.GetComponent<T>()
                        : null;
                    if (hitNode == null && hit.collider != null)
                        hitNode = hit.collider.GetComponentInParent<T>();
                    if (hitNode == null || !hitNode.gameObject.activeInHierarchy)
                        continue;
                    for (var index = 0; index < nodes.Length; index++)
                    {
                        if (nodes[index] != hitNode)
                            continue;
                        point = hit.point;
                        return index;
                    }
                }
            }

            // Some dropdown prefabs have their collider on a sibling rather
            // than the text node. Test the rendered text bounds next so aiming
            // directly at a visible word still selects it.
            var closestRendererHit = float.MaxValue;
            var rendererIndex = -1;
            var rendererPoint = point;
            foreach (var pair in activeNodes)
            {
                foreach (var renderer in pair.Value.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || !renderer.enabled)
                        continue;
                    var bounds = renderer.bounds;
                    bounds.Expand(0.035f);
                    float hitDistance;
                    if (!bounds.IntersectRay(ray, out hitDistance) ||
                        hitDistance <= 0f || hitDistance > 6f ||
                        hitDistance >= closestRendererHit)
                        continue;
                    closestRendererHit = hitDistance;
                    rendererIndex = pair.Key;
                    rendererPoint = ray.GetPoint(hitDistance);
                }
            }
            if (rendererIndex >= 0)
            {
                point = rendererPoint;
                return rendererIndex;
            }

            var spacing = 0.055f;
            if (activeNodes.Count > 1)
            {
                spacing = float.MaxValue;
                for (var index = 1; index < activeNodes.Count; index++)
                    spacing = Mathf.Min(spacing, Vector3.Distance(
                        activeNodes[index - 1].Value.position,
                        activeNodes[index].Value.position));
                if (spacing == float.MaxValue || spacing < 0.005f)
                    spacing = 0.055f;
            }
            var maximumDistance = Mathf.Clamp(spacing * 1.25f, 0.10f, 0.24f);
            var bestIndex = -1;
            var bestDistance = maximumDistance;
            foreach (var pair in activeNodes)
            {
                var toNode = pair.Value.position - ray.origin;
                var alongRay = Vector3.Dot(toNode, ray.direction);
                if (alongRay <= 0f || alongRay > 6f)
                    continue;
                var nearestRayPoint = ray.GetPoint(alongRay);
                var distance = Vector3.Distance(nearestRayPoint,
                    pair.Value.position);
                if (distance >= bestDistance)
                    continue;
                bestIndex = pair.Key;
                bestDistance = distance;
                point = pair.Value.position;
            }
            return bestIndex;
        }

        private static ItemInInventory FindPointerInventoryItem(Ray ray,
            List<InventoryInWorld> inventories, out InventoryInWorld owner,
            out Vector3 point)
        {
            owner = null;
            point = ray.GetPoint(3f);
            var inventoryLayer = LayerMask.NameToLayer("Inventory");
            if (inventoryLayer < 0)
                return null;
            var hits = Physics.RaycastAll(ray, 6f, 1 << inventoryLayer,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null)
                    continue;
                var sender = hit.collider.GetComponent<InteractionSender>();
                var item = sender != null ? sender.item :
                    hit.collider.GetComponentInParent<ItemInInventory>();
                if (item == null || !item.gameObject.activeInHierarchy)
                    continue;
                var itemOwner = GetItemInventory(item);
                if (itemOwner == null || !inventories.Contains(itemOwner))
                    continue;
                owner = itemOwner;
                point = hit.point;
                return item;
            }
            return null;
        }

        private static InventoryInWorld GetItemInventory(ItemInInventory item)
        {
            return item != null && itemInventoryField != null
                ? itemInventoryField.GetValue(item) as InventoryInWorld
                : null;
        }

        private static Vector3 GetClosestItemPointerPoint(Ray ray,
            ItemInInventory item, Vector3 fallback)
        {
            var collider = item != null ? item.GetMyCol() : null;
            if (collider == null)
                return fallback;
            RaycastHit hit;
            return collider.Raycast(ray, out hit, 6f) ? hit.point : fallback;
        }

        private static InventorySquare FindPointerInventorySquare(Ray ray,
            List<InventoryInWorld> inventories, out InventoryInWorld owner,
            ref Vector3 point)
        {
            owner = null;
            InventorySquare bestSquare = null;
            var bestRayDistance = float.MaxValue;
            foreach (var inventory in inventories)
            {
                var rows = inventoryRowsField?.GetValue(inventory) as InventoryRowOfSquares[];
                var first = GetFirstInventorySquare(rows);
                if (first == null)
                    continue;
                var plane = new Plane(inventory.transform.up, first.transform.position);
                float enter;
                if (!plane.Raycast(ray, out enter) || enter <= 0f || enter > 6f)
                    continue;
                var planePoint = ray.GetPoint(enter);
                var spacing = GetInventorySlotSpacing(rows);
                var maximumDistance = Mathf.Clamp(spacing * 0.72f, 0.055f, 0.18f);
                InventorySquare closest = null;
                var closestDistance = maximumDistance;
                foreach (var row in rows)
                {
                    if (row == null || row.row == null)
                        continue;
                    foreach (var square in row.row)
                    {
                        if (square == null)
                            continue;
                        var distance = Vector3.Distance(planePoint,
                            square.transform.position);
                        if (distance < closestDistance)
                        {
                            closest = square;
                            closestDistance = distance;
                        }
                    }
                }
                if (closest == null || enter >= bestRayDistance)
                    continue;
                bestSquare = closest;
                owner = inventory;
                point = planePoint;
                bestRayDistance = enter;
            }
            return bestSquare;
        }

        private static InventorySquare GetFirstInventorySquare(
            InventoryRowOfSquares[] rows)
        {
            if (rows == null)
                return null;
            foreach (var row in rows)
            {
                if (row == null || row.row == null)
                    continue;
                foreach (var square in row.row)
                {
                    if (square != null)
                        return square;
                }
            }
            return null;
        }

        private static float GetInventorySlotSpacing(InventoryRowOfSquares[] rows)
        {
            if (rows == null || rows.Length == 0 || rows[0] == null ||
                rows[0].row == null)
                return 0.12f;
            var first = rows[0].row;
            if (first.Length > 1 && first[0] != null && first[1] != null)
                return Vector3.Distance(first[0].transform.position,
                    first[1].transform.position);
            if (rows.Length > 1 && first.Length > 0 && first[0] != null &&
                rows[1] != null && rows[1].row != null &&
                rows[1].row.Length > 0 && rows[1].row[0] != null)
                return Vector3.Distance(first[0].transform.position,
                    rows[1].row[0].transform.position);
            return 0.12f;
        }

        private static void PositionNativeInventoryCursor(InventoryInWorld inventory,
            Vector3 point, InventorySquare square)
        {
            if (inventory == null || inventoryCursorField == null)
                return;
            var cursor = inventoryCursorField.GetValue(inventory) as Component;
            if (cursor == null)
                return;
            cursor.transform.position = square != null
                ? new Vector3(square.transform.position.x, point.y,
                    square.transform.position.z)
                : point;
            if (square != null)
            {
                inventoryCurrentSquareField?.SetValue(inventory, square);
                inventoryClosestToField?.SetValue(inventory, square);
            }
        }

        private static void SetNativeInventoryHover(Player player,
            InventoryInWorld inventory, ItemInInventory item)
        {
            if (menuPointerHoveredItem == item && menuPointerInventory == inventory)
                return;
            if (menuPointerHoveredItem != null)
            {
                try { player.GetInventoryItemNameController().UnsetText(); }
                catch { }
            }
            if (menuPointerInventory != null && inventoryCurrentlyHoveringField != null)
                inventoryCurrentlyHoveringField.SetValue(menuPointerInventory, null);
            menuPointerHoveredItem = item;
            menuPointerInventory = inventory;
            if (inventory != null && inventoryCurrentlyHoveringField != null)
                inventoryCurrentlyHoveringField.SetValue(inventory, item);
            if (item != null)
            {
                player.SetCurrentInventory(inventory);
                // ItemInInventory.DidEnter also turns every occupied grid square
                // white. For a ray pointer that is distracting and can leave stale
                // counters behind. Reproduce only the selection state required by
                // pickup/dropdown actions and the item-name readout.
                try
                {
                    player.SetLastDidEnter(item);
                    var node = item.GetMyInventoryNode();
                    player.GetInventoryItemNameController().SetText(
                        InventoryManager.GetStringFromItem(node.myItem, node.amount,
                            node.extraData).ToString().ToUpper());
                    player.GetMyInventoryDropdown().SetDropdownSettings(item);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("MFNVR: inventory pointer hover setup failed: " +
                        exception);
                }
            }
        }

        private static void SetNativeInventorySquare(InventoryInWorld inventory,
            ItemInInventory heldItem, InventorySquare square)
        {
            if (heldItem == null || inventory == null || square == null)
            {
                ClearNativeInventorySquareHighlight();
                if (inventory != null)
                    inventoryPickingUpSquareField?.SetValue(inventory, null);
                return;
            }
            if (menuPointerSquare == square &&
                menuPointerHighlightItem == heldItem &&
                menuPointerHighlightInventory == inventory)
                return;
            ClearNativeInventorySquareHighlight();
            menuPointerSquare = square;
            menuPointerHighlightItem = heldItem;
            menuPointerHighlightInventory = inventory;
            inventoryPickingUpSquareField?.SetValue(inventory, square);
            try { inventory.SetHoverSquares(heldItem, true, square); }
            catch { }
        }

        private static void ClearNativeInventorySquareHighlight()
        {
            if (menuPointerHighlightInventory != null)
            {
                // MFN and the VR pointer can both touch the native hover counter in
                // one frame. Normalize every square instead of decrementing only once;
                // this also repairs highlights left by an earlier failed placement.
                var rows = inventoryRowsField?.GetValue(menuPointerHighlightInventory)
                    as InventoryRowOfSquares[];
                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        if (row == null || row.row == null)
                            continue;
                        foreach (var square in row.row)
                        {
                            if (square == null)
                                continue;
                            while (square.GetHoverAmount() > 0)
                                square.SetHoverOff();
                        }
                    }
                }
            }
            menuPointerSquare = null;
            menuPointerHighlightItem = null;
            menuPointerHighlightInventory = null;
        }

        private static void InvokeInventoryRotate(InventoryInWorld inventory)
        {
            if (inventory == null || inventoryRotateMethod == null)
                return;
            try
            {
                var parameterType = inventoryRotateMethod.GetParameters()[0].ParameterType;
                inventoryRotateMethod.Invoke(inventory,
                    new[] { Activator.CreateInstance(parameterType) });
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: right-hand inventory rotation failed: " + exception);
            }
        }

        private static bool UpdateNativeInteractablePointer(Player player, Ray ray,
            bool triggerStarted, out Vector3 point)
        {
            ClearNativeInventoryPointerState(player);
            const float interactionPointerRange = 2f;
            point = ray.GetPoint(interactionPointerRange);
            Interactable target = null;
            // The toolbox selector colliders do not cover the visible grids reliably.
            // Determine which real grid the ray is over and map it to the authored
            // upper/lower PointAndClickNode that opens that exact inventory section.
            TryFindToolboxGridSectionTarget(player, ray, interactionPointerRange,
                out target, out point);
            // Toolbox section choices are authored as the current point-and-click
            // view's active child nodes. Test those exact clickers first so the visible
            // upper/lower section under the ray wins regardless of surrounding toolbox
            // geometry or component ordering.
            if (target == null)
                TryFindExplicitCurrentViewTarget(player, ray, interactionPointerRange,
                    out target, out point);
            // Use the same interaction layers as MFN's free-cursor path. Inspection
            // props are frequently placed behind the closet/desk model's decorative
            // colliders, so nearby scenery must not occlude nodes belonging to the
            // current view. The current-view ownership check below is the safety gate.
            var mask = 0;
            AddNamedLayer(ref mask, "Inventory");
            AddNamedLayer(ref mask, "Examine");
            AddNamedLayer(ref mask, "ExamineHover");
            AddNamedLayer(ref mask, "UI");
            AddNamedLayer(ref mask, "InventoryHover");
            AddNamedLayer(ref mask, "InvisibleHover");
            AddNamedLayer(ref mask, "Invisible");
            AddNamedLayer(ref mask, "Default");
            AddNamedLayer(ref mask, "Level");
            AddNamedLayer(ref mask, "DefaultHover");
            AddNamedLayer(ref mask, "LevelProjectilePassthrough");
            if (target == null)
            {
                var hits = Physics.RaycastAll(ray, interactionPointerRange, mask,
                    QueryTriggerInteraction.Collide);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var hit in hits)
                {
                    if (hit.collider == null)
                        continue;
                    // Ignore the player/controller presentation itself. Other geometry
                    // may be crossed only while looking for a node explicitly owned by
                    // the currently open interaction view.
                    if (hit.collider.GetComponentInParent<Player>() != null ||
                        (menuRightHandVisualRoot != null &&
                         hit.collider.transform.IsChildOf(menuRightHandVisualRoot)))
                        continue;
                    foreach (var behaviour in
                        hit.collider.GetComponentsInParent<MonoBehaviour>(true))
                    {
                        var interactable = behaviour as Interactable;
                        if (interactable == null || interactable is ItemInInventory ||
                            interactable is InventorySquare)
                            continue;
                        if (IsTargetInCurrentInteractionView(player, hit.collider,
                            interactable))
                        {
                            target = interactable;
                            point = hit.point;
                            break;
                        }
                    }
                    if (target != null)
                        break;
                }
            }

            if (!ReferenceEquals(menuPointerHoveredInteractable, target))
            {
                if (menuPointerHoveredInteractable != null)
                {
                    try { menuPointerHoveredInteractable.DidExit(player); }
                    catch { }
                }
                menuPointerHoveredInteractable = target;
                if (target != null)
                {
                    try { target.DidEnter(player); }
                    catch { }
                }
            }
            if (target != null && triggerStarted)
            {
                try
                {
                    var pointedNode = target.GetGamepadNode();
                    var gamepadControl = player.GetMyGamepadControl();
                    if (pointedNode != null)
                    {
                        gamepadControl.UnsetFreeInteraction();
                        gamepadControl.SetToNode(pointedNode);
                        gamepadControl.Action();
                    }
                    else
                    {
                        target.Interact(player);
                    }
                    MFN_ApplyControllerHaptic(1, 0.28f, 0.045f, 0f);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("MFNVR: pointed menu interaction failed: " + exception);
                }
            }
            return target != null;
        }

        private static bool TryFindToolboxGridSectionTarget(Player player, Ray ray,
            float maximumDistance, out Interactable target, out Vector3 point)
        {
            target = null;
            point = ray.GetPoint(maximumDistance);
            var itemBox = ItemBoxParent.current;
            var playerCamera = player != null ? player.GetMyCamera() : null;
            var currentView = playerCamera != null
                ? playerCamera.GetCurrentPAC()
                : null;
            if (itemBox == null || currentView == null)
                return false;

            var upperInventory = itemBox.GetInventory();
            var drawerInventory = itemBox.GetInventoryInWorld();
            var hitUpper = TryHitInventoryGrid(ray, upperInventory, maximumDistance,
                out var upperPoint, out var upperDistance);
            var hitDrawer = TryHitInventoryGrid(ray, drawerInventory, maximumDistance,
                out var drawerPoint, out var drawerDistance);
            if (!hitUpper && !hitDrawer)
                return false;

            var selectDrawer = hitDrawer && (!hitUpper || drawerDistance < upperDistance);
            point = selectDrawer ? drawerPoint : upperPoint;
            PointAndClickNode sectionNode = null;
            if (currentView.activeForThisView != null)
            {
                foreach (var candidate in currentView.activeForThisView)
                {
                    if (IsMatchingToolboxSection(candidate, itemBox, selectDrawer))
                    {
                        sectionNode = candidate;
                        break;
                    }
                }
            }
            if (sectionNode == null && currentView.nodes != null)
            {
                foreach (var gamepadNode in currentView.nodes)
                {
                    var candidate = gamepadNode != null
                        ? gamepadNode.GetMyInteractable() as PointAndClickNode
                        : null;
                    if (IsMatchingToolboxSection(candidate, itemBox, selectDrawer))
                    {
                        sectionNode = candidate;
                        break;
                    }
                }
            }
            if (sectionNode == null)
                return false;
            target = sectionNode;
            return true;
        }

        private static bool TryHitInventoryGrid(Ray ray, InventoryInWorld inventory,
            float maximumDistance, out Vector3 point, out float distance)
        {
            point = ray.GetPoint(maximumDistance);
            distance = float.PositiveInfinity;
            if (inventory == null)
                return false;
            InventoryInWorld owner;
            var candidatePoint = point;
            var square = FindPointerInventorySquare(ray,
                new List<InventoryInWorld> { inventory }, out owner,
                ref candidatePoint);
            if (square == null || owner != inventory)
                return false;
            distance = Vector3.Distance(ray.origin, candidatePoint);
            if (distance > maximumDistance)
                return false;
            point = candidatePoint;
            return true;
        }

        private static bool IsMatchingToolboxSection(PointAndClickNode candidate,
            ItemBoxParent itemBox, bool selectDrawer)
        {
            if (candidate == null || !candidate.isActive)
                return false;
            var section = candidate.interactor as ItemBoxSetToInventory;
            if (section == null)
                return false;
            var owner = itemBoxSectionParentField?.GetValue(section) as ItemBoxParent;
            if (owner != null && owner != itemBox)
                return false;
            var isDrawer = itemBoxSectionIsDrawerField != null &&
                itemBoxSectionIsDrawerField.GetValue(section) is bool drawer && drawer;
            return isDrawer == selectDrawer;
        }

        private static bool TryFindExplicitCurrentViewTarget(Player player, Ray ray,
            float maximumDistance, out Interactable target, out Vector3 point)
        {
            target = null;
            point = ray.GetPoint(maximumDistance);
            var playerCamera = player != null ? player.GetMyCamera() : null;
            var currentView = playerCamera != null
                ? playerCamera.GetCurrentPAC()
                : null;
            if (currentView == null || currentView.activeForThisView == null)
                return false;

            var closestDistance = maximumDistance;
            foreach (var allowed in currentView.activeForThisView)
            {
                if (allowed == null || !allowed.isActive || allowed.myClicker == null ||
                    !allowed.myClicker.enabled)
                    continue;
                RaycastHit hit;
                if (!allowed.myClicker.Raycast(ray, out hit, maximumDistance) ||
                    hit.distance >= closestDistance)
                    continue;
                target = allowed;
                point = hit.point;
                closestDistance = hit.distance;
            }
            return target != null;
        }

        private static bool IsTargetInCurrentInteractionView(Player player,
            Collider hitCollider, Interactable target)
        {
            if (player == null || target == null)
                return false;
            var playerCamera = player.GetMyCamera();
            var currentView = playerCamera != null
                ? playerCamera.GetCurrentPAC()
                : null;

            // Outside a point-and-click camera stack, retain MFN's normal UI-layer
            // behavior. World-layer interactables are deliberately excluded here;
            // gameplay interaction belongs to Player.CheckForInteraction instead.
            if (currentView == null)
            {
                var layer = (target as Component)?.gameObject.layer ?? -1;
                return layer == LayerMask.NameToLayer("Inventory") ||
                       layer == LayerMask.NameToLayer("Examine") ||
                       layer == LayerMask.NameToLayer("ExamineHover") ||
                       layer == LayerMask.NameToLayer("UI") ||
                       layer == LayerMask.NameToLayer("InventoryHover") ||
                       layer == LayerMask.NameToLayer("InvisibleHover") ||
                       layer == LayerMask.NameToLayer("Invisible");
            }

            // Only nodes explicitly exposed by the view on top of MFN's camera stack
            // may receive hover/clicks. This prevents active nodes belonging to a nearby
            // desk, toolbox, door, or an older nested menu from leaking into this view.
            var pointAndClick = target as PointAndClickNode;
            if (pointAndClick != null)
            {
                if (!pointAndClick.isActive || pointAndClick.myClicker == null ||
                    !pointAndClick.myClicker.enabled)
                    return false;
                if (currentView.activeForThisView != null)
                {
                    foreach (var allowed in currentView.activeForThisView)
                    {
                        if (ReferenceEquals(allowed, pointAndClick))
                            return true;
                    }
                }
            }

            if (currentView.activateCollidersForThisView != null)
            {
                foreach (var allowedCollider in currentView.activateCollidersForThisView)
                {
                    if (allowedCollider == null)
                        continue;
                    if (ReferenceEquals(allowedCollider, hitCollider) ||
                        hitCollider.transform.IsChildOf(allowedCollider.transform) ||
                        allowedCollider.transform.IsChildOf(hitCollider.transform))
                        return true;
                }
            }

            var targetNode = target.GetGamepadNode();
            if (targetNode != null && currentView.nodes != null)
            {
                foreach (var allowedNode in currentView.nodes)
                {
                    if (ReferenceEquals(allowedNode, targetNode) &&
                        allowedNode.GetIsActive())
                        return true;
                }
            }
            return false;
        }

        private static void PoseMenuPointerHand()
        {
            if (menuRightHandVisualRoot == null)
                CreateMenuRightHandVisual();
            if (menuRightHandVisualRoot == null)
                return;
            menuRightHandVisualRoot.SetPositionAndRotation(rightGripWorldPosition,
                rightAimWorldRotation);
            if (!menuRightHandVisualRoot.gameObject.activeSelf)
                menuRightHandVisualRoot.gameObject.SetActive(true);
        }

        private static void ResetMenuPointerInteraction()
        {
            var player = Player.current;
            var inventoryToRestore = menuPointerInventory;
            if (menuPointerHoveredInteractable != null && player != null)
            {
                try { menuPointerHoveredInteractable.DidExit(player); }
                catch { }
            }
            menuPointerHoveredInteractable = null;
            ClearNativeInventoryPointerState(player);
            if (!menuPointerEnabled && inventoryToRestore != null && player != null &&
                IsUiModeActive() && !inventoryToRestore.IsClosing())
            {
                try { inventoryToRestore.EnableCursor(); }
                catch { }
            }
            menuPointerInputActive = false;
            RestoreInventoryDropdownLayers();
            previousInventoryTriggerPressed = false;
            previousInventoryPrimaryPressed = false;
            previousMenuPointerLeftTriggerPressed = false;
            previousInventoryRotatePressed = false;
            toolboxSectionChoiceActive = false;
            SetInventoryPointerVisible(false);
            if (menuRightHandVisualRoot != null &&
                menuRightHandVisualRoot.gameObject.activeSelf)
                menuRightHandVisualRoot.gameObject.SetActive(false);
        }

        private static void ClearNativeInventoryPointerState(Player player)
        {
            var inventory = menuPointerInventory;
            ClearNativeInventorySquareHighlight();
            if (menuPointerHoveredItem != null && player != null)
            {
                try { player.GetInventoryItemNameController().UnsetText(); }
                catch { }
            }
            if (inventory != null && inventoryCurrentlyHoveringField != null)
                inventoryCurrentlyHoveringField.SetValue(inventory, null);
            if (inventory != null && inventoryPickingUpSquareField != null)
                inventoryPickingUpSquareField.SetValue(inventory, null);
            menuPointerHoveredItem = null;
            menuPointerInventory = null;
        }

        private static void EnsureInventoryPointerVisual()
        {
            if (inventoryPointerDot != null && inventoryPointerLine != null)
                return;
            var shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            inventoryPointerMaterial = new Material(shader);
            inventoryPointerMaterial.color = new Color(1f, 0.78f, 0.05f, 1f);

            inventoryPointerDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            inventoryPointerDot.name = "MFN VR Inventory Pointer Dot";
            var inventoryPointerLayer = LayerMask.NameToLayer("Default");
            if (inventoryPointerLayer < 0)
                inventoryPointerLayer = 0;
            inventoryPointerDot.layer = inventoryPointerLayer;
            inventoryPointerDot.transform.localScale = Vector3.one * 0.025f;
            var collider = inventoryPointerDot.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.Destroy(collider);
            var dotRenderer = inventoryPointerDot.GetComponent<MeshRenderer>();
            dotRenderer.material = inventoryPointerMaterial;
            dotRenderer.shadowCastingMode = ShadowCastingMode.Off;
            dotRenderer.receiveShadows = false;

            var lineObject = new GameObject("MFN VR Inventory Pointer Ray");
            lineObject.layer = inventoryPointerLayer;
            inventoryPointerLine = lineObject.AddComponent<LineRenderer>();
            inventoryPointerLine.sharedMaterial = inventoryPointerMaterial;
            inventoryPointerLine.positionCount = 2;
            inventoryPointerLine.startWidth = 0.012f;
            inventoryPointerLine.endWidth = 0.008f;
            inventoryPointerLine.useWorldSpace = true;
            inventoryPointerLine.shadowCastingMode = ShadowCastingMode.Off;
            inventoryPointerLine.receiveShadows = false;
            SetInventoryPointerVisible(false);
        }

        private static void SetInventoryPointerColor(Color color)
        {
            if (inventoryPointerMaterial != null)
                inventoryPointerMaterial.color = color;
        }

        private static void SetInventoryPointerVisible(bool visible)
        {
            if (inventoryPointerDot != null && inventoryPointerDot.activeSelf != visible)
                inventoryPointerDot.SetActive(visible);
            if (inventoryPointerLine != null &&
                inventoryPointerLine.gameObject.activeSelf != visible)
                inventoryPointerLine.gameObject.SetActive(visible);
        }

        private static void SetMenuPointerVisualLayer(int layer)
        {
            if (layer < 0)
                layer = 0;
            if (inventoryPointerDot != null)
                inventoryPointerDot.layer = layer;
            if (inventoryPointerLine != null)
                inventoryPointerLine.gameObject.layer = layer;
            if (menuRightHandVisualRoot != null)
            {
                foreach (var child in menuRightHandVisualRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (child != null)
                        child.gameObject.layer = layer;
                }
            }
        }

        private static void UpdatePhysicalInventoryInteraction(Player player,
            InventoryInWorld inventory)
        {
            if (player == null || inventory == null)
                return;
            EnsurePhysicalInventoryState(inventory);
            inventory.DisableCursor();

            float stickX, stickY, trigger, squeeze;
            int primary, secondary, stickClick, menu;
            var haveInput = MFN_GetControllerInput(0, out stickX, out stickY,
                out trigger, out squeeze, out primary, out secondary,
                out stickClick, out menu) != 0;
            var gripPressed = haveInput && squeeze >= 0.72f;

            if (gripPressed && !previousInventoryGripPressed &&
                physicalInventoryHeldItem == null && leftPoseValid && motionPoseValid &&
                !inventory.IsClosing())
                TryGrabPhysicalInventoryItem(player, inventory);

            if (!gripPressed && previousInventoryGripPressed &&
                physicalInventoryHeldItem != null)
                ReleasePhysicalInventoryItem(player, inventory);

            previousInventoryGripPressed = gripPressed;
            UpdatePhysicalInventoryHeldPose();
        }

        private static void TryGrabPhysicalInventoryItem(Player player,
            InventoryInWorld inventory)
        {
            ItemInInventory nearest = null;
            var nearestDistance = 0.14f;
            foreach (var item in inventory.GetComponentsInChildren<ItemInInventory>(false))
            {
                if (item == null || !item.gameObject.activeInHierarchy ||
                    !item.GetCanInteract())
                    continue;
                var collider = item.GetMyCol();
                if (collider == null || !collider.enabled ||
                    !collider.gameObject.activeInHierarchy)
                    continue;
                var distance = Vector3.Distance(leftGripWorldPosition,
                    collider.ClosestPoint(leftGripWorldPosition));
                if (distance >= nearestDistance)
                    continue;
                nearest = item;
                nearestDistance = distance;
            }
            if (nearest == null)
                return;

            var node = nearest.GetMyInventoryNode();
            physicalItemOriginalLocalPosition = nearest.transform.localPosition;
            physicalItemOriginalLocalRotation = nearest.transform.localRotation;
            physicalItemOriginalX = node.xPos;
            physicalItemOriginalY = node.yPos;
            physicalItemOriginalRotation = node.rotation;
            physicalGrabControllerRotation = leftGripWorldRotation;
            physicalGrabPositionOffset = Quaternion.Inverse(leftGripWorldRotation) *
                (nearest.transform.position - leftGripWorldPosition);
            physicalGrabRotationOffset = Quaternion.Inverse(leftGripWorldRotation) *
                nearest.transform.rotation;

            player.bufferAFrameForInventory = -1f;
            inventory.PickUpItem(nearest, true, true);
            if (inventory.GetCurrentlyHoldingItem() != nearest)
                return;
            physicalInventoryHeldItem = nearest;
            nearest.DisableLerp();
            MFN_ApplyControllerHaptic(0, 0.35f, 0.055f, 0f);
            Debug.Log("MFNVR: physically grabbed inventory item " + nearest.name + ".");
        }

        private static void UpdatePhysicalInventoryHeldPose()
        {
            if (physicalInventoryHeldItem == null || !leftPoseValid)
                return;
            physicalInventoryHeldItem.transform.SetPositionAndRotation(
                leftGripWorldPosition + leftGripWorldRotation * physicalGrabPositionOffset,
                leftGripWorldRotation * physicalGrabRotationOffset);
        }

        private static void ReleasePhysicalInventoryItem(Player player,
            InventoryInWorld inventory)
        {
            var item = physicalInventoryHeldItem;
            if (item == null)
                return;
            var node = item.GetMyInventoryNode();
            var closest = FindClosestPhysicalInventorySquare(item.transform.position,
                out var closestDistance);
            var slotSpacing = GetPhysicalInventorySlotSpacing();
            var releaseRadius = Mathf.Clamp(slotSpacing * 1.35f, 0.14f, 0.28f);
            var placed = false;

            if (closest != null && closestDistance <= releaseRadius)
            {
                node.rotation = GetPhysicalInventoryRotation(inventory.transform,
                    physicalGrabControllerRotation, leftGripWorldRotation,
                    physicalItemOriginalRotation);
                item.transform.localRotation = physicalItemOriginalLocalRotation;
                item.transform.localPosition = new Vector3(closest.transform.localPosition.x,
                    physicalItemOriginalLocalPosition.y, closest.transform.localPosition.z);
                item.SetRotation(true);
                placed = InvokeInventoryPutDown(player, inventory);
            }

            if (!placed)
            {
                // MFN rejected the slot (overlap/out of bounds), or the hand was released
                // away from the grid. Restore the exact saved node and pose, then let MFN's
                // own placement routine relink its controls and occupancy map.
                node.xPos = physicalItemOriginalX;
                node.yPos = physicalItemOriginalY;
                node.rotation = physicalItemOriginalRotation;
                item.transform.localPosition = physicalItemOriginalLocalPosition;
                item.transform.localRotation = physicalItemOriginalLocalRotation;
                item.SetRotation(true);
                InvokeInventoryPutDown(player, inventory);
            }

            item.EnableLerp();
            physicalInventoryHeldItem = null;
            MFN_ApplyControllerHaptic(0, placed ? 0.28f : 0.16f,
                placed ? 0.050f : 0.035f, 0f);
            Debug.Log(placed
                ? "MFNVR: physically placed inventory item into a valid slot."
                : "MFNVR: returned inventory item to its pickup slot.");
        }

        private static bool InvokeInventoryPutDown(Player player,
            InventoryInWorld inventory)
        {
            if (player == null || inventory == null || inventoryPutDownMethod == null)
                return false;
            try
            {
                player.bufferAFrameForInventory = -1f;
                var parameterType = inventoryPutDownMethod.GetParameters()[0].ParameterType;
                inventoryPutDownMethod.Invoke(inventory,
                    new[] { Activator.CreateInstance(parameterType) });
                return inventory.GetCurrentlyHoldingItem() == null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: physical inventory placement failed: " + exception);
                return false;
            }
        }

        private static InventorySquare FindClosestPhysicalInventorySquare(
            Vector3 worldPosition, out float distance)
        {
            InventorySquare closest = null;
            distance = float.MaxValue;
            if (physicalInventoryRows == null)
                return null;
            foreach (var row in physicalInventoryRows)
            {
                if (row == null || row.row == null)
                    continue;
                foreach (var square in row.row)
                {
                    if (square == null)
                        continue;
                    var candidateDistance = Vector3.Distance(worldPosition,
                        square.transform.position);
                    if (candidateDistance >= distance)
                        continue;
                    closest = square;
                    distance = candidateDistance;
                }
            }
            return closest;
        }

        private static float GetPhysicalInventorySlotSpacing()
        {
            if (physicalInventoryRows == null || physicalInventoryRows.Length == 0 ||
                physicalInventoryRows[0] == null || physicalInventoryRows[0].row == null)
                return 0.12f;
            var firstRow = physicalInventoryRows[0].row;
            if (firstRow.Length > 1 && firstRow[0] != null && firstRow[1] != null)
                return Vector3.Distance(firstRow[0].transform.position,
                    firstRow[1].transform.position);
            if (physicalInventoryRows.Length > 1 && firstRow.Length > 0 &&
                firstRow[0] != null && physicalInventoryRows[1] != null &&
                physicalInventoryRows[1].row != null &&
                physicalInventoryRows[1].row.Length > 0 &&
                physicalInventoryRows[1].row[0] != null)
                return Vector3.Distance(firstRow[0].transform.position,
                    physicalInventoryRows[1].row[0].transform.position);
            return 0.12f;
        }

        private static int GetPhysicalInventoryRotation(Transform inventoryRoot,
            Quaternion startControllerRotation, Quaternion currentControllerRotation,
            int originalRotation)
        {
            var normal = inventoryRoot != null ? inventoryRoot.up : Vector3.up;
            var startDirection = Vector3.ProjectOnPlane(
                startControllerRotation * Vector3.forward, normal);
            var currentDirection = Vector3.ProjectOnPlane(
                currentControllerRotation * Vector3.forward, normal);
            if (startDirection.sqrMagnitude < 0.01f || currentDirection.sqrMagnitude < 0.01f)
                return originalRotation;
            var angle = Vector3.SignedAngle(startDirection, currentDirection, normal);
            var quarterTurns = Mathf.RoundToInt(angle / 90f);
            var rotation = (originalRotation + quarterTurns) % 4;
            if (rotation < 0)
                rotation += 4;
            return rotation;
        }

        private static void ApplyPhysicalInventoryHandPose()
        {
            if (!leftPoseValid)
                return;
            LoadLeftHandCalibration();
            var rotation = GetTrackedLeftHandVisualRotation();
            if (usingBakedLeftHand && leftHandVisualRoot != null)
            {
                leftHandVisualRoot.SetPositionAndRotation(leftGripWorldPosition, rotation);
                EnsureLeftHandVisible();
            }
            else if (leftHandRoot != null)
            {
                ApplyLeftHandVisualPose(leftGripWorldPosition, rotation);
            }
        }

        private static void ConfigureGameplayWorld(Camera source, Camera left, Camera right,
            RenderTexture leftTexture, RenderTexture rightTexture,
            bool coreUsingComfortRig)
        {
            ApplyCutscenePositionFollow(source, left, right, coreUsingComfortRig);
            // Capture the exact Rift matrices and physical eye offsets before configuring
            // any head-locked overlay. This keeps overlay geometry undistorted after the
            // OpenXR compositor applies the headset lens warp.
            worldLeftProjection = left.projectionMatrix;
            worldRightProjection = right.projectionMatrix;
            worldLeftEyePosition = left.transform.position;
            worldRightEyePosition = right.transform.position;
            worldLeftEyeRotation = left.transform.rotation;
            worldRightEyeRotation = right.transform.rotation;
            worldCenterProjection = AverageProjection(worldLeftProjection, worldRightProjection);
            var centerPosition = (left.transform.position + right.transform.position) * 0.5f;
            var centerRotation = Quaternion.Slerp(left.transform.rotation, right.transform.rotation, 0.5f);
            var inverseCenter = Quaternion.Inverse(centerRotation);
            worldLeftEyeOffset = inverseCenter * (left.transform.position - centerPosition);
            worldRightEyeOffset = inverseCenter * (right.transform.position - centerPosition);
            haveWorldEyeData = true;

            left.enabled = true;
            right.enabled = true;
            left.stereoTargetEye = StereoTargetEyeMask.None;
            right.stereoTargetEye = StereoTargetEyeMask.None;
            left.targetTexture = leftTexture;
            right.targetTexture = rightTexture;
            left.useOcclusionCulling = false;
            right.useOcclusionCulling = false;
            left.cullingMask = source.cullingMask & ~(1 << MenuLayer);
            right.cullingMask = source.cullingMask & ~(1 << MenuLayer);
            left.clearFlags = source.clearFlags;
            right.clearFlags = source.clearFlags;
            left.backgroundColor = source.backgroundColor;
            right.backgroundColor = source.backgroundColor;
            left.depth = source.depth;
            right.depth = source.depth;
            EnableRenderEffects(left);
            EnableRenderEffects(right);
            if (menuScreen != null)
                menuScreen.SetActive(false);
            ResetFlatMenuPointerInteraction();
        }

        private static void EnableRenderEffects(Camera camera)
        {
            var cameraId = camera.GetInstanceID();
            Camera cachedCamera;
            if (camerasWithRenderEffectsEnabled.TryGetValue(cameraId, out cachedCamera) &&
                ReferenceEquals(cachedCamera, camera))
                return;
            foreach (var behaviour in camera.GetComponents<Behaviour>())
            {
                if (behaviour != null && behaviour != camera &&
                    !(behaviour is VrStereoOutlineEffect))
                    behaviour.enabled = true;
            }
            camerasWithRenderEffectsEnabled[cameraId] = camera;
        }

        private static void ConfigureHud(Camera source, Camera left, Camera right,
            RenderTexture leftTexture, RenderTexture rightTexture, bool gameplay)
        {
            if (!gameplay)
            {
                CompositeMenuHud(source);
                left.enabled = false;
                right.enabled = false;
                return;
            }

            CopyOverlayCamera(source, left, leftTexture);
            CopyOverlayCamera(source, right, rightTexture);
            var position = source.transform.position;
            var rotation = source.transform.rotation;
            left.transform.SetPositionAndRotation(position, rotation);
            right.transform.SetPositionAndRotation(position, rotation);

            // Preserve MFNVR's stable monoscopic overlay, then add only the small stereo
            // convergence offset needed to place it at the configured virtual distance.
            var projection = haveWorldEyeData
                ? worldCenterProjection
                : Matrix4x4.Perspective(source.fieldOfView,
                    leftTexture.width / (float)Mathf.Max(1, leftTexture.height),
                    source.nearClipPlane, source.farClipPlane);
            projection[0, 0] *= hudScale;
            projection[1, 1] *= hudScale;
            var leftProjection = projection;
            var rightProjection = projection;
            var halfIpd = haveWorldEyeData
                ? Vector3.Distance(worldLeftEyePosition, worldRightEyePosition) * 0.5f
                : 0.032f;
            var horizontalShift = projection[0, 0] * halfIpd / Mathf.Max(0.5f, hudDistance);
            var verticalShift = projection[1, 1] * hudHeightOffset / Mathf.Max(0.5f, hudDistance);
            leftProjection[0, 2] += horizontalShift;
            rightProjection[0, 2] -= horizontalShift;
            leftProjection[1, 2] -= verticalShift;
            rightProjection[1, 2] -= verticalShift;
            left.projectionMatrix = leftProjection;
            right.projectionMatrix = rightProjection;
        }

        private static Matrix4x4 AverageProjection(Matrix4x4 left, Matrix4x4 right)
        {
            var result = new Matrix4x4();
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                    result[row, column] = (left[row, column] + right[row, column]) * 0.5f;
            }
            return result;
        }

        private static void CompositeMenuHud(Camera hudCamera)
        {
            if (hudCamera == null || menuCapture == null)
                return;

            var oldTarget = hudCamera.targetTexture;
            var oldClearFlags = hudCamera.clearFlags;
            var oldOcclusion = hudCamera.useOcclusionCulling;
            try
            {
                hudCamera.targetTexture = menuCapture;
                hudCamera.clearFlags = CameraClearFlags.Nothing;
                hudCamera.useOcclusionCulling = false;
                hudCamera.Render();
            }
            finally
            {
                hudCamera.targetTexture = oldTarget;
                hudCamera.clearFlags = oldClearFlags;
                hudCamera.useOcclusionCulling = oldOcclusion;
            }
        }

        private static void CopyOverlayCamera(Camera source, Camera destination,
            RenderTexture target)
        {
            destination.CopyFrom(source);
            destination.enabled = true;
            destination.stereoTargetEye = StereoTargetEyeMask.None;
            destination.targetTexture = target;
            destination.useOcclusionCulling = false;
        }

        private static void ConfigureMenuScreen(Camera source, Camera left, Camera right,
            RenderTexture leftTexture, RenderTexture rightTexture)
        {
            EnsureMenuObjects(source, left, right);
            ConfigureMenuEye(left, leftTexture);
            ConfigureMenuEye(right, rightTexture);
            if (menuScreen != null)
                menuScreen.SetActive(true);
            UpdateFlatMenuPointer(source, left, right);
            CaptureMenu(source);
        }

        private static void UpdateFlatMenuPointer(Camera source, Camera left,
            Camera right)
        {
            if (flatMenuPointerFrame == Time.frameCount)
                return;
            flatMenuPointerFrame = Time.frameCount;
            var player = Player.current;
            var menuControllerActive = Menus.MenuController.me != null &&
                Menus.MenuController.me.GetCurrentMenu() != null;
            var activeSceneName = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().name ?? string.Empty;
            var explicitMainMenu = player != null &&
                (player.GetIsMainMenu() ||
                 ReadBooleanField(isOnMainMenuField, player) ||
                 ReadBooleanField(isBackToMenuSceneField, player) ||
                 ReadBooleanField(isIntroSceneField, player));
            var mainMenuScene = activeSceneName.IndexOf("menu",
                StringComparison.OrdinalIgnoreCase) >= 0;
            // Some title-scene builds do not use "menu" in the scene name and set
            // Player's main-menu flag late. The title activation component is an
            // unambiguous fallback and prevents the title from being mistaken for a
            // pause menu (both enable pause-menu controls internally).
            var mainMenuActivatorPresent = UnityEngine.Object
                .FindObjectOfType<ActivateMenuControllerOnMain>() != null;
            var mainMenu = explicitMainMenu || mainMenuActivatorPresent ||
                (menuControllerActive && mainMenuScene);
            // MFN calls EnablePauseMenuControls on its title screen too. Main-menu
            // detection must take priority or the pointer incorrectly raycasts from
            // the HUD camera, where none of the title selections exist.
            var pauseMenu = player != null && !mainMenu &&
                ReadBooleanField(pauseMenuEnabledField, player);
            var pointerMode = mainMenu ? 1 : (pauseMenu ? 2 : 0);
            if (pointerMode != flatMenuPointerMode)
            {
                flatMenuPointerMode = pointerMode;
                Debug.Log("MFNVR: flat pointer mode=" +
                    (pointerMode == 1 ? "main" : pointerMode == 2 ? "pause" : "none") +
                    ", scene='" + activeSceneName + "', explicitMain=" +
                    explicitMainMenu + ", menuStack=" + menuControllerActive + ".");
            }
            if (!menuPointerEnabled || player == null || menuScreen == null ||
                menuCapture == null || (!mainMenu && !pauseMenu))
            {
                ResetFlatMenuPointerInteraction();
                return;
            }

            // The title has no gameplay motion rig. Use the OpenXR eye and controller
            // poses directly and map them into the menu eyes' displayed coordinate
            // system, rather than reusing a stale gameplay transform.
            if (!TryMapRightControllerToFlatMenu(left, right))
            {
                ResetFlatMenuPointerInteraction();
                return;
            }

            float stickX, stickY, trigger, squeeze;
            int primary, secondary, stickClick, menu;
            var haveInput = MFN_GetControllerInput(1, out stickX, out stickY,
                out trigger, out squeeze, out primary, out secondary,
                out stickClick, out menu) != 0;
            var triggerPressed = haveInput && trigger >= 0.72f;
            var primaryPressed = haveInput && primary != 0;
            var triggerStarted = (triggerPressed && !previousFlatMenuTriggerPressed) ||
                (primaryPressed && !previousFlatMenuPrimaryPressed);
            previousFlatMenuTriggerPressed = triggerPressed;
            previousFlatMenuPrimaryPressed = primaryPressed;
            menuPointerInputActive = true;
            flatMenuPointerActive = true;

            var ray = new Ray(rightAimWorldPosition,
                rightAimWorldRotation * Vector3.forward);
            var plane = new Plane(menuScreen.transform.forward,
                menuScreen.transform.position);
            float enter;
            var onPanel = plane.Raycast(ray, out enter) && enter > 0f && enter <= 30f;
            var point = onPanel ? ray.GetPoint(enter) : ray.GetPoint(3f);
            var local = onPanel
                ? menuScreen.transform.InverseTransformPoint(point)
                : Vector3.zero;
            onPanel = onPanel && Mathf.Abs(local.x) <= 0.5f &&
                      Mathf.Abs(local.y) <= 0.5f;

            Interactable target = null;
            if (onPanel)
            {
                var u = Mathf.Clamp01(local.x + 0.5f);
                var v = Mathf.Clamp01(local.y + 0.5f);
                var mousePosition = new Vector2(u * Mathf.Max(1, Screen.width),
                    v * Mathf.Max(1, Screen.height));
                if (Mouse.current != null)
                {
                    Mouse.current.WarpCursorPosition(mousePosition);
                    // WarpCursorPosition moves the OS cursor, while changing the
                    // device state immediately lets MFN process hover in this frame.
                    InputState.Change(Mouse.current.position, mousePosition);
                }
                Player.wasLastUsingGamepad = false;
                target = FindFlatMenuPointerTarget(player, source, pauseMenu, u, v);
            }

            if (!ReferenceEquals(flatMenuPointerHoveredInteractable, target))
            {
                if (flatMenuPointerHoveredInteractable != null)
                {
                    try { flatMenuPointerHoveredInteractable.DidExit(player); }
                    catch { }
                }
                flatMenuPointerHoveredInteractable = target;
                if (target != null)
                {
                    try { target.DidEnter(player); }
                    catch { }
                }
            }

            if (target != null)
            {
                try
                {
                    var node = target.GetGamepadNode();
                    if (node != null && player.GetMyGamepadControl().GetCurrentNode() != node)
                        player.GetMyGamepadControl().SetToNode(node);
                }
                catch { }
                if (triggerStarted)
                {
                    try
                    {
                        target.Interact(player);
                        MFN_ApplyControllerHaptic(1, 0.28f, 0.045f, 0f);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("MFNVR flat-menu pointer click failed: " + exception);
                    }
                }
            }

            EnsureInventoryPointerVisual();
            // Flat menus only need a clean ray and endpoint. Keeping the full hand mesh
            // out of this camera avoids the oversized/incorrectly clipped pause-menu
            // hand that the earlier implementation produced.
            if (menuRightHandVisualRoot != null &&
                menuRightHandVisualRoot.gameObject.activeSelf)
                menuRightHandVisualRoot.gameObject.SetActive(false);
            SetMenuPointerVisualLayer(MenuLayer);
            SetInventoryPointerVisible(true);
            // Keep the endpoint slightly in front of the textured quad so it cannot
            // disappear into the menu surface through depth fighting.
            var visualPoint = onPanel
                ? point - menuScreen.transform.forward * 0.012f
                : point;
            inventoryPointerDot.transform.position = visualPoint;
            inventoryPointerLine.SetPosition(0, rightAimWorldPosition);
            inventoryPointerLine.SetPosition(1, visualPoint);
            SetInventoryPointerColor(target != null
                ? new Color(0.20f, 1f, 0.35f, 1f)
                : new Color(1f, 0.78f, 0.05f, 1f));
        }

        private static Interactable FindFlatMenuPointerTarget(Player player,
            Camera source, bool pauseMenu, float u, float v)
        {
            // MFN's OnscreenCursor and its own free-cursor menu raycast both use the
            // HUD camera on the title screen as well as the pause screen. Using the
            // world source camera here made title buttons impossible to hover even
            // when the desktop mouse reached the correct pixel.
            var targetCamera = player.GetHUDCamera();
            if (targetCamera == null)
                targetCamera = source;
            if (targetCamera == null)
                return null;
            var rect = targetCamera.pixelRect;
            var screenPoint = new Vector3(rect.x + u * rect.width,
                rect.y + v * rect.height, 0f);
            var ray = targetCamera.ScreenPointToRay(screenPoint);
            var mask = 0;
            AddNamedLayer(ref mask, "UI");
            var hits = Physics.RaycastAll(ray, 30f, mask,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null)
                    continue;
                var target = hit.collider.GetComponent<Interactable>();
                if (target == null)
                    target = hit.collider.GetComponentInParent<Interactable>();
                if (target != null)
                    return target;
            }
            return null;
        }

        private static void ResetFlatMenuPointerInteraction()
        {
            if (!flatMenuPointerActive && flatMenuPointerHoveredInteractable == null)
                return;
            var player = Player.current;
            if (flatMenuPointerHoveredInteractable != null && player != null)
            {
                try { flatMenuPointerHoveredInteractable.DidExit(player); }
                catch { }
            }
            flatMenuPointerHoveredInteractable = null;
            flatMenuPointerActive = false;
            previousFlatMenuTriggerPressed = false;
            previousFlatMenuPrimaryPressed = false;
            flatMenuPointerFrame = -1;
            flatMenuPointerMode = -1;
            menuPointerInputActive = false;
            SetMenuPointerVisualLayer(LayerMask.NameToLayer("Default"));
            SetInventoryPointerVisible(false);
            if (menuRightHandVisualRoot != null &&
                menuRightHandVisualRoot.gameObject.activeSelf)
                menuRightHandVisualRoot.gameObject.SetActive(false);
        }

        private static void ConfigureMenuEye(Camera camera, RenderTexture target)
        {
            camera.enabled = true;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << MenuLayer;
            camera.useOcclusionCulling = false;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 50f;

            foreach (var behaviour in camera.GetComponents<Behaviour>())
            {
                if (behaviour != null && behaviour != camera)
                    behaviour.enabled = false;
            }
            camerasWithRenderEffectsEnabled.Remove(camera.GetInstanceID());
        }

        private static void EnsureMenuObjects(Camera source, Camera left, Camera right)
        {
            if (menuSource == source && menuScreen != null && menuCapture != null)
            {
                if (menuSettingsRevision != settingsRevision)
                    UpdateMenuTransform(left, right, menuCapture.width, menuCapture.height);
                return;
            }

            DestroyMenuObjects();
            menuSource = source;
            source.cullingMask &= ~(1 << MenuLayer);

            var width = Mathf.Max(640, source.pixelWidth);
            var height = Mathf.Max(360, source.pixelHeight);
            menuCapture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            menuCapture.name = "MFN VR Menu Monitor Capture";
            menuCapture.Create();

            menuScreen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            menuScreen.name = "MFN VR Fixed Menu Screen (10m)";
            menuScreen.layer = MenuLayer;
            var collider = menuScreen.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.Destroy(collider);

            var shader = Shader.Find("Unlit/Texture");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            menuMaterial = new Material(shader);
            menuMaterial.mainTexture = menuCapture;
            menuScreen.GetComponent<Renderer>().material = menuMaterial;

            UpdateMenuTransform(left, right, width, height);
            menuCaptureFrame = -1;
        }

        private static void UpdateMenuTransform(Camera left, Camera right, int width, int height)
        {
            if (menuScreen == null || left == null || right == null)
                return;
            var center = (left.transform.position + right.transform.position) * 0.5f;
            var rotation = left.transform.rotation;
            menuScreen.transform.SetPositionAndRotation(
                center + rotation * Vector3.forward * menuDistance, rotation);
            var panelWidth = 2f * menuDistance *
                             Mathf.Tan(MenuHorizontalFov * 0.5f * Mathf.Deg2Rad) * menuScale;
            var aspect = width / (float)Mathf.Max(1, height);
            menuScreen.transform.localScale = new Vector3(panelWidth, panelWidth / aspect, 1f);
            menuSettingsRevision = settingsRevision;
        }

        private static void CaptureMenu(Camera source)
        {
            if (menuCapture == null || menuCaptureFrame == Time.frameCount)
                return;
            menuCaptureFrame = Time.frameCount;

            var oldTarget = source.targetTexture;
            var oldOcclusion = source.useOcclusionCulling;
            var screenWasActive = menuScreen != null && menuScreen.activeSelf;
            try
            {
                if (menuScreen != null)
                    menuScreen.SetActive(false);
                source.targetTexture = menuCapture;
                source.useOcclusionCulling = false;
                source.Render();

                // MFN's inventory is not part of the main or HUD camera. It is a small
                // in-world scene rendered by its own camera, with a second camera for the
                // cursor/hover layer. When the normal camera is redirected to the VR menu
                // panel those two later cameras would otherwise continue rendering only to
                // the desktop backbuffer, leaving an empty panel in the headset. Render the
                // authored cameras into the same texture, in their authored depth order, so
                // their culling masks, item models, animations, post effects and cursor all
                // remain exactly as the flat game expects.
                CompositeInventoryCameras(source);
            }
            finally
            {
                source.targetTexture = oldTarget;
                source.useOcclusionCulling = oldOcclusion;
                if (menuScreen != null)
                    menuScreen.SetActive(screenWasActive);
            }
        }

        private static void CompositeInventoryCameras(Camera source)
        {
            var player = Player.current;
            if (player == null || menuCapture == null)
                return;

            Camera inventoryCamera = null;
            Camera cursorCamera = null;
            try
            {
                inventoryCamera = player.GetInventoryCamera();
                cursorCamera = inventoryCursorCameraField != null
                    ? inventoryCursorCameraField.GetValue(player) as Camera
                    : null;

                var cameras = new List<Camera>(2);
                if (inventoryCamera != null && inventoryCamera != source &&
                    inventoryCamera.enabled && inventoryCamera.gameObject.activeInHierarchy)
                    cameras.Add(inventoryCamera);
                if (cursorCamera != null && cursorCamera != source && cursorCamera != inventoryCamera &&
                    cursorCamera.enabled && cursorCamera.gameObject.activeInHierarchy)
                    cameras.Add(cursorCamera);

                cameras.Sort((a, b) => a.depth.CompareTo(b.depth));
                foreach (var camera in cameras)
                    RenderCameraIntoMenuCapture(camera);

                if (cameras.Count > 0)
                    inventoryCaptureWarningLogged = false;
            }
            catch (Exception exception)
            {
                if (!inventoryCaptureWarningLogged)
                {
                    Debug.LogWarning("MFNVR: inventory screen capture failed: " + exception);
                    inventoryCaptureWarningLogged = true;
                }
            }
        }

        private static void RenderCameraIntoMenuCapture(Camera camera)
        {
            var oldTarget = camera.targetTexture;
            var oldOcclusion = camera.useOcclusionCulling;
            var oldStereoTarget = camera.stereoTargetEye;
            try
            {
                camera.targetTexture = menuCapture;
                camera.useOcclusionCulling = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.Render();
            }
            finally
            {
                camera.targetTexture = oldTarget;
                camera.useOcclusionCulling = oldOcclusion;
                camera.stereoTargetEye = oldStereoTarget;
            }
        }

        private static void DestroyMenuObjects()
        {
            if (menuScreen != null)
                UnityEngine.Object.Destroy(menuScreen);
            if (menuMaterial != null)
                UnityEngine.Object.Destroy(menuMaterial);
            if (menuCapture != null)
            {
                menuCapture.Release();
                UnityEngine.Object.Destroy(menuCapture);
            }
            menuScreen = null;
            menuMaterial = null;
            menuCapture = null;
            menuSource = null;
            haveWorldEyeData = false;
        }
    }

    public sealed class VrStereoOutlineEffect : MonoBehaviour
    {
        private Camera eyeCamera;
        private Camera outlineCamera;
        private Camera sourceOutlineCamera;
        private Material material;
        private Material sourceMaterial;
        private RenderTexture outlineTexture;

        public void Configure(PostProcessExample source)
        {
            eyeCamera = eyeCamera != null ? eyeCamera : GetComponent<Camera>();
            var nextOutlineCamera = source != null ? source.GetOutlineCamera() : null;
            var nextMaterial = source != null ? source.PostProcessMat : null;
            if (sourceOutlineCamera != nextOutlineCamera)
            {
                sourceOutlineCamera = nextOutlineCamera;
                if (outlineCamera != null)
                    Destroy(outlineCamera.gameObject);
                outlineCamera = null;
            }
            if (sourceMaterial != nextMaterial)
            {
                sourceMaterial = nextMaterial;
                if (material != null)
                    Destroy(material);
                material = sourceMaterial != null ? new Material(sourceMaterial) : null;
            }
            if (outlineCamera == null && sourceOutlineCamera != null)
            {
                var cameraObject = new GameObject("MFN VR Stereo Outline Camera");
                cameraObject.transform.SetParent(transform, false);
                outlineCamera = cameraObject.AddComponent<Camera>();
                outlineCamera.enabled = false;
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (eyeCamera == null || sourceOutlineCamera == null ||
                outlineCamera == null || material == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            EnsureTexture(source);
            outlineCamera.CopyFrom(sourceOutlineCamera);
            outlineCamera.enabled = false;
            outlineCamera.stereoTargetEye = StereoTargetEyeMask.None;
            outlineCamera.targetTexture = outlineTexture;
            outlineCamera.useOcclusionCulling = false;
            outlineCamera.transform.SetPositionAndRotation(
                eyeCamera.transform.position, eyeCamera.transform.rotation);
            outlineCamera.projectionMatrix = eyeCamera.projectionMatrix;
            outlineCamera.Render();

            material.SetColor("_OutlineColor", Color.white);
            material.SetTexture("_MainTex", source);
            material.SetTexture("_AltTex", outlineTexture);
            Graphics.Blit(source, destination, material);
        }

        private void EnsureTexture(RenderTexture source)
        {
            if (outlineTexture != null)
            {
                // Dynamic resolution can change the eye texture by a few pixels from
                // frame to frame. Reallocating a GPU render target for every change
                // causes large stalls exactly when an interaction view opens. The
                // outline shader samples normalized UVs, so a persistent buffer remains
                // valid as long as the aspect ratio has not materially changed.
                var sourceAspect = source.width / (float)Mathf.Max(1, source.height);
                var textureAspect = outlineTexture.width /
                    (float)Mathf.Max(1, outlineTexture.height);
                if (Mathf.Abs(sourceAspect - textureAspect) < 0.02f)
                    return;
            }
            if (outlineTexture != null)
            {
                outlineTexture.Release();
                Destroy(outlineTexture);
            }
            var descriptor = source.descriptor;
            // Highlight geometry is a simple binary mask. Half resolution preserves the
            // visible outline while cutting each auxiliary eye render to one quarter of
            // the pixels and avoiding an interaction-only performance cliff.
            descriptor.width = Mathf.Max(320, descriptor.width / 2);
            descriptor.height = Mathf.Max(180, descriptor.height / 2);
            descriptor.depthBufferBits = 24;
            descriptor.msaaSamples = 1;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            outlineTexture = new RenderTexture(descriptor)
            {
                name = "MFN VR Stereo Outline Texture"
            };
            outlineTexture.Create();
        }

        private void OnDestroy()
        {
            if (outlineCamera != null)
                Destroy(outlineCamera.gameObject);
            if (material != null)
                Destroy(material);
            if (outlineTexture != null)
            {
                outlineTexture.Release();
                Destroy(outlineTexture);
            }
        }
    }

    public sealed class MirrorBlitEffect : MonoBehaviour
    {
        public RenderTexture Texture;

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (enabled && Texture != null)
                Graphics.Blit(Texture, destination);
            else
                Graphics.Blit(source, destination);
        }
    }

    /// <summary>
    /// Avoids rendering MFN's world, hands and HUD a third time for the desktop window.
    /// The component deliberately changes only the automatic backbuffer render. Explicit
    /// menu/inventory captures use a target texture and therefore retain their full scene.
    /// </summary>
    public sealed class SourceBackbufferOptimizer : MonoBehaviour
    {
        private Camera sourceCamera;
        private bool changed;
        private int savedCullingMask;
        private CameraClearFlags savedClearFlags;
        private Color savedBackgroundColor;

        private void Awake()
        {
            sourceCamera = GetComponent<Camera>();
        }

        private void OnPreCull()
        {
            if (sourceCamera == null)
                sourceCamera = GetComponent<Camera>();
            if (!RenderBridge.ShouldSkipSourceBackbuffer(sourceCamera))
                return;

            savedCullingMask = sourceCamera.cullingMask;
            savedClearFlags = sourceCamera.clearFlags;
            savedBackgroundColor = sourceCamera.backgroundColor;
            sourceCamera.cullingMask = 0;
            sourceCamera.clearFlags = CameraClearFlags.SolidColor;
            sourceCamera.backgroundColor = Color.black;
            changed = true;
        }

        private void OnPostRender()
        {
            RestoreCamera();
        }

        private void OnDisable()
        {
            RestoreCamera();
        }

        private void OnDestroy()
        {
            RestoreCamera();
        }

        private void RestoreCamera()
        {
            if (!changed || sourceCamera == null)
                return;
            sourceCamera.cullingMask = savedCullingMask;
            sourceCamera.clearFlags = savedClearFlags;
            sourceCamera.backgroundColor = savedBackgroundColor;
            changed = false;
        }
    }

    [HarmonyPatch(typeof(NewIntentoryDropdown), nameof(NewIntentoryDropdown.DoAction))]
    internal static class VrToolboxDropdownOwnerPatch
    {
        private static readonly FieldInfo HeldItemField = AccessTools.Field(
            typeof(NewIntentoryDropdown), "holdItemInInventory");
        private static readonly FieldInfo ItemOwnerField = AccessTools.Field(
            typeof(ItemInInventory), "myInventory");
        private static readonly FieldInfo HoveringField = AccessTools.Field(
            typeof(InventoryInWorld), "currentlyHovering");

        [HarmonyPrefix]
        private static void Prefix(NewIntentoryDropdown __instance)
        {
            if (__instance == null || ItemBoxParent.current == null || Player.current == null)
                return;
            var item = HeldItemField?.GetValue(__instance) as ItemInInventory;
            var owner = item != null
                ? ItemOwnerField?.GetValue(item) as InventoryInWorld
                : null;
            if (owner == null)
                return;

            // Every vanilla dropdown action operates through Player.GetInventory() and
            // that inventory's currentlyHovering field. Bind both to the item that opened
            // the menu so Stash/Take cannot target the other linked toolbox grid.
            Player.current.SetCurrentInventory(owner);
            HoveringField?.SetValue(owner, item);
        }
    }

    [HarmonyPatch(typeof(InventoryInWorld), "ResetItemsNoCaller")]
    internal static class VrToolboxDuplicateVisualRepairPatch
    {
        private static readonly FieldInfo InventoryItemsField = AccessTools.Field(
            typeof(InventoryInWorld), "myInventoryItems");

        [HarmonyPrefix]
        private static void Prefix(InventoryInWorld __instance)
        {
            if (__instance == null || ItemBoxParent.current == null)
                return;
            var items = InventoryItemsField?.GetValue(__instance) as List<ItemInInventory>;
            if (items == null || items.Count < 2)
                return;

            var firstById = new Dictionary<int, ItemInInventory>();
            var repaired = 0;
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                if (item == null || item.GetMyInventoryNode() == null)
                    continue;
                var id = item.GetMyInventoryNode().myID;
                if (!firstById.TryGetValue(id, out var first))
                {
                    firstById.Add(id, item);
                    continue;
                }

                items.RemoveAt(index--);
                repaired++;
                // A duplicated list reference must not destroy the one retained visual.
                if (item != first)
                    UnityEngine.Object.Destroy(item.gameObject);
            }

            if (repaired > 0)
                Debug.Log("MFNVR: removed " + repaired +
                    " duplicate toolbox item visual(s) before inventory refresh.");
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.FireProjectile))]
    internal static class DirectProjectilePatch
    {
        [HarmonyPrefix]
        private static void Prefix(ref Vector3 direction, Player fromPlayer,
            ref Vector3 changeToCollisionAtThisPoint)
        {
            RenderBridge.OverridePlayerProjectile(ref direction, fromPlayer,
                ref changeToCollisionAtThisPoint);
        }
    }

    [HarmonyPatch(typeof(ReticleManager), "Update")]
    internal static class GunReticlePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ReticleManager __instance)
        {
            RenderBridge.PositionReticle(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.CheckForInteraction))]
    internal static class VrInteractionAssistPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Player __instance, ref Interactable __result)
        {
            return !RenderBridge.TryAssistInteraction(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.DetachCamera))]
    internal static class VrInteractionMenuEntryCleanupPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance)
        {
            // Clear the gameplay hover before PointAndClickNode pushes its inspection
            // camera. Its DidExit implementation uses that stack to choose which HUD
            // counter to decrement, so doing this after DetachCamera leaves the original
            // INSPECT/OPEN prompt permanently counted as visible.
            RenderBridge.ClearGameplayInteractionHover(__instance);
            RenderBridge.BeginInteractionCameraLock();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ReAttachCamera))]
    internal static class VrInteractionMenuExitCleanupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            RenderBridge.ResetGameplayInteractionReadout(__instance);
            RenderBridge.EndInteractionCameraLock();
        }
    }

    [HarmonyPatch(typeof(Player), "ExamineAction")]
    internal static class VrSuppressLegacyExamineActionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(InputAction.CallbackContext context)
        {
            return !RenderBridge.ShouldSuppressLegacyPointerAction(context);
        }
    }

    [HarmonyPatch(typeof(Player), "CheckInventoryInteract")]
    internal static class VrSuppressLegacyInventoryActionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(InputAction.CallbackContext context)
        {
            return !RenderBridge.ShouldSuppressLegacyPointerAction(context);
        }
    }

    [HarmonyPatch(typeof(Player), "CheckMenuInteract")]
    internal static class VrSuppressLegacyFilesActionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(InputAction.CallbackContext context)
        {
            return !RenderBridge.ShouldSuppressLegacyPointerAction(context);
        }
    }

    [HarmonyPatch(typeof(Player), "PauseActionGamepad")]
    internal static class VrSuppressLegacyPauseActionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(InputAction.CallbackContext context)
        {
            return !RenderBridge.ShouldSuppressLegacyPointerAction(context);
        }
    }

    [HarmonyPatch(typeof(Player), "CheckInteractGamepad")]
    internal static class VrSuppressLegacyWorldGamepadActionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(InputAction.CallbackContext context)
        {
            return !RenderBridge.ShouldSuppressLegacyPointerAction(context);
        }
    }
}
