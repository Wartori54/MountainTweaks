using Monocle;

namespace Celeste.Mod.MountainTweaks;

public static class Commands {
    [Command("mt_hook_prof_start", "")]
    public static void HookProfStart() {
        HookOverheadProfiler.Instance.Start();
    }

    [Command("mt_hook_prof_stop", "")]
    public static void HookProfStop() {
        HookOverheadProfiler.Instance.Stop();
    }

    [Command("mt_print_all_detours", "")]
    public static void PrintAllDetours() {
        HookOverheadProfiler.PrintAllDetours();
    }
}