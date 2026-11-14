using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System;
using HarmonyLib;
using Ships.Physics;
using Ships;
using UnityEngine;
using WarpTen.Simulation;
using static HarmonyLib.AccessTools;
using HarmonyEngine = HarmonyLib.Harmony;

namespace WarpTen.Runtime
{
    internal static class WarpRotationLimit
    {
        public static void Install(HarmonyEngine harmony)
        {
            var method = Method(typeof(ShipWarpPhysics), "UpdateWarpRotation") ??
                         throw new MissingMethodException("Native warp rotation unavailable.");
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(WarpRotationLimit), nameof(LimitVelocity)));
        }

        private static IEnumerable<CodeInstruction> LimitVelocity(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var velocity = Field(typeof(ShipWarpPhysics), "_warpAngularVelocity");
            var ship = Field(typeof(ShipWarpPhysics), "_ship");
            if (velocity == null || ship == null)
                throw new MissingFieldException("Native warp rotation fields changed.");
            var replacements = 0;
            for (var i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Stfld || !Equals(code[i].operand, velocity))
                    continue;
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.MoveLabelsFrom(code[i]);
                code.InsertRange(i,
                    new[]
                    {
                        load, new CodeInstruction(OpCodes.Ldfld, ship),
                        new CodeInstruction(OpCodes.Call, Method(typeof(WarpRotationLimit), nameof(Clamp)))
                    });
                i += 3;
                replacements++;
            }

            if (replacements != 1)
                throw new InvalidOperationException(
                    "Native warp rotation changed; expected one angular velocity integration.");
            return code;
        }

        private static Vector3 Clamp(Vector3 velocity, TrackedShipServer ship)
        {
            if (!DriveRuntime.OwnsWarp(ship))
                return velocity;
            var scale = WarpRotationPolicy.Scale(velocity.x, velocity.y, velocity.z);
            return scale == 0 ? Vector3.zero : velocity * (float)scale;
        }
    }
}