using System;
using System.Diagnostics.CodeAnalysis;
using YamlDotNet.Serialization;

namespace Celeste.Mod.MountainTweaks {
    public class MountainTweaksModuleSettings : EverestModuleSettings {
        public DoubleBarrierBool DoNotLoseFullscreen { get; set; } = new();
        
        public void CreateDoNotLoseFullscreenEntry(TextMenu menu, bool _) {
            CreateDoubleBarrierBool(menu, DoNotLoseFullscreen, nameof(DoNotLoseFullscreen), true);
        }

        public DoubleBarrierBool DumpDMDs { get; set; } = new();

        public void CreateDumpDMDsEntry(TextMenu menu, bool _) {
            CreateDoubleBarrierBool(menu, DumpDMDs, nameof(DumpDMDs), true);
        }
        
        [SettingName("MODOPTIONS_MOUNTAINTWEAKS_DisableInliningPushSprite")]
        [SettingSubText("MODOPTIONS_MOUNTAINTWEAKS_DisableInliningPushSpriteEnabled_DESC")]
        public bool DisableInliningPushSprite { get; set; }

        // Generate the menus ourselves for the custom type
        private void CreateDoubleBarrierBool(TextMenu menu, DoubleBarrierBool element, string name, bool desc = false) {
            element.Init();
            TextMenu.Option<bool> globalEntry = new TextMenu.OnOff(("MODOPTIONS_MOUNTAINTWEAKS_" + name.ToUpperInvariant() + "_GLOBAL").DialogClean(), element.EnabledGlobally);
            TextMenu.Option<bool> localEntry = new TextMenu.OnOff(("MODOPTIONS_MOUNTAINTWEAKS_" + name.ToUpperInvariant()).DialogClean(), element.Enabled && element.InitialGlobal.Value).Change(v => element.Enabled = v);
            localEntry.Disabled = !element.InitialGlobal.Value || !element.EnabledGlobally;
            globalEntry.Change(v => {
                element.EnabledGlobally = v;
                localEntry.Disabled = !v || !element.InitialGlobal.Value;
                if (localEntry.Disabled) {
                    localEntry.Index = 0;
                    element.Enabled = false;
                }
            });
            menu.Add(globalEntry);
            menu.Add(localEntry);
            if (!element.InitialGlobal.Value) {
                globalEntry.AddDescription(menu, "MODOPTIONS_MOUNTAINTWEAKS_RESTART_DBB".DialogClean());
            }
            globalEntry.AddDescription(menu, $"MODOPTIONS_MOUNTAINTWEAKS_GLOBAL_DBB_DESC".DialogClean());
            if (desc)
                (element.InitialGlobal.Value ? localEntry : globalEntry).AddDescription(menu, $"MODOPTIONS_MOUNTAINTWEAKS_{name.ToUpperInvariant()}_DESC".DialogClean());
            
        }

        public record DoubleBarrierBool {
            public bool EnabledGlobally;
            public bool Enabled;
            [YamlIgnore] public bool? InitialGlobal;
            [MemberNotNull(nameof(InitialGlobal))]
            public void Init() {
                InitialGlobal ??= EnabledGlobally;
            }
        }
    }
}
