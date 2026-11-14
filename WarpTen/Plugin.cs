using System.IO;
using System;
using MelonLoader.Utils;
using MelonLoader;
using Newtonsoft.Json;
using StellarStriderLib;
using UnityEngine.InputSystem;
using UnityEngine;
using WarpTen.Runtime;
using WarpTen.Simulation;

[assembly: MelonInfo(typeof(WarpTen.Plugin), "Warp10", "1.0.0", "StriderDev")]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]
[assembly: MelonAdditionalDependencies("StellarStriderLib")]
[assembly: MelonOptionalDependencies("Compute")]

namespace WarpTen
{
    public sealed class Plugin : MelonMod
    {
        private ModRegistry registry;

        public sealed class Settings
        {
            public Tuning Balance = new();
        }

        public override void OnInitializeMelon()
        {
            var path = Path.Combine(MelonEnvironment.UserDataDirectory, "WarpTen.json");
            var settings = File.Exists(path)
                ? JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path))
                : new Settings();
            if (settings?.Balance == null)
                throw new InvalidOperationException("WarpTen.json must contain valid settings.");
            settings.Balance.Validate();
            var oldReserve = settings.Balance.ReserveSeconds == 5;
            if (oldReserve)
                settings.Balance.ReserveSeconds = 2;
            if (!File.Exists(path) || oldReserve)
                File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
            DriveRuntime.Tuning = settings.Balance;
            registry = StriderRegistry.Create(Content.ModId, typeof(Plugin).Assembly);
            OptionalPeripheral.Initialize();
            Content.Register(registry);
            registry.WorldBound += Content.Bind;
            registry.WorldUnbinding += Clear;
            NativeHooks.Install(HarmonyInstance);
            ConduitBuildTool.Install(HarmonyInstance);
            CoilBuildTool.Install(HarmonyInstance);
            WarpTunnel.Install(HarmonyInstance);
            InfuserHover.Install(HarmonyInstance);
            CorePanel.Install(HarmonyInstance);
            MelonLogger.Msg(
                "WarpTen ready.");
        }

        public override void OnUpdate()
        {
            var keyboard = Keyboard.current;
            if (!Application.isFocused || keyboard == null)
                return;
            if (keyboard.f9Key.wasPressedThisFrame)
                NacelleDebug.Toggle();
        }

        public override void OnLateUpdate()
        {
            NacelleDebug.LateUpdate();
            WarpTunnel.LateUpdate();
        }

        public override void OnGUI()
        {
            NacelleDebug.OnGUI();
        }

        private static void Clear()
        {
            CorePanel.Clear();
            ConduitBuildTool.Clear();
            CoilBuildTool.Clear();
            WarpTunnel.Clear();
            NacelleDebug.Clear();
            NacelleGeometry.Clear();
            DriveRuntime.Clear();
            Content.Clear();
        }

        public override void OnDeinitializeMelon()
        {
            if (registry != null)
            {
                registry.WorldBound -= Content.Bind;
                registry.WorldUnbinding -= Clear;
            }

            CorePanel.Shutdown();
            WarpTunnel.Shutdown();
            HarmonyInstance.UnpatchSelf();
            Clear();
        }
    }
}