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
        // Keep the complete implementation compiled in, but gate it off until its
        // weapon transforms and trigger routing are ready for a future release.
        private const bool LeftHandedModeAvailable = false;
        private static bool leftHandedMode;
        private static int DominantHandIndex => leftHandedMode ? 0 : 1;
        private static int SupportHandIndex => leftHandedMode ? 1 : 0;
        private static bool settingsMenuOpen;
        private static bool settingsMenuPausedTime;
        private static float settingsMenuPreviousTimeScale = 1f;
        private static int settingsMenuGestureFrame = -1;
        private static float settingsMenuGestureHoldStarted = -1f;
        private static bool settingsMenuGestureTriggered;
        private static bool settingsMenuToggleRequested;
        private static bool settingsMenuGestureInputLogged;
        private static int settingsRevision;
        private static int menuSettingsRevision = -1;
        private const float MenuSettingsButtonMinU = 0.735f;
        private const float MenuSettingsButtonMaxU = 0.965f;
        private const float MenuSettingsButtonMinV = 0.035f;
        private const float MenuSettingsButtonMaxV = 0.125f;
        private static Texture2D menuSettingsButtonTexture;
        private static Texture2D menuSettingsButtonHoverTexture;
        private static bool menuSettingsButtonHovered;
        private static bool menuSettingsButtonVisible;
        private static bool menuSettingsButtonWarningLogged;
        private static Texture2D settingsMenuTexture;
        private static bool settingsMenuTextureDirty = true;
        private static int settingsMenuCategory;
        private static int settingsMenuHoveredTab = -1;
        private static int settingsMenuHoveredOption = -1;
        private static int settingsMenuDraggingOption = -1;
        private static bool settingsMenuCloseHovered;
        private static readonly float[] settingsMenuValues = new float[19];
        private static readonly System.Drawing.StringFormat SettingsCenteredFormat =
            new System.Drawing.StringFormat
            {
                Alignment = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center
            };
        private static MethodInfo getSettingsMenuValuesMethod;
        private static MethodInfo setSettingsMenuValueMethod;
        private static Func<float[], bool> settingsMenuValueReader;
        private static Func<int, float, bool> settingsMenuValueWriter;
        private static Action<bool> settingsMenuVisibilityChanged;
        private static bool settingsMenuProviderWarningLogged;
        private const float SettingsPanelMinU = 0.10f;
        private const float SettingsPanelMaxU = 0.90f;
        private const float SettingsPanelMinV = 0.08f;
        private const float SettingsPanelMaxV = 0.92f;

        private sealed class SettingsMenuOption
        {
            public string Label;
            public string Description;
            public bool DescriptionIsWarning;
            public int Category;
            public bool Toggle;
            public float Minimum;
            public float Maximum;
            public bool Logarithmic;
        }

        private static readonly string[] SettingsMenuCategories =
        {
            "Rendering", "Crosshair", "UI", "Camera & Turning", "Controls"
        };

        private static readonly SettingsMenuOption[] SettingsMenuOptions =
        {
            new SettingsMenuOption { Label = "Resolution Scale", Description = "Restart required", DescriptionIsWarning = true, Category = 0, Minimum = 0.5f, Maximum = 1.5f },
            new SettingsMenuOption { Label = "Dynamic Resolution", Description = "Automatically adjusts render resolution - restart required", DescriptionIsWarning = true, Category = 0, Toggle = true },
            new SettingsMenuOption { Label = "Dynamic Minimum Scale", Description = "Lowest scale Dynamic Resolution is allowed to use", Category = 0, Minimum = 0.5f, Maximum = 1.5f },
            new SettingsMenuOption { Label = "Dynamic Target FPS", Description = "Performance target used by Dynamic Resolution", Category = 0, Minimum = 45f, Maximum = 144f },
            new SettingsMenuOption { Label = "Crosshair Enabled", Category = 1, Toggle = true },
            new SettingsMenuOption { Label = "Crosshair Distance", Category = 1, Minimum = 0.25f, Maximum = 10f },
            new SettingsMenuOption { Label = "Crosshair Size", Category = 1, Minimum = 0.002f, Maximum = 0.05f },
            new SettingsMenuOption { Label = "HUD Distance", Category = 2, Minimum = 0.5f, Maximum = 1000f, Logarithmic = true },
            new SettingsMenuOption { Label = "HUD Scale", Category = 2, Minimum = 0.25f, Maximum = 2f },
            new SettingsMenuOption { Label = "HUD Height Offset", Category = 2, Minimum = -2f, Maximum = 2f },
            new SettingsMenuOption { Label = "Menu Distance", Category = 2, Minimum = 1f, Maximum = 20f },
            new SettingsMenuOption { Label = "Menu Scale", Category = 2, Minimum = 0.25f, Maximum = 2f },
            new SettingsMenuOption { Label = "UI Screens", Description = "ON: flat screens are used only for Main, Pause, and Files", Category = 2, Toggle = true },
            new SettingsMenuOption { Label = "Menu Pointer", Description = "Use the tracked dominant-hand pointer in menus and interactions", Category = 2, Toggle = true },
            new SettingsMenuOption { Label = "Interaction Camera Movement", Description = "Allow MFN to reposition your view in interaction menus", Category = 3, Toggle = true },
            new SettingsMenuOption { Label = "Smooth Turning", Description = "ON: smooth turning   OFF: snap turning", Category = 3, Toggle = true },
            new SettingsMenuOption { Label = "Snap Turn Angle", Category = 3, Minimum = 15f, Maximum = 90f },
            new SettingsMenuOption { Label = "Smooth Turn Speed", Category = 3, Minimum = 30f, Maximum = 360f },
            new SettingsMenuOption { Label = "Physical Weapon Switching", Description = "Switch weapons with dominant-grip hip and shoulder holsters", Category = 4, Toggle = true }
        };

        private static readonly string[] SettingsMenuConfigSections =
        {
            "Rendering", "Rendering", "Rendering", "Rendering",
            "Crosshair", "Crosshair", "Crosshair",
            "HUD", "HUD", "HUD", "MainMenu", "MainMenu", "UI", "UI",
            "Camera", "Turning", "Turning", "Turning", "Controls"
        };

        private static readonly string[] SettingsMenuConfigKeys =
        {
            "ResolutionScale", "DynamicResolution", "DynamicResolutionMinScale",
            "DynamicResolutionTargetFPS", "Enabled", "Distance", "Size",
            "Distance", "Scale", "HeightOffset", "Distance", "Scale",
            "UIScreens", "MenuPointer", "InteractionCameraMovement",
            "SmoothTurning", "SnapAngle", "SmoothTurnSpeed",
            "PhysicalWeaponSwitching"
        };

        private static readonly float[] SettingsMenuDefaults =
        {
            1f, 0f, 0.7f, 80f, 1f, 1.08f, 0.011f, 1000f, 0.78f, 0f,
            10f, 1f, 1f, 1f, 0f, 0f, 30f, 90f, 1f
        };

        private static Camera menuSource;
        private static Camera lastMenuLeftEye;
        private static Camera lastMenuRightEye;
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
        private static readonly Dictionary<int, Camera> sourceBackbufferOptimizers =
            new Dictionary<int, Camera>();
        private static readonly Dictionary<int, Camera> menuEyesWithEffectsDisabled =
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
        private static readonly FieldInfo noteReadoutVisibleField = AccessTools.Field(
            typeof(NoteReadoutInWorld), "isVisible");
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
        private static readonly FieldInfo menuSelectionBarNubField = AccessTools.Field(
            typeof(Menus.MenuSelectionBar), "nub");
        private static readonly FieldInfo menuBarNubPickedUpField = AccessTools.Field(
            typeof(Menus.MenuBarNub), "pickedUp");
        private static readonly FieldInfo menuBarNubLeftExtentField = AccessTools.Field(
            typeof(Menus.MenuBarNub), "leftExtent");
        private static readonly FieldInfo menuBarNubRightExtentField = AccessTools.Field(
            typeof(Menus.MenuBarNub), "rightExtent");
        private static readonly FieldInfo equippedItemsField = AccessTools.Field(
            typeof(EquippedManager), "items");
        private static EquippedManager motionManager;
        private static ItemInHand motionItem;
        private static bool motionItemIsEmptyHandRig;
        private static Renderer[] motionItemRenderers = new Renderer[0];
        private static MeshRenderer[] motionItemMeshRenderers = new MeshRenderer[0];
        private static Transform motionAnchor;
        private static Transform rightWrist;
        private static Transform leftHandRoot;
        private static Transform leftWrist;
        private static Transform mirroredDominantWrist;
        private static Vector3 originalDominantWristScale;
        private static bool dominantWristMirrored;
        private static readonly List<KeyValuePair<Renderer, Material[]>>
            mirroredDominantMaterialRestores =
                new List<KeyValuePair<Renderer, Material[]>>();
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
        private static int bakedHandCharacterKey = int.MinValue;
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
        // This is the user's final calibrated Rift S neutral pose. It is intentionally
        // baked into the mod so hand alignment is stable on every launch and cannot be
        // accidentally changed by a gameplay button hold.
        private static readonly Quaternion SavedLeftGripToHandRotation =
            new Quaternion(0.7296127f, -0.343231469f, 0.2430663f, 0.539236665f);
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
        private static FieldInfo coreLastTouchInputFrameField;
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
        private static int lastGunRayFrame = -1;
        private static Vector3 lastGunRayOrigin;
        private static Vector3 lastGunRayDirection;
        private static int gunRayMask;
        private static bool gunRayMaskReady;
        private static int gameplayInteractionMask;
        private static bool gameplayInteractionMaskReady;
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
        private static readonly RaycastHit[] wrenchSweepHits = new RaycastHit[64];
        private static readonly Collider[] wrenchOverlapColliders = new Collider[64];
        private static readonly RaycastHit[] interactionAssistHits = new RaycastHit[64];
        private static InventoryInWorld physicalInventory;
        private static InventoryRowOfSquares[] physicalInventoryRows;
        private static ItemInInventory physicalInventoryHeldItem;
        private static bool physicalInventoryPositioned;
        private static bool previousInventoryTriggerPressed;
        private static bool previousInventoryPrimaryPressed;
        private static bool previousMenuPointerLeftTriggerPressed;
        private static bool previousInventoryRotatePressed;
        private static bool toolboxSectionChoiceActive;
        private static int inventoryPointerFrame = -1;
        private static GameObject inventoryPointerDot;
        private static Material inventoryPointerMaterial;
        private static LineRenderer inventoryPointerLine;
        private static Color inventoryPointerColor;
        private static bool inventoryPointerColorReady;
        private static int menuPointerVisualLayer = int.MinValue;
        private static Transform menuRightHandVisualRoot;
        private static Transform[] menuRightHandVisualTransforms = new Transform[0];
        private static GameObject menuRightHandVisualObject;
        private static Mesh menuRightHandVisualMesh;
        private static Quaternion rightGripToMenuHandRotation = Quaternion.identity;
        private static bool menuPointerInputActive;
        // Native/world-space pointer work is transition driven. In ordinary gameplay this
        // remains false, so no hover cleanup, inventory traversal, raycast or visual update
        // is performed just because motion-controller tracking is active.
        private static bool menuPointerRuntimeActive;
        private static Interactable menuPointerHoveredInteractable;
        private static Interactable flatMenuPointerHoveredInteractable;
        private static Menus.MenuBarNub flatMenuGrabbedSlider;
        private static bool flatMenuSliderCaptureActive;
        private static bool flatMenuSliderUsedVanillaPickup;
        private static float nextFlatMenuSliderValueUpdateTime;
        private static bool flatMenuPointerActive;
        private static bool previousFlatMenuTriggerPressed;
        private static bool previousFlatMenuPrimaryPressed;
        private static bool previousFlatMenuLeftTriggerPressed;
        private static int flatMenuPointerFrame = -1;
        private static int flatMenuPointerMode = -1;
        private static int cachedMainMenuSceneHandle = int.MinValue;
        private static int nextMainMenuActivatorProbeFrame;
        private static bool cachedMainMenuActivatorPresent;
        private static readonly RaycastHit[] flatMenuPointerHits = new RaycastHit[32];
        private static readonly RaycastHit[] dropdownPointerHits = new RaycastHit[32];
        private static readonly RaycastHit[] inventoryItemPointerHits = new RaycastHit[64];
        private static readonly RaycastHit[] interactionPointerHits = new RaycastHit[64];
        private static readonly List<InventoryInWorld> pointerInventories =
            new List<InventoryInWorld>(3);
        private static readonly List<InventoryInWorld> singlePointerInventory =
            new List<InventoryInWorld>(1);
        private static readonly List<KeyValuePair<int, Transform>> dropdownActiveNodes =
            new List<KeyValuePair<int, Transform>>(8);
        private static readonly List<MonoBehaviour> pointerParentBehaviours =
            new List<MonoBehaviour>(8);
        private static readonly Dictionary<Transform, Renderer[]> dropdownRendererCache =
            new Dictionary<Transform, Renderer[]>();
        private static readonly Dictionary<Transform, Transform[]> dropdownTransformCache =
            new Dictionary<Transform, Transform[]>();
        private static readonly Dictionary<int, InventoryRowOfSquares[]>
            suppressedInventoryHighlightRows =
                new Dictionary<int, InventoryRowOfSquares[]>();
        private static int suppressedInventoryHighlightSceneHandle = int.MinValue;
        private static int defaultLayer = int.MinValue;
        private static int inventoryLayer = int.MinValue;
        private static int inventoryCursorLayer = int.MinValue;
        private static int worldAttachedUiHoverMask;
        private static bool worldAttachedUiHoverMaskReady;
        private static NoteReadoutInWorld cachedNoteReadout;
        private static Renderer[] cachedNoteReadoutRenderers;
        private static int cachedNoteReadoutLayerMask;
        private static bool noteReadoutLayerLogged;
        private static ItemBoxParent cachedToolboxVisualItemBox;
        private static InventoryInWorld cachedToolboxVisualUpper;
        private static InventoryInWorld cachedToolboxVisualDrawer;
        private static int cachedToolboxVisualLayerMask;
        private static bool cachedToolboxVisualsReady;
        private static int pointerInteractionMask;
        private static bool pointerInteractionMaskReady;
        private static int pointerUiLayerMask;
        private static bool pointerUiLayerMaskReady;
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

        public static void ApplyLeftHandedSettings(bool enabled)
        {
            enabled = LeftHandedModeAvailable && enabled;
            if (leftHandedMode == enabled)
                return;
            var itemToRebind = motionItem;
            var wasEmptyHandRig = motionItemIsEmptyHandRig;
            if (itemToRebind != null)
                RestorePreviousItem();
            DestroyCachedCharacterHandVisuals();
            leftHandedMode = enabled;
            previousInventoryTriggerPressed = false;
            previousInventoryPrimaryPressed = false;
            previousMenuPointerLeftTriggerPressed = false;
            previousInventoryRotatePressed = false;
            previousFlatMenuTriggerPressed = false;
            previousFlatMenuPrimaryPressed = false;
            previousFlatMenuLeftTriggerPressed = false;
            previousPhysicalWeaponGripPressed = false;
            previousLeftGripPressed = false;
            ReleaseSupportGrip(false);
            if (itemToRebind != null && !wasEmptyHandRig)
                BindHeldItem(itemToRebind);
            settingsMenuTextureDirty = true;
            settingsRevision++;
            Debug.Log("MFNVR: dominant hand changed to " +
                (leftHandedMode ? "left" : "right") +
                "; weapon, pointer, trigger, and grip routing updated.");
        }

        public static void SetSettingsMenuOpen(bool open)
        {
            if (settingsMenuOpen == open)
                return;
            settingsMenuOpen = open;
            settingsMenuTextureDirty = true;
            settingsMenuHoveredTab = -1;
            settingsMenuHoveredOption = -1;
            settingsMenuDraggingOption = -1;
            if (open)
            {
                settingsMenuPreviousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                settingsMenuPausedTime = true;
                ReadSettingsMenuValues();
                ResetMenuPointerInteraction();
                // Do not reset the flat pointer's pressed-edge state here. This method
                // can be called from inside its own click handler; clearing the state
                // would make the same held A/trigger register again later in the frame.
                if (muzzleSight != null)
                    muzzleSight.SetActive(false);
            }
            else if (settingsMenuPausedTime)
            {
                Time.timeScale = settingsMenuPreviousTimeScale;
                settingsMenuPausedTime = false;
            }
            settingsMenuVisibilityChanged?.Invoke(open);
            Debug.Log("MFNVR: captured VR Settings screen " +
                      (open ? "opened." : "closed."));
        }

        public static bool IsSettingsMenuOpen()
        {
            return settingsMenuOpen;
        }

        public static void RegisterSettingsMenuProvider(
            Func<float[], bool> reader, Func<int, float, bool> writer,
            Action<bool> visibilityChanged)
        {
            settingsMenuValueReader = reader;
            settingsMenuValueWriter = writer;
            settingsMenuVisibilityChanged = visibilityChanged;
            getSettingsMenuValuesMethod = null;
            setSettingsMenuValueMethod = null;
            settingsMenuProviderWarningLogged = false;
            if (settingsMenuOpen)
            {
                settingsMenuVisibilityChanged?.Invoke(true);
                ReadSettingsMenuValues();
            }
            Debug.Log("MFNVR: live settings provider connected to the captured menu.");
        }

        public static void ToggleSettingsMenu()
        {
            SetSettingsMenuOpen(!settingsMenuOpen);
        }

        public static bool ConsumeSettingsMenuToggleRequest()
        {
            UpdateSettingsMenuToggleGesture();
            if (!settingsMenuToggleRequested)
                return false;
            settingsMenuToggleRequested = false;
            return true;
        }

        private static void UpdateSettingsMenuToggleGesture()
        {
            if (settingsMenuGestureFrame == Time.frameCount)
                return;
            settingsMenuGestureFrame = Time.frameCount;

            float lx, ly, lt, lg, rx, ry, rt, rg;
            int lp, ls, lc, lm, rp, rs, rc, rm;
            var haveLeft = MFN_GetControllerInput(0, out lx, out ly, out lt, out lg,
                out lp, out ls, out lc, out lm) != 0;
            MFN_GetControllerInput(1, out rx, out ry, out rt, out rg,
                out rp, out rs, out rc, out rm);
            var leftHeld = haveLeft && lc != 0;
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
            if (!settingsMenuGestureInputLogged && leftHeld)
            {
                settingsMenuGestureInputLogged = true;
                Debug.Log("MFNVR: left-stick settings gesture input detected.");
            }
            if (!leftHeld)
            {
                settingsMenuGestureHoldStarted = -1f;
                settingsMenuGestureTriggered = false;
                return;
            }

            if (settingsMenuGestureHoldStarted < 0f)
                settingsMenuGestureHoldStarted = Time.realtimeSinceStartup;
            if (settingsMenuGestureTriggered ||
                Time.realtimeSinceStartup - settingsMenuGestureHoldStarted < 2f)
                return;

            settingsMenuGestureTriggered = true;
            ToggleSettingsMenu();
            Debug.Log("MFNVR: two-second left-stick settings gesture completed.");
        }

        public static bool TryGetSettingsMenuTracking(float[] values)
        {
            // 0..2 head position, 3..6 head rotation, 7..9 right-aim position,
            // 10..13 right-aim rotation, 14 right trigger. A flat array keeps the companion
            // loosely coupled to this assembly and avoids a Unity-type ABI dependency.
            if (values == null || values.Length < 15)
                return false;
            Vector3 headPosition;
            Quaternion headRotation;
            if (menuScreen != null && (settingsMenuOpen || menuScreen.activeInHierarchy) &&
                lastMenuLeftEye != null && lastMenuRightEye != null &&
                TryMapRightControllerToFlatMenu(lastMenuLeftEye, lastMenuRightEye))
            {
                headPosition = (lastMenuLeftEye.transform.position +
                                lastMenuRightEye.transform.position) * 0.5f;
                headRotation = Quaternion.Slerp(lastMenuLeftEye.transform.rotation,
                    lastMenuRightEye.transform.rotation, 0.5f);
            }
            else if (haveWorldEyeData && motionPoseValid)
            {
                headPosition = (worldLeftEyePosition + worldRightEyePosition) * 0.5f;
                headRotation = Quaternion.Slerp(worldLeftEyeRotation,
                    worldRightEyeRotation, 0.5f);
            }
            else
            {
                return false;
            }
            values[0] = headPosition.x;
            values[1] = headPosition.y;
            values[2] = headPosition.z;
            values[3] = headRotation.x;
            values[4] = headRotation.y;
            values[5] = headRotation.z;
            values[6] = headRotation.w;
            values[7] = rightAimWorldPosition.x;
            values[8] = rightAimWorldPosition.y;
            values[9] = rightAimWorldPosition.z;
            values[10] = rightAimWorldRotation.x;
            values[11] = rightAimWorldRotation.y;
            values[12] = rightAimWorldRotation.z;
            values[13] = rightAimWorldRotation.w;
            float sx, sy, trigger, squeeze;
            int primary, secondary, stickClick, menu;
            values[14] = MFN_GetControllerInput(DominantHandIndex, out sx, out sy, out trigger,
                out squeeze, out primary, out secondary, out stickClick, out menu) != 0
                ? trigger
                : 0f;
            return true;
        }

        public static void ConfigureTrackedPairPost(Camera source, Camera left, Camera right,
            RenderTexture leftTexture, RenderTexture rightTexture,
            bool isWorld, bool isHud, bool gameplay)
        {
            if (source == null || left == null || right == null ||
                leftTexture == null || rightTexture == null)
                return;

            UpdateSettingsMenuToggleGesture();

            var physicalInventoryActive = IsPhysicalInventoryActive();
            var cutsceneActive = IsCutsceneActive();
            if (isWorld && !physicalInventoryActive)
                RestoreToolboxDrawerPosition();
            if (isWorld)
            {
                interactionPointerCameraActive = false;
                interactionPointerUsesStableRig = false;
                SetStereoOutlineActive(left, right, source, false);
                if (settingsMenuOpen)
                    ConfigureMenuScreen(source, left, right, leftTexture, rightTexture);
                else if (physicalInventoryActive)
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
                // The settings panel is already the final element composited into the
                // captured menu texture. Do not render MFN's pause/HUD cameras over the
                // stereo menu eyes afterward, or the pause overlay obscures this panel.
                if (settingsMenuOpen)
                {
                    left.enabled = false;
                    right.enabled = false;
                    return;
                }
                // With UI Screens limited to main/pause/files, interaction interfaces
                // remain in the real world camera instead of a captured panel. Their
                // prompts, labels and action menus still live on MFN's HUD camera, so
                // render that stereo overlay instead of compositing it into the unused
                // flat-screen texture.
                var worldAttachedUi = IsUiModeActive() && !ShouldUseFlatUiScreen();
                ConfigureHud(source, left, right, leftTexture, rightTexture,
                    gameplay || physicalInventoryActive || cutsceneActive ||
                    worldAttachedUi);
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

            var uiModeActive = IsUiModeActive();
            int noteReadoutLayerMask;
            var noteReadoutVisible = TryGetVisibleNoteReadoutLayerMask(
                out noteReadoutLayerMask);
            var cutsceneActive = IsCutsceneActive();
            var hideGameplayHands = (uiModeActive && !noteReadoutVisible) ||
                                    cutsceneActive;

            // Re-sample immediately before the hands cameras render. This is a late pose update,
            // so controller motion is not a frame behind the headset and animation cannot pull
            // the wrists away from their physical Touch controller positions.
            if (motionActive && motionContextValid && !uiModeActive &&
                !cutsceneActive)
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
            if (noteReadoutVisible)
            {
                // MFN draws the readable note panel with SpriteRenderer/TMP objects on
                // its first-person overlay camera, not on the normal HUD camera. Keep
                // the gameplay hands camera suppressed in interaction views, but let its
                // stereo clones draw only the layers occupied by the active note reader.
                // The game's normal SetHandsGone state keeps weapon/arm renderers out.
                var readableMask = noteReadoutLayerMask & source.cullingMask;
                if (readableMask != 0)
                {
                    left.cullingMask = readableMask;
                    right.cullingMask = readableMask;
                    if (!noteReadoutLayerLogged)
                    {
                        Debug.Log("MFNVR: note reader stereo overlay enabled (mask 0x" +
                                  readableMask.ToString("X8") + ").");
                        noteReadoutLayerLogged = true;
                    }
                }
                else
                {
                    // A few scenes author the readout on a layer omitted from the source
                    // overlay mask. Render that exact discovered layer rather than hiding
                    // the note entirely.
                    left.cullingMask = noteReadoutLayerMask;
                    right.cullingMask = noteReadoutLayerMask;
                }
            }
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

            // UI modes and cutscenes must not have the first-person weapon, authored
            // arms, or floating VR hands composited over them. Disabling only the cloned
            // Hands cameras leaves gameplay/equipment state untouched, preserves the
            // separate menu pointer, and restores everything on returning to gameplay.
            if (hideGameplayHands)
            {
                left.enabled = false;
                right.enabled = false;
            }
        }

        private static bool TryGetVisibleNoteReadoutLayerMask(out int layerMask)
        {
            layerMask = 0;
            try
            {
                var player = Player.current;
                var readout = player != null ? player.GetNoteReadout() : null;
                if (readout == null ||
                    !ReadBooleanField(noteReadoutVisibleField, readout))
                {
                    noteReadoutLayerLogged = false;
                    return false;
                }

                if (!ReferenceEquals(cachedNoteReadout, readout) ||
                    cachedNoteReadoutRenderers == null)
                {
                    cachedNoteReadout = readout;
                    cachedNoteReadoutRenderers = readout
                        .GetComponentsInChildren<Renderer>(true);
                    cachedNoteReadoutLayerMask = 0;
                    foreach (var renderer in cachedNoteReadoutRenderers)
                    {
                        if (renderer != null)
                            cachedNoteReadoutLayerMask |= 1 << renderer.gameObject.layer;
                    }
                }

                layerMask = cachedNoteReadoutLayerMask;
                return layerMask != 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool TickNativeHands(Player player, Vector3 originPosition,
            Quaternion originRotation, Vector3 rigPosition, Quaternion rigRotation,
            bool hasOrigin, bool useRig)
        {
            var inventory = GetPhysicalInventory(player);
            var physicalInventoryActive = inventory != null;
            var uiModeActive = IsUiModeActive(player, inventory);
            if (Player.current != null && player != Player.current)
                return motionPoseValid && (motionItem != null || physicalInventoryActive);
            motionOriginPosition = originPosition;
            motionOriginRotation = originRotation;
            // Keep a genuinely stable gameplay base for the normal Y inventory. MFN
            // animates rigPosition onto its inventory camera after Y is pressed; saving
            // that animated value made the VR view move with the flat-camera animation.
            if (!physicalInventoryActive && !uiModeActive && !IsCutsceneActive() &&
                hasOrigin && useRig)
            {
                lastGameplayRigPosition = rigPosition;
                lastGameplayRigRotation = rigRotation;
                haveLastGameplayRig = true;
            }
            motionRigPosition = rigPosition;
            motionRigRotation = rigRotation;
            var menuPointerContext = IsNativeMenuPointerContext(player, inventory);
            motionContextValid = player != null && hasOrigin &&
                (useRig || physicalInventoryActive || menuPointerContext);
            if (!motionContextValid)
            {
                motionPoseValid = false;
                if (menuPointerRuntimeActive)
                    ResetMenuPointerInteraction();
                return false;
            }

            try
            {
                EnsureGameplayPatches();
                RefreshControllerPoses();
                if (settingsMenuOpen)
                {
                    if (menuPointerRuntimeActive)
                        ResetMenuPointerInteraction();
                    return false;
                }
                UpdatePhysicalWeaponSwitching(player, physicalInventoryActive);
                var menuPointerActive = motionPoseValid && menuPointerContext;
                if (menuPointerActive)
                {
                    motionManager = player.GetEquipManager();
                    BindHeldItemOrEmptyHands(motionManager);
                    UpdateMenuPointerInteraction(player, inventory);
                    return false;
                }

                if (menuPointerRuntimeActive)
                    ResetMenuPointerInteraction();
                if (physicalInventoryActive)
                {
                    EnsurePhysicalInventoryState(inventory);
                    return false;
                }

                ResetPhysicalInventoryStateIfClosed();
                motionManager = player.GetEquipManager();
                BindHeldItemOrEmptyHands(motionManager);
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
                var pointerInputPrefix = new HarmonyMethod(typeof(RenderBridge).GetMethod(
                    nameof(SuppressCoreTouchGamepadWhilePointerPrefix),
                    BindingFlags.Static | BindingFlags.NonPublic));
                if (updateTouchGamepad != null && dpadPostfix.method != null)
                {
                    coreTouchGamepadField = AccessTools.Field(coreType, "touchGamepad");
                    coreLastTouchInputFrameField = AccessTools.Field(coreType,
                        "lastTouchInputFrame");
                    harmony.Patch(updateTouchGamepad, prefix: pointerInputPrefix,
                        postfix: dpadPostfix);
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
                if (settingsMenuOpen || menuPointerInputActive)
                    return;
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

        private static bool SuppressCoreTouchGamepadWhilePointerPrefix(object __instance)
        {
            // Skipping the complete core update also removes B, so do that only for the
            // modal settings panel and while directly selecting its custom launch button.
            // Native interaction-pointer menus keep the core update and selectively mask
            // A/trigger later, allowing their ordinary B/back binding to remain intact.
            var suppressCompleteGamepad = settingsMenuOpen ||
                (flatMenuPointerActive && menuSettingsButtonHovered);
            if (!suppressCompleteGamepad && !leftHandedMode)
                return true;
            try
            {
                var gamepad = coreTouchGamepadField?.GetValue(__instance) as Gamepad;
                if (gamepad == null || !gamepad.added)
                {
                    // Let the stable core create its virtual device. From the next frame
                    // onward this prefix emits the one authoritative left-handed state.
                    return true;
                }

                behindHeadGamepad = gamepad;
                if (suppressCompleteGamepad)
                {
                    InputSystem.QueueStateEvent(gamepad, new GamepadState());
                    return false;
                }

                var currentFrame = Time.frameCount;
                if (coreLastTouchInputFrameField != null)
                {
                    var lastFrame = (int)coreLastTouchInputFrameField.GetValue(__instance);
                    if (lastFrame == currentFrame)
                        return false;
                    coreLastTouchInputFrameField.SetValue(__instance, currentFrame);
                }

                float leftX, leftY, leftTrigger, leftGrip;
                float rightX, rightY, rightTrigger, rightGrip;
                int leftPrimary, leftSecondary, leftStickClick, leftMenu;
                int rightPrimary, rightSecondary, rightStickClick, rightMenu;
                if (MFN_GetControllerInput(0, out leftX, out leftY, out leftTrigger,
                        out leftGrip, out leftPrimary, out leftSecondary,
                        out leftStickClick, out leftMenu) == 0 ||
                    MFN_GetControllerInput(1, out rightX, out rightY, out rightTrigger,
                        out rightGrip, out rightPrimary, out rightSecondary,
                        out rightStickClick, out rightMenu) == 0)
                    return false;

                // MFN expects firing on the virtual Xbox right trigger. In left-handed
                // mode that value comes only from the physical left trigger. The physical
                // right trigger stays raw for support-hand inventory and action-menu use,
                // preventing either trigger from also flipping/firing the weapon.
                var state = new GamepadState
                {
                    leftStick = ApplyVirtualGamepadStickDeadzone(
                        new Vector2(leftX, leftY)),
                    rightStick = ApplyVirtualGamepadStickDeadzone(
                        new Vector2(rightX, rightY)),
                    leftTrigger = 0f,
                    rightTrigger = ApplyVirtualGamepadTriggerDeadzone(leftTrigger)
                };
                state = state.WithButton(GamepadButton.West, leftPrimary != 0)
                    .WithButton(GamepadButton.North, leftSecondary != 0)
                    .WithButton(GamepadButton.South, rightPrimary != 0)
                    .WithButton(GamepadButton.East, rightSecondary != 0)
                    .WithButton(GamepadButton.LeftShoulder, false)
                    .WithButton(GamepadButton.RightShoulder, leftGrip > 0.55f)
                    .WithButton(GamepadButton.LeftStick, leftStickClick != 0)
                    .WithButton(GamepadButton.RightStick, rightStickClick != 0)
                    .WithButton(GamepadButton.Start, leftMenu != 0);
                InputSystem.QueueStateEvent(gamepad, state);
            }
            catch (Exception exception)
            {
                if (Time.frameCount >= motionDiagnosticFrame)
                {
                    motionDiagnosticFrame = Time.frameCount + 240;
                    Debug.LogWarning("MFNVR left-handed virtual gamepad failed: " +
                        exception.Message);
                }
                // If remapping fails, retain ordinary controller input rather than
                // leaving the player without controls.
                return true;
            }
            return false;
        }

        private static Vector2 ApplyVirtualGamepadStickDeadzone(Vector2 value)
        {
            const float deadzone = 0.18f;
            var magnitude = value.magnitude;
            if (magnitude <= deadzone)
                return Vector2.zero;
            return value.normalized * Mathf.Clamp01((magnitude - deadzone) /
                (1f - deadzone));
        }

        private static float ApplyVirtualGamepadTriggerDeadzone(float value)
        {
            const float deadzone = 0.04f;
            if (value <= deadzone)
                return 0f;
            return Mathf.Clamp01((value - deadzone) / (1f - deadzone));
        }

        private static void SuppressBehindHeadWalkingBeforeInputUpdate()
        {
            // The stable camera core queues its complete virtual-gamepad state from
            // Player.Update. Unity processes that state at the next InputSystem update.
            // Override locomotion in onBeforeUpdate while the gesture is active. Files
            // itself is opened explicitly above and no D-pad state is synthesized.
            if (settingsMenuOpen)
            {
                // The settings screen is modal. Neutralize every gamepad instead of only
                // the Touch-backed virtual pad so a physical controller cannot move the
                // player or operate MFN's menu behind the VR settings panel either.
                foreach (var activeGamepad in Gamepad.all)
                {
                    if (activeGamepad != null && activeGamepad.added)
                        NeutralizeGamepad(activeGamepad);
                }
                return;
            }
            var suppressPointerGamepad = menuPointerInputActive &&
                (!flatMenuPointerActive || menuSettingsButtonHovered);
            if (suppressPointerGamepad)
            {
                // Pointer clicks come from raw OpenXR. Remove their virtual-Xbox copies
                // before MFN can activate its joystick-selected item behind the pointer.
                // This is especially important on the title screen, where leaked A was
                // activating Quit while the VR Settings button was being selected.
                foreach (var activeGamepad in Gamepad.all)
                {
                    if (activeGamepad == null || !activeGamepad.added)
                        continue;
                    InputSystem.QueueDeltaStateEvent(activeGamepad.leftTrigger, 0f);
                    InputSystem.QueueDeltaStateEvent(activeGamepad.rightTrigger, 0f);
                    InputSystem.QueueDeltaStateEvent(activeGamepad.buttonSouth, 0f);
                    InputSystem.QueueDeltaStateEvent(activeGamepad.rightStickButton, 0f);
                }
            }
            var gamepad = behindHeadGamepad;
            if (gamepad == null || !gamepad.added)
                return;
            if (leftHandDpadMode)
                InputSystem.QueueDeltaStateEvent(gamepad.leftStick, Vector2.zero);
            // The menu pointer reads raw OpenXR input before MFN's virtual Xbox layer.
            // Suppress the translated copies while it is active so one trigger pull cannot
            // both click the pointed target and activate the old gamepad selection.
            if (suppressPointerGamepad)
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

        private static void NeutralizeGamepad(Gamepad gamepad)
        {
            InputSystem.QueueDeltaStateEvent(gamepad.leftStick, Vector2.zero);
            InputSystem.QueueDeltaStateEvent(gamepad.rightStick, Vector2.zero);
            InputSystem.QueueDeltaStateEvent(gamepad.leftTrigger, 0f);
            InputSystem.QueueDeltaStateEvent(gamepad.rightTrigger, 0f);
            InputSystem.QueueDeltaStateEvent(gamepad.leftShoulder, 0f);
            InputSystem.QueueDeltaStateEvent(gamepad.rightShoulder, 0f);
            InputSystem.QueueDeltaStateEvent(gamepad.buttonSouth, 0f);
            InputSystem.QueueDeltaStateEvent(gamepad.buttonNorth, 0f);
            InputSystem.QueueDeltaStateEvent(gamepad.buttonEast, 0f);
            InputSystem.QueueDeltaStateEvent(gamepad.buttonWest, 0f);
            InputSystem.QueueDeltaStateEvent(gamepad.leftStickButton, 0f);
            InputSystem.QueueDeltaStateEvent(gamepad.rightStickButton, 0f);
            InputSystem.QueueDeltaStateEvent(gamepad.startButton, 0f);
            InputSystem.QueueDeltaStateEvent(gamepad.selectButton, 0f);
            InputSystem.QueueDeltaStateEvent(gamepad.dpad, Vector2.zero);
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
            var haveInput = MFN_GetControllerInput(DominantHandIndex, out stickX, out stickY,
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
            var dominantSide = leftHandedMode ? -1f : 1f;
            var hip = headPosition + right * (0.25f * dominantSide) -
                Vector3.up * 0.56f - forward * 0.03f;
            var shoulder = headPosition + right * (0.20f * dominantSide) -
                Vector3.up * 0.10f - forward * 0.25f;
            var hipDistance = Vector3.Distance(rightGripWorldPosition, hip);
            var shoulderDistance = Vector3.Distance(rightGripWorldPosition, shoulder);

            if (hipDistance <= 0.30f)
            {
                CyclePhysicalWeapon(player, new[]
                {
                    InventoryItem.Wrench,
                    InventoryItem.LetterGrenade
                }, leftHandedMode ? "left hip" : "right hip");
            }
            else if (shoulderDistance <= 0.34f)
            {
                CyclePhysicalWeapon(player, new[]
                {
                    InventoryItem.BoxingGloveGun,
                    InventoryItem.BoxingGloveShotgun,
                    InventoryItem.FinalGun
                }, leftHandedMode ? "left shoulder" : "right shoulder");
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

        private static void BindHeldItemOrEmptyHands(EquippedManager manager)
        {
            var heldItem = GetHeldItem(manager);
            if (heldItem != null)
            {
                BindHeldItem(heldItem);
                return;
            }
            if (motionItemIsEmptyHandRig && motionItem != null)
                return;

            var emptyHandRig = CreateEmptyHandRig(manager);
            if (emptyHandRig == null)
            {
                BindHeldItem(null);
                return;
            }
            BindHeldItem(emptyHandRig);
            motionItemIsEmptyHandRig = motionItem == emptyHandRig;
            if (motionItemIsEmptyHandRig)
                Debug.Log("MFNVR: created tracked empty hands before weapon unlock.");
        }

        private static ItemInHand CreateEmptyHandRig(EquippedManager manager)
        {
            if (manager == null || equippedItemsField == null)
                return null;
            try
            {
                var prefabs = equippedItemsField.GetValue(manager) as ItemInHand[];
                if (prefabs == null || prefabs.Length == 0)
                    return null;

                ItemInHand handSource = null;
                var preferredIndex = (int)InventoryItem.BoxingGloveGun;
                if (preferredIndex >= 0 && preferredIndex < prefabs.Length)
                    handSource = prefabs[preferredIndex];
                if (handSource == null ||
                    FindNamedTransform(handSource, "PL_HAND_R") == null)
                {
                    foreach (var candidate in prefabs)
                    {
                        if (candidate != null &&
                            FindNamedTransform(candidate, "PL_HAND_R") != null &&
                            FindNamedTransform(candidate, "PL_HAND_L") != null)
                        {
                            handSource = candidate;
                            break;
                        }
                    }
                }
                if (handSource == null)
                    return null;

                var clone = UnityEngine.Object.Instantiate(handSource,
                    Vector3.down * 1000f, Quaternion.identity);
                clone.name = "MFN VR Empty Tracked Hands";
                var rightRoot = FindNamedTransform(clone, "PL_HAND_R");
                var leftRoot = FindNamedTransform(clone, "PL_HAND_L");
                foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null)
                        continue;
                    var skinned = renderer as SkinnedMeshRenderer;
                    var containsHandSkin = skinned != null &&
                        (RendererUsesBoneTree(skinned, rightRoot) ||
                         RendererUsesBoneTree(skinned, leftRoot));
                    // Retain only the shared character skin that contains the hands.
                    // All gun meshes, particles, magazines and effects stay invisible.
                    renderer.enabled = containsHandSkin;
                }
                return clone;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: could not create pre-weapon hands: " +
                    exception.Message);
                return null;
            }
        }

        private static void BindHeldItem(ItemInHand item)
        {
            if (item == motionItem)
                return;

            RestorePreviousItem();
            motionItem = item;
            ApplyCurrentNeighborhordeHands(motionItem);
            EnsureCachedHandsMatchCurrentCharacter();
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
            var authoredRightRoot = FindNamedTransform(motionItem, "PL_HAND_R");
            var authoredLeftRoot = FindNamedTransform(motionItem, "PL_HAND_L");
            // Keep MFN's proven weapon hierarchy attached to its authored right wrist.
            // The authored left wrist is a support-animation target; making it dominant
            // allowed fire/reload animations to rotate the entire weapon. Handedness is
            // represented visually by mirroring the hands, not by changing weapon bones.
            rightWrist = FindWristUnder(authoredRightRoot);
            leftHandRoot = authoredLeftRoot;
            leftWrist = FindWristUnder(leftHandRoot);
            itemTransform.SetParent(motionAnchor, true);
            NormalizeHandScale(itemTransform, rightWrist);
            ShrinkWrenchAssembly(motionItem);
            PrepareFloatingHands(motionItem);
            motionItemRenderers = motionItem.GetComponentsInChildren<Renderer>(true);
            motionItemMeshRenderers = motionItem.GetComponentsInChildren<MeshRenderer>(true);
            CacheLeftHandRenderers();
            CaptureOriginalLeftHandTransform();
            usingBakedLeftHand = leftHandVisualRoot != null &&
                leftHandVisualObject != null && leftHandVisualMesh != null;
            if (!usingBakedLeftHand)
                usingBakedLeftHand = CreateIndependentLeftHandVisual();
            if (!usingBakedLeftHand)
                DetachLeftHandFromWeaponAnimator();
            EnsureLeftHandVisible();
            ApplyDominantHandMirror(authoredRightRoot);
            Debug.Log("MFNVR: bound " + motionItem.name +
                " to direct floating-hand tracking; rightWrist=" + (rightWrist != null) +
                ", leftWrist=" + (leftWrist != null) +
                ", independentLeftHand=" + (usingBakedLeftHand || leftHandDetached) +
                ", bakedLeftHand=" + usingBakedLeftHand + ".");
        }

        private static void ApplyCurrentNeighborhordeHands(ItemInHand item)
        {
            var player = Player.current;
            if (item == null || player == null || !player.GetIsMercenaries())
                return;
            try
            {
                foreach (var changer in item.GetComponentsInChildren<
                    MercenariesHandsChanger>(true))
                {
                    if (changer != null)
                        changer.SwitchToHands(MercenariesController.playerID);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: could not apply Neighborhorde hand skin: " +
                    exception.Message);
            }
        }

        private static void EnsureCachedHandsMatchCurrentCharacter()
        {
            var player = Player.current;
            var characterKey = player != null && player.GetIsMercenaries()
                ? 1000 + MercenariesController.playerID
                : 0;
            if (bakedHandCharacterKey == characterKey)
                return;
            DestroyCachedCharacterHandVisuals();
            bakedHandCharacterKey = characterKey;
            Debug.Log("MFNVR: rebuilding tracked hands for character key " +
                characterKey + ".");
        }

        private static void DestroyCachedCharacterHandVisuals()
        {
            DestroyRuntimeHandVisual(ref leftHandVisualRoot,
                ref leftHandVisualObject, ref leftHandVisualMesh);
            DestroyRuntimeHandVisual(ref menuRightHandVisualRoot,
                ref menuRightHandVisualObject, ref menuRightHandVisualMesh);
            menuRightHandVisualTransforms = new Transform[0];
            rightGripToMenuHandRotation = Quaternion.identity;
            menuPointerVisualLayer = int.MinValue;
            usingBakedLeftHand = false;
        }

        private static void DestroyRuntimeHandVisual(ref Transform root,
            ref GameObject visualObject, ref Mesh mesh)
        {
            if (visualObject != null)
            {
                var renderer = visualObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null)
                            UnityEngine.Object.Destroy(material);
                }
            }
            if (root != null)
                UnityEngine.Object.Destroy(root.gameObject);
            else if (visualObject != null)
                UnityEngine.Object.Destroy(visualObject);
            if (mesh != null)
                UnityEngine.Object.Destroy(mesh);
            root = null;
            visualObject = null;
            mesh = null;
        }

        private static void RestorePreviousItem()
        {
            if (motionItem == null)
                return;

            var wasEmptyHandRig = motionItemIsEmptyHandRig;
            RestoreDominantHandMirror();
            RestoreLeftHandToWeapon();
            var itemTransform = motionItem.transform;
            if (wasEmptyHandRig)
            {
                if (motionItem.gameObject != null)
                {
                    motionItem.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(motionItem.gameObject);
                }
            }
            else if (itemTransform != null && itemTransform.parent == motionAnchor)
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
            motionItemIsEmptyHandRig = false;
            motionItemRenderers = new Renderer[0];
            motionItemMeshRenderers = new MeshRenderer[0];
            leftHandRenderers = new Renderer[0];
        }

        private static void ApplyDominantHandMirror(Transform authoredRightRoot)
        {
            RestoreDominantHandMirror();
            if (!leftHandedMode || motionItem == null || rightWrist == null)
                return;

            // Mirror only the authored right hand around its own wrist. The wrist position,
            // weapon, muzzle and animator hierarchy remain untouched, so this changes the
            // visible hand from right to left without destabilizing weapon tracking.
            mirroredDominantWrist = rightWrist;
            originalDominantWristScale = rightWrist.localScale;
            var mirroredScale = originalDominantWristScale;
            mirroredScale.x = -mirroredScale.x;
            rightWrist.localScale = mirroredScale;
            dominantWristMirrored = true;

            // Mirroring reverses the hand triangles' winding. Use private two-sided
            // material instances on skinned renderers driven by this hand, then restore
            // the originals when the item is changed or handedness is disabled.
            foreach (var renderer in motionItem.GetComponentsInChildren<
                         SkinnedMeshRenderer>(true))
            {
                if (renderer == null || authoredRightRoot == null ||
                    !RendererUsesBoneTree(renderer, authoredRightRoot))
                    continue;
                var originals = renderer.sharedMaterials;
                mirroredDominantMaterialRestores.Add(
                    new KeyValuePair<Renderer, Material[]>(renderer, originals));
                renderer.sharedMaterials = CreateTwoSidedHandMaterials(originals);
            }
            Debug.Log("MFNVR: mirrored the stable authored right-hand hierarchy for " +
                "left-handed weapon rendering.");
        }

        private static void RestoreDominantHandMirror()
        {
            if (dominantWristMirrored && mirroredDominantWrist != null)
                mirroredDominantWrist.localScale = originalDominantWristScale;
            mirroredDominantWrist = null;
            dominantWristMirrored = false;

            foreach (var entry in mirroredDominantMaterialRestores)
            {
                var renderer = entry.Key;
                if (renderer == null)
                    continue;
                var mirroredMaterials = renderer.sharedMaterials;
                renderer.sharedMaterials = entry.Value;
                foreach (var material in mirroredMaterials)
                {
                    if (material != null && Array.IndexOf(entry.Value, material) < 0)
                        UnityEngine.Object.Destroy(material);
                }
            }
            mirroredDominantMaterialRestores.Clear();
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
                    if (leftHandedMode)
                    {
                        // The independent support mesh is authored as a left hand. Reflect
                        // it across its wrist-local lateral axis so the physical right
                        // controller displays a true mirrored right hand.
                        for (var vertex = 0; vertex < canonicalVertices.Length; vertex++)
                        {
                            var mirroredVertex = canonicalVertices[vertex];
                            mirroredVertex.x = -mirroredVertex.x;
                            canonicalVertices[vertex] = mirroredVertex;
                            if (haveBakedNormals)
                            {
                                var mirroredNormal = canonicalNormals[vertex];
                                mirroredNormal.x = -mirroredNormal.x;
                                canonicalNormals[vertex] = mirroredNormal;
                            }
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
            var pointerRoot = FindNamedTransform(motionItem,
                leftHandedMode ? "PL_HAND_L" : "PL_HAND_R");
            var pointerWrist = FindWristUnder(pointerRoot);
            if (motionItem == null || pointerRoot == null || pointerWrist == null)
                return false;
            try
            {
                foreach (var source in motionItem.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (source == null || source.sharedMesh == null ||
                        !RendererUsesBoneTree(source, pointerRoot))
                        continue;

                    var handDirection = FindHandDirection(pointerWrist);
                    var palmNormal = FindPalmNormal(pointerWrist, handDirection);
                    var savedFingerRotations = ApplyMenuPointerFingerPose(pointerWrist,
                        handDirection, palmNormal);
                    var baked = new Mesh
                    {
                        name = source.sharedMesh.name + " (MFN VR Menu Dominant Hand Only)"
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

                    var wristPosition = pointerWrist.position;
                    var boneReach = FindHandBoneReach(pointerWrist, handDirection);
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

                    var rootObject = new GameObject("MFN VR Menu Dominant Hand Root");
                    menuRightHandVisualRoot = rootObject.transform;
                    rightGripToMenuHandRotation = Quaternion.Inverse(rightGripWorldRotation) *
                        canonicalRotation;
                    menuRightHandVisualRoot.SetPositionAndRotation(rightGripWorldPosition,
                        canonicalRotation);

                    menuRightHandVisualObject = new GameObject("MFN VR Menu Dominant Hand Mesh");
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
                    menuRightHandVisualTransforms = menuRightHandVisualRoot
                        .GetComponentsInChildren<Transform>(true);
                    menuPointerVisualLayer = int.MinValue;
                    rootObject.SetActive(false);
                    Debug.Log("MFNVR: created independent " +
                        (leftHandedMode ? "left" : "right") +
                        "-hand menu pointer with " + keptTriangles + " triangles.");
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: could not create dominant-hand menu pointer: " +
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
                    if (renderer != null && !renderer.enabled)
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
                if (renderer != null && !renderer.enabled)
                    renderer.enabled = true;
            }
        }

        private static bool ShouldShowAuthoredSupportHand()
        {
            if (leftHandedMode || !twoHanded || !lockedSupportGripSteersWeapon ||
                motionManager == null)
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
            motionPoseValid = TryGetControllerPose(DominantHandIndex, false, out rawRightGrip,
                out rawRightGripRotation) && TryGetControllerPose(DominantHandIndex, true,
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
            leftPoseValid = TryGetControllerPose(SupportHandIndex, false, out rawLeftGrip,
                out rawLeftGripRotation);
            var leftAimValid = TryGetControllerPose(SupportHandIndex, true, out rawLeftAim,
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
                !TryGetControllerPose(DominantHandIndex, true, out rawAimPosition,
                    out rawAimRotation) ||
                !TryGetControllerPose(DominantHandIndex, false, out rawGripPosition,
                    out rawGripRotation))
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
            var haveInput = MFN_GetControllerInput(SupportHandIndex, out stickX, out stickY,
                out trigger, out squeeze, out primary, out secondary,
                out stickClick, out menu) != 0;
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
                        MFN_ApplyControllerHaptic(SupportHandIndex, 0.42f, 0.065f, 0f);
                        Debug.Log("MFNVR: support hand snapped and locked to " + supportName +
                            "; press support grip again to release.");
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

        private static void ReleaseSupportGrip(bool movedTooFar)
        {
            if (!twoHanded)
                return;
            var releasedName = lockedSupportGripName;
            twoHanded = false;
            lockedSupportGripName = null;
            lockedSupportGripSteersWeapon = false;
            lockedSupportGripReleaseDistance = 0f;
            MFN_ApplyControllerHaptic(SupportHandIndex, movedTooFar ? 0.30f : 0.18f,
                movedTooFar ? 0.055f : 0.045f, 0f);
            if (movedTooFar)
                Debug.Log("MFNVR: automatically released " + releasedName +
                    " because the physical left hand moved beyond its grip tether.");
        }

        private static Quaternion GetTrackedLeftHandVisualRotation()
        {
            if (leftHandedMode)
                return leftAimWorldRotation;
            return leftGripWorldRotation * SavedLeftGripToHandRotation;
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
            foreach (var renderer in motionItemRenderers)
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
            foreach (var renderer in motionItemRenderers)
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
            var nextOrigin = rightAimWorldPosition;
            var aimUp = rightAimWorldRotation * Vector3.up;
            var nextDirection = Quaternion.AngleAxis(GunAimYawCorrectionDegrees, aimUp) *
                (rightAimWorldRotation * Vector3.forward);
            if (nextDirection.sqrMagnitude < 0.5f)
                nextDirection = Vector3.forward;
            nextDirection.Normalize();

            // Reticle, late hand pose and projectile hooks can request the same ray several
            // times in one frame. Reuse it unless the late controller sample actually moved.
            if (lastGunRayFrame == Time.frameCount &&
                (nextOrigin - lastGunRayOrigin).sqrMagnitude < 0.00000025f &&
                Vector3.Dot(nextDirection, lastGunRayDirection) > 0.999999f)
                return;
            lastGunRayFrame = Time.frameCount;
            lastGunRayOrigin = nextOrigin;
            lastGunRayDirection = nextDirection;
            gunRayOrigin = nextOrigin;
            gunRayDirection = nextDirection;

            RaycastHit hit;
            var rayStart = gunRayOrigin + gunRayDirection * 0.035f;
            if (Physics.Raycast(rayStart, gunRayDirection, out hit, 1000f,
                GetGunRayMask(),
                QueryTriggerInteraction.Ignore))
                gunRayTarget = hit.point;
            else
                gunRayTarget = gunRayOrigin + gunRayDirection * 1000f;
        }

        private static bool IsGunEquipped()
        {
            if (motionManager == null || motionItem == null)
                return false;
            var item = motionManager.GetCurrentItem();
            return item == InventoryItem.BoxingGloveGun ||
                   item == InventoryItem.BoxingGloveShotgun ||
                   item == InventoryItem.FinalGun;
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
            return motionManager.GetCurrentItem() == InventoryItem.Wrench;
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

            if (settingsMenuOpen || !motionPoseValid || !IsWrenchEquipped())
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
            var hitCount = Physics.SphereCastNonAlloc(sweepStart,
                PhysicalWrenchHeadRadius, physicalDeltaWorld / sweepDistance,
                wrenchSweepHits, sweepDistance, ~0, QueryTriggerInteraction.Collide);

            // Only the wrench head and its actual swept arc can hit. The previous full-shaft
            // capsule was the source of stationary proximity hits shown in the recording.
            const float maximumReach = PhysicalWrenchReach + PhysicalWrenchHeadRadius;
            // Never use an isolated peak sample for damage/stun. Capping it by the
            // current and average swing speed prevents a tracking spike from becoming
            // a high-power hit after the hand has nearly stopped.
            var impactSpeed = Mathf.Min(wrenchSwingPeakSpeed,
                Mathf.Min(speed, averageSwingSpeed * 1.20f));
            for (var hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                var hit = wrenchSweepHits[hitIndex];
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
            var colliderCount = Physics.OverlapSphereNonAlloc(headWorldPosition,
                PhysicalWrenchHeadRadius, wrenchOverlapColliders, ~0,
                QueryTriggerInteraction.Collide);
            for (var colliderIndex = 0; colliderIndex < colliderCount; colliderIndex++)
            {
                var collider = wrenchOverlapColliders[colliderIndex];
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
                foreach (var renderer in motionItemMeshRenderers)
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
                MFN_ApplyControllerHaptic(DominantHandIndex,
                    Mathf.Clamp01(speed / 10.0f), 0.07f, 0f);
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
                if (muzzleSight != null && muzzleSight.activeSelf)
                    muzzleSight.SetActive(false);
                return;
            }
            EnsureMuzzleSight();
            if (!muzzleSight.activeSelf)
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
            var currentItem = motionManager != null
                ? motionManager.GetCurrentItem()
                : InventoryItem.NONE;
            if (currentItem == InventoryItem.BoxingGloveShotgun)
            {
                shotDirection = Quaternion.AngleAxis(UnityEngine.Random.Range(-8f, 8f),
                    rightAimWorldRotation * Vector3.up) *
                    Quaternion.AngleAxis(UnityEngine.Random.Range(-8f, 8f),
                    rightAimWorldRotation * Vector3.right) * shotDirection;
            }
            else if (currentItem == InventoryItem.FinalGun)
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
            if (renderer.enabled)
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
            var mask = GetGameplayInteractionMask();
            var reach = 3f;
            try
            {
                var configuredReach = player.GetReachDistance();
                if (configuredReach > 0.1f)
                    reach = configuredReach;
            }
            catch { }

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
            var hitCount = Physics.SphereCastNonAlloc(origin, assistRadius, direction,
                interactionAssistHits, reach, mask, QueryTriggerInteraction.Collide);
            Interactable candidate = null;
            var candidateDistance = float.PositiveInfinity;
            for (var hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                var hit = interactionAssistHits[hitIndex];
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
            return (settingsMenuOpen ||
                    (menuPointerEnabled && menuPointerInputActive)) &&
                   context.control != null && context.control.device is Gamepad;
        }

        private static void EnsureSourceBackbufferOptimizer(Camera source)
        {
            if (source == null)
                return;
            var sourceId = source.GetInstanceID();
            Camera cachedSource;
            if (sourceBackbufferOptimizers.TryGetValue(sourceId, out cachedSource) &&
                ReferenceEquals(cachedSource, source))
                return;
            if (source.GetComponent<SourceBackbufferOptimizer>() == null)
                source.gameObject.AddComponent<SourceBackbufferOptimizer>();
            sourceBackbufferOptimizers[sourceId] = source;
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
            return IsUiModeActive(Player.current, GetPhysicalInventory());
        }

        private static bool IsUiModeActive(Player player, InventoryInWorld inventory)
        {
            if (settingsMenuOpen)
                return true;
            if (inventory != null)
                return true;
            if (player == null)
                return false;
            return ReadBooleanField(inventoryControlsEnabledField, player) ||
                   ReadBooleanField(menuControlsEnabledField, player) ||
                   ReadBooleanField(mapControlsEnabledField, player) ||
                   ReadBooleanField(pauseMenuEnabledField, player) ||
                   ReadBooleanField(investigateControlsEnabledField, player);
        }

        private static bool IsNativeMenuPointerContext(Player player,
            InventoryInWorld inventory)
        {
            if (settingsMenuOpen || !menuPointerEnabled || player == null)
                return false;
            if (inventory != null)
                return true;
            // Main, pause and files are handled by UpdateFlatMenuPointer. The native ray
            // exists only for real-camera inventory and point-and-click interaction views.
            if (ShouldUseFlatUiScreen())
                return false;
            return ReadBooleanField(inventoryControlsEnabledField, player) ||
                   ReadBooleanField(mapControlsEnabledField, player) ||
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
            if (!worldAttachedUiHoverMaskReady)
            {
                worldAttachedUiHoverMask = 0;
                AddNamedLayer(ref worldAttachedUiHoverMask, "DefaultHover");
                AddNamedLayer(ref worldAttachedUiHoverMask, "ExamineHover");
                AddNamedLayer(ref worldAttachedUiHoverMask, "InvisibleHover");
                worldAttachedUiHoverMaskReady = true;
            }
            left.cullingMask |= worldAttachedUiHoverMask;
            right.cullingMask |= worldAttachedUiHoverMask;
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

        private static int GetCachedLayer(ref int cache, string layerName,
            int fallback = -1)
        {
            if (cache == int.MinValue)
            {
                cache = LayerMask.NameToLayer(layerName);
                if (cache < 0)
                    cache = fallback;
            }
            return cache;
        }

        private static int GetGunRayMask()
        {
            if (!gunRayMaskReady)
            {
                gunRayMask = LayerMask.GetMask("Enemy", "Level", "Default",
                    "DontInteractWithPlayer");
                gunRayMaskReady = true;
            }
            return gunRayMask;
        }

        private static int GetGameplayInteractionMask()
        {
            if (!gameplayInteractionMaskReady)
            {
                AddNamedLayer(ref gameplayInteractionMask, "Default");
                AddNamedLayer(ref gameplayInteractionMask, "Level");
                AddNamedLayer(ref gameplayInteractionMask, "DefaultHover");
                AddNamedLayer(ref gameplayInteractionMask, "LevelProjectilePassthrough");
                gameplayInteractionMaskReady = true;
            }
            return gameplayInteractionMask;
        }

        private static int GetPointerInteractionMask()
        {
            if (!pointerInteractionMaskReady)
            {
                AddNamedLayer(ref pointerInteractionMask, "Inventory");
                AddNamedLayer(ref pointerInteractionMask, "Examine");
                AddNamedLayer(ref pointerInteractionMask, "ExamineHover");
                AddNamedLayer(ref pointerInteractionMask, "UI");
                AddNamedLayer(ref pointerInteractionMask, "InventoryHover");
                AddNamedLayer(ref pointerInteractionMask, "InvisibleHover");
                AddNamedLayer(ref pointerInteractionMask, "Invisible");
                AddNamedLayer(ref pointerInteractionMask, "Default");
                AddNamedLayer(ref pointerInteractionMask, "Level");
                AddNamedLayer(ref pointerInteractionMask, "DefaultHover");
                AddNamedLayer(ref pointerInteractionMask, "LevelProjectilePassthrough");
                pointerInteractionMaskReady = true;
            }
            return pointerInteractionMask;
        }

        private static int GetPointerUiLayerMask()
        {
            if (!pointerUiLayerMaskReady)
            {
                AddNamedLayer(ref pointerUiLayerMask, "Inventory");
                AddNamedLayer(ref pointerUiLayerMask, "Examine");
                AddNamedLayer(ref pointerUiLayerMask, "ExamineHover");
                AddNamedLayer(ref pointerUiLayerMask, "UI");
                AddNamedLayer(ref pointerUiLayerMask, "InventoryHover");
                AddNamedLayer(ref pointerUiLayerMask, "InvisibleHover");
                AddNamedLayer(ref pointerUiLayerMask, "Invisible");
                pointerUiLayerMaskReady = true;
            }
            return pointerUiLayerMask;
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
            var upper = itemBox.GetInventory();
            var drawer = itemBox.GetInventoryInWorld();
            if (cachedToolboxVisualsReady &&
                ReferenceEquals(cachedToolboxVisualItemBox, itemBox) &&
                ReferenceEquals(cachedToolboxVisualUpper, upper) &&
                ReferenceEquals(cachedToolboxVisualDrawer, drawer))
                return cachedToolboxVisualLayerMask;

            cachedToolboxVisualItemBox = itemBox;
            cachedToolboxVisualUpper = upper;
            cachedToolboxVisualDrawer = drawer;
            cachedToolboxVisualLayerMask = 0;
            AddToolboxInventoryVisuals(upper, ref cachedToolboxVisualLayerMask);
            AddToolboxInventoryVisuals(drawer, ref cachedToolboxVisualLayerMask);
            cachedToolboxVisualsReady = true;
            return cachedToolboxVisualLayerMask;
        }

        internal static void InvalidateToolboxVisualCache()
        {
            cachedToolboxVisualsReady = false;
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
            if (!motionPoseValid || !IsNativeMenuPointerContext(player, inventory))
            {
                if (menuPointerRuntimeActive)
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

            menuPointerRuntimeActive = true;
            menuPointerInputActive = true;
            DisableInventorySquareHighlights(inventory);
            if (inventory == null || IsToolboxInventory(inventory))
                ApplyInteractionPointerCameraSpace();
            PoseMenuPointerHand();
            SetMenuPointerVisualLayer(GetCachedLayer(ref defaultLayer, "Default", 0));
            float stickX, stickY, trigger, squeeze;
            int primary, secondary, stickClick, menu;
            var haveInput = MFN_GetControllerInput(DominantHandIndex, out stickX, out stickY,
                out trigger, out squeeze, out primary, out secondary,
                out stickClick, out menu) != 0;
            var triggerPressed = haveInput && trigger >= 0.72f;
            var primaryPressed = haveInput && primary != 0;
            var rotatePressed = haveInput && stickClick != 0;
            var triggerStarted = triggerPressed && !previousInventoryTriggerPressed;
            var primaryStarted = primaryPressed && !previousInventoryPrimaryPressed;
            var pointerSelectStarted = triggerStarted || primaryStarted;
            var rotateStarted = rotatePressed && !previousInventoryRotatePressed;
            float supportStickX, supportStickY, supportTrigger, supportSqueeze;
            int supportPrimary, supportSecondary, supportStickClick, supportMenu;
            var haveSupportInput = MFN_GetControllerInput(SupportHandIndex,
                out supportStickX, out supportStickY, out supportTrigger,
                out supportSqueeze, out supportPrimary, out supportSecondary,
                out supportStickClick, out supportMenu) != 0;
            var leftTriggerPressed = haveSupportInput && supportTrigger >= 0.72f;
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
                singlePointerInventory.Clear();
                AddPointerInventory(singlePointerInventory, heldInventory);
                pointedSquare = FindPointerInventorySquare(ray,
                    singlePointerInventory, out targetInventory,
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
                    MFN_ApplyControllerHaptic(SupportHandIndex, 0.28f, 0.045f, 0f);
            }

            if (activeInventory != null && rotateStarted && heldItem != null &&
                !activeInventory.GetIsInDropdown())
            {
                InvokeInventoryRotate(activeInventory);
                MFN_ApplyControllerHaptic(DominantHandIndex, 0.22f, 0.035f, 0f);
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
                        MFN_ApplyControllerHaptic(DominantHandIndex, 0.35f, 0.055f, 0f);
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
                    MFN_ApplyControllerHaptic(DominantHandIndex,
                        placed ? 0.30f : 0.12f,
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
            pointerInventories.Clear();
            AddPointerInventory(pointerInventories, initialInventory);
            var itemBox = ItemBoxParent.current;
            if (itemBox != null)
            {
                AddPointerInventory(pointerInventories, itemBox.GetInventory());
                AddPointerInventory(pointerInventories, itemBox.GetInventoryInWorld());
            }
            return pointerInventories;
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
            var sceneHandle = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().handle;
            if (suppressedInventoryHighlightSceneHandle != sceneHandle)
            {
                suppressedInventoryHighlightSceneHandle = sceneHandle;
                suppressedInventoryHighlightRows.Clear();
            }
            var inventories = GetPointerInventories(initialInventory);
            foreach (var inventory in inventories)
            {
                var rows = inventoryRowsField?.GetValue(inventory) as
                    InventoryRowOfSquares[];
                if (rows == null)
                    continue;
                var inventoryId = inventory.GetInstanceID();
                InventoryRowOfSquares[] suppressedRows;
                if (suppressedInventoryHighlightRows.TryGetValue(inventoryId,
                        out suppressedRows) && ReferenceEquals(suppressedRows, rows))
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
                suppressedInventoryHighlightRows[inventoryId] = rows;
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
            var targetLayer = GetCachedLayer(ref inventoryCursorLayer,
                "InventoryCursor");
            if (targetLayer < 0)
                targetLayer = GetCachedLayer(ref inventoryLayer, "Inventory");
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
            Transform[] children;
            if (!dropdownTransformCache.TryGetValue(root, out children) || children == null)
            {
                children = root.GetComponentsInChildren<Transform>(true);
                dropdownTransformCache[root] = children;
            }
            foreach (var child in children)
            {
                if (child == null)
                    continue;
                var gameObject = child.gameObject;
                if (!inventoryDropdownOriginalLayers.ContainsKey(gameObject))
                    inventoryDropdownOriginalLayers.Add(gameObject, gameObject.layer);
                if (gameObject.layer != layer)
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
                        nodes[index].GetText() + " with the dominant-hand pointer.");
                    confirmer.DoAction();
                    MFN_ApplyControllerHaptic(DominantHandIndex, 0.28f, 0.045f, 0f);
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
                    " with the dominant-hand pointer.");
                dropdown.CheckForConfirm();
                MFN_ApplyControllerHaptic(DominantHandIndex, 0.28f, 0.045f, 0f);
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
            dropdownActiveNodes.Clear();
            for (var index = 0; index < nodes.Length; index++)
            {
                var node = nodes[index];
                if (node != null && node.gameObject.activeInHierarchy)
                    dropdownActiveNodes.Add(new KeyValuePair<int, Transform>(index,
                        node.transform));
            }
            if (dropdownActiveNodes.Count == 0)
                return -1;

            // The vanilla free-cursor path targets these exact colliders. Using
            // them first keeps the VR pointer's hover and selection identical to
            // the game's own mouse/gamepad dropdown behavior.
            var cursorLayer = GetCachedLayer(ref inventoryCursorLayer,
                "InventoryCursor");
            if (cursorLayer >= 0)
            {
                var hitCount = Physics.RaycastNonAlloc(ray, dropdownPointerHits, 6f,
                    1 << cursorLayer, QueryTriggerInteraction.Collide);
                var closestNodeIndex = -1;
                var closestNodeDistance = float.MaxValue;
                var closestNodePoint = point;
                for (var hitIndex = 0; hitIndex < hitCount; hitIndex++)
                {
                    var hit = dropdownPointerHits[hitIndex];
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
                        if (hit.distance < closestNodeDistance)
                        {
                            closestNodeIndex = index;
                            closestNodeDistance = hit.distance;
                            closestNodePoint = hit.point;
                        }
                        break;
                    }
                }
                if (closestNodeIndex >= 0)
                {
                    point = closestNodePoint;
                    return closestNodeIndex;
                }
            }

            // Some dropdown prefabs have their collider on a sibling rather
            // than the text node. Test the rendered text bounds next so aiming
            // directly at a visible word still selects it.
            var closestRendererHit = float.MaxValue;
            var rendererIndex = -1;
            var rendererPoint = point;
            foreach (var pair in dropdownActiveNodes)
            {
                Renderer[] renderers;
                if (!dropdownRendererCache.TryGetValue(pair.Value, out renderers) ||
                    renderers == null)
                {
                    renderers = pair.Value.GetComponentsInChildren<Renderer>(true);
                    dropdownRendererCache[pair.Value] = renderers;
                }
                foreach (var renderer in renderers)
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
            if (dropdownActiveNodes.Count > 1)
            {
                spacing = float.MaxValue;
                for (var index = 1; index < dropdownActiveNodes.Count; index++)
                    spacing = Mathf.Min(spacing, Vector3.Distance(
                        dropdownActiveNodes[index - 1].Value.position,
                        dropdownActiveNodes[index].Value.position));
                if (spacing == float.MaxValue || spacing < 0.005f)
                    spacing = 0.055f;
            }
            var maximumDistance = Mathf.Clamp(spacing * 1.25f, 0.10f, 0.24f);
            var bestIndex = -1;
            var bestDistance = maximumDistance;
            foreach (var pair in dropdownActiveNodes)
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
            var itemLayer = GetCachedLayer(ref inventoryLayer, "Inventory");
            if (itemLayer < 0)
                return null;
            var hitCount = Physics.RaycastNonAlloc(ray, inventoryItemPointerHits, 6f,
                1 << itemLayer, QueryTriggerInteraction.Collide);
            ItemInInventory closestItem = null;
            InventoryInWorld closestOwner = null;
            var closestDistance = float.MaxValue;
            var closestPoint = point;
            for (var hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                var hit = inventoryItemPointerHits[hitIndex];
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
                if (hit.distance >= closestDistance)
                    continue;
                closestItem = item;
                closestOwner = itemOwner;
                closestDistance = hit.distance;
                closestPoint = hit.point;
            }
            owner = closestOwner;
            point = closestPoint;
            return closestItem;
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
            var mask = GetPointerInteractionMask();
            if (target == null)
            {
                var hitCount = Physics.RaycastNonAlloc(ray, interactionPointerHits,
                    interactionPointerRange, mask, QueryTriggerInteraction.Collide);
                var closestDistance = float.MaxValue;
                for (var hitIndex = 0; hitIndex < hitCount; hitIndex++)
                {
                    var hit = interactionPointerHits[hitIndex];
                    if (hit.collider == null)
                        continue;
                    if (hit.distance >= closestDistance)
                        continue;
                    // Ignore the player/controller presentation itself. Other geometry
                    // may be crossed only while looking for a node explicitly owned by
                    // the currently open interaction view.
                    if (hit.collider.GetComponentInParent<Player>() != null ||
                        (menuRightHandVisualRoot != null &&
                         hit.collider.transform.IsChildOf(menuRightHandVisualRoot)))
                        continue;
                    pointerParentBehaviours.Clear();
                    hit.collider.GetComponentsInParent(true, pointerParentBehaviours);
                    foreach (var behaviour in pointerParentBehaviours)
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
                            closestDistance = hit.distance;
                            break;
                        }
                    }
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
                    MFN_ApplyControllerHaptic(DominantHandIndex, 0.28f, 0.045f, 0f);
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
            singlePointerInventory.Clear();
            AddPointerInventory(singlePointerInventory, inventory);
            var square = FindPointerInventorySquare(ray,
                singlePointerInventory, out owner,
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
                return layer >= 0 && layer < 32 &&
                       (GetPointerUiLayerMask() & (1 << layer)) != 0;
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
            menuPointerRuntimeActive = false;
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
            var inventoryPointerLayer = GetCachedLayer(ref defaultLayer, "Default", 0);
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
            inventoryPointerColor = inventoryPointerMaterial.color;
            inventoryPointerColorReady = true;
            menuPointerVisualLayer = int.MinValue;
            SetInventoryPointerVisible(false);
        }

        private static void SetInventoryPointerColor(Color color)
        {
            if (inventoryPointerMaterial == null ||
                (inventoryPointerColorReady && inventoryPointerColor == color))
                return;
            inventoryPointerMaterial.color = color;
            inventoryPointerColor = color;
            inventoryPointerColorReady = true;
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
            if (menuPointerVisualLayer == layer)
                return;
            menuPointerVisualLayer = layer;
            if (inventoryPointerDot != null)
                inventoryPointerDot.layer = layer;
            if (inventoryPointerLine != null)
                inventoryPointerLine.gameObject.layer = layer;
            if (menuRightHandVisualRoot != null)
            {
                if (menuRightHandVisualTransforms == null ||
                    menuRightHandVisualTransforms.Length == 0)
                    menuRightHandVisualTransforms = menuRightHandVisualRoot
                        .GetComponentsInChildren<Transform>(true);
                foreach (var child in menuRightHandVisualTransforms)
                {
                    if (child != null)
                        child.gameObject.layer = layer;
                }
            }
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
            if (settingsMenuOpen)
            {
                left.cullingMask |= 1 << MenuLayer;
                right.cullingMask |= 1 << MenuLayer;
            }
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
            menuEyesWithEffectsDisabled.Remove(cameraId);
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
            lastMenuLeftEye = left;
            lastMenuRightEye = right;
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
            EnsureGameplayPatches();
            if (!behindHeadInputHookInstalled)
            {
                InputSystem.onBeforeUpdate += SuppressBehindHeadWalkingBeforeInputUpdate;
                behindHeadInputHookInstalled = true;
            }
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
            // A full scene search here used to run once per frame. Probe once per scene,
            // then only retry occasionally while the title activator is still spawning.
            var activeSceneHandle = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().handle;
            if (cachedMainMenuSceneHandle != activeSceneHandle)
            {
                cachedMainMenuSceneHandle = activeSceneHandle;
                cachedMainMenuActivatorPresent = false;
                nextMainMenuActivatorProbeFrame = 0;
            }
            if (!cachedMainMenuActivatorPresent &&
                Time.frameCount >= nextMainMenuActivatorProbeFrame)
            {
                cachedMainMenuActivatorPresent = UnityEngine.Object
                    .FindObjectOfType<ActivateMenuControllerOnMain>() != null;
                nextMainMenuActivatorProbeFrame = Time.frameCount + 120;
            }
            var mainMenuActivatorPresent = cachedMainMenuActivatorPresent;
            var mainMenu = explicitMainMenu || mainMenuActivatorPresent ||
                (menuControllerActive && mainMenuScene);
            // MFN calls EnablePauseMenuControls on its title screen too. Main-menu
            // detection must take priority or the pointer incorrectly raycasts from
            // the HUD camera, where none of the title selections exist.
            var pauseMenu = player != null && !mainMenu &&
                ReadBooleanField(pauseMenuEnabledField, player);
            menuSettingsButtonVisible = !settingsMenuOpen && (mainMenu || pauseMenu);
            var pointerMode = settingsMenuOpen ? 3 : mainMenu ? 1 : (pauseMenu ? 2 : 0);
            if (pointerMode != flatMenuPointerMode)
            {
                flatMenuPointerMode = pointerMode;
                Debug.Log("MFNVR: flat pointer mode=" +
                    (pointerMode == 1 ? "main" : pointerMode == 2 ? "pause" :
                        pointerMode == 3 ? "vr-settings" : "none") +
                    ", scene='" + activeSceneName + "', explicitMain=" +
                    explicitMainMenu + ", menuStack=" + menuControllerActive + ".");
            }
            if ((!menuPointerEnabled && !settingsMenuOpen) || menuScreen == null ||
                menuCapture == null || (!settingsMenuOpen &&
                    (player == null || (!mainMenu && !pauseMenu))))
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
            var haveInput = MFN_GetControllerInput(DominantHandIndex,
                out stickX, out stickY,
                out trigger, out squeeze, out primary, out secondary,
                out stickClick, out menu) != 0;
            var triggerPressed = haveInput && trigger >= 0.72f;
            var primaryPressed = haveInput && primary != 0;
            var triggerStarted = (triggerPressed && !previousFlatMenuTriggerPressed) ||
                (primaryPressed && !previousFlatMenuPrimaryPressed);
            previousFlatMenuTriggerPressed = triggerPressed;
            previousFlatMenuPrimaryPressed = primaryPressed;
            float supportStickX, supportStickY, supportTrigger, supportSqueeze;
            int supportPrimary, supportSecondary, supportStickClick, supportMenu;
            var haveSupportInput = MFN_GetControllerInput(SupportHandIndex,
                out supportStickX, out supportStickY, out supportTrigger,
                out supportSqueeze, out supportPrimary, out supportSecondary,
                out supportStickClick, out supportMenu) != 0;
            var leftTriggerPressed = haveSupportInput && supportTrigger >= 0.72f;
            var leftTriggerStarted = leftTriggerPressed &&
                                     !previousFlatMenuLeftTriggerPressed;
            previousFlatMenuLeftTriggerPressed = leftTriggerPressed;
            if (flatMenuSliderCaptureActive && !triggerPressed && !primaryPressed)
                ReleaseFlatMenuSlider();
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
            Vector2 pointerScreenPosition = default(Vector2);
            var havePointerScreenPosition = false;
            var settingsHoverActive = false;
            if (onPanel)
            {
                var u = Mathf.Clamp01(local.x + 0.5f);
                var v = Mathf.Clamp01(local.y + 0.5f);
                var mousePosition = new Vector2(u * Mathf.Max(1, Screen.width),
                    v * Mathf.Max(1, Screen.height));
                pointerScreenPosition = mousePosition;
                havePointerScreenPosition = true;
                if (!settingsMenuOpen && Mouse.current != null)
                {
                    Mouse.current.WarpCursorPosition(mousePosition);
                    // WarpCursorPosition moves the OS cursor, while changing the
                    // device state immediately lets MFN process hover in this frame.
                    InputState.Change(Mouse.current.position, mousePosition);
                }
                if (!settingsMenuOpen)
                {
                    Player.wasLastUsingGamepad = false;
                    target = FindFlatMenuPointerTarget(player, source, pauseMenu, u, v);
                    // Account for the captured quad's vertical texture orientation. The
                    // second rectangle also keeps older menu captures compatible.
                    menuSettingsButtonHovered = u >= MenuSettingsButtonMinU &&
                        u <= MenuSettingsButtonMaxU &&
                        ((v >= MenuSettingsButtonMinV && v <= MenuSettingsButtonMaxV) ||
                         (1f - v >= MenuSettingsButtonMinV &&
                          1f - v <= MenuSettingsButtonMaxV));
                }
                else
                {
                    menuSettingsButtonHovered = false;
                    settingsHoverActive = UpdateCapturedSettingsPointer(u, v,
                        triggerPressed || primaryPressed, triggerStarted);
                    target = null;
                    triggerStarted = false;
                }
            }
            else
            {
                menuSettingsButtonHovered = false;
            }

            // The VR Settings button is composited directly into this captured screen.
            // Its normalized hit rectangle is identical to the drawn rectangle, so it
            // cannot drift with resolution or aspect-ratio changes.
            if (menuSettingsButtonVisible && menuSettingsButtonHovered)
            {
                target = null;
                if (triggerStarted)
                {
                    SetSettingsMenuOpen(true);
                    triggerStarted = false;
                    MFN_ApplyControllerHaptic(DominantHandIndex, 0.34f, 0.055f, 0f);
                    Debug.Log("MFNVR: VR Settings selected from the main/pause screen.");
                }
            }

            if (!settingsMenuOpen &&
                !ReferenceEquals(flatMenuPointerHoveredInteractable, target))
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

            if (!settingsMenuOpen && target != null)
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
                        var slider = ResolveFlatMenuSlider(target);
                        if (slider != null)
                        {
                            if (!IsFlatMenuSliderPickedUp(slider))
                                AcquireFlatMenuSlider(slider);
                            if (IsFlatMenuSliderPickedUp(slider))
                            {
                                flatMenuGrabbedSlider = slider;
                                flatMenuSliderCaptureActive = true;
                                nextFlatMenuSliderValueUpdateTime = 0f;
                            }
                        }
                        else
                            target.Interact(player);
                        MFN_ApplyControllerHaptic(DominantHandIndex, 0.28f, 0.045f, 0f);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("MFNVR flat-menu pointer click failed: " + exception);
                    }
                }
            }

            if (flatMenuSliderCaptureActive && havePointerScreenPosition)
                UpdateFlatMenuSliderDrag(player, source, pointerScreenPosition);

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
            SetInventoryPointerColor(target != null || settingsHoverActive ||
                menuSettingsButtonHovered
                ? new Color(0.20f, 1f, 0.35f, 1f)
                : new Color(1f, 0.78f, 0.05f, 1f));
        }

        private static bool UpdateCapturedSettingsPointer(float u, float v,
            bool selectPressed, bool selectStarted)
        {
            const float textureWidth = 1400f;
            const float textureHeight = 900f;
            var previousTab = settingsMenuHoveredTab;
            var previousOption = settingsMenuHoveredOption;
            var previousClose = settingsMenuCloseHovered;
            settingsMenuHoveredTab = -1;
            settingsMenuHoveredOption = -1;
            settingsMenuCloseHovered = false;

            var inside = u >= SettingsPanelMinU && u <= SettingsPanelMaxU &&
                         v >= SettingsPanelMinV && v <= SettingsPanelMaxV;
            if (!inside)
            {
                if (settingsMenuDraggingOption >= 0 && !selectPressed)
                    CommitCapturedSetting(settingsMenuDraggingOption,
                        settingsMenuValues[settingsMenuDraggingOption]);
                if (previousTab != -1 || previousOption != -1 || previousClose)
                    settingsMenuTextureDirty = true;
                return false;
            }

            var normalizedX = (u - SettingsPanelMinU) /
                              (SettingsPanelMaxU - SettingsPanelMinU);
            var normalizedY = (v - SettingsPanelMinV) /
                              (SettingsPanelMaxV - SettingsPanelMinV);
            var x = normalizedX * textureWidth;
            var y = (1f - normalizedY) * textureHeight;

            var closeHovered = x >= 1315f && x <= 1385f &&
                               y >= 18f && y <= 82f;
            settingsMenuCloseHovered = closeHovered;
            for (var category = 0; category < SettingsMenuCategories.Length; category++)
            {
                var tabX = 30f + category * 268f;
                if (x >= tabX && x <= tabX + 250f && y >= 108f && y <= 168f)
                {
                    settingsMenuHoveredTab = category;
                    break;
                }
            }

            var row = 0;
            for (var index = 0; index < SettingsMenuOptions.Length; index++)
            {
                if (SettingsMenuOptions[index].Category != settingsMenuCategory)
                    continue;
                var rowY = 205f + row * 105f;
                if (x >= 35f && x <= 1365f && y >= rowY && y <= rowY + 76f)
                    settingsMenuHoveredOption = index;
                row++;
            }

            if (selectStarted)
            {
                if (closeHovered)
                {
                    SetSettingsMenuOpen(false);
                    MFN_ApplyControllerHaptic(DominantHandIndex, 0.3f, 0.05f, 0f);
                    return true;
                }
                if (settingsMenuHoveredTab >= 0)
                {
                    settingsMenuCategory = settingsMenuHoveredTab;
                    settingsMenuHoveredOption = -1;
                    settingsMenuDraggingOption = -1;
                    settingsMenuTextureDirty = true;
                    MFN_ApplyControllerHaptic(DominantHandIndex, 0.2f, 0.035f, 0f);
                }
                else if (settingsMenuHoveredOption >= 0)
                {
                    var option = SettingsMenuOptions[settingsMenuHoveredOption];
                    if (option.Toggle)
                    {
                        var value = settingsMenuValues[settingsMenuHoveredOption] >= 0.5f
                            ? 0f : 1f;
                        CommitCapturedSetting(settingsMenuHoveredOption, value);
                    }
                    else
                    {
                        settingsMenuDraggingOption = settingsMenuHoveredOption;
                        UpdateCapturedSliderValue(settingsMenuDraggingOption, x);
                    }
                    MFN_ApplyControllerHaptic(DominantHandIndex, 0.26f, 0.045f, 0f);
                }
            }

            if (settingsMenuDraggingOption >= 0)
            {
                if (selectPressed)
                    UpdateCapturedSliderValue(settingsMenuDraggingOption, x);
                else
                {
                    CommitCapturedSetting(settingsMenuDraggingOption,
                        settingsMenuValues[settingsMenuDraggingOption]);
                    settingsMenuDraggingOption = -1;
                }
            }

            if (previousTab != settingsMenuHoveredTab ||
                previousOption != settingsMenuHoveredOption ||
                previousClose != settingsMenuCloseHovered)
                settingsMenuTextureDirty = true;
            return closeHovered || settingsMenuHoveredTab >= 0 ||
                   settingsMenuHoveredOption >= 0;
        }

        private static void UpdateCapturedSliderValue(int index, float pointerX)
        {
            if (index < 0 || index >= SettingsMenuOptions.Length)
                return;
            var option = SettingsMenuOptions[index];
            if (option.Toggle)
                return;
            var normalized = Mathf.Clamp01((pointerX - 610f) / 590f);
            settingsMenuValues[index] = option.Logarithmic
                ? Mathf.Exp(Mathf.Lerp(Mathf.Log(option.Minimum),
                    Mathf.Log(option.Maximum), normalized))
                : Mathf.Lerp(option.Minimum, option.Maximum, normalized);
            settingsMenuTextureDirty = true;
        }

        private static bool ReadSettingsMenuValues()
        {
            try
            {
                // The file is the source of truth and is deliberately reread on every
                // opening. This avoids plugin load-order and Assembly.LoadFrom context
                // differences between BepInEx, Oculus OpenXR and SteamVR OpenXR.
                if (ReadSettingsMenuValuesFromFile())
                {
                    ApplyCapturedSettingsLocally();
                    settingsMenuTextureDirty = true;
                    return true;
                }
                if (settingsMenuValueReader != null)
                {
                    var provided = settingsMenuValueReader(settingsMenuValues);
                    settingsMenuTextureDirty = true;
                    return provided;
                }
                var configType = FindLoadedType("MFNVRConfig.MFNVRConfigPlugin");
                getSettingsMenuValuesMethod = getSettingsMenuValuesMethod ??
                    configType?.GetMethod("GetVrSettingsMenuValues",
                        BindingFlags.Static | BindingFlags.Public);
                var read = getSettingsMenuValuesMethod != null &&
                           getSettingsMenuValuesMethod.Invoke(null,
                               new object[] { settingsMenuValues }) is bool success && success;
                if (!read && !settingsMenuProviderWarningLogged)
                {
                    settingsMenuProviderWarningLogged = true;
                    Debug.LogWarning("MFNVR: live config values were unavailable to the " +
                                     "captured settings screen.");
                }
                settingsMenuTextureDirty = true;
                return read;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: could not read captured settings values: " +
                                 exception.Message);
                return false;
            }
        }

        private static void CommitCapturedSetting(int index, float value)
        {
            if (index < 0 || index >= SettingsMenuOptions.Length)
                return;
            settingsMenuValues[index] = value;
            try
            {
                if (WriteSettingsMenuValueToFile(index, value))
                {
                    ApplyCapturedSettingsLocally();
                    if (settingsMenuValueWriter != null)
                        settingsMenuValueWriter(index, value);
                    settingsMenuTextureDirty = true;
                    return;
                }
                if (settingsMenuValueWriter != null)
                {
                    if (!settingsMenuValueWriter(index, value))
                        Debug.LogWarning("MFNVR: captured settings change was not accepted.");
                    else
                        ReadSettingsMenuValues();
                    settingsMenuTextureDirty = true;
                    return;
                }
                var configType = FindLoadedType("MFNVRConfig.MFNVRConfigPlugin");
                setSettingsMenuValueMethod = setSettingsMenuValueMethod ??
                    configType?.GetMethod("SetVrSettingsMenuValue",
                        BindingFlags.Static | BindingFlags.Public);
                if (setSettingsMenuValueMethod == null ||
                    !(setSettingsMenuValueMethod.Invoke(null,
                        new object[] { index, value }) is bool applied) || !applied)
                    Debug.LogWarning("MFNVR: captured settings change was not accepted.");
                else
                    ReadSettingsMenuValues();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: could not apply captured settings value: " +
                                 exception.Message);
            }
            settingsMenuTextureDirty = true;
        }

        private static string GetSettingsMenuConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "BepInEx", "config", "MFNVR.cfg");
        }

        private static bool ReadSettingsMenuValuesFromFile()
        {
            var path = GetSettingsMenuConfigPath();
            if (!File.Exists(path))
                return false;
            Array.Copy(SettingsMenuDefaults, settingsMenuValues,
                SettingsMenuDefaults.Length);
            var section = string.Empty;
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;
                var key = line.Substring(0, separator).Trim();
                var textValue = line.Substring(separator + 1).Trim();
                for (var index = 0; index < SettingsMenuOptions.Length; index++)
                {
                    if (!string.Equals(section, SettingsMenuConfigSections[index],
                            StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(key, SettingsMenuConfigKeys[index],
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (SettingsMenuOptions[index].Toggle)
                    {
                        bool enabled;
                        if (bool.TryParse(textValue, out enabled))
                            settingsMenuValues[index] = enabled ? 1f : 0f;
                    }
                    else
                    {
                        float number;
                        if (float.TryParse(textValue, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out number))
                            settingsMenuValues[index] = Mathf.Clamp(number,
                                SettingsMenuOptions[index].Minimum,
                                SettingsMenuOptions[index].Maximum);
                    }
                    break;
                }
            }
            Debug.Log("MFNVR: captured settings values reloaded directly from " + path + ".");
            return true;
        }

        private static bool WriteSettingsMenuValueToFile(int index, float value)
        {
            if (index < 0 || index >= SettingsMenuOptions.Length)
                return false;
            var path = GetSettingsMenuConfigPath();
            if (!File.Exists(path))
                return false;
            var lines = new List<string>(File.ReadAllLines(path));
            var section = string.Empty;
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex].Trim();
                if (line.Length > 1 && line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                var separator = line.IndexOf('=');
                if (separator <= 0 || !string.Equals(section,
                        SettingsMenuConfigSections[index], StringComparison.OrdinalIgnoreCase))
                    continue;
                var key = line.Substring(0, separator).Trim();
                if (!string.Equals(key, SettingsMenuConfigKeys[index],
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                var serialized = SettingsMenuOptions[index].Toggle
                    ? (value >= 0.5f ? "true" : "false")
                    : Mathf.Clamp(value, SettingsMenuOptions[index].Minimum,
                            SettingsMenuOptions[index].Maximum)
                        .ToString("0.######", CultureInfo.InvariantCulture);
                lines[lineIndex] = SettingsMenuConfigKeys[index] + " = " + serialized;
                File.WriteAllLines(path, lines.ToArray());
                Debug.Log("MFNVR: wrote " + SettingsMenuConfigSections[index] + "/" +
                          SettingsMenuConfigKeys[index] + " to MFNVR.cfg.");
                return true;
            }
            return false;
        }

        private static void ApplyCapturedSettingsLocally()
        {
            ApplyUserSettings(settingsMenuValues[4] >= 0.5f,
                settingsMenuValues[5], settingsMenuValues[6], settingsMenuValues[7],
                settingsMenuValues[8], settingsMenuValues[9], settingsMenuValues[10],
                settingsMenuValues[11]);
            ApplyUiScreenSettings(settingsMenuValues[12] >= 0.5f);
            ApplyMenuPointerSettings(settingsMenuValues[13] >= 0.5f);
            ApplyInteractionCameraSettings(settingsMenuValues[14] >= 0.5f);
            ApplyPhysicalWeaponSwitchingSettings(settingsMenuValues[18] >= 0.5f);
            ApplyLeftHandedSettings(false);
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
                    // Dynamic and partially loaded assemblies can reject type queries.
                }
            }
            return null;
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
            var hitCount = Physics.RaycastNonAlloc(ray, flatMenuPointerHits, 30f, mask,
                QueryTriggerInteraction.Collide);
            Interactable closestTarget = null;
            var closestDistance = float.MaxValue;
            for (var hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                var hit = flatMenuPointerHits[hitIndex];
                if (hit.collider == null)
                    continue;
                var target = hit.collider.GetComponent<Interactable>();
                if (target == null)
                    target = hit.collider.GetComponentInParent<Interactable>();
                if (target == null || hit.distance >= closestDistance)
                    continue;
                closestTarget = target;
                closestDistance = hit.distance;
            }
            return closestTarget;
        }

        private static void ResetFlatMenuPointerInteraction()
        {
            if (!flatMenuPointerActive && flatMenuPointerHoveredInteractable == null &&
                !flatMenuSliderCaptureActive)
                return;
            ReleaseFlatMenuSlider();
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
            previousFlatMenuLeftTriggerPressed = false;
            flatMenuPointerFrame = -1;
            flatMenuPointerMode = -1;
            menuSettingsButtonHovered = false;
            menuPointerInputActive = false;
            SetMenuPointerVisualLayer(GetCachedLayer(ref defaultLayer, "Default", 0));
            SetInventoryPointerVisible(false);
            if (menuRightHandVisualRoot != null &&
                menuRightHandVisualRoot.gameObject.activeSelf)
                menuRightHandVisualRoot.gameObject.SetActive(false);
        }

        private static Menus.MenuBarNub ResolveFlatMenuSlider(Interactable target)
        {
            var nub = target as Menus.MenuBarNub;
            if (nub != null)
                return nub;
            var bar = target as Menus.MenuSelectionBar;
            return bar != null
                ? menuSelectionBarNubField?.GetValue(bar) as Menus.MenuBarNub
                : null;
        }

        private static bool IsFlatMenuSliderPickedUp(Menus.MenuBarNub slider)
        {
            if (slider == null || menuBarNubPickedUpField == null)
                return slider != null;
            try
            {
                return menuBarNubPickedUpField.GetValue(slider) is bool pickedUp && pickedUp;
            }
            catch
            {
                return true;
            }
        }

        private static void UpdateFlatMenuSliderDrag(Player player, Camera source,
            Vector2 pointerScreenPosition)
        {
            var slider = flatMenuGrabbedSlider;
            if (!flatMenuSliderCaptureActive || slider == null)
                return;
            try
            {
                var leftExtent = menuBarNubLeftExtentField?.GetValue(slider) as Transform;
                var rightExtent = menuBarNubRightExtentField?.GetValue(slider) as Transform;
                var targetCamera = player != null ? player.GetHUDCamera() : null;
                if (targetCamera == null)
                    targetCamera = source;
                if (leftExtent == null || rightExtent == null || targetCamera == null)
                    return;

                // Use the same camera and pixel rectangle as pointer hit testing. This
                // avoids MenuBarNub.GetCursorPos(), whose HUD/mouse coordinate path can
                // select the joystick cursor while a VR controller owns the slider.
                var targetRect = targetCamera.pixelRect;
                var screenWidth = Mathf.Max(1f, Screen.width);
                var normalizedX = Mathf.Clamp01(pointerScreenPosition.x / screenWidth);
                var pointerX = targetRect.x + normalizedX * targetRect.width;
                var leftX = targetCamera.WorldToScreenPoint(leftExtent.position).x;
                var rightX = targetCamera.WorldToScreenPoint(rightExtent.position).x;
                var span = rightX - leftX;
                if (Mathf.Abs(span) < 0.001f)
                    return;

                var amount = Mathf.Clamp01((pointerX - leftX) / span);
                var localPosition = slider.transform.localPosition;
                localPosition.x = Mathf.Lerp(leftExtent.localPosition.x,
                    rightExtent.localPosition.x, amount);
                slider.transform.localPosition = localPosition;

                // Preserve MFN's normal 40 Hz live-setting update cadence. PutDown()
                // commits one final value when the controller button is released.
                if (Time.unscaledTime >= nextFlatMenuSliderValueUpdateTime)
                {
                    nextFlatMenuSliderValueUpdateTime = Time.unscaledTime + 0.025f;
                    slider.UpdateValue();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR slider drag update failed: " +
                    exception.Message);
                ReleaseFlatMenuSlider();
            }
        }

        private static void AcquireFlatMenuSlider(Menus.MenuBarNub slider)
        {
            if (slider == null)
                return;
            flatMenuGrabbedSlider = slider;
            flatMenuSliderCaptureActive = true;
            flatMenuSliderUsedVanillaPickup = false;
            nextFlatMenuSliderValueUpdateTime = 0f;
            try
            {
                if (menuBarNubPickedUpField != null)
                {
                    // MenuBarNub.PickUp disables MenuController globally. That is safe
                    // for MFN's mouse flow, but it stops the pause-menu VR pointer from
                    // receiving further drag frames. VR owns only this nub instead.
                    menuBarNubPickedUpField.SetValue(slider, true);
                    Player.current?.PlayMenuPickUp();
                    return;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR direct slider pickup failed; using vanilla: " +
                    exception.Message);
            }

            flatMenuSliderUsedVanillaPickup = true;
            slider.PickUp();
        }

        internal static bool OwnsFlatMenuSlider(Menus.MenuBarNub slider)
        {
            return flatMenuSliderCaptureActive &&
                ReferenceEquals(flatMenuGrabbedSlider, slider);
        }

        private static void ReleaseFlatMenuSlider()
        {
            if (!flatMenuSliderCaptureActive)
                return;
            var slider = flatMenuGrabbedSlider;
            var usedVanillaPickup = flatMenuSliderUsedVanillaPickup;
            flatMenuGrabbedSlider = null;
            flatMenuSliderCaptureActive = false;
            flatMenuSliderUsedVanillaPickup = false;
            nextFlatMenuSliderValueUpdateTime = 0f;
            try
            {
                if (slider != null && IsFlatMenuSliderPickedUp(slider))
                {
                    if (usedVanillaPickup)
                        slider.PutDown(default(InputAction.CallbackContext));
                    else
                    {
                        menuBarNubPickedUpField?.SetValue(slider, false);
                        slider.UpdateValue();
                        Player.current?.PlayMenuPutDown();
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR slider release recovered safely: " +
                    exception.Message);
            }
            finally
            {
                // PickUp disables MFN's entire MenuController. Always restore it when
                // the VR-owned drag ends, even if the knob was destroyed with its menu.
                Menus.MenuController.disabled = false;
            }
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

            var cameraId = camera.GetInstanceID();
            Camera cachedCamera;
            if (!menuEyesWithEffectsDisabled.TryGetValue(cameraId, out cachedCamera) ||
                !ReferenceEquals(cachedCamera, camera))
            {
                foreach (var behaviour in camera.GetComponents<Behaviour>())
                {
                    if (behaviour != null && behaviour != camera)
                        behaviour.enabled = false;
                }
                menuEyesWithEffectsDisabled[cameraId] = camera;
                camerasWithRenderEffectsEnabled.Remove(cameraId);
            }
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
                if (settingsMenuOpen)
                    DrawCapturedSettingsMenu();
                else if (menuSettingsButtonVisible)
                    DrawMenuSettingsButton();
            }
            finally
            {
                source.targetTexture = oldTarget;
                source.useOcclusionCulling = oldOcclusion;
                if (menuScreen != null)
                    menuScreen.SetActive(screenWasActive);
            }
        }

        private static void DrawMenuSettingsButton()
        {
            if (menuCapture == null)
                return;
            var previous = RenderTexture.active;
            var matrixPushed = false;
            try
            {
                EnsureMenuSettingsButtonTextures();
                var texture = menuSettingsButtonHovered
                    ? menuSettingsButtonHoverTexture
                    : menuSettingsButtonTexture;
                if (texture == null)
                    return;
                RenderTexture.active = menuCapture;
                GL.PushMatrix();
                matrixPushed = true;
                GL.LoadPixelMatrix(0f, menuCapture.width, 0f, menuCapture.height);
                var rectangle = new Rect(
                    MenuSettingsButtonMinU * menuCapture.width,
                    MenuSettingsButtonMinV * menuCapture.height,
                    (MenuSettingsButtonMaxU - MenuSettingsButtonMinU) *
                        menuCapture.width,
                    (MenuSettingsButtonMaxV - MenuSettingsButtonMinV) *
                        menuCapture.height);
                Graphics.DrawTexture(rectangle, texture);
                GL.PopMatrix();
                matrixPushed = false;
                menuSettingsButtonWarningLogged = false;
            }
            catch (Exception exception)
            {
                if (!menuSettingsButtonWarningLogged)
                {
                    menuSettingsButtonWarningLogged = true;
                    Debug.LogWarning("MFNVR: could not composite the VR Settings " +
                                     "button: " + exception);
                }
            }
            finally
            {
                if (matrixPushed)
                    GL.PopMatrix();
                RenderTexture.active = previous;
            }
        }

        private static void DrawCapturedSettingsMenu()
        {
            if (menuCapture == null)
                return;
            var previous = RenderTexture.active;
            var matrixPushed = false;
            try
            {
                EnsureCapturedSettingsTexture();
                if (settingsMenuTexture == null)
                    return;
                RenderTexture.active = menuCapture;
                GL.PushMatrix();
                matrixPushed = true;
                GL.LoadPixelMatrix(0f, menuCapture.width, 0f, menuCapture.height);
                var rectangle = new Rect(
                    SettingsPanelMinU * menuCapture.width,
                    SettingsPanelMinV * menuCapture.height,
                    (SettingsPanelMaxU - SettingsPanelMinU) * menuCapture.width,
                    (SettingsPanelMaxV - SettingsPanelMinV) * menuCapture.height);
                Graphics.DrawTexture(rectangle, settingsMenuTexture);
                GL.PopMatrix();
                matrixPushed = false;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MFNVR: could not draw captured settings screen: " +
                                 exception);
            }
            finally
            {
                if (matrixPushed)
                    GL.PopMatrix();
                RenderTexture.active = previous;
            }
        }

        private static void EnsureCapturedSettingsTexture()
        {
            if (settingsMenuTexture != null && !settingsMenuTextureDirty)
                return;
            var replacement = CreateCapturedSettingsTexture();
            if (replacement == null)
                return;
            if (settingsMenuTexture != null)
                UnityEngine.Object.Destroy(settingsMenuTexture);
            settingsMenuTexture = replacement;
            settingsMenuTextureDirty = false;
        }

        private static Texture2D CreateCapturedSettingsTexture()
        {
            const int width = 1400;
            const int height = 900;
            using (var bitmap = new System.Drawing.Bitmap(width, height,
                       System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            using (var titleFont = new System.Drawing.Font("Arial", 38f,
                       System.Drawing.FontStyle.Bold,
                       System.Drawing.GraphicsUnit.Pixel))
            using (var tabFont = new System.Drawing.Font("Arial", 22f,
                       System.Drawing.FontStyle.Bold,
                       System.Drawing.GraphicsUnit.Pixel))
            using (var rowFont = new System.Drawing.Font("Arial", 27f,
                       System.Drawing.FontStyle.Bold,
                       System.Drawing.GraphicsUnit.Pixel))
            using (var descriptionFont = new System.Drawing.Font("Arial", 17f,
                       System.Drawing.FontStyle.Regular,
                       System.Drawing.GraphicsUnit.Pixel))
            using (var valueFont = new System.Drawing.Font("Arial", 23f,
                       System.Drawing.FontStyle.Regular,
                       System.Drawing.GraphicsUnit.Pixel))
            using (var hintFont = new System.Drawing.Font("Arial", 20f,
                       System.Drawing.FontStyle.Regular,
                       System.Drawing.GraphicsUnit.Pixel))
            using (var stream = new MemoryStream())
            {
                graphics.SmoothingMode =
                    System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint =
                    System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(System.Drawing.Color.FromArgb(252, 8, 12, 20));
                using (var header = new System.Drawing.SolidBrush(
                           System.Drawing.Color.FromArgb(255, 26, 54, 94)))
                    graphics.FillRectangle(header, 0, 0, width, 95);
                using (var foreground = new System.Drawing.SolidBrush(
                           System.Drawing.Color.White))
                {
                    graphics.DrawString("MFNVR SETTINGS", titleFont, foreground,
                        new System.Drawing.PointF(34f, 24f));
                    graphics.DrawString(
                        "Point with the dominant controller  |  Dominant trigger or primary selects  |  Hold L3 for 2 seconds to close",
                        hintFont, foreground, new System.Drawing.PointF(430f, 38f));
                }

                using (var closeBrush = new System.Drawing.SolidBrush(
                           settingsMenuCloseHovered
                               ? System.Drawing.Color.FromArgb(255, 205, 58, 62)
                               : System.Drawing.Color.FromArgb(255, 112, 38, 44)))
                using (var foreground = new System.Drawing.SolidBrush(
                           System.Drawing.Color.White))
                {
                    graphics.FillRectangle(closeBrush, 1315, 18, 70, 64);
                    graphics.DrawString("X", titleFont, foreground,
                        new System.Drawing.RectangleF(1315, 20, 70, 58),
                        SettingsCenteredFormat);
                }

                for (var category = 0; category < SettingsMenuCategories.Length;
                     category++)
                {
                    var x = 30 + category * 268;
                    var selected = category == settingsMenuCategory;
                    var hovered = category == settingsMenuHoveredTab;
                    using (var brush = new System.Drawing.SolidBrush(selected
                               ? System.Drawing.Color.FromArgb(255, 42, 103, 184)
                               : hovered
                                   ? System.Drawing.Color.FromArgb(255, 42, 65, 101)
                                   : System.Drawing.Color.FromArgb(255, 24, 31, 45)))
                    using (var foreground = new System.Drawing.SolidBrush(
                               System.Drawing.Color.White))
                    {
                        graphics.FillRectangle(brush, x, 108, 250, 60);
                        graphics.DrawString(SettingsMenuCategories[category], tabFont,
                            foreground, new System.Drawing.RectangleF(x, 108, 250, 60),
                            SettingsCenteredFormat);
                    }
                }

                var row = 0;
                for (var index = 0; index < SettingsMenuOptions.Length; index++)
                {
                    var option = SettingsMenuOptions[index];
                    if (option.Category != settingsMenuCategory)
                        continue;
                    var y = 205 + row * 105;
                    var hovered = index == settingsMenuHoveredOption;
                    using (var rowBrush = new System.Drawing.SolidBrush(hovered
                               ? System.Drawing.Color.FromArgb(255, 34, 54, 83)
                               : System.Drawing.Color.FromArgb(248, 18, 25, 38)))
                    using (var foreground = new System.Drawing.SolidBrush(
                                System.Drawing.Color.White))
                    {
                        graphics.FillRectangle(rowBrush, 35, y, 1330, 76);
                        if (string.IsNullOrEmpty(option.Description))
                        {
                            graphics.DrawString(option.Label, rowFont, foreground,
                                new System.Drawing.PointF(62f, y + 21f));
                        }
                        else
                        {
                            graphics.DrawString(option.Label, rowFont, foreground,
                                new System.Drawing.PointF(62f, y + 7f));
                            using (var descriptionBrush = new System.Drawing.SolidBrush(
                                       option.DescriptionIsWarning
                                           ? System.Drawing.Color.FromArgb(255, 255, 190, 64)
                                           : System.Drawing.Color.FromArgb(255, 167, 183, 207)))
                                graphics.DrawString(option.Description, descriptionFont,
                                    descriptionBrush,
                                    new System.Drawing.RectangleF(64f, y + 42f, 520f, 26f));
                        }
                    }
                    DrawCapturedSettingsControl(graphics, valueFont, option, index, y);
                    row++;
                }

                bitmap.RotateFlip(System.Drawing.RotateFlipType.RotateNoneFlipY);
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    name = "MFNVR Captured Settings Screen",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.LoadImage(stream.ToArray(), true);
                return texture;
            }
        }

        private static void DrawCapturedSettingsControl(System.Drawing.Graphics graphics,
            System.Drawing.Font valueFont, SettingsMenuOption option, int index, int y)
        {
            var value = settingsMenuValues[index];
            if (option.Toggle)
            {
                var enabled = value >= 0.5f;
                using (var box = new System.Drawing.SolidBrush(enabled
                           ? System.Drawing.Color.FromArgb(255, 46, 145, 235)
                           : System.Drawing.Color.FromArgb(255, 24, 31, 45)))
                using (var foreground = new System.Drawing.SolidBrush(
                           System.Drawing.Color.White))
                {
                    graphics.FillRectangle(box, 1240, y + 14, 48, 48);
                    if (enabled)
                        graphics.DrawString("X", valueFont, foreground,
                            new System.Drawing.RectangleF(1240, y + 14, 48, 48),
                            SettingsCenteredFormat);
                }
                return;
            }

            var normalized = option.Logarithmic
                ? Mathf.InverseLerp(Mathf.Log(option.Minimum),
                    Mathf.Log(option.Maximum),
                    Mathf.Log(Mathf.Max(option.Minimum, value)))
                : Mathf.InverseLerp(option.Minimum, option.Maximum, value);
            using (var track = new System.Drawing.SolidBrush(
                       System.Drawing.Color.FromArgb(255, 20, 29, 44)))
            using (var fill = new System.Drawing.SolidBrush(
                       System.Drawing.Color.FromArgb(255, 53, 128, 226)))
            using (var foreground = new System.Drawing.SolidBrush(
                       System.Drawing.Color.White))
            {
                graphics.FillRectangle(track, 610, y + 27, 590, 24);
                graphics.FillRectangle(fill, 610, y + 27,
                    Math.Max(2, (int)(590f * normalized)), 24);
                var span = option.Maximum - option.Minimum;
                var text = span > 50f ? value.ToString("0") :
                    span < 0.1f ? value.ToString("0.000") : value.ToString("0.00");
                graphics.DrawString(text, valueFont, foreground,
                    new System.Drawing.RectangleF(1210, y + 14, 130, 48),
                    SettingsCenteredFormat);
            }
        }

        private static void EnsureMenuSettingsButtonTextures()
        {
            if (menuSettingsButtonTexture != null &&
                menuSettingsButtonHoverTexture != null)
                return;
            menuSettingsButtonTexture = CreateMenuSettingsButtonTexture(false);
            menuSettingsButtonHoverTexture = CreateMenuSettingsButtonTexture(true);
        }

        private static Texture2D CreateMenuSettingsButtonTexture(bool hovered)
        {
            const int width = 736;
            const int height = 144;
            using (var bitmap = new System.Drawing.Bitmap(width, height,
                       System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            using (var background = new System.Drawing.SolidBrush(hovered
                       ? System.Drawing.Color.FromArgb(250, 31, 173, 235)
                       : System.Drawing.Color.FromArgb(246, 14, 19, 28)))
            using (var border = new System.Drawing.Pen(
                       System.Drawing.Color.FromArgb(255, 245, 194, 30), 7f))
            using (var foreground = new System.Drawing.SolidBrush(
                       System.Drawing.Color.White))
            using (var font = new System.Drawing.Font("Arial", 54f,
                       System.Drawing.FontStyle.Bold,
                       System.Drawing.GraphicsUnit.Pixel))
            using (var format = new System.Drawing.StringFormat
                   {
                       Alignment = System.Drawing.StringAlignment.Center,
                       LineAlignment = System.Drawing.StringAlignment.Center
                   })
            using (var stream = new MemoryStream())
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint =
                    System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(System.Drawing.Color.Transparent);
                graphics.FillRectangle(background, 3f, 3f, width - 6f, height - 6f);
                graphics.DrawRectangle(border, 4f, 4f, width - 8f, height - 8f);
                graphics.DrawString("VR SETTINGS", font, foreground,
                    new System.Drawing.RectangleF(0f, 0f, width, height), format);
                // System.Drawing uses a top-left bitmap origin while this render-texture
                // pixel matrix uses a bottom-left origin. Flip only vertically; rotating
                // 180 degrees also mirrors the lettering horizontally.
                bitmap.RotateFlip(System.Drawing.RotateFlipType.RotateNoneFlipY);
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    name = hovered
                        ? "MFNVR Menu Settings Button Hover"
                        : "MFNVR Menu Settings Button",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.LoadImage(stream.ToArray(), true);
                return texture;
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

                var renderInventory = inventoryCamera != null && inventoryCamera != source &&
                    inventoryCamera.enabled && inventoryCamera.gameObject.activeInHierarchy;
                var renderCursor = cursorCamera != null && cursorCamera != source &&
                    cursorCamera != inventoryCamera && cursorCamera.enabled &&
                    cursorCamera.gameObject.activeInHierarchy;
                if (renderInventory && renderCursor && cursorCamera.depth < inventoryCamera.depth)
                {
                    RenderCameraIntoMenuCapture(cursorCamera);
                    RenderCameraIntoMenuCapture(inventoryCamera);
                }
                else
                {
                    if (renderInventory)
                        RenderCameraIntoMenuCapture(inventoryCamera);
                    if (renderCursor)
                        RenderCameraIntoMenuCapture(cursorCamera);
                }

                if (renderInventory || renderCursor)
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
            if (menuSettingsButtonTexture != null)
                UnityEngine.Object.Destroy(menuSettingsButtonTexture);
            if (menuSettingsButtonHoverTexture != null)
                UnityEngine.Object.Destroy(menuSettingsButtonHoverTexture);
            if (settingsMenuTexture != null)
                UnityEngine.Object.Destroy(settingsMenuTexture);
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
            menuSettingsButtonTexture = null;
            menuSettingsButtonHoverTexture = null;
            settingsMenuTexture = null;
            settingsMenuTextureDirty = true;
            menuSettingsButtonHovered = false;
            menuSettingsButtonVisible = false;
            menuSettingsButtonWarningLogged = false;
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

    [HarmonyPatch(typeof(Menus.MenuBarNub), nameof(Menus.MenuBarNub.Update))]
    internal static class VrFlatMenuSliderUpdatePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Menus.MenuBarNub __instance)
        {
            // While a VR controller owns this nub, RenderBridge updates it from the
            // pointer ray. Suppress the vanilla HUD-camera/mouse calculation so it
            // cannot overwrite that position with the joystick cursor.
            return !RenderBridge.OwnsFlatMenuSlider(__instance);
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
            RenderBridge.InvalidateToolboxVisualCache();
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
        private static bool Prefix(ref Vector3 direction, Player fromPlayer,
            ref Vector3 changeToCollisionAtThisPoint)
        {
            if (RenderBridge.IsSettingsMenuOpen() && fromPlayer != null)
                return false;
            RenderBridge.OverridePlayerProjectile(ref direction, fromPlayer,
                ref changeToCollisionAtThisPoint);
            return true;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UseItem))]
    internal static class VrSettingsSuppressUseItemPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !RenderBridge.IsSettingsMenuOpen();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.CancelItem))]
    internal static class VrSettingsSuppressCancelItemPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !RenderBridge.IsSettingsMenuOpen();
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
