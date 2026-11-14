using System.Collections.Generic;
using System.Reflection;
using System;
using HarmonyLib;
using Players.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Parts.Common;
using Ships;
using StellarStriderLib;
using UI.Interaction;
using static HarmonyLib.AccessTools;
using HarmonyEngine = HarmonyLib.Harmony;

namespace WarpTen.Runtime
{
    internal static class InfuserHover
    {
        private static readonly Dictionary<PartContext, PartInstance> Infusers = new();
        private static readonly FieldInfo ContextField = Field(typeof(PartInteraction), "PartContext");
        private static PartInteraction hovered;

        internal static TrackedShipClient HoveredShip
        {
            get
            {
                if (hovered == null || !hovered.isActiveAndEnabled || !hovered.IsActive ||
                    !Infusers.TryGetValue((PartContext)ContextField.GetValue(hovered), out var part) || !part.IsActive)
                    return null;
                return part.Ship is TrackedShipClient ship && !ship.IsShipDestroyed && ship.IsVisualLoaded &&
                       ship.VisualObject != null
                    ? ship
                    : null;
            }
        }

        internal static void Install(HarmonyEngine harmony)
        {
            if (ContextField == null)
                throw new MissingFieldException(typeof(PartInteraction).FullName, "PartContext");
            harmony.Patch(Method(typeof(InteractionTooltipsActivator), "OnPartHover"),
                postfix: new HarmonyMethod(typeof(InfuserHover), nameof(Hover)));
            harmony.Patch(Method(typeof(InteractionTooltipsActivator), "OnPartLeave"),
                postfix: new HarmonyMethod(typeof(InfuserHover), nameof(Leave)));
            harmony.Patch(Method(typeof(InteractionTooltipsActivator), "OnDisable"),
                postfix: new HarmonyMethod(typeof(InfuserHover), nameof(Hide)));
        }

        internal static void Observe(PartInstance part)
        {
            if (!part.IsServer && Content.InfuserId != 0 && part.Part.Settings.id == Content.InfuserId)
                Infusers[part.Id] = part;
        }

        internal static void Remove(PartInstance part)
        {
            if (!part.IsServer)
                Infusers.Remove(part.Id);
        }

        private static void Hover(IInteractableObject __0) => hovered = __0 as PartInteraction;

        private static void Leave(IInteractableObject __0)
        {
            if (ReferenceEquals(hovered, __0))
                Hide();
        }

        private static void Hide() => hovered = null;

        internal static void Clear()
        {
            Hide();
            Infusers.Clear();
        }
    }
}