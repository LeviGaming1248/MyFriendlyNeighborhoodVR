using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--identity-only")
            return PatchReleaseIdentity(args[1], args[2], args[3]);

        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: CoreCameraPatcher <working-input.dll> <patched-output.dll>");
            Console.Error.WriteLine("   or: CoreCameraPatcher --identity-only <input.dll> <output.dll> <version>");
            return 2;
        }

        var assembly = AssemblyDefinition.ReadAssembly(Path.GetFullPath(args[0]),
            new ReaderParameters { ReadSymbols = false, InMemory = true });
        var module = assembly.MainModule;
        var plugin = module.Types.Single(type => type.FullName == "MFNVR.MFNVRPlugin");
        var trackedPair = plugin.Methods.Single(method => method.Name == "ConfigureTrackedPair");
        var overlayPair = plugin.Methods.Single(method => method.Name == "ConfigureOverlayPair");
        var configureEyes = plugin.Methods.Single(method => method.Name == "ConfigureEyeCameras");
        var onPlayerUpdate = plugin.Methods.Single(method => method.Name == "OnPlayerUpdate");
        var lateUpdate = plugin.Methods.Single(method => method.Name == "LateUpdate");
        var flatMeleePrefix = plugin.Methods.Single(method => method.Name == "OnFlatMeleePrefix");
        var flatWrenchPrefix = plugin.Methods.Single(method => method.Name == "OnFlatWrenchSwingPrefix");

        var allCalls = plugin.Methods.Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Where(instruction => instruction.Operand is MethodReference)
            .Select(instruction => (MethodReference)instruction.Operand)
            .ToArray();
        var objectEquality = allCalls.First(reference => reference.Name == "op_Equality" &&
            reference.DeclaringType.FullName == "UnityEngine.Object");

        var gameplayCamera = plugin.Fields.Single(field => field.Name == "gameplayCamera");
        var gameplayHudCamera = plugin.Fields.Single(field => field.Name == "gameplayHudCamera");
        var leftEyeTexture = plugin.Fields.Single(field => field.Name == "leftEyeTexture");
        var rightEyeTexture = plugin.Fields.Single(field => field.Name == "rightEyeTexture");
        var useComfortRig = plugin.Fields.Single(field => field.Name == "useComfortRig");
        var rightAimValid = plugin.Fields.Single(field => field.Name == "rightAimValid");
        var rightAimWorldPosition = plugin.Fields.Single(field => field.Name == "rightAimWorldPosition");
        var rightAimWorldRotation = plugin.Fields.Single(field => field.Name == "rightAimWorldRotation");
        var currentRightGripLocalPosition = plugin.Fields.Single(field => field.Name == "currentRightGripLocalPosition");
        var currentRightAimLocalRotation = plugin.Fields.Single(field => field.Name == "currentRightAimLocalRotation");
        var trackingOriginPosition = plugin.Fields.Single(field => field.Name == "trackingOriginPosition");
        var trackingOriginRotation = plugin.Fields.Single(field => field.Name == "trackingOriginRotation");
        var renderRigPosition = plugin.Fields.Single(field => field.Name == "renderRigPosition");
        var renderRigRotation = plugin.Fields.Single(field => field.Name == "renderRigRotation");
        var hasTrackingOrigin = plugin.Fields.Single(field => field.Name == "hasTrackingOrigin");
        var instance = plugin.Fields.Single(field => field.Name == "instance");
        var cameraType = trackedPair.Parameters[0].ParameterType;
        var renderTextureType = leftEyeTexture.FieldType;

        var bridgeAssembly = module.AssemblyReferences.FirstOrDefault(reference =>
            reference.Name == "MFNVRRenderBridge");
        if (bridgeAssembly == null)
        {
            bridgeAssembly = new AssemblyNameReference("MFNVRRenderBridge", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(bridgeAssembly);
        }
        var bridgeType = new TypeReference("MFNVRBridge", "RenderBridge", module, bridgeAssembly, false);
        var configureTrackedPairPost = StaticMethod(module, bridgeType, "ConfigureTrackedPairPost",
            cameraType, cameraType, cameraType, renderTextureType, renderTextureType,
            module.TypeSystem.Boolean, module.TypeSystem.Boolean, module.TypeSystem.Boolean);
        var configureHands = StaticMethod(module, bridgeType, "ConfigureHands",
            cameraType, cameraType, cameraType, renderTextureType, renderTextureType,
            module.TypeSystem.Boolean);
        var ensureMirror = StaticMethod(module, bridgeType, "EnsureMirror",
            cameraType, cameraType, renderTextureType, module.TypeSystem.Boolean);
        var playerType = onPlayerUpdate.Parameters[0].ParameterType;
        var tickNativeHands = StaticFunction(module, bridgeType, "TickNativeHands",
            module.TypeSystem.Boolean, playerType, trackingOriginPosition.FieldType,
            trackingOriginRotation.FieldType, renderRigPosition.FieldType,
            renderRigRotation.FieldType, module.TypeSystem.Boolean, module.TypeSystem.Boolean);
        var getMotionAimPosition = StaticFunction(module, bridgeType, "GetMotionAimPosition",
            rightAimWorldPosition.FieldType);
        var getMotionAimRotation = StaticFunction(module, bridgeType, "GetMotionAimRotation",
            rightAimWorldRotation.FieldType);
        var getMotionGripLocalPosition = StaticFunction(module, bridgeType,
            "GetMotionGripLocalPosition", currentRightGripLocalPosition.FieldType);
        var getMotionAimLocalRotation = StaticFunction(module, bridgeType,
            "GetMotionAimLocalRotation", currentRightAimLocalRotation.FieldType);

        RewriteHandsOverlay(overlayPair, leftEyeTexture, rightEyeTexture, rightAimValid,
            configureHands);
        AppendTrackedPairBridge(trackedPair, gameplayCamera, gameplayHudCamera,
            leftEyeTexture, rightEyeTexture, useComfortRig, objectEquality,
            configureTrackedPairPost);
        AppendMirrorBridge(configureEyes, gameplayHudCamera, gameplayCamera,
            leftEyeTexture, useComfortRig, ensureMirror);
        // The old experimental motion path is intentionally disabled. The bridge uses a fresh
        // direct wrist-to-controller solver and only borrows the core's stable coordinate frame.
        lateUpdate.Name = "LegacyMotionUpdateDisabled";
        AppendNativeMotionTick(onPlayerUpdate, instance, trackingOriginPosition,
            trackingOriginRotation, renderRigPosition, renderRigRotation, hasTrackingOrigin,
            useComfortRig, rightAimValid, rightAimWorldPosition, rightAimWorldRotation,
            currentRightGripLocalPosition, currentRightAimLocalRotation, tickNativeHands,
            getMotionAimPosition, getMotionAimRotation, getMotionGripLocalPosition,
            getMotionAimLocalRotation);
        RewriteAsTrueReturn(flatMeleePrefix);
        RewriteAsTrueReturn(flatWrenchPrefix);

        trackedPair.Body.MaxStackSize = Math.Max(trackedPair.Body.MaxStackSize, 9);
        configureEyes.Body.MaxStackSize = Math.Max(configureEyes.Body.MaxStackSize, 5);
        assembly.Write(Path.GetFullPath(args[1]));
        Console.WriteLine("Patched core to use the separated VR render bridge.");
        return 0;
    }

    private static int PatchReleaseIdentity(string inputPath, string outputPath,
        string releaseVersion)
    {
        Version parsedVersion;
        if (!Version.TryParse(releaseVersion + ".0", out parsedVersion))
        {
            Console.Error.WriteLine("Release version must use major.minor.patch format.");
            return 2;
        }

        var assembly = AssemblyDefinition.ReadAssembly(Path.GetFullPath(inputPath),
            new ReaderParameters { ReadSymbols = false, InMemory = true });
        var module = assembly.MainModule;
        var plugin = module.Types.Single(type => type.FullName == "MFNVR.MFNVRPlugin");
        const string releaseName = "MFNVR";

        foreach (var field in plugin.Fields)
        {
            if (field.Name == "PluginName")
                field.Constant = releaseName;
            else if (field.Name == "PluginVersion")
                field.Constant = releaseVersion;
        }

        var pluginAttribute = plugin.CustomAttributes.Single(attribute =>
            attribute.AttributeType.FullName == "BepInEx.BepInPlugin");
        pluginAttribute.ConstructorArguments[1] = new CustomAttributeArgument(
            module.TypeSystem.String, releaseName);
        pluginAttribute.ConstructorArguments[2] = new CustomAttributeArgument(
            module.TypeSystem.String, releaseVersion);

        foreach (var method in plugin.Methods.Where(method => method.HasBody))
        {
            foreach (var instruction in method.Body.Instructions.Where(instruction =>
                         instruction.OpCode == OpCodes.Ldstr))
            {
                var text = instruction.Operand as string;
                if (text == "MFN VR Prototype")
                    instruction.Operand = releaseName;
                else if (text == "0.3.0" || text == "0.4.1")
                    instruction.Operand = releaseVersion;
            }
        }

        assembly.Name.Version = parsedVersion;
        foreach (var attribute in assembly.CustomAttributes)
        {
            var attributeName = attribute.AttributeType.FullName;
            if (attribute.ConstructorArguments.Count != 1)
                continue;
            if (attributeName == "System.Reflection.AssemblyFileVersionAttribute" ||
                attributeName == "System.Reflection.AssemblyInformationalVersionAttribute")
            {
                attribute.ConstructorArguments[0] = new CustomAttributeArgument(
                    module.TypeSystem.String, attributeName.EndsWith(
                        "AssemblyFileVersionAttribute", StringComparison.Ordinal)
                        ? releaseVersion + ".0"
                        : releaseVersion);
            }
        }

        assembly.Write(Path.GetFullPath(outputPath));
        Console.WriteLine("Updated MFNVR release identity to " + releaseVersion + ".");
        return 0;
    }

    private static MethodReference StaticMethod(ModuleDefinition module, TypeReference declaringType,
        string name, params TypeReference[] parameters)
    {
        var method = new MethodReference(name, module.TypeSystem.Void, declaringType)
        {
            HasThis = false,
            ExplicitThis = false,
            CallingConvention = MethodCallingConvention.Default
        };
        foreach (var parameter in parameters)
            method.Parameters.Add(new ParameterDefinition(parameter));
        return method;
    }

    private static MethodReference StaticFunction(ModuleDefinition module,
        TypeReference declaringType, string name, TypeReference returnType,
        params TypeReference[] parameters)
    {
        var method = new MethodReference(name, returnType, declaringType)
        {
            HasThis = false,
            ExplicitThis = false,
            CallingConvention = MethodCallingConvention.Default
        };
        foreach (var parameter in parameters)
            method.Parameters.Add(new ParameterDefinition(parameter));
        return method;
    }

    private static void RewriteHandsOverlay(MethodDefinition method,
        FieldDefinition leftTexture, FieldDefinition rightTexture,
        FieldDefinition rightAimValid,
        MethodReference configureHands)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, leftTexture));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, rightTexture));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, rightAimValid));
        il.Append(il.Create(OpCodes.Call, configureHands));
        il.Append(il.Create(OpCodes.Ret));
        method.Body.MaxStackSize = 6;
    }

    private static void AppendTrackedPairBridge(MethodDefinition method,
        FieldDefinition gameplayCamera, FieldDefinition gameplayHudCamera,
        FieldDefinition leftTexture, FieldDefinition rightTexture,
        FieldDefinition useComfortRig, MethodReference objectEquality,
        MethodReference bridgeMethod)
    {
        var il = method.Body.GetILProcessor();
        var returns = method.Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Ret)
            .ToArray();
        foreach (var ret in returns)
        {
            var anchor = il.Create(OpCodes.Nop);
            RetargetBranches(method, ret, anchor);
            il.InsertBefore(ret, anchor);
            var patch = new[]
            {
                il.Create(OpCodes.Ldarg_1),
                il.Create(OpCodes.Ldarg_2),
                il.Create(OpCodes.Ldarg_3),
                il.Create(OpCodes.Ldarg_0),
                il.Create(OpCodes.Ldfld, leftTexture),
                il.Create(OpCodes.Ldarg_0),
                il.Create(OpCodes.Ldfld, rightTexture),
                il.Create(OpCodes.Ldarg_1),
                il.Create(OpCodes.Ldarg_0),
                il.Create(OpCodes.Ldfld, gameplayCamera),
                il.Create(OpCodes.Call, objectEquality),
                il.Create(OpCodes.Ldarg_1),
                il.Create(OpCodes.Ldarg_0),
                il.Create(OpCodes.Ldfld, gameplayHudCamera),
                il.Create(OpCodes.Call, objectEquality),
                il.Create(OpCodes.Ldarg_0),
                il.Create(OpCodes.Ldfld, useComfortRig),
                il.Create(OpCodes.Call, bridgeMethod)
            };
            foreach (var instruction in patch)
                il.InsertBefore(ret, instruction);
        }
    }

    private static void AppendMirrorBridge(MethodDefinition method,
        FieldDefinition gameplayHudCamera, FieldDefinition gameplayCamera,
        FieldDefinition leftTexture, FieldDefinition useComfortRig,
        MethodReference ensureMirror)
    {
        var il = method.Body.GetILProcessor();
        var finalReturn = method.Body.Instructions.Last(instruction => instruction.OpCode == OpCodes.Ret);
        var anchor = il.Create(OpCodes.Nop);
        RetargetBranches(method, finalReturn, anchor);
        il.InsertBefore(finalReturn, anchor);
        var patch = new[]
        {
            il.Create(OpCodes.Ldarg_0),
            il.Create(OpCodes.Ldfld, gameplayHudCamera),
            il.Create(OpCodes.Ldarg_0),
            il.Create(OpCodes.Ldfld, gameplayCamera),
            il.Create(OpCodes.Ldarg_0),
            il.Create(OpCodes.Ldfld, leftTexture),
            il.Create(OpCodes.Ldarg_0),
            il.Create(OpCodes.Ldfld, useComfortRig),
            il.Create(OpCodes.Call, ensureMirror)
        };
        foreach (var instruction in patch)
            il.InsertBefore(finalReturn, instruction);
    }

    private static void RetargetBranches(MethodDefinition method, Instruction oldTarget,
        Instruction newTarget)
    {
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operand == oldTarget)
                instruction.Operand = newTarget;
            else if (instruction.Operand is Instruction[] targets)
            {
                for (var index = 0; index < targets.Length; index++)
                {
                    if (targets[index] == oldTarget)
                        targets[index] = newTarget;
                }
            }
        }
    }

    private static void AppendNativeMotionTick(MethodDefinition method,
        FieldDefinition instance, FieldDefinition trackingOriginPosition,
        FieldDefinition trackingOriginRotation, FieldDefinition renderRigPosition,
        FieldDefinition renderRigRotation, FieldDefinition hasTrackingOrigin,
        FieldDefinition useComfortRig, FieldDefinition rightAimValid,
        FieldDefinition rightAimWorldPosition, FieldDefinition rightAimWorldRotation,
        FieldDefinition currentRightGripLocalPosition,
        FieldDefinition currentRightAimLocalRotation, MethodReference tickNativeHands,
        MethodReference getMotionAimPosition, MethodReference getMotionAimRotation,
        MethodReference getMotionGripLocalPosition,
        MethodReference getMotionAimLocalRotation)
    {
        var il = method.Body.GetILProcessor();
        var ret = method.Body.Instructions.Last(instruction => instruction.OpCode == OpCodes.Ret);
        var skip = il.Create(OpCodes.Nop);
        il.InsertBefore(ret, il.Create(OpCodes.Ldsfld, instance));
        il.InsertBefore(ret, il.Create(OpCodes.Brfalse, skip));

        il.InsertBefore(ret, il.Create(OpCodes.Ldsfld, instance));
        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(ret, il.Create(OpCodes.Ldsfld, instance));
        il.InsertBefore(ret, il.Create(OpCodes.Ldfld, trackingOriginPosition));
        il.InsertBefore(ret, il.Create(OpCodes.Ldsfld, instance));
        il.InsertBefore(ret, il.Create(OpCodes.Ldfld, trackingOriginRotation));
        il.InsertBefore(ret, il.Create(OpCodes.Ldsfld, instance));
        il.InsertBefore(ret, il.Create(OpCodes.Ldfld, renderRigPosition));
        il.InsertBefore(ret, il.Create(OpCodes.Ldsfld, instance));
        il.InsertBefore(ret, il.Create(OpCodes.Ldfld, renderRigRotation));
        il.InsertBefore(ret, il.Create(OpCodes.Ldsfld, instance));
        il.InsertBefore(ret, il.Create(OpCodes.Ldfld, hasTrackingOrigin));
        il.InsertBefore(ret, il.Create(OpCodes.Ldsfld, instance));
        il.InsertBefore(ret, il.Create(OpCodes.Ldfld, useComfortRig));
        il.InsertBefore(ret, il.Create(OpCodes.Call, tickNativeHands));
        il.InsertBefore(ret, il.Create(OpCodes.Stfld, rightAimValid));

        il.InsertBefore(ret, il.Create(OpCodes.Ldsfld, instance));
        il.InsertBefore(ret, il.Create(OpCodes.Call, getMotionAimPosition));
        il.InsertBefore(ret, il.Create(OpCodes.Stfld, rightAimWorldPosition));

        il.InsertBefore(ret, il.Create(OpCodes.Ldsfld, instance));
        il.InsertBefore(ret, il.Create(OpCodes.Call, getMotionAimRotation));
        il.InsertBefore(ret, il.Create(OpCodes.Stfld, rightAimWorldRotation));

        il.InsertBefore(ret, il.Create(OpCodes.Ldsfld, instance));
        il.InsertBefore(ret, il.Create(OpCodes.Call, getMotionGripLocalPosition));
        il.InsertBefore(ret, il.Create(OpCodes.Stfld, currentRightGripLocalPosition));

        il.InsertBefore(ret, il.Create(OpCodes.Ldsfld, instance));
        il.InsertBefore(ret, il.Create(OpCodes.Call, getMotionAimLocalRotation));
        il.InsertBefore(ret, il.Create(OpCodes.Stfld, currentRightAimLocalRotation));
        il.InsertBefore(ret, skip);
        ExpandShortBranches(method);
        method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 8);
    }

    private static void ExpandShortBranches(MethodDefinition method)
    {
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.OpCode == OpCodes.Br_S) instruction.OpCode = OpCodes.Br;
            else if (instruction.OpCode == OpCodes.Brfalse_S) instruction.OpCode = OpCodes.Brfalse;
            else if (instruction.OpCode == OpCodes.Brtrue_S) instruction.OpCode = OpCodes.Brtrue;
            else if (instruction.OpCode == OpCodes.Beq_S) instruction.OpCode = OpCodes.Beq;
            else if (instruction.OpCode == OpCodes.Bge_S) instruction.OpCode = OpCodes.Bge;
            else if (instruction.OpCode == OpCodes.Bge_Un_S) instruction.OpCode = OpCodes.Bge_Un;
            else if (instruction.OpCode == OpCodes.Bgt_S) instruction.OpCode = OpCodes.Bgt;
            else if (instruction.OpCode == OpCodes.Bgt_Un_S) instruction.OpCode = OpCodes.Bgt_Un;
            else if (instruction.OpCode == OpCodes.Ble_S) instruction.OpCode = OpCodes.Ble;
            else if (instruction.OpCode == OpCodes.Ble_Un_S) instruction.OpCode = OpCodes.Ble_Un;
            else if (instruction.OpCode == OpCodes.Blt_S) instruction.OpCode = OpCodes.Blt;
            else if (instruction.OpCode == OpCodes.Blt_Un_S) instruction.OpCode = OpCodes.Blt_Un;
            else if (instruction.OpCode == OpCodes.Bne_Un_S) instruction.OpCode = OpCodes.Bne_Un;
            else if (instruction.OpCode == OpCodes.Leave_S) instruction.OpCode = OpCodes.Leave;
        }
    }

    private static void RewriteAsTrueReturn(MethodDefinition method)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Ret));
        method.Body.MaxStackSize = 1;
    }

}
