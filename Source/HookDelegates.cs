using System;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;

namespace Celeste.Mod.MountainTweaks;

public static class HookDelegates {

    private static string LoseFullscreenPatchDelegate() {
        // We can use any string different from "x11"
        return MountainTweaksModule.Settings.DoNotLoseFullscreen.Enabled ? "not x11" : "x11";
    }
    
    private static int _dmdIndex = 0;
    // Write the method MSIL representation in a semi fancy way on a txt before they get compiled and exported to a DM.
    // Do it before specifically since the DMD is modified once it gets copied to a DM.
    internal static MethodInfo DumpDMDsHook(Func<DynamicMethodDefinition, object?, MethodInfo> orig, DynamicMethodDefinition dmd, object ctx) {
        if (!MountainTweaksModule.Settings.DumpDMDs.Enabled) return orig(dmd, ctx);
                
        Logger.Log(LogLevel.Info, nameof(MountainTweaksModule), $"Generating dmd {dmd.Definition.FullName}");
        using FileStream fileStream = File.OpenWrite(Path.Combine("GeneratedDMDs", $"{_dmdIndex}.dmd"));
        _dmdIndex++;
        using StreamWriter streamWriter = new(fileStream);
        Util.PrettyLogAllInstrs(streamWriter, dmd.Definition.Body);
        return orig(dmd, ctx);
    }
    
    // FNA is hardcoded to always lose fullscreen when the window loses focus, disable that
    internal static void LoseFullscreenPatch(ILContext ctx) {
        ILCursor il = new(ctx);
        ILCursor? targetIl = null;
        while (true) {
            if (!il.TryGotoNext(
                    MoveType.Before, 
                    ins => ins.Match(OpCodes.Call) 
                           && ((MethodReference)ins.Operand).Name == "SDL_GetCurrentVideoDriver",
                    ins => ins.MatchLdstr("x11")))
                break;
            targetIl = il.Clone();
        }

        if (targetIl == null) {
            Logger.Log(LogLevel.Error, nameof(MountainTweaksModule), "Could not find instructions for x11 patch!");
            return;
        }
        // targetIl is now at the call instruction
        targetIl.GotoNext(); // so move to the ldstr

#pragma warning disable CL0005
        targetIl.Remove(); // nuke it and replace with a fancy method
#pragma warning restore CL0005
        targetIl.EmitDelegate(LoseFullscreenPatchDelegate);
    }
}