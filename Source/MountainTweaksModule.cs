using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace Celeste.Mod.MountainTweaks {
    public class MountainTweaksModule : EverestModule {

        private static MountainTweaksModule? _instance;
        public static MountainTweaksModule Instance {
            get => _instance ??= new MountainTweaksModule();
            private set => _instance = value;
        }

        public override Type SettingsType => typeof(MountainTweaksModuleSettings);
        public static MountainTweaksModuleSettings Settings => (MountainTweaksModuleSettings) Instance._Settings;

        public override Type SessionType => typeof(MountainTweaksModuleSession);
        public static MountainTweaksModuleSession Session => (MountainTweaksModuleSession) Instance._Session;

        public override Type SaveDataType => typeof(MountainTweaksModuleSaveData);
        public static MountainTweaksModuleSaveData SaveData => (MountainTweaksModuleSaveData) Instance._SaveData;

        private List<IDisposable> _hooks = [];

        public MountainTweaksModule() {
            Instance = this;
#if DEBUG
            // debug builds use verbose logging
            Logger.SetLogLevel(nameof(MountainTweaksModule), LogLevel.Verbose);
#else
            // release builds use info logging to reduce spam in log files
            Logger.SetLogLevel(nameof(MountainTweaksModule), LogLevel.Info);
#endif
        }

        public override void Load() {
            if (Settings.DumpDMDs.EnabledGlobally) {
                DumpDMDsEnable();
            }

            if (Settings.DoNotLoseFullscreen.EnabledGlobally) {
                LoseFullscreenPatchEnable();
            }

            if (Settings.DisableInliningPushSprite) {
                DisableInliningPushSpriteEnable();
            }
        }

        [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Reflection types should have a custom naming scheme")]
        private void DumpDMDsEnable() {
            Directory.CreateDirectory("GeneratedDMDs");
            
            MethodInfo? m_DMD_Generate = typeof(DynamicMethodDefinition).GetMethod("Generate", BindingFlags.Instance | BindingFlags.Public, [typeof(object)]);
            if (m_DMD_Generate == null) {
                Logger.Log(LogLevel.Error, nameof(MountainTweaksModule), $"Could not find method Generate in {nameof(DynamicMethodDefinition)}!");
                return;
            }
            Hook _dmdGenerateHook = new(m_DMD_Generate, HookDelegates.DumpDMDsHook);
            _hooks.Add(_dmdGenerateHook);
        }

        [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Reflection types should have a custom naming scheme")]
        private void LoseFullscreenPatchEnable() {
            foreach (string targetType in (ReadOnlySpan<string>) ["Microsoft.Xna.Framework.SDL2_FNAPlatform", "Microsoft.Xna.Framework.SDL3_FNAPlatform"]) {
                Type? t_SDL2_FNAPlatform = typeof(Game).Assembly.GetType(targetType);
                if (t_SDL2_FNAPlatform == null) {
                    // Sometimes it's intended for one sdl platform to not be there, so it's not really an error
                    Logger.Log(LogLevel.Warn, nameof(MountainTweaksModule), $"Could not load {targetType}!");
                    return;
                }

                MethodInfo? m_PollEvents = t_SDL2_FNAPlatform.GetMethod("PollEvents", BindingFlags.Static | BindingFlags.Public);
                if (m_PollEvents == null) {
                    Logger.Log(LogLevel.Error, nameof(MountainTweaksModule), $"Could not find method PollEvents in {targetType}!");
                    return;
                }

                ILHook _loseFullscreenPatch = new(m_PollEvents, HookDelegates.LoseFullscreenPatch);
                _hooks.Add(_loseFullscreenPatch);
            }
        }
        
        [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Reflection types should have a custom naming scheme")]
        private static void DisableInliningPushSpriteEnable() {
            Type t_SpriteBatch = typeof(SpriteBatch);
            
            MethodInfo? m_PushSprite = t_SpriteBatch.GetMethod("PushSprite", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m_PushSprite == null) {
                Logger.Log(LogLevel.Error, nameof(MountainTweaksModule), $"Could not find method PushSprite in {nameof(SpriteBatch)}!");
                return;
            }
            
            MonoMod.Core.Platforms.PlatformTriple.Current.TryDisableInlining(m_PushSprite);
        }

        public override void Unload() {
            foreach (IDisposable hook in _hooks) {
                hook.Dispose();
            }
        }
    }
}
