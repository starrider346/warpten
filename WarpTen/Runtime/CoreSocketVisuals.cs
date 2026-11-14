using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System;
using HarmonyLib;
using Ships.Interface.Model.Parts.Common;
using Ships.Visuals;
using UnityEngine;
using WarpTen.Simulation;
using static HarmonyLib.AccessTools;
using HarmonyEngine = HarmonyLib.Harmony;

namespace WarpTen.Runtime
{
    internal static class CoreSocketVisuals
    {
        public static void Install(HarmonyEngine harmony)
        {
            var method =
                Method(typeof(ShipPartInstancer), "CreateFloatingPartVisualParent",
                    new[] { typeof(GameObject), typeof(Transform) }) ??
                throw new MissingMethodException("Native part visual instancer unavailable.");
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(CoreSocketVisuals), nameof(AllocateSlots)));
        }

        private static IEnumerable<CodeInstruction> AllocateSlots(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var replacements = 0;
            for (var i = 0; i + 2 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldlen || code[i + 1].opcode != OpCodes.Conv_I4 ||
                    code[i + 2].opcode != OpCodes.Newarr || !Equals(code[i + 2].operand, typeof(Transform)))
                    continue;
                code[i].opcode = OpCodes.Ldarg_1;
                code.Insert(i + 1,
                    new CodeInstruction(OpCodes.Call, Method(typeof(CoreSocketVisuals), nameof(Capacity))));
                replacements++;
                i++;
            }

            if (replacements != 1)
                throw new InvalidOperationException(
                    "Native socket visual allocation changed; expected one Transform array allocation.");
            return code;
        }

        private static int Capacity(SocketObject[] sockets, GameObject part) => part.name == Content.ModId + ":core"
            ? SocketLayout.VisualCapacity(sockets.Select(s => s.Id).ToArray())
            : sockets.Length;
    }
}