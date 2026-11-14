using System.Collections.Generic;
using System.Linq;
using System;
using MelonLoader;
using Newtonsoft.Json.Linq;
using Ships.Cables.Fluids;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.Parts;
using Ships;
using StellarStriderLib;
using UnityEngine;
using WarpTen.Simulation;
using WorldProvision.Interface.Services;
using WorldTracking.Interface.Model.Spaces;
using WorldTracking.Interface.Model;
using WorldTracking.Interface.Services;
using WorldTracking.Spaces;
using static System.Math;
using static Core.Services.ServiceLocator;

namespace WarpTen.Runtime
{
    internal static class DriveRuntime
    {
        private static readonly Dictionary<PartContext, PartInstance> Parts = new();
        private static readonly Dictionary<uint, DriveSimulation> Drives = new();
        private static readonly Dictionary<uint, (uint Space, PreciseWorldPosition Position)> WarpPositions = new();
        private static readonly Dictionary<uint, WarpEntryPolicy> Entries = new();
        private static readonly HashSet<uint> OwnedShips = new();
        public static Tuning Tuning = new();
        public static uint Hydrogen => Content.HydrogenId;

        public static bool TryGet(uint id, out DriveSimulation drive) => Drives.TryGetValue(id, out drive);

        public static void Add(PartInstance p)
        {
            if (!p.IsServer)
                CoreInventory.Add(p);
            if (p.IsServer)
            {
                Parts[p.Id] = p;
                if (Content.IsCore(p.Part) && p.Ship is TrackedShipServer ship)
                {
                    Drives.Remove(p.Id.ShipId);
                    OwnedShips.Add(p.Id.ShipId);
                    CoreInventory.Add(p);
                }
            }
        }

        public static void Remove(PartInstance p)
        {
            CoreInventory.Remove(p);
            InfuserHover.Remove(p);
            NacelleDebug.Remove(p);
            if (Content.IsCore(p.Part))
                TelemetrySnapshots.Remove(p.Id.ShipId, p.Id.PartId);
            if (Content.IsCore(p.Part))
                PlasmaConduits.Forget(p.Ship as TrackedShip);
            if (Content.IsCore(p.Part))
                NacelleGeometry.Forget(p.Ship as TrackedShip);
            if (p.IsServer)
            {
                Parts.Remove(p.Id);
                if (Content.IsCore(p.Part))
                    Drives.Remove(p.Id.ShipId);
            }
        }

        public static void Clear()
        {
            CoreInventory.Clear();
            InfuserHover.Clear();
            TelemetrySnapshots.Clear();
            PlasmaConduits.Clear();
            Parts.Clear();
            Drives.Clear();
            Entries.Clear();
            WarpPositions.Clear();
            OwnedShips.Clear();
        }

        private static PartInstance Instance(uint ship, ushort part) =>
            Parts.TryGetValue(new PartContext(ship, part), out var p) && p.IsActive ? p : null;

        private static FluidGroupServerState Fluid(uint ship, ushort part, byte port)
        {
            var tracker = GetService<FluidsServerTracker>();
            return tracker != null &&
                   tracker.TryGetGroupFromSocket(new EntityPartSocket(ship, part, port), out var group)
                ? group
                : null;
        }

        private static double Available(FluidGroupServerState g, uint item) =>
            g != null && g.FluidItemId == item && Tuning.Finite(g.Available) ? Max(0, g.Available) : 0;

        private static double ConnectedDemand(TrackedShipServer ship, ushort core)
        {
            var network = PlasmaConduits.Find(ship);
            if (ship.StatefulParts.Count(Content.IsCore) != 1)
                return 0;
            var demand = 0d;
            foreach (var infuser in ship.StatefulParts.Where(p => p.Settings.id == Content.InfuserId))
                demand += PlasmaDelivery.Demand(network.Connected(core, infuser.Id),
                    NacelleGeometry.Feed(ship, infuser).Coils, Tuning.CoilPlasmaPerSecond);
            return demand;
        }

        public static void Complete(TrackedShipServer ship, float dt)
        {
            if (!StriderRegistry.WorldReady || Content.CoreId == 0 || !Tuning.Finite(dt) || dt <= 0)
                return;
            var hydrogenRate = 0d;
            var plasmaRate = 0d;
            foreach (var part in ship.StatefulParts.Where(Content.IsCore).ToArray())
            {
                CoreInventory.RetirePower(ship.Id, part.Id);
                var instance = Instance(ship.Id, part.Id);
                if (instance == null || !CoreInventory.Read(instance, out var grade))
                    continue;
                var input = Fluid(ship.Id, part.Id, 2);
                var produced =
                    MatrixPolicy.Produce(grade, dt, Available(input, Hydrogen), ConnectedDemand(ship, part.Id));
                if (produced.Hydrogen > 0)
                {
                    input.RemoveFluid((float)produced.Hydrogen);
                }

                hydrogenRate = produced.Hydrogen / dt;
                plasmaRate = produced.Plasma / dt;
            }

            var cores = ship.StatefulParts.Where(Content.IsCore).ToArray();
            if (cores.Length == 0)
            {
                var unpowered = ship.StatefulParts.Where(p => p.Settings.id == Content.InfuserId).ToArray();
                var optimal = unpowered.Length == 0
                    ? 0
                    : Max(1,
                        Ceiling(Tuning.RecommendedPairs * Max(1, ship.GetMassIncludingConnectedShips()) /
                                Tuning.ReferenceMass));
                foreach (var infuser in unpowered)
                {
                    var feed = NacelleGeometry.Feed(ship, infuser);
                    Instance(ship.Id, infuser.Id)?.State.Set("status",
                        InfuserDiagnostics.Describe(optimal, feed.Coils,
                            new[] { "Install one Warp Core and two aligned infusers", feed.Issue }, false, "Offline"));
                }

                Drives.Remove(ship.Id);
                Entries.Remove(ship.Id);
                WarpPositions.Remove(ship.Id);
                return;
            }

            var corePart = cores[0];
            var coreInstance = Instance(ship.Id, corePart.Id);
            if (coreInstance == null)
                return;
            var geometry = NacelleGeometry.Find(ship);
            var infusers = ship.StatefulParts.Where(p => p.Settings.id == Content.InfuserId).OrderBy(p => p.Id)
                .ToArray();
            var flows = new double[Max(2, infusers.Length)];
            var feeds = new InfuserFeed[infusers.Length];
            for (var i = 0; i < infusers.Length; i++)
                feeds[i] = NacelleGeometry.Feed(ship, infusers[i]);
            var network = PlasmaConduits.Find(ship);
            var rates = feeds.Select(f => Tuning.NacelleRate(f.Coils)).ToArray();
            var totalDemand = ConnectedDemand(ship, corePart.Id);
            for (var i = 0; i < infusers.Length; i++)
            {
                var connected = network.Connected(corePart.Id, infusers[i].Id);
                flows[i] = PlasmaDelivery.Share(plasmaRate, connected ? rates[i] : 0, totalDemand);
                Instance(ship.Id, infusers[i].Id)?.State.Set(new JObject
                    { ["plasmaConnected"] = connected, ["plasmaDemand"] = rates[i], ["plasmaDelivered"] = flows[i] });
            }

            foreach (var part in ship.StatefulParts)
                if (PlasmaConduits.Kind(part, out var kind) && kind >= PlasmaPartKind.Straight)
                {
                    var connected = network.Connected(corePart.Id, part.Id);
                    Instance(ship.Id, part.Id)?.State.Set(new JObject
                    {
                        ["plasmaConnected"] = connected,
                        ["charge"] = connected && totalDemand > 0 ? Min(1, plasmaRate / totalDemand) : 0
                    });
                }

            if (!Drives.TryGetValue(ship.Id, out var drive))
                Drives[ship.Id] = drive = new DriveSimulation();
            var spaces = GetService<ISpacesServerProvider>();
            var space = spaces.GetSpace(ship.SpaceId);
            var inWarp = space is WarpSpace warp && warp.SourceShipId == ship.Id;
            var canWarp = GetService<IWorldEnvironmentServerProvider>().CanWarp(ship.SpaceId) &&
                          !(space is ShipRelativeSpace);
            if (!inWarp && drive.Phase == DrivePhase.Warping)
                drive.Reset();
            double shipMass = ship.GetMassIncludingConnectedShips();
            drive.Step(Tuning, dt, true, geometry.Blocker, shipMass, geometry.Strength(Tuning), flows[0], flows[1],
                inWarp, canWarp, geometry.LeftCoils.Count, geometry.RightCoils.Count);
            if (!Entries.TryGetValue(ship.Id, out var entry))
                Entries[ship.Id] = entry = new WarpEntryPolicy();
            var previousFailure = entry.Failure;
            var entered = entry.TryEnter(drive.Phase, inWarp, canWarp, Time.realtimeSinceStartupAsDouble,
                () => GetService<SpacesServer>().CreateWarpSpaceForShip(ship.Id, corePart.Id),
                () => spaces.GetSpace(ship.SpaceId) is WarpSpace active && active.SourceShipId == ship.Id &&
                      active.SourcePartId == corePart.Id);
            if (entered)
            {
                GetService<ActiveSpacesServerTracker>().UpdateActiveSpaces();
                inWarp = true;
                drive.ConfirmWarpEntry();
                MelonLogger.Msg(
                    $"WarpTen ship {ship.Id} entered native warp space {ship.SpaceId} using core {corePart.Id}.");
            }
            else if (entry.Failure != null && entry.Failure != previousFailure)
                MelonLogger.Error($"Ship {ship.Id}: {entry.Failure}");

            var currentSpace = spaces.GetSpace(ship.SpaceId);
            var warpRestriction = WarpEligibility.Restriction(currentSpace, canWarp, inWarp);
            var displayStatus = entry.Failure ??
                                (drive.Status == "Native warp restrictions" ? warpRestriction : drive.Status);
            double? measuredSpeed = inWarp ? null : 0, environmentCap = null;
            if (inWarp && spaces.GetSpace(ship.SpaceId) is WarpSpace moving)
            {
                if (WarpPositions.TryGetValue(ship.Id, out var previous) && previous.Space == ship.SpaceId)
                    measuredSpeed = moving.Position.DeltaDouble(previous.Position).Magnitude / dt;
                WarpPositions[ship.Id] = (ship.SpaceId, moving.Position);
                environmentCap = GetService<IWorldEnvironmentServerProvider>()
                    .GetMaxWarpSpeedAtPosition(moving.Position);
            }
            else
                WarpPositions.Remove(ship.Id);

            var optimalPairs = Max(1, Ceiling(drive.Recommended / Tuning.PairStrength));
            var optimalCoils = optimalPairs * 2;
            var coilTarget = $"Optimal coils (full field): {optimalCoils:F0} total ({optimalPairs:F0} per nacelle)";
            var hasMatrix = CoreInventory.Read(coreInstance, out var matrixGrade);
            if (hasMatrix && optimalCoils * Tuning.CoilPlasmaPerSecond > matrixGrade.MaxPlasmaRate)
                coilTarget += "\nFull coil flow exceeds one core's plasma capacity";
            var connectionStatus =
                $"Conduits: L {(infusers.Length > 0 && network.Connected(corePart.Id, infusers[0].Id) ? "connected" : "NO CONNECTION")}, R {(infusers.Length > 1 && network.Connected(corePart.Id, infusers[1].Id) ? "connected" : "NO CONNECTION")}";
            var feedStatus = string.Join("\n",
                infusers.Select((p, i) =>
                    $"Infuser {p.Id}: {feeds[i].Coils} valid coils; {feeds[i].Issue ?? "coil chain ready"}"));
            var productionStatus = plasmaRate > 0
                ? "Producing"
                : !hasMatrix
                    ? "NO OUTPUT: install an Annihilation Matrix"
                    : totalDemand <= 0
                        ? "NO OUTPUT: no connected valid coil demand (check infuser diagnostics)"
                        : Available(Fluid(ship.Id, corePart.Id, 2), Hydrogen) <= 0
                            ? "NO OUTPUT: native hydrogen unavailable at core socket 2"
                            : "NO OUTPUT: no usable hydrogen flow";
            var matrixStatus = hasMatrix
                ? $"Matrix {matrixGrade.Code} | H2:plasma {matrixGrade.HydrogenPerPlasma:F0}:1 | max {matrixGrade.MaxPlasmaRate:F1}/s ({matrixGrade.RatedCoils} coils)"
                : "Matrix slot empty";
            var issues = new List<string>();
            if (geometry.Blocker != null)
                issues.Add(geometry.Blocker);
            if (!hasMatrix)
                issues.Add("Install an Annihilation Matrix in the core");
            var infuserIssues = new List<(int Index, string Message)>();
            for (var i = 0; i < infusers.Length; i++)
            {
                if (feeds[i].Issue != null)
                    infuserIssues.Add((i, feeds[i].Issue));
                if (!network.Connected(corePart.Id, infusers[i].Id))
                    infuserIssues.Add((i, "connect a plasma conduit to the core"));
                else if (rates[i] > 0 && !WarpSpeedPolicy.Sustained(flows[i] / rates[i], Tuning.MinimumFlowFraction))
                    infuserIssues.Add((i,
                        $"needs at least {rates[i] * Tuning.MinimumFlowFraction:F2} plasma/s (receiving {flows[i]:F2})"));
            }

            if (hasMatrix && totalDemand > 0 && plasmaRate <= 0)
                issues.Add(productionStatus);
            if (warpRestriction != null)
                issues.Add(warpRestriction);
            if (drive.Phase == DrivePhase.Blocked && geometry.Blocker == null &&
                drive.Status != "Insufficient plasma / reserve depleted")
                issues.Add(displayStatus);
            if (entry.Failure != null)
                issues.Add(entry.Failure);
            var boundsBlocked = geometry.Clearance != null && (!geometry.WidthValid || geometry.ClearanceBlocks > 0);
            var progress = drive.Phase == DrivePhase.Charging
                ? $"Charging: {drive.Timer:F1}/{Tuning.ChargeSeconds:F0}s"
                : drive.Phase.ToString();
            var advisory = hasMatrix && optimalCoils * Tuning.CoilPlasmaPerSecond > matrixGrade.MaxPlasmaRate
                ? $"Grade {matrixGrade.Code} cannot supply all optimal coils at full flow"
                : null;
            if (feeds.Length == 2 && feeds[0].Coils != feeds[1].Coils)
                advisory = (advisory == null ? "" : advisory + "\n") +
                           $"Only {geometry.Pairs.Count} matched coil pairs contribute; balance both chains";
            for (var i = 0; i < infusers.Length; i++)
            {
                var localIssues = issues.Concat(infuserIssues.Select(issue =>
                    (issue.Index == i ? "This infuser: " : "Other infuser: ") + issue.Message));
                Instance(ship.Id, infusers[i].Id)?.State.Set("status",
                    InfuserDiagnostics.Describe(optimalPairs, feeds[i].Coils, localIssues, boundsBlocked, progress,
                        advisory));
            }

            var status =
                $"{matrixStatus}\n{coilTarget}\n{connectionStatus}\n{feedStatus}\n{productionStatus}\n{displayStatus} | pairs {geometry.Pairs.Count} | charge {drive.Timer:F1}/{Tuning.ChargeSeconds:F0}s\nField {drive.Strength:F2}/{drive.Recommended:F2} | target {drive.Speed:E2} / ceiling {drive.SpeedCeiling:E2} m/s | native {(inWarp ? "FTL" : "sublight")}\nMeasured FTL {measuredSpeed:E2} m/s | environment cap {environmentCap:E2} m/s\nH2 {hydrogenRate:F3}/s | output {plasmaRate:F2}/{totalDemand:F2} demand/s | nacelles {flows[0]:F2}/{(rates.Length > 0 ? rates[0] : 0):F2}, {flows[1]:F2}/{(rates.Length > 1 ? rates[1] : 0):F2}/s";
            coreInstance.State.Set(new JObject
            {
                ["status"] = status,
                ["charge"] = Min(drive.LeftCharge, drive.RightCharge),
                ["plasmaOutput"] = plasmaRate,
                ["plasmaDemand"] = totalDemand,
                ["phase"] = drive.Phase.ToString(),
                ["chargeRemaining"] = Max(0, Tuning.ChargeSeconds - drive.Timer)
            });
            if (infusers.Length > 0)
                PublishCharge(ship.Id, infusers[0].Id, drive.LeftCharge);
            if (infusers.Length > 1)
                PublishCharge(ship.Id, infusers[1].Id, drive.RightCharge);
            var coilCharges = geometry.LeftCoils.ToDictionary(p => p.Id, _ => drive.LeftCharge);
            foreach (var p in geometry.RightCoils)
                coilCharges[p.Id] = drive.RightCharge;
            foreach (var p in ship.StatefulParts.Where(p => p.Settings.id == Content.CoilId))
                PublishCharge(ship.Id, p.Id, coilCharges.TryGetValue(p.Id, out var charge) ? charge : 0);

            JObject Infuser(int i, double? charge) => new JObject
            {
                ["infuserId"] = infusers[i].Id,
                ["charge"] = charge,
                ["validCoils"] = feeds[i].Coils,
                ["plasmaDemand"] = rates[i],
                ["deliveredRate"] = flows[i],
                ["conduitConnected"] = network.Connected(corePart.Id, infusers[i].Id),
                ["issue"] = feeds[i].Issue
            };

            var diagnostics = new JObject
            {
                ["layoutBlocker"] = geometry.Blocker,
                ["warpEntryFailure"] = entry.Failure,
                ["warpRestriction"] = warpRestriction,
                ["recommendedPairs"] = optimalPairs,
                ["recommendedCoils"] = optimalCoils,
                ["recommendedCoilsPerNacelle"] = optimalPairs,
                ["leftHullOverhang"] = geometry.Clearance == null ? (double?)null : geometry.LeftOverhang,
                ["rightHullOverhang"] = geometry.Clearance == null ? (double?)null : geometry.RightOverhang,
                ["clearanceBlocks"] = geometry.Clearance == null ? (int?)null : geometry.ClearanceBlocks,
                ["productionStatus"] = productionStatus,
                ["infusers"] = new JArray(infusers.Select((_, i) =>
                    Infuser(i, i == 0 ? drive.LeftCharge : i == 1 ? drive.RightCharge : null)))
            };
            foreach (var core in cores)
            {
                var installed = CoreInventory.Read(Instance(ship.Id, core.Id), out var grade);
                var sample = new JObject
                {
                    ["phase"] = drive.Phase.ToString(),
                    ["status"] = displayStatus,
                    ["inWarp"] = inWarp,
                    ["charge"] = Min(drive.LeftCharge, drive.RightCharge),
                    ["chargeSeconds"] = drive.Timer,
                    ["chargeRemainingSeconds"] = Max(0, Tuning.ChargeSeconds - drive.Timer),
                    ["targetSpeed"] = drive.Speed,
                    ["measuredSpeed"] = measuredSpeed,
                    ["speedCeiling"] = drive.SpeedCeiling,
                    ["environmentSpeedCap"] = environmentCap,
                    ["fullChargeSpeedCeiling"] = drive.FullChargeSpeedCeiling,
                    ["minimumSpeed"] = WarpSpeedPolicy.MinimumSpeed,
                    ["minimumFlowFraction"] = Tuning.MinimumFlowFraction,
                    ["speedCurve"] = WarpSpeedPolicy.Curve,
                    ["speedCurveSharpness"] = WarpSpeedPolicy.Sharpness,
                    ["minimumFieldFraction"] = Tuning.MinimumFieldFraction,
                    ["maximumSpeed"] = Tuning.MaximumSpeed,
                    ["shipMass"] = shipMass,
                    ["fieldStrength"] = drive.Strength,
                    ["recommendedFieldStrength"] = drive.Recommended,
                    ["matchedPairs"] = geometry.Pairs.Count,
                    ["hydrogenRate"] = hydrogenRate,
                    ["powerWatts"] = 0d,
                    ["plasmaOutput"] = plasmaRate,
                    ["plasmaDemand"] = totalDemand,
                    ["matrix"] = installed
                        ? new JObject
                        {
                            ["grade"] = grade.Code.ToString(),
                            ["ratedCoils"] = grade.RatedCoils,
                            ["plasmaPerHydrogen"] = grade.PlasmaPerHydrogen,
                            ["maxPlasmaRate"] = grade.MaxPlasmaRate,
                            ["maxHydrogenRate"] = grade.MaxHydrogenRate
                        }
                        : null,
                    ["left"] = infusers.Length > 0 ? Infuser(0, drive.LeftCharge) : null,
                    ["right"] = infusers.Length > 1 ? Infuser(1, drive.RightCharge) : null,
                    ["diagnostics"] = diagnostics.DeepClone()
                };
                TelemetrySnapshots.Publish(ship.Id, core.Id, sample);
                var engineering = TelemetrySnapshots.Read(ship.Id, core.Id);
                engineering.Remove("sampleSequence");
                Instance(ship.Id, core.Id)?.State.Set("engineering", engineering);
            }
        }

        private static void PublishCharge(uint ship, ushort part, double charge) =>
            Instance(ship, part)?.State.Set("charge", charge);

        internal static WarpDriveState NewNative(WarpDriveStateType state, float timer)
        {
            var value = new WarpDriveState();
            typeof(WarpDriveState).GetProperty("State").SetValue(value, state);
            typeof(WarpDriveState).GetProperty("WarpStartTimer").SetValue(value, timer);
            return value;
        }

        public static bool OwnsWarp(TrackedShipServer ship)
        {
            if (!StriderRegistry.WorldReady)
                return false;
            var spaces = GetService<ISpacesServerProvider>();
            return spaces.GetSpace(ship.SpaceId) is WarpSpace warp && warp.SourceShipId == ship.Id &&
                   (OwnedShips.Contains(ship.Id) ||
                    ship.TryGetStatefulPart(warp.SourcePartId, out var part) && Content.IsCore(part));
        }

        public static void ClientUpdate(PartInstance p, float dt)
        {
            NacelleDebug.Observe(p);
            InfuserHover.Observe(p);
            if (p.Ship is not TrackedShipClient ship || !ship.TryGetFloatingPartVisual(p.Id.PartId, out var visual))
                return;
            var charge = (float)p.State.Get<double>("charge");
            foreach (var renderer in visual.GameObject.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.gameObject.name.EndsWith("__charge.obj", StringComparison.Ordinal) &&
                    !renderer.gameObject.name.EndsWith("__status.obj", StringComparison.Ordinal))
                    continue;
                ModelCatalog.PaintPower(renderer, charge);
            }
        }
    }
}