using System.Collections.Generic;
using System.Linq;
using System;
using Items.Model;
using Newtonsoft.Json.Linq;
using Players.Interface.Model;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts;
using Ships.Parts.Common;
using StellarStriderLib;
using UnityEngine;
using WarpTen.Runtime;
using WarpTen.Simulation;
using static HarmonyLib.AccessTools;

namespace WarpTen
{
    internal static class Content
    {
        public const string ModId = "warpten";
        public static ushort CoreId, InfuserId, CoilId, StraightId, CornerId, TeeId, SpacerId;
        public const uint HydrogenId = 106;
        private const uint CopperItemId = 112, StellarShardItemId = 103;
        private const ushort ChemicalPropulsionId = 20;

        public static bool IsConduit(ushort id) =>
            StraightId != 0 && (id == StraightId || id == CornerId || id == TeeId || id == SpacerId);

        public static PlasmaPartKind ConduitKind(ushort id) => id == StraightId
            ? PlasmaPartKind.Straight
            : id == CornerId
                ? PlasmaPartKind.Corner
                : id == SpacerId
                    ? PlasmaPartKind.Spacer
                    : PlasmaPartKind.Tee;

        public static bool IsCore(StatefulPart p) => CoreId != 0 && p.Settings.id == CoreId;

        public static void Register(ModRegistry registry)
        {
            registry.RegisterLoadingWallpaper(new LoadingWallpaperDefinition
                { Id = "tunnel", TextureResource = "WarpTen.Wallpapers.tunnel.png" });
            MatrixItems.Register(registry);
            registry.RegisterCategory(new CategoryDefinition { Id = "warp", Name = "Warp10", SortOrder = 110 });
            RegisterPart(registry, "core", "Warp Core", "StorageChest", new Vector3(3, 3, 3), 1000,
                NativeSockets("core"));
            RegisterPart(registry, "infuser", "Warp Coil Infuser", "Constant", new Vector3(3, 2, 2), 100,
                Array.Empty<SocketDefinition>());
            RegisterPart(registry, "coil", "Warp Coil", "Constant", new Vector3(3, 2, 1), 100,
                Array.Empty<SocketDefinition>());
            RegisterPart(registry, "conduit_straight", "Warp Plasma Conduit — Straight", "Constant", Vector3.one, 5,
                Array.Empty<SocketDefinition>());
            RegisterPart(registry, "conduit_corner", "Warp Plasma Conduit — Corner", "Constant", Vector3.one, 5,
                Array.Empty<SocketDefinition>());
            RegisterPart(registry, "conduit_tee", "Warp Plasma Conduit — T", "Constant", Vector3.one, 5,
                Array.Empty<SocketDefinition>());
            RegisterPart(registry, "conduit_spacer", "Warp Plasma Conduit — Snap Spacer (0.25 m)", "Constant",
                new Vector3(.5f, .5f, .25f), 2, Array.Empty<SocketDefinition>());
            registry.RegisterResearchTree(new ResearchTreeDefinition
            {
                Id = "prototype",
                Nodes = new[]
                {
                    new ResearchNodeDefinition
                    {
                        Id = "prototype", Name = "Interstellar Travel", AvailableInDemo = true,
                        Prerequisites = new[] { ResearchRef.Native(ChemicalPropulsionId) },
                        Costs = Recipe(80, 60, shards: 20),
                        UnlockedParts =
                            new[]
                            {
                                "core", "infuser", "coil", "conduit_straight", "conduit_corner", "conduit_tee",
                                "conduit_spacer"
                            }.Select(id => PartRef.Mod(ModId + ":" + id)).ToArray()
                    }
                }
            });
        }

        private static ItemCost[] Recipe(int iron, int copper, int glass = 0, int shards = 0)
        {
            var costs = new List<ItemCost>
            {
                new()
                {
                    Item = ItemRef.Native(ItemsConstants.IronItemId),
                    Quantity = iron
                },
                new()
                {
                    Item = ItemRef.Native(CopperItemId),
                    Quantity = copper
                }
            };
            if (glass > 0)
                costs.Add(new ItemCost { Item = ItemRef.Native(ItemsConstants.GlassItemId), Quantity = glass });
            if (shards > 0)
                costs.Add(new ItemCost { Item = ItemRef.Native(StellarShardItemId), Quantity = shards });
            return costs.ToArray();
        }

        private static ItemCost[] CostsForPart(string id) => id switch
        {
            "core" => Recipe(60, 80, 30, 20),
            "infuser" => Recipe(40, 60, shards: 10),
            "coil" => Recipe(15, 30, shards: 5),
            _ when id.StartsWith("conduit_", StringComparison.Ordinal) => Recipe(2, 2, 2),
            _ => throw new ArgumentException("Unknown Warp10 part: " + id)
        };

        private static SocketDefinition Socket(byte id, CableType type, float x, float y, float z, float yaw) =>
            new SocketDefinition
            {
                Id = id,
                Type = type,
                Position = new Vector3(x, y, z),
                Rotation = new Vector3(0, yaw, 0)
            };

        private static SocketDefinition[] NativeSockets(string part)
        {
            if (part == "core")
            {
                var sockets = new[]
                {
                    Socket(2, CableType.Fluid, -1.5f, .07f, .125f, 90)
                };
                return OptionalPeripheral.Enabled
                    ? sockets.Concat(new[] { Socket(4, CableType.Signal, -1.5f, .07f, -.375f, 90) }).ToArray()
                    : sockets;
            }

            return Array.Empty<SocketDefinition>();
        }

        private static void RegisterPart(ModRegistry r, string id, string name, string donor, Vector3 size, float mass,
            SocketDefinition[] sockets)
        {
            r.RegisterPart(new PartDefinition
            {
                Id = id,
                Name = name,
                Donor = donor,
                Category = "warp",
                Size = size,
                Mass = mass,
                Description = id switch
                {
                    "core" =>
                        "Converts hydrogen into plasma for your warp drive. Install an Annihilation Matrix and connect the core to both infusers. Higher-grade matrices provide more plasma.",
                    "infuser" =>
                        "Feeds plasma into a row of warp coils. Place two infusers side by side, connect them to the core, and build matching rows of coils behind them.",
                    "coil" =>
                        "Generates the warp field. Add matching coils behind both infusers to support heavier ships and reach higher speeds. Coils snap into place when aimed at an infuser or another coil.",
                    "conduit_straight" =>
                        "Carries plasma between your warp core and infusers. Connect straight sections to build a plasma line.",
                    "conduit_corner" =>
                        "Turns a plasma line around a corner. Scroll while placing it to choose the direction.",
                    "conduit_tee" =>
                        "Splits a plasma line so one core can feed both infusers. Scroll while placing it to position the branch.",
                    "conduit_spacer" =>
                        "A quarter-metre plasma conduit for closing small gaps. Stack spacers to line up your connections.",
                    _ => throw new ArgumentException("Unknown Warp10 part: " + id)
                },
                BuildingCost = CostsForPart(id),
                Sockets = sockets,
                PhysicsShapes = ModelCatalog.Physics(id, size),
                InteractionShapes = id == "core"
                    ? new[]
                    {
                        new InteractionShapeDefinition
                        {
                            Boxes = new[]
                            {
                                new BoxShape
                                    { Center = new Vector3(0, size.y / 2, 0), Size = size + Vector3.one * .02f }
                            }
                        }
                    }
                    : null,
                Models = ModelCatalog.Meshes(id),
                ConfigureMesh = ModelCatalog.Configure,
                Configure = context =>
                {
                    if (id.StartsWith("conduit_", StringComparison.Ordinal))
                        foreach (var interaction in context.Prefab.GetComponentsInChildren<PartInteraction>(true))
                        {
                            Field(typeof(PartInteraction), "isActive").SetValue(interaction, false);
                            Field(typeof(PartInteraction), "interactions")
                                .SetValue(interaction, new List<PartInteractionConfig>());
                        }

                    if (id == "core")
                    {
                        context.Settings.internalStateClass = "StorageState";
                        context.Settings.customCreationClass = null;
                    }

                    if (id == "core" && OptionalPeripheral.Enabled)
                        foreach (var socket in context.Prefab.GetComponentsInChildren<SocketObject>(true))
                            if (socket.Id == 4)
                                OptionalPeripheral.ConfigureSocket(socket);
                },
                State = id == "core" ? CoreStateDefinition() : StateDefinition(),
                Callbacks = new PartCallbacks
                {
                    Spawned = DriveRuntime.Add,
                    Removed = DriveRuntime.Remove,
                    ClientUpdate = DriveRuntime.ClientUpdate
                }
            });
        }

        private static PartStateDefinition CoreStateDefinition()
        {
            var definition = StateDefinition();
            var validate = definition.Validate;
            definition.Version = 4;
            definition.Migrate = (version, fields) =>
            {
                if (version != 1 && version != 2 && version != 3)
                    throw new ArgumentException("Unsupported Warp Core state version.");
                fields.Remove("plasma");
                fields.Remove("matrixItem");
                fields.Remove("matrixLife");
                return fields;
            };
            definition.Fields = definition.Fields.Concat(new[]
            {
                new PartStateField { Name = "phase", Default = new JValue("Offline"), Saved = false },
                new PartStateField { Name = "chargeRemaining", Default = new JValue(0d), Saved = false },
                new PartStateField { Name = "engineering", Default = new JObject(), Saved = false }
            }).ToArray();
            definition.Validate = fields =>
            {
                validate(fields);
                var remaining = (double)fields["chargeRemaining"];
                if (!Tuning.Finite(remaining) || remaining < 0)
                    throw new ArgumentException("Invalid Warp Core charging state.");
            };
            return definition;
        }

        private static PartStateDefinition StateDefinition() => new PartStateDefinition
        {
            Version = 2,
            Migrate = MigrateFields,
            Fields = new[]
            {
                new PartStateField
                {
                    Name = "enabled",
                    Default = new JValue(true)
                },
                new PartStateField
                {
                    Name = "plasmaOutput",
                    Default = new JValue(0d),
                    Saved = false
                },
                new PartStateField
                {
                    Name = "plasmaDemand",
                    Default = new JValue(0d),
                    Saved = false
                },
                new PartStateField
                {
                    Name = "plasmaDelivered",
                    Default = new JValue(0d),
                    Saved = false
                },
                new PartStateField
                {
                    Name = "plasmaConnected",
                    Default = new JValue(false),
                    Saved = false
                },
                new PartStateField
                {
                    Name = "charge",
                    Default = new JValue(0d),
                    Saved = false
                },
                new PartStateField
                {
                    Name = "status",
                    Default = new JValue("Offline"),
                    Saved = false
                }
            },
            Validate = fields =>
            {
                foreach (var name in new[]
                         {
                             "charge",
                             "plasmaOutput",
                             "plasmaDemand",
                             "plasmaDelivered"
                         }
                        )
                {
                    var value = (double)fields[name];
                    if (!Tuning.Finite(value) || value < 0 || name == "charge" && value > 1)
                        throw new ArgumentException("Invalid WarpTen rate or charge state.");
                }
            }
        };

        private static JObject MigrateFields(int version, JObject fields)
        {
            if (version != 1)
                throw new ArgumentException("Unsupported WarpTen state version.");
            fields.Remove("plasma");
            return fields;
        }

        public static void Bind()
        {
            MatrixItems.Bind();
            if (!StriderRegistry.TryGetItem(ItemRef.Native(HydrogenId), out var hydrogen) ||
                hydrogen.itemName != "Hydrogen" || !hydrogen.isFluid)
                throw new InvalidOperationException("Native Hydrogen (item 106) is unavailable or incompatible.");
            if (!StriderRegistry.TryGetNumericId(PartRef.Mod("warpten:core"), out CoreId) ||
                !StriderRegistry.TryGetNumericId(PartRef.Mod("warpten:infuser"), out InfuserId) ||
                !StriderRegistry.TryGetNumericId(PartRef.Mod("warpten:coil"), out CoilId) ||
                !StriderRegistry.TryGetNumericId(PartRef.Mod("warpten:conduit_straight"), out StraightId) ||
                !StriderRegistry.TryGetNumericId(PartRef.Mod("warpten:conduit_corner"), out CornerId) ||
                !StriderRegistry.TryGetNumericId(PartRef.Mod("warpten:conduit_tee"), out TeeId) ||
                !StriderRegistry.TryGetNumericId(PartRef.Mod("warpten:conduit_spacer"), out SpacerId))
                throw new InvalidOperationException("WarpTen IDs unavailable.");
        }

        public static void Clear()
        {
            MatrixItems.Unload();
            CoreId = InfuserId = CoilId = StraightId = CornerId = TeeId = SpacerId = 0;
        }
    }
}