using System.Collections.Generic;
using System.Linq;
using System;
using Compute.Api;
using Newtonsoft.Json.Linq;
using Ships.Interface.Model.Parts.Common;
using StellarStriderLib;

namespace WarpTen.StellarCompute
{
    public static class Adapter
    {
        public static Action<SocketObject> Initialize(Func<uint, ushort, JObject> read)
        {
            Convert(new JObject { ["probe"] = new JArray(true, 1d, "ok", JValue.CreateNull()) });
            Action<SocketObject> configure = PeripheralRegistry.ConfigureSocket;
            PeripheralRegistry.Register(new PeripheralDefinition
            {
                TypeId = "warpten:warp_core",
                Part = PartRef.Mod("warpten:core"),
                SocketId = 4,
                Methods = new[] { "getTelemetry" },
                CreateDevice = context => new Core(context, read)
            });
            return configure;
        }

        private sealed class Core : PeripheralDevice
        {
            private readonly PeripheralContext context;
            private readonly Func<uint, ushort, JObject> read;

            public Core(PeripheralContext context, Func<uint, ushort, JObject> read)
            {
                this.context = context;
                this.read = read;
            }

            public override IReadOnlyList<PeripheralValue> Invoke(string method,
                IReadOnlyList<PeripheralValue> arguments)
            {
                if (method != "getTelemetry" || arguments.Count != 0)
                    throw new ArgumentException("getTelemetry expects no arguments.");
                var sample = read(context.Identity.ShipId, context.Identity.PartId);
                return sample == null
                    ? new[]
                    {
                        PeripheralValue.Nil,
                        PeripheralValue.From("unavailable")
                    }
                    : new[]
                    {
                        Convert(sample)
                    };
            }
        }

        private static PeripheralValue Convert(JToken token)
        {
            if (token == null || token is JValue scalar && scalar.Value == null)
                return PeripheralValue.Nil;
            switch (token.Type)
            {
                case JTokenType.Object:
                    return PeripheralValue.Object(((JObject)token).Properties().Select(p =>
                        new KeyValuePair<string, PeripheralValue>(p.Name, Convert(p.Value))));
                case JTokenType.Array:
                    return PeripheralValue.Array(token.Select(Convert));
                case JTokenType.Boolean:
                    return PeripheralValue.From((bool)token);
                case JTokenType.Integer:
                case JTokenType.Float:
                    return PeripheralValue.From((double)token);
                case JTokenType.String:
                    return PeripheralValue.From((string)token);
                default:
                    return PeripheralValue.Nil;
            }
        }
    }
}