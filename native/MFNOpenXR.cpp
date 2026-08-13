#include <d3d11.h>
#include <d3dcompiler.h>
#include <algorithm>
#include <atomic>
#include <vector>
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D11.h"
#include <openxr/openxr.h>
#define XR_USE_GRAPHICS_API_D3D11
#include <openxr/openxr_platform.h>

#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <utility>
#include <vector>

namespace
{
    IUnityInterfaces* g_unityInterfaces = nullptr;
    IUnityGraphicsD3D11* g_d3d11 = nullptr;
    XrInstance g_instance = XR_NULL_HANDLE;
    XrSession g_session = XR_NULL_HANDLE;
    XrSystemId g_systemId = XR_NULL_SYSTEM_ID;
    XrSpace g_localSpace = XR_NULL_HANDLE;
    XrSessionState g_sessionState = XR_SESSION_STATE_UNKNOWN;
    bool g_sessionRunning = false;
    struct EyeSwapchain
    {
        XrSwapchain handle = XR_NULL_HANDLE;
        int32_t width = 0;
        int32_t height = 0;
        std::vector<XrSwapchainImageD3D11KHR> images;
        std::vector<ID3D11RenderTargetView*> renderTargets;
    };
    std::vector<EyeSwapchain> g_eyeSwapchains;
    ID3D11Texture2D* g_sourceTextures[2] = {nullptr, nullptr};
    ID3D11Texture2D* g_sourceViewTextures[2] = {nullptr, nullptr};
    ID3D11ShaderResourceView* g_sourceViews[2] = {nullptr, nullptr};
    ID3D11VertexShader* g_flipVertexShader = nullptr;
    ID3D11PixelShader* g_flipPixelShader = nullptr;
    ID3D11SamplerState* g_flipSampler = nullptr;
    ID3D11RasterizerState* g_flipRasterizer = nullptr;
    ID3D11DepthStencilState* g_flipDepthState = nullptr;
    ID3D11BlendState* g_flipBlendState = nullptr;
    struct FlipIntermediate
    {
        ID3D11Texture2D* sampleSource = nullptr;
        ID3D11Texture2D* renderTarget = nullptr;
        UINT width = 0;
        UINT height = 0;
        DXGI_FORMAT format = DXGI_FORMAT_UNKNOWN;
    };
    FlipIntermediate g_flipIntermediate[2];
    std::atomic<int> g_flipPath{0};
    struct CpuFlipResources
    {
        ID3D11Texture2D* readback = nullptr;
        D3D11_TEXTURE2D_DESC description{};
    };
    CpuFlipResources g_cpuFlip[2];

    XrQuaternionf RollViewBy180Degrees(const XrQuaternionf& orientation)
    {
        // Multiply the OpenXR view orientation by a 180-degree roll (0, 0, 1, 0).
        return {orientation.y, -orientation.x, orientation.w, -orientation.z};
    }
    char g_status[512] = "Native bridge loaded; waiting for Unity's D3D11 render device.";
    std::mutex g_statusMutex;
    std::mutex g_headPoseMutex;
    XrQuaternionf g_headOrientation{0.f, 0.f, 0.f, 1.f};
    XrView g_latestViews[2] = {{XR_TYPE_VIEW}, {XR_TYPE_VIEW}};
    bool g_hasHeadOrientation = false;

    XrActionSet g_touchActionSet = XR_NULL_HANDLE;
    XrAction g_gripPoseAction = XR_NULL_HANDLE;
    XrAction g_aimPoseAction = XR_NULL_HANDLE;
    XrAction g_triggerAction = XR_NULL_HANDLE;
    XrAction g_triggerClickAction = XR_NULL_HANDLE;
    XrAction g_squeezeAction = XR_NULL_HANDLE;
    XrAction g_squeezeClickAction = XR_NULL_HANDLE;
    XrAction g_thumbstickAction = XR_NULL_HANDLE;
    XrAction g_primaryButtonAction = XR_NULL_HANDLE;
    XrAction g_secondaryButtonAction = XR_NULL_HANDLE;
    XrAction g_thumbstickClickAction = XR_NULL_HANDLE;
    XrAction g_menuButtonAction = XR_NULL_HANDLE;
    XrAction g_hapticAction = XR_NULL_HANDLE;
    XrPath g_handPaths[2] = {XR_NULL_PATH, XR_NULL_PATH};
    XrSpace g_gripSpaces[2] = {XR_NULL_HANDLE, XR_NULL_HANDLE};
    XrSpace g_aimSpaces[2] = {XR_NULL_HANDLE, XR_NULL_HANDLE};
    bool g_touchActionsAttached = false;
    uint32_t g_touchAttachRetryCountdown = 0;
    struct ControllerState
    {
        bool gripPoseValid = false;
        bool aimPoseValid = false;
        XrPosef gripPose{{0.f, 0.f, 0.f, 1.f}, {0.f, 0.f, 0.f}};
        XrPosef aimPose{{0.f, 0.f, 0.f, 1.f}, {0.f, 0.f, 0.f}};
        XrVector2f thumbstick{0.f, 0.f};
        float trigger = 0.f;
        float squeeze = 0.f;
        bool primary = false;
        bool secondary = false;
        bool thumbstickClick = false;
        bool menu = false;
    };
    ControllerState g_controllerStates[2];
    std::mutex g_controllerMutex;
    char g_runtimeName[XR_MAX_RUNTIME_NAME_SIZE] = "unknown OpenXR runtime";

    DXGI_FORMAT TypedColorFormat(DXGI_FORMAT format);
    void SetStatus(const char* format, ...);

    bool AttachTouchActions()
    {
        if (g_session == XR_NULL_HANDLE || g_touchActionSet == XR_NULL_HANDLE)
            return false;

        if (!g_touchActionsAttached)
        {
            XrSessionActionSetsAttachInfo attach{XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO};
            attach.countActionSets = 1;
            attach.actionSets = &g_touchActionSet;
            const XrResult attachResult = xrAttachSessionActionSets(g_session, &attach);
            if (XR_FAILED(attachResult))
            {
                SetStatus("OpenXR controller action attachment is waiting for the runtime: %d",
                    attachResult);
                return false;
            }
            g_touchActionsAttached = true;
        }

        bool spacesReady = true;
        for (int hand = 0; hand < 2; ++hand)
        {
            XrActionSpaceCreateInfo spaceInfo{XR_TYPE_ACTION_SPACE_CREATE_INFO};
            spaceInfo.poseInActionSpace.orientation.w = 1.f;
            spaceInfo.subactionPath = g_handPaths[hand];
            if (g_gripSpaces[hand] == XR_NULL_HANDLE)
            {
                spaceInfo.action = g_gripPoseAction;
                if (XR_FAILED(xrCreateActionSpace(g_session, &spaceInfo, &g_gripSpaces[hand])))
                    spacesReady = false;
            }
            if (g_aimSpaces[hand] == XR_NULL_HANDLE)
            {
                spaceInfo.action = g_aimPoseAction;
                if (XR_FAILED(xrCreateActionSpace(g_session, &spaceInfo, &g_aimSpaces[hand])))
                    spacesReady = false;
            }
        }

        if (spacesReady)
            SetStatus("OpenXR controller actions attached after runtime focus via %s.",
                g_runtimeName);
        return spacesReady;
    }

    bool CreateTouchActions()
    {
        if (g_touchActionSet != XR_NULL_HANDLE)
            return true;

        xrStringToPath(g_instance, "/user/hand/left", &g_handPaths[0]);
        xrStringToPath(g_instance, "/user/hand/right", &g_handPaths[1]);

        XrActionSetCreateInfo setInfo{XR_TYPE_ACTION_SET_CREATE_INFO};
        strcpy_s(setInfo.actionSetName, "mfn_touch");
        strcpy_s(setInfo.localizedActionSetName, "MFN Touch Controls");
        setInfo.priority = 0;
        if (XR_FAILED(xrCreateActionSet(g_instance, &setInfo, &g_touchActionSet)))
            return false;

        auto createAction = [](XrActionType type, const char* name, const char* localized, XrAction* action)
        {
            XrActionCreateInfo info{XR_TYPE_ACTION_CREATE_INFO};
            info.actionType = type;
            strcpy_s(info.actionName, name);
            strcpy_s(info.localizedActionName, localized);
            info.countSubactionPaths = 2;
            info.subactionPaths = g_handPaths;
            return XR_SUCCEEDED(xrCreateAction(g_touchActionSet, &info, action));
        };

        if (!createAction(XR_ACTION_TYPE_POSE_INPUT, "grip_pose", "Grip Pose", &g_gripPoseAction) ||
            !createAction(XR_ACTION_TYPE_POSE_INPUT, "aim_pose", "Aim Pose", &g_aimPoseAction) ||
            !createAction(XR_ACTION_TYPE_FLOAT_INPUT, "trigger", "Trigger", &g_triggerAction) ||
            !createAction(XR_ACTION_TYPE_BOOLEAN_INPUT, "trigger_click", "Trigger Click", &g_triggerClickAction) ||
            !createAction(XR_ACTION_TYPE_FLOAT_INPUT, "squeeze", "Grip", &g_squeezeAction) ||
            !createAction(XR_ACTION_TYPE_BOOLEAN_INPUT, "squeeze_click", "Grip Click", &g_squeezeClickAction) ||
            !createAction(XR_ACTION_TYPE_VECTOR2F_INPUT, "thumbstick", "Thumbstick", &g_thumbstickAction) ||
            !createAction(XR_ACTION_TYPE_BOOLEAN_INPUT, "primary", "Primary Button", &g_primaryButtonAction) ||
            !createAction(XR_ACTION_TYPE_BOOLEAN_INPUT, "secondary", "Secondary Button", &g_secondaryButtonAction) ||
            !createAction(XR_ACTION_TYPE_BOOLEAN_INPUT, "stick_click", "Stick Click", &g_thumbstickClickAction) ||
            !createAction(XR_ACTION_TYPE_BOOLEAN_INPUT, "menu", "Menu Button", &g_menuButtonAction) ||
            !createAction(XR_ACTION_TYPE_VIBRATION_OUTPUT, "haptic", "Haptic", &g_hapticAction))
            return false;

        auto suggestProfile = [&](const char* profileName,
            const std::vector<std::pair<XrAction, const char*>>& mappings)
        {
            std::vector<XrActionSuggestedBinding> bindings;
            for (const auto& mapping : mappings)
            {
                XrPath bindingPath = XR_NULL_PATH;
                if (XR_SUCCEEDED(xrStringToPath(g_instance, mapping.second, &bindingPath)))
                    bindings.push_back({mapping.first, bindingPath});
            }
            XrPath profile = XR_NULL_PATH;
            if (XR_FAILED(xrStringToPath(g_instance, profileName, &profile)) ||
                profile == XR_NULL_PATH || bindings.empty())
                return false;
            XrInteractionProfileSuggestedBinding suggested{
                XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING};
            suggested.interactionProfile = profile;
            suggested.countSuggestedBindings = static_cast<uint32_t>(bindings.size());
            suggested.suggestedBindings = bindings.data();
            return XR_SUCCEEDED(xrSuggestInteractionProfileBindings(g_instance, &suggested));
        };

        // Keep the complete, already-tested Oculus Touch mapping unchanged.
        const bool oculusSuggested = suggestProfile(
            "/interaction_profiles/oculus/touch_controller", {
                {g_gripPoseAction, "/user/hand/left/input/grip/pose"},
                {g_gripPoseAction, "/user/hand/right/input/grip/pose"},
                {g_aimPoseAction, "/user/hand/left/input/aim/pose"},
                {g_aimPoseAction, "/user/hand/right/input/aim/pose"},
                {g_triggerAction, "/user/hand/left/input/trigger/value"},
                {g_triggerAction, "/user/hand/right/input/trigger/value"},
                {g_squeezeAction, "/user/hand/left/input/squeeze/value"},
                {g_squeezeAction, "/user/hand/right/input/squeeze/value"},
                {g_thumbstickAction, "/user/hand/left/input/thumbstick"},
                {g_thumbstickAction, "/user/hand/right/input/thumbstick"},
                {g_primaryButtonAction, "/user/hand/left/input/x/click"},
                {g_primaryButtonAction, "/user/hand/right/input/a/click"},
                {g_secondaryButtonAction, "/user/hand/left/input/y/click"},
                {g_secondaryButtonAction, "/user/hand/right/input/b/click"},
                {g_thumbstickClickAction, "/user/hand/left/input/thumbstick/click"},
                {g_thumbstickClickAction, "/user/hand/right/input/thumbstick/click"},
                {g_menuButtonAction, "/user/hand/left/input/menu/click"},
                {g_hapticAction, "/user/hand/left/output/haptic"},
                {g_hapticAction, "/user/hand/right/output/haptic"}
            });

        const bool indexSuggested = suggestProfile(
            "/interaction_profiles/valve/index_controller", {
                {g_gripPoseAction, "/user/hand/left/input/grip/pose"},
                {g_gripPoseAction, "/user/hand/right/input/grip/pose"},
                {g_aimPoseAction, "/user/hand/left/input/aim/pose"},
                {g_aimPoseAction, "/user/hand/right/input/aim/pose"},
                {g_triggerAction, "/user/hand/left/input/trigger/value"},
                {g_triggerAction, "/user/hand/right/input/trigger/value"},
                {g_squeezeAction, "/user/hand/left/input/squeeze/value"},
                {g_squeezeAction, "/user/hand/right/input/squeeze/value"},
                {g_thumbstickAction, "/user/hand/left/input/thumbstick"},
                {g_thumbstickAction, "/user/hand/right/input/thumbstick"},
                {g_primaryButtonAction, "/user/hand/left/input/a/click"},
                {g_primaryButtonAction, "/user/hand/right/input/a/click"},
                {g_secondaryButtonAction, "/user/hand/left/input/b/click"},
                {g_secondaryButtonAction, "/user/hand/right/input/b/click"},
                {g_thumbstickClickAction, "/user/hand/left/input/thumbstick/click"},
                {g_thumbstickClickAction, "/user/hand/right/input/thumbstick/click"},
                {g_menuButtonAction, "/user/hand/left/input/system/click"},
                {g_menuButtonAction, "/user/hand/right/input/system/click"},
                {g_hapticAction, "/user/hand/left/output/haptic"},
                {g_hapticAction, "/user/hand/right/output/haptic"}
            });

        const bool viveSuggested = suggestProfile(
            "/interaction_profiles/htc/vive_controller", {
                {g_gripPoseAction, "/user/hand/left/input/grip/pose"},
                {g_gripPoseAction, "/user/hand/right/input/grip/pose"},
                {g_aimPoseAction, "/user/hand/left/input/aim/pose"},
                {g_aimPoseAction, "/user/hand/right/input/aim/pose"},
                {g_triggerAction, "/user/hand/left/input/trigger/value"},
                {g_triggerAction, "/user/hand/right/input/trigger/value"},
                {g_squeezeClickAction, "/user/hand/left/input/squeeze/click"},
                {g_squeezeClickAction, "/user/hand/right/input/squeeze/click"},
                {g_thumbstickAction, "/user/hand/left/input/trackpad"},
                {g_thumbstickAction, "/user/hand/right/input/trackpad"},
                {g_primaryButtonAction, "/user/hand/left/input/trackpad/click"},
                {g_primaryButtonAction, "/user/hand/right/input/trackpad/click"},
                {g_secondaryButtonAction, "/user/hand/left/input/menu/click"},
                {g_secondaryButtonAction, "/user/hand/right/input/menu/click"},
                {g_menuButtonAction, "/user/hand/left/input/menu/click"},
                {g_menuButtonAction, "/user/hand/right/input/menu/click"},
                {g_hapticAction, "/user/hand/left/output/haptic"},
                {g_hapticAction, "/user/hand/right/output/haptic"}
            });

        const bool microsoftSuggested = suggestProfile(
            "/interaction_profiles/microsoft/motion_controller", {
                {g_gripPoseAction, "/user/hand/left/input/grip/pose"},
                {g_gripPoseAction, "/user/hand/right/input/grip/pose"},
                {g_aimPoseAction, "/user/hand/left/input/aim/pose"},
                {g_aimPoseAction, "/user/hand/right/input/aim/pose"},
                {g_triggerAction, "/user/hand/left/input/trigger/value"},
                {g_triggerAction, "/user/hand/right/input/trigger/value"},
                {g_squeezeClickAction, "/user/hand/left/input/squeeze/click"},
                {g_squeezeClickAction, "/user/hand/right/input/squeeze/click"},
                {g_thumbstickAction, "/user/hand/left/input/thumbstick"},
                {g_thumbstickAction, "/user/hand/right/input/thumbstick"},
                {g_primaryButtonAction, "/user/hand/left/input/trackpad/click"},
                {g_primaryButtonAction, "/user/hand/right/input/trackpad/click"},
                {g_secondaryButtonAction, "/user/hand/left/input/menu/click"},
                {g_secondaryButtonAction, "/user/hand/right/input/menu/click"},
                {g_thumbstickClickAction, "/user/hand/left/input/thumbstick/click"},
                {g_thumbstickClickAction, "/user/hand/right/input/thumbstick/click"},
                {g_menuButtonAction, "/user/hand/left/input/menu/click"},
                {g_menuButtonAction, "/user/hand/right/input/menu/click"},
                {g_hapticAction, "/user/hand/left/output/haptic"},
                {g_hapticAction, "/user/hand/right/output/haptic"}
            });

        const bool simpleSuggested = suggestProfile(
            "/interaction_profiles/khr/simple_controller", {
                {g_gripPoseAction, "/user/hand/left/input/grip/pose"},
                {g_gripPoseAction, "/user/hand/right/input/grip/pose"},
                {g_aimPoseAction, "/user/hand/left/input/aim/pose"},
                {g_aimPoseAction, "/user/hand/right/input/aim/pose"},
                {g_triggerClickAction, "/user/hand/left/input/select/click"},
                {g_triggerClickAction, "/user/hand/right/input/select/click"},
                {g_primaryButtonAction, "/user/hand/left/input/select/click"},
                {g_primaryButtonAction, "/user/hand/right/input/select/click"},
                {g_menuButtonAction, "/user/hand/left/input/menu/click"},
                {g_menuButtonAction, "/user/hand/right/input/menu/click"},
                {g_hapticAction, "/user/hand/left/output/haptic"},
                {g_hapticAction, "/user/hand/right/output/haptic"}
            });

        if (!oculusSuggested && !indexSuggested && !viveSuggested &&
            !microsoftSuggested && !simpleSuggested)
            return false;

        // Do not attach here. SteamVR may have been auto-launched by xrCreateInstance
        // and can create the session before its input subsystem is focused. Attaching in
        // that window produces a permanently inactive action set until the game restarts.
        // ProcessOpenXrEvents attaches once the session reaches FOCUSED instead.
        return true;
    }

    void UpdateTouchActions(XrTime displayTime)
    {
        if (g_touchActionSet == XR_NULL_HANDLE || g_localSpace == XR_NULL_HANDLE)
            return;
        if (!g_touchActionsAttached || g_gripSpaces[0] == XR_NULL_HANDLE ||
            g_gripSpaces[1] == XR_NULL_HANDLE || g_aimSpaces[0] == XR_NULL_HANDLE ||
            g_aimSpaces[1] == XR_NULL_HANDLE)
        {
            if (g_sessionState == XR_SESSION_STATE_FOCUSED)
            {
                if (g_touchAttachRetryCountdown == 0)
                {
                    AttachTouchActions();
                    g_touchAttachRetryCountdown = 90;
                }
                else
                    --g_touchAttachRetryCountdown;
            }
            if (!g_touchActionsAttached)
                return;
        }
        XrActiveActionSet active{g_touchActionSet, XR_NULL_PATH};
        XrActionsSyncInfo sync{XR_TYPE_ACTIONS_SYNC_INFO};
        sync.countActiveActionSets = 1;
        sync.activeActionSets = &active;
        if (XR_FAILED(xrSyncActions(g_session, &sync)))
            return;

        ControllerState states[2];
        for (int hand = 0; hand < 2; ++hand)
        {
            auto getFloat = [&](XrAction action)
            {
                XrActionStateGetInfo info{XR_TYPE_ACTION_STATE_GET_INFO};
                info.action = action; info.subactionPath = g_handPaths[hand];
                XrActionStateFloat value{XR_TYPE_ACTION_STATE_FLOAT};
                xrGetActionStateFloat(g_session, &info, &value);
                return value.isActive ? value.currentState : 0.f;
            };
            auto getBool = [&](XrAction action)
            {
                XrActionStateGetInfo info{XR_TYPE_ACTION_STATE_GET_INFO};
                info.action = action; info.subactionPath = g_handPaths[hand];
                XrActionStateBoolean value{XR_TYPE_ACTION_STATE_BOOLEAN};
                xrGetActionStateBoolean(g_session, &info, &value);
                return value.isActive && value.currentState;
            };
            XrActionStateGetInfo stickInfo{XR_TYPE_ACTION_STATE_GET_INFO};
            stickInfo.action = g_thumbstickAction; stickInfo.subactionPath = g_handPaths[hand];
            XrActionStateVector2f stick{XR_TYPE_ACTION_STATE_VECTOR2F};
            xrGetActionStateVector2f(g_session, &stickInfo, &stick);
            states[hand].thumbstick = stick.isActive ? stick.currentState : XrVector2f{0.f, 0.f};
            states[hand].trigger = std::max(getFloat(g_triggerAction),
                getBool(g_triggerClickAction) ? 1.f : 0.f);
            states[hand].squeeze = std::max(getFloat(g_squeezeAction),
                getBool(g_squeezeClickAction) ? 1.f : 0.f);
            states[hand].primary = getBool(g_primaryButtonAction);
            states[hand].secondary = getBool(g_secondaryButtonAction);
            states[hand].thumbstickClick = getBool(g_thumbstickClickAction);
            states[hand].menu = getBool(g_menuButtonAction);

            XrSpaceLocation grip{XR_TYPE_SPACE_LOCATION};
            XrSpaceLocation aim{XR_TYPE_SPACE_LOCATION};
            if (g_gripSpaces[hand] != XR_NULL_HANDLE && XR_SUCCEEDED(xrLocateSpace(g_gripSpaces[hand], g_localSpace, displayTime, &grip)))
            {
                states[hand].gripPoseValid = (grip.locationFlags & XR_SPACE_LOCATION_POSITION_VALID_BIT) &&
                                             (grip.locationFlags & XR_SPACE_LOCATION_ORIENTATION_VALID_BIT);
                states[hand].gripPose = grip.pose;
            }
            if (g_aimSpaces[hand] != XR_NULL_HANDLE && XR_SUCCEEDED(xrLocateSpace(g_aimSpaces[hand], g_localSpace, displayTime, &aim)))
            {
                states[hand].aimPoseValid = (aim.locationFlags & XR_SPACE_LOCATION_POSITION_VALID_BIT) &&
                                            (aim.locationFlags & XR_SPACE_LOCATION_ORIENTATION_VALID_BIT);
                states[hand].aimPose = aim.pose;
            }
        }
        std::lock_guard<std::mutex> lock(g_controllerMutex);
        g_controllerStates[0] = states[0];
        g_controllerStates[1] = states[1];
    }

    void SetStatus(const char* format, ...)
    {
        std::lock_guard<std::mutex> lock(g_statusMutex);
        va_list arguments;
        va_start(arguments, format);
        vsnprintf_s(g_status, sizeof(g_status), _TRUNCATE, format, arguments);
        va_end(arguments);
    }

    void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType)
    {
        if (eventType == kUnityGfxDeviceEventInitialize && g_unityInterfaces != nullptr)
            g_d3d11 = g_unityInterfaces->Get<IUnityGraphicsD3D11>();

        if (eventType == kUnityGfxDeviceEventShutdown)
            g_d3d11 = nullptr;
    }

    void CreateD3D11Session()
    {
        if (g_session != XR_NULL_HANDLE)
            return;

        if (g_d3d11 == nullptr || g_d3d11->GetDevice() == nullptr)
        {
            SetStatus("Waiting for Unity's D3D11 device on the render thread.");
            return;
        }

        XrInstanceCreateInfo instanceInfo{XR_TYPE_INSTANCE_CREATE_INFO};
        std::strncpy(instanceInfo.applicationInfo.applicationName, "MFN VR", XR_MAX_APPLICATION_NAME_SIZE - 1);
        instanceInfo.applicationInfo.applicationVersion = 1;
        std::strncpy(instanceInfo.applicationInfo.engineName, "MFNOpenXR", XR_MAX_ENGINE_NAME_SIZE - 1);
        instanceInfo.applicationInfo.engineVersion = 1;
        instanceInfo.applicationInfo.apiVersion = XR_API_VERSION_1_0;
        const char* extensions[] = {XR_KHR_D3D11_ENABLE_EXTENSION_NAME};
        instanceInfo.enabledExtensionCount = 1;
        instanceInfo.enabledExtensionNames = extensions;

        XrResult result = xrCreateInstance(&instanceInfo, &g_instance);
        if (XR_FAILED(result))
        {
            SetStatus("xrCreateInstance failed: %d", result);
            return;
        }

        XrInstanceProperties instanceProperties{XR_TYPE_INSTANCE_PROPERTIES};
        if (XR_SUCCEEDED(xrGetInstanceProperties(g_instance, &instanceProperties)))
            strncpy_s(g_runtimeName, instanceProperties.runtimeName, _TRUNCATE);

        XrSystemGetInfo systemInfo{XR_TYPE_SYSTEM_GET_INFO};
        systemInfo.formFactor = XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;
        result = xrGetSystem(g_instance, &systemInfo, &g_systemId);
        if (XR_FAILED(result))
        {
            SetStatus("xrGetSystem failed: %d", result);
            xrDestroyInstance(g_instance);
            g_instance = XR_NULL_HANDLE;
            return;
        }

        PFN_xrGetD3D11GraphicsRequirementsKHR getRequirements = nullptr;
        result = xrGetInstanceProcAddr(g_instance, "xrGetD3D11GraphicsRequirementsKHR",
                                       reinterpret_cast<PFN_xrVoidFunction*>(&getRequirements));
        if (XR_FAILED(result) || getRequirements == nullptr)
        {
            SetStatus("Could not resolve xrGetD3D11GraphicsRequirementsKHR: %d", result);
            xrDestroyInstance(g_instance);
            g_instance = XR_NULL_HANDLE;
            return;
        }

        XrGraphicsRequirementsD3D11KHR requirements{XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR};
        result = getRequirements(g_instance, g_systemId, &requirements);
        if (XR_FAILED(result))
        {
            SetStatus("xrGetD3D11GraphicsRequirementsKHR failed: %d", result);
            xrDestroyInstance(g_instance);
            g_instance = XR_NULL_HANDLE;
            return;
        }

        XrGraphicsBindingD3D11KHR binding{XR_TYPE_GRAPHICS_BINDING_D3D11_KHR};
        binding.device = g_d3d11->GetDevice();
        XrSessionCreateInfo sessionInfo{XR_TYPE_SESSION_CREATE_INFO};
        sessionInfo.next = &binding;
        sessionInfo.systemId = g_systemId;
        result = xrCreateSession(g_instance, &sessionInfo, &g_session);
        if (XR_FAILED(result))
        {
            SetStatus("xrCreateSession failed: %d", result);
            xrDestroyInstance(g_instance);
            g_instance = XR_NULL_HANDLE;
            return;
        }

        const bool actionsReady = CreateTouchActions();
        SetStatus("OpenXR D3D11 session created via %s; controller action definitions %s.",
            g_runtimeName, actionsReady ? "ready" : "failed");
    }

    void DestroyRenderResources()
    {
        for (const auto& swapchain : g_eyeSwapchains)
        {
            for (auto* target : swapchain.renderTargets)
                if (target) target->Release();
            if (swapchain.handle != XR_NULL_HANDLE)
                xrDestroySwapchain(swapchain.handle);
        }
        g_eyeSwapchains.clear();

        if (g_localSpace != XR_NULL_HANDLE)
            xrDestroySpace(g_localSpace);
        g_localSpace = XR_NULL_HANDLE;
    }

    bool CreateRenderResources()
    {
        if (!g_eyeSwapchains.empty())
            return true;

        uint32_t viewCount = 0;
        XrResult result = xrEnumerateViewConfigurationViews(g_instance, g_systemId,
            XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO, 0, &viewCount, nullptr);
        if (XR_FAILED(result) || viewCount != 2)
        {
            SetStatus("Could not enumerate stereo views: result=%d, count=%u", result, viewCount);
            return false;
        }

        std::vector<XrViewConfigurationView> viewConfigs(viewCount, {XR_TYPE_VIEW_CONFIGURATION_VIEW});
        result = xrEnumerateViewConfigurationViews(g_instance, g_systemId,
            XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO, viewCount, &viewCount, viewConfigs.data());
        if (XR_FAILED(result))
        {
            SetStatus("Could not read stereo view configuration: %d", result);
            return false;
        }

        uint32_t formatCount = 0;
        result = xrEnumerateSwapchainFormats(g_session, 0, &formatCount, nullptr);
        if (XR_FAILED(result))
        {
            SetStatus("Could not enumerate swapchain formats: %d", result);
            return false;
        }
        std::vector<int64_t> formats(formatCount);
        xrEnumerateSwapchainFormats(g_session, formatCount, &formatCount, formats.data());
        const int64_t requestedFormat = static_cast<int64_t>(DXGI_FORMAT_R8G8B8A8_UNORM_SRGB);
        int64_t colorFormat = formats.front();
        for (const auto format : formats)
            if (format == requestedFormat)
                colorFormat = format;

        for (uint32_t eye = 0; eye < viewCount; ++eye)
        {
            XrSwapchainCreateInfo createInfo{XR_TYPE_SWAPCHAIN_CREATE_INFO};
            createInfo.usageFlags = XR_SWAPCHAIN_USAGE_SAMPLED_BIT | XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT;
            createInfo.format = colorFormat;
            createInfo.sampleCount = viewConfigs[eye].recommendedSwapchainSampleCount;
            createInfo.width = viewConfigs[eye].recommendedImageRectWidth;
            createInfo.height = viewConfigs[eye].recommendedImageRectHeight;
            createInfo.faceCount = 1;
            createInfo.arraySize = 1;
            createInfo.mipCount = 1;

            EyeSwapchain swapchain;
            swapchain.width = static_cast<int32_t>(createInfo.width);
            swapchain.height = static_cast<int32_t>(createInfo.height);
            result = xrCreateSwapchain(g_session, &createInfo, &swapchain.handle);
            if (XR_FAILED(result))
            {
                SetStatus("Could not create eye swapchain %u: %d", eye, result);
                DestroyRenderResources();
                return false;
            }

            uint32_t imageCount = 0;
            xrEnumerateSwapchainImages(swapchain.handle, 0, &imageCount, nullptr);
            swapchain.images.resize(imageCount, {XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR});
            result = xrEnumerateSwapchainImages(swapchain.handle, imageCount, &imageCount,
                reinterpret_cast<XrSwapchainImageBaseHeader*>(swapchain.images.data()));
            if (XR_FAILED(result))
            {
                SetStatus("Could not enumerate eye swapchain %u images: %d", eye, result);
                DestroyRenderResources();
                return false;
            }
            swapchain.renderTargets.resize(imageCount, nullptr);
            for (uint32_t image = 0; image < imageCount; ++image)
            {
                D3D11_TEXTURE2D_DESC textureDescription{};
                swapchain.images[image].texture->GetDesc(&textureDescription);
                D3D11_RENDER_TARGET_VIEW_DESC viewDescription{};
                viewDescription.Format = TypedColorFormat(textureDescription.Format);
                viewDescription.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D;
                g_d3d11->GetDevice()->CreateRenderTargetView(swapchain.images[image].texture,
                    &viewDescription, &swapchain.renderTargets[image]);
            }
            g_eyeSwapchains.push_back(std::move(swapchain));
        }

        XrReferenceSpaceCreateInfo spaceInfo{XR_TYPE_REFERENCE_SPACE_CREATE_INFO};
        spaceInfo.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_LOCAL;
        spaceInfo.poseInReferenceSpace.orientation.w = 1.0f;
        result = xrCreateReferenceSpace(g_session, &spaceInfo, &g_localSpace);
        if (XR_FAILED(result))
        {
            SetStatus("Could not create local reference space: %d", result);
            DestroyRenderResources();
            return false;
        }

        SetStatus("OpenXR stereo swapchains ready via %s: %dx%d per eye.",
                  g_runtimeName, g_eyeSwapchains[0].width, g_eyeSwapchains[0].height);
        return true;
    }

    void ProcessOpenXrEvents()
    {
        if (g_instance == XR_NULL_HANDLE)
            return;

        XrEventDataBuffer event{XR_TYPE_EVENT_DATA_BUFFER};
        while (xrPollEvent(g_instance, &event) == XR_SUCCESS)
        {
            if (event.type == XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED)
            {
                const auto* stateChanged = reinterpret_cast<const XrEventDataSessionStateChanged*>(&event);
                g_sessionState = stateChanged->state;
                if (g_sessionState == XR_SESSION_STATE_READY && !g_sessionRunning)
                {
                    XrSessionBeginInfo beginInfo{XR_TYPE_SESSION_BEGIN_INFO};
                    beginInfo.primaryViewConfigurationType = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
                    const XrResult result = xrBeginSession(g_session, &beginInfo);
                    if (XR_SUCCEEDED(result))
                    {
                        g_sessionRunning = true;
                        CreateRenderResources();
                    }
                    else
                        SetStatus("xrBeginSession failed: %d", result);
                }
                else if (g_sessionState == XR_SESSION_STATE_STOPPING && g_sessionRunning)
                {
                    xrEndSession(g_session);
                    g_sessionRunning = false;
                }
                if (g_sessionState == XR_SESSION_STATE_FOCUSED && !g_touchActionsAttached)
                {
                    g_touchAttachRetryCountdown = 0;
                    AttachTouchActions();
                }
            }
            event = {XR_TYPE_EVENT_DATA_BUFFER};
        }
    }

    bool EnsureFlipShader()
    {
        if (g_flipVertexShader != nullptr)
            return true;

        constexpr const char* source = R"(
struct Output { float4 position : SV_Position; float2 uv : TEXCOORD0; };
Output VS(uint id : SV_VertexID) {
    float2 position = float2(id == 2 ? 3.0 : -1.0, id == 1 ? 3.0 : -1.0);
    Output output;
    output.position = float4(position, 0.0, 1.0);
    output.uv = float2((position.x + 1.0) * 0.5, (position.y + 1.0) * 0.5);
    return output;
}
Texture2D sourceTexture : register(t0);
SamplerState sourceSampler : register(s0);
float4 PS(Output input) : SV_Target {
    uint width, height;
    sourceTexture.GetDimensions(width, height);
    int2 sourcePixel = int2(int(input.position.x), int(height) - 1 - int(input.position.y));
    return sourceTexture.Load(int3(sourcePixel, 0));
}
)";

        ID3DBlob* vertexBlob = nullptr;
        ID3DBlob* pixelBlob = nullptr;
        const auto compileVertex = D3DCompile(source, strlen(source), nullptr, nullptr, nullptr, "VS", "vs_4_0", 0, 0, &vertexBlob, nullptr);
        const auto compilePixel = D3DCompile(source, strlen(source), nullptr, nullptr, nullptr, "PS", "ps_4_0", 0, 0, &pixelBlob, nullptr);
        if (FAILED(compileVertex) || FAILED(compilePixel))
        {
            if (vertexBlob) vertexBlob->Release();
            if (pixelBlob) pixelBlob->Release();
            SetStatus("Could not compile native vertical-flip shader.");
            return false;
        }

        auto* device = g_d3d11->GetDevice();
        device->CreateVertexShader(vertexBlob->GetBufferPointer(), vertexBlob->GetBufferSize(), nullptr, &g_flipVertexShader);
        device->CreatePixelShader(pixelBlob->GetBufferPointer(), pixelBlob->GetBufferSize(), nullptr, &g_flipPixelShader);
        vertexBlob->Release();
        pixelBlob->Release();
        D3D11_SAMPLER_DESC sampler{};
        sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
        sampler.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampler.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampler.MaxLOD = D3D11_FLOAT32_MAX;
        device->CreateSamplerState(&sampler, &g_flipSampler);
        D3D11_RASTERIZER_DESC rasterizer{};
        rasterizer.FillMode = D3D11_FILL_SOLID;
        rasterizer.CullMode = D3D11_CULL_NONE;
        rasterizer.DepthClipEnable = TRUE;
        device->CreateRasterizerState(&rasterizer, &g_flipRasterizer);
        D3D11_DEPTH_STENCIL_DESC depth{};
        depth.DepthEnable = FALSE;
        depth.StencilEnable = FALSE;
        device->CreateDepthStencilState(&depth, &g_flipDepthState);
        D3D11_BLEND_DESC blend{};
        blend.RenderTarget[0].BlendEnable = FALSE;
        blend.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
        device->CreateBlendState(&blend, &g_flipBlendState);
        return g_flipVertexShader != nullptr && g_flipPixelShader != nullptr && g_flipSampler != nullptr &&
               g_flipRasterizer != nullptr && g_flipDepthState != nullptr && g_flipBlendState != nullptr;
    }

    DXGI_FORMAT TypedColorFormat(DXGI_FORMAT format)
    {
        switch (format)
        {
        case DXGI_FORMAT_R8G8B8A8_TYPELESS: return DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
        case DXGI_FORMAT_B8G8R8A8_TYPELESS: return DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
        case DXGI_FORMAT_B8G8R8X8_TYPELESS: return DXGI_FORMAT_B8G8R8X8_UNORM_SRGB;
        default: return format;
        }
    }

    void CopyWithVerticalFlip(ID3D11DeviceContext* context, int eye, ID3D11Texture2D* source,
                              ID3D11Texture2D* destination, ID3D11RenderTargetView* cachedDestinationView)
    {
        if (!EnsureFlipShader())
        {
            g_flipPath.store(16);
            context->CopyResource(destination, source);
            return;
        }

        ID3D11ShaderResourceView* sourceView = g_sourceViews[eye];
        ID3D11RenderTargetView* destinationView = cachedDestinationView;
        bool releaseSourceView = false;
        bool releaseDestinationView = false;
        auto* device = g_d3d11->GetDevice();
        D3D11_TEXTURE2D_DESC sourceDescription{};
        D3D11_TEXTURE2D_DESC destinationDescription{};
        source->GetDesc(&sourceDescription);
        destination->GetDesc(&destinationDescription);
        D3D11_SHADER_RESOURCE_VIEW_DESC sourceViewDescription{};
        sourceViewDescription.Format = TypedColorFormat(sourceDescription.Format);
        sourceViewDescription.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
        sourceViewDescription.Texture2D.MipLevels = 1;
        D3D11_RENDER_TARGET_VIEW_DESC targetViewDescription{};
        targetViewDescription.Format = TypedColorFormat(destinationDescription.Format);
        targetViewDescription.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D;
        HRESULT sourceResult = S_OK;
        if (g_sourceViewTextures[eye] != source || sourceView == nullptr)
        {
            if (g_sourceViews[eye]) g_sourceViews[eye]->Release();
            g_sourceViews[eye] = nullptr;
            g_sourceViewTextures[eye] = source;
            sourceResult = device->CreateShaderResourceView(source, &sourceViewDescription, &g_sourceViews[eye]);
            sourceView = g_sourceViews[eye];
        }
        HRESULT destinationResult = destinationView != nullptr ? S_OK : E_FAIL;
        auto& intermediate = g_flipIntermediate[eye];
        if (intermediate.width != sourceDescription.Width || intermediate.height != sourceDescription.Height ||
            intermediate.format != sourceDescription.Format)
        {
            if (intermediate.sampleSource) intermediate.sampleSource->Release();
            if (intermediate.renderTarget) intermediate.renderTarget->Release();
            intermediate = {};
            intermediate.width = sourceDescription.Width;
            intermediate.height = sourceDescription.Height;
            intermediate.format = sourceDescription.Format;
        }
        if (FAILED(sourceResult) || sourceView == nullptr)
        {
            g_flipPath.fetch_or(2);
            D3D11_TEXTURE2D_DESC description = sourceDescription;
            description.Usage = D3D11_USAGE_DEFAULT;
            description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
            description.CPUAccessFlags = 0;
            description.MiscFlags = 0;
            if (!intermediate.sampleSource)
                device->CreateTexture2D(&description, nullptr, &intermediate.sampleSource);
            if (intermediate.sampleSource)
            {
                context->CopyResource(intermediate.sampleSource, source);
                device->CreateShaderResourceView(intermediate.sampleSource, &sourceViewDescription, &sourceView);
                releaseSourceView = sourceView != nullptr;
            }
        }
        bool copyRenderedTarget = false;
        if (FAILED(destinationResult) || destinationView == nullptr)
        {
            g_flipPath.fetch_or(4);
            D3D11_TEXTURE2D_DESC description{};
            destination->GetDesc(&description);
            description.Usage = D3D11_USAGE_DEFAULT;
            description.BindFlags = D3D11_BIND_RENDER_TARGET;
            description.CPUAccessFlags = 0;
            description.MiscFlags = 0;
            if (!intermediate.renderTarget)
                device->CreateTexture2D(&description, nullptr, &intermediate.renderTarget);
            if (intermediate.renderTarget)
            {
                device->CreateRenderTargetView(intermediate.renderTarget, &targetViewDescription, &destinationView);
                releaseDestinationView = destinationView != nullptr;
                copyRenderedTarget = true;
            }
        }
        if (!sourceView || !destinationView)
        {
            g_flipPath.fetch_or(8);
            if (releaseSourceView && sourceView) sourceView->Release();
            if (releaseDestinationView && destinationView) destinationView->Release();
            context->CopyResource(destination, source);
            return;
        }
        D3D11_TEXTURE2D_DESC description{};
        destination->GetDesc(&description);
        D3D11_VIEWPORT viewport{0.f, 0.f, static_cast<float>(description.Width), static_cast<float>(description.Height), 0.f, 1.f};
        context->RSSetViewports(1, &viewport);
        context->OMSetRenderTargets(1, &destinationView, nullptr);
        context->OMSetBlendState(g_flipBlendState, nullptr, 0xFFFFFFFF);
        context->OMSetDepthStencilState(g_flipDepthState, 0);
        context->RSSetState(g_flipRasterizer);
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(g_flipVertexShader, nullptr, 0);
        context->PSSetShader(g_flipPixelShader, nullptr, 0);
        context->PSSetSamplers(0, 1, &g_flipSampler);
        context->PSSetShaderResources(0, 1, &sourceView);
        context->Draw(3, 0);
        g_flipPath.fetch_or(1);
        ID3D11ShaderResourceView* empty[] = {nullptr};
        context->PSSetShaderResources(0, 1, empty);
        if (copyRenderedTarget)
            context->CopyResource(destination, intermediate.renderTarget);
        if (releaseSourceView && sourceView) sourceView->Release();
        if (releaseDestinationView && destinationView) destinationView->Release();
    }

    bool CopyWithCpuVerticalFlip(ID3D11DeviceContext* context, int eye, ID3D11Texture2D* source, ID3D11Texture2D* destination)
    {
        D3D11_TEXTURE2D_DESC sourceDesc{};
        source->GetDesc(&sourceDesc);
        if (sourceDesc.SampleDesc.Count != 1)
            return false;

        auto& resources = g_cpuFlip[eye];
        if (resources.readback == nullptr || resources.description.Width != sourceDesc.Width ||
            resources.description.Height != sourceDesc.Height || resources.description.Format != sourceDesc.Format)
        {
            if (resources.readback) resources.readback->Release();
            resources = {};
            resources.description = sourceDesc;
            D3D11_TEXTURE2D_DESC readbackDesc = sourceDesc;
            readbackDesc.Usage = D3D11_USAGE_STAGING;
            readbackDesc.BindFlags = 0;
            readbackDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            readbackDesc.MiscFlags = 0;
            auto* device = g_d3d11->GetDevice();
            if (FAILED(device->CreateTexture2D(&readbackDesc, nullptr, &resources.readback)))
                return false;
        }

        context->CopyResource(resources.readback, source);
        D3D11_MAPPED_SUBRESOURCE input{};
        if (FAILED(context->Map(resources.readback, 0, D3D11_MAP_READ, 0, &input)))
            return false;
        const auto rowBytes = sourceDesc.Width * 4;
        std::vector<unsigned char> flipped(static_cast<size_t>(rowBytes) * sourceDesc.Height);
        for (UINT row = 0; row < sourceDesc.Height; ++row)
        {
            memcpy(flipped.data() + static_cast<size_t>(row) * rowBytes,
                   static_cast<unsigned char*>(input.pData) + (sourceDesc.Height - 1 - row) * input.RowPitch,
                   std::min<UINT>(rowBytes, input.RowPitch));
        }
        context->Unmap(resources.readback, 0);
        context->UpdateSubresource(destination, 0, nullptr, flipped.data(), rowBytes, 0);
        return true;
    }

    void SubmitBlackFrame()
    {
        if (!g_sessionRunning || g_eyeSwapchains.size() != 2 || g_localSpace == XR_NULL_HANDLE)
            return;

        XrFrameState frameState{XR_TYPE_FRAME_STATE};
        // XrFrameWaitInfo is required by the OpenXR specification.  The Meta
        // runtime happened to tolerate a null pointer here, but SteamVR rejects
        // the frame before it can reach the compositor.
        XrFrameWaitInfo waitInfo{XR_TYPE_FRAME_WAIT_INFO};
        if (XR_FAILED(xrWaitFrame(g_session, &waitInfo, &frameState)))
            return;
        XrFrameBeginInfo beginInfo{XR_TYPE_FRAME_BEGIN_INFO};
        if (XR_FAILED(xrBeginFrame(g_session, &beginInfo)))
            return;

        std::vector<XrCompositionLayerBaseHeader*> layers;
        XrCompositionLayerProjection projection{XR_TYPE_COMPOSITION_LAYER_PROJECTION};
        XrCompositionLayerProjectionView views[2] = {{XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW}, {XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW}};

        if (frameState.shouldRender)
        {
            XrViewLocateInfo locateInfo{XR_TYPE_VIEW_LOCATE_INFO};
            locateInfo.viewConfigurationType = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
            locateInfo.displayTime = frameState.predictedDisplayTime;
            locateInfo.space = g_localSpace;
            XrViewState viewState{XR_TYPE_VIEW_STATE};
            XrView xrViews[2] = {{XR_TYPE_VIEW}, {XR_TYPE_VIEW}};
            uint32_t viewCount = 0;
            if (XR_SUCCEEDED(xrLocateViews(g_session, &locateInfo, &viewState, 2, &viewCount, xrViews)) && viewCount == 2)
            {
                UpdateTouchActions(frameState.predictedDisplayTime);
                XrView renderedViews[2] = {{XR_TYPE_VIEW}, {XR_TYPE_VIEW}};
                bool hasRenderedPose = false;
                {
                    std::lock_guard<std::mutex> lock(g_headPoseMutex);
                    hasRenderedPose = g_hasHeadOrientation;
                    if (hasRenderedPose)
                    {
                        renderedViews[0] = g_latestViews[0];
                        renderedViews[1] = g_latestViews[1];
                    }
                    g_headOrientation = xrViews[0].pose.orientation;
                    g_latestViews[0] = xrViews[0];
                    g_latestViews[1] = xrViews[1];
                    g_hasHeadOrientation = true;
                }
                if (!hasRenderedPose)
                {
                    renderedViews[0] = xrViews[0];
                    renderedViews[1] = xrViews[1];
                }
                auto* context = g_d3d11->GetDevice() ? [&]() { ID3D11DeviceContext* value = nullptr; g_d3d11->GetDevice()->GetImmediateContext(&value); return value; }() : nullptr;
                for (uint32_t eye = 0; eye < 2; ++eye)
                {
                    uint32_t imageIndex = 0;
                    XrSwapchainImageAcquireInfo acquireInfo{XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO};
                    XrSwapchainImageWaitInfo waitInfo{XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO};
                    waitInfo.timeout = XR_INFINITE_DURATION;
                    if (XR_SUCCEEDED(xrAcquireSwapchainImage(g_eyeSwapchains[eye].handle, &acquireInfo, &imageIndex)) &&
                        XR_SUCCEEDED(xrWaitSwapchainImage(g_eyeSwapchains[eye].handle, &waitInfo)))
                    {
                        if (context != nullptr && g_sourceTextures[eye] != nullptr)
                        {
                            CopyWithVerticalFlip(context, eye, g_sourceTextures[eye],
                                g_eyeSwapchains[eye].images[imageIndex].texture,
                                g_eyeSwapchains[eye].renderTargets[imageIndex]);
                        }
                        else
                        {
                            ID3D11RenderTargetView* target = nullptr;
                            g_d3d11->GetDevice()->CreateRenderTargetView(g_eyeSwapchains[eye].images[imageIndex].texture, nullptr, &target);
                            if (target != nullptr && context != nullptr)
                            {
                                const float black[] = {0.f, 0.f, 0.f, 1.f};
                                context->ClearRenderTargetView(target, black);
                                target->Release();
                            }
                        }
                        XrSwapchainImageReleaseInfo releaseInfo{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};
                        xrReleaseSwapchainImage(g_eyeSwapchains[eye].handle, &releaseInfo);
                    }
                    // Unity rendered these textures from the previously published view pose.
                    // Submit that matching pose so the OpenXR compositor can time-warp from the
                    // rendered pose to the current display pose instead of treating stale pixels
                    // as if they were rendered from the newly located view.
                    views[eye].pose = renderedViews[eye].pose;
                    views[eye].fov = renderedViews[eye].fov;
                    views[eye].subImage.swapchain = g_eyeSwapchains[eye].handle;
                    views[eye].subImage.imageRect.extent = {g_eyeSwapchains[eye].width, g_eyeSwapchains[eye].height};
                }
                if (context != nullptr)
                    context->Release();
                projection.space = g_localSpace;
                projection.viewCount = 2;
                projection.views = views;
                layers.push_back(reinterpret_cast<XrCompositionLayerBaseHeader*>(&projection));
            }
        }

        XrFrameEndInfo endInfo{XR_TYPE_FRAME_END_INFO};
        endInfo.displayTime = frameState.predictedDisplayTime;
        endInfo.environmentBlendMode = XR_ENVIRONMENT_BLEND_MODE_OPAQUE;
        endInfo.layerCount = static_cast<uint32_t>(layers.size());
        endInfo.layers = layers.empty() ? nullptr : const_cast<const XrCompositionLayerBaseHeader**>(layers.data());
        xrEndFrame(g_session, &endInfo);
    }

    void UNITY_INTERFACE_API OnRenderEvent(int eventId)
    {
        if (eventId == 1)
        {
            CreateD3D11Session();
            ProcessOpenXrEvents();
            SubmitBlackFrame();
        }
    }
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    g_unityInterfaces = unityInterfaces;
    auto* graphics = unityInterfaces->Get<IUnityGraphics>();
    graphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
    OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
{
    for (int hand = 0; hand < 2; ++hand)
    {
        if (g_gripSpaces[hand] != XR_NULL_HANDLE) xrDestroySpace(g_gripSpaces[hand]);
        if (g_aimSpaces[hand] != XR_NULL_HANDLE) xrDestroySpace(g_aimSpaces[hand]);
    }
    for (int eye = 0; eye < 2; ++eye)
    {
        if (g_sourceViews[eye]) g_sourceViews[eye]->Release();
        g_sourceViews[eye] = nullptr;
        g_sourceViewTextures[eye] = nullptr;
    }
    DestroyRenderResources();
    if (g_session != XR_NULL_HANDLE)
        xrDestroySession(g_session);
    if (g_touchActionSet != XR_NULL_HANDLE)
        xrDestroyActionSet(g_touchActionSet);
    if (g_instance != XR_NULL_HANDLE)
        xrDestroyInstance(g_instance);
    g_session = XR_NULL_HANDLE;
    g_instance = XR_NULL_HANDLE;
}

extern "C" __declspec(dllexport) UnityRenderingEvent __cdecl MFN_GetRenderEvent()
{
    return OnRenderEvent;
}

extern "C" __declspec(dllexport) void __cdecl MFN_GetStatus(char* message, const int messageLength)
{
    if (message == nullptr || messageLength <= 0)
        return;

    std::lock_guard<std::mutex> lock(g_statusMutex);
    strncpy_s(message, static_cast<size_t>(messageLength), g_status, _TRUNCATE);
}

extern "C" __declspec(dllexport) int __cdecl MFN_GetEyeWidth()
{
    return g_eyeSwapchains.empty() ? 0 : g_eyeSwapchains[0].width;
}

extern "C" __declspec(dllexport) int __cdecl MFN_GetEyeHeight()
{
    return g_eyeSwapchains.empty() ? 0 : g_eyeSwapchains[0].height;
}

extern "C" __declspec(dllexport) void __cdecl MFN_SetSourceTextures(void* left, void* right)
{
    g_sourceTextures[0] = static_cast<ID3D11Texture2D*>(left);
    g_sourceTextures[1] = static_cast<ID3D11Texture2D*>(right);
}
extern "C" __declspec(dllexport) int __cdecl MFN_GetHeadOrientation(float* x,float* y,float* z,float* w){ std::lock_guard<std::mutex> lock(g_headPoseMutex); if(!g_hasHeadOrientation) return 0; *x=g_headOrientation.x;*y=g_headOrientation.y;*z=g_headOrientation.z;*w=g_headOrientation.w; return 1; }
extern "C" __declspec(dllexport) int __cdecl MFN_GetEyeView(
    int eye, float* px, float* py, float* pz, float* qx, float* qy, float* qz, float* qw,
    float* angleLeft, float* angleRight, float* angleUp, float* angleDown)
{
    if (eye < 0 || eye > 1 || !px || !py || !pz || !qx || !qy || !qz || !qw ||
        !angleLeft || !angleRight || !angleUp || !angleDown)
        return 0;
    std::lock_guard<std::mutex> lock(g_headPoseMutex);
    if (!g_hasHeadOrientation)
        return 0;
    const auto& view = g_latestViews[eye];
    *px = view.pose.position.x; *py = view.pose.position.y; *pz = view.pose.position.z;
    *qx = view.pose.orientation.x; *qy = view.pose.orientation.y;
    *qz = view.pose.orientation.z; *qw = view.pose.orientation.w;
    *angleLeft = view.fov.angleLeft; *angleRight = view.fov.angleRight;
    *angleUp = view.fov.angleUp; *angleDown = view.fov.angleDown;
    return 1;
}

extern "C" __declspec(dllexport) int __cdecl MFN_GetControllerPose(
    int hand, int aim, float* px, float* py, float* pz,
    float* qx, float* qy, float* qz, float* qw)
{
    if (hand < 0 || hand > 1 || !px || !py || !pz || !qx || !qy || !qz || !qw)
        return 0;
    std::lock_guard<std::mutex> lock(g_controllerMutex);
    const auto& state = g_controllerStates[hand];
    if ((aim != 0 && !state.aimPoseValid) || (aim == 0 && !state.gripPoseValid))
        return 0;
    const auto& pose = aim != 0 ? state.aimPose : state.gripPose;
    *px = pose.position.x; *py = pose.position.y; *pz = pose.position.z;
    *qx = pose.orientation.x; *qy = pose.orientation.y;
    *qz = pose.orientation.z; *qw = pose.orientation.w;
    return 1;
}

extern "C" __declspec(dllexport) int __cdecl MFN_GetControllerInput(
    int hand, float* stickX, float* stickY, float* trigger, float* squeeze,
    int* primary, int* secondary, int* stickClick, int* menu)
{
    if (hand < 0 || hand > 1 || !stickX || !stickY || !trigger || !squeeze ||
        !primary || !secondary || !stickClick || !menu)
        return 0;
    std::lock_guard<std::mutex> lock(g_controllerMutex);
    const auto& state = g_controllerStates[hand];
    *stickX = state.thumbstick.x; *stickY = state.thumbstick.y;
    *trigger = state.trigger; *squeeze = state.squeeze;
    *primary = state.primary ? 1 : 0; *secondary = state.secondary ? 1 : 0;
    *stickClick = state.thumbstickClick ? 1 : 0; *menu = state.menu ? 1 : 0;
    return 1;
}

extern "C" __declspec(dllexport) int __cdecl MFN_ApplyControllerHaptic(
    int hand, float amplitude, float durationSeconds, float frequency)
{
    if (hand < 0 || hand > 1 || g_session == XR_NULL_HANDLE || g_hapticAction == XR_NULL_HANDLE)
        return 0;
    XrHapticActionInfo info{XR_TYPE_HAPTIC_ACTION_INFO};
    info.action = g_hapticAction;
    info.subactionPath = g_handPaths[hand];
    XrHapticVibration vibration{XR_TYPE_HAPTIC_VIBRATION};
    vibration.amplitude = std::max(0.f, std::min(1.f, amplitude));
    vibration.duration = static_cast<XrDuration>(std::max(0.001f, durationSeconds) * 1000000000.0);
    vibration.frequency = frequency;
    return XR_SUCCEEDED(xrApplyHapticFeedback(g_session, &info,
        reinterpret_cast<const XrHapticBaseHeader*>(&vibration))) ? 1 : 0;
}
extern "C" __declspec(dllexport) int __cdecl MFN_GetFlipPath()
{
    return g_flipPath.load();
}
