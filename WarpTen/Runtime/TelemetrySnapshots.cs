using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using WarpTen.Simulation;

namespace WarpTen.Runtime
{
    internal static class TelemetrySnapshots
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<(uint Ship, ushort Core), JObject> Samples = new();
        private static long sequence;

        internal static void Publish(uint ship, ushort core, JObject sample)
        {
            var copy = (JObject)sample.DeepClone();
            Sanitize(copy);
            lock (Gate)
            {
                copy["schemaVersion"] = 3;
                copy["sampleSequence"] = ++sequence;
                copy["shipId"] = ship;
                copy["coreId"] = core;
                Samples[(ship, core)] = copy;
            }
        }

        private static void Sanitize(JToken token)
        {
            if (token is JValue value && value.Type == JTokenType.Float && !Tuning.Finite((double)value))
                value.Value = null;
            else
                foreach (var child in token.Children())
                    Sanitize(child);
        }

        internal static JObject Read(uint ship, ushort core)
        {
            lock (Gate)
                return Samples.TryGetValue((ship, core), out var sample) ? (JObject)sample.DeepClone() : null;
        }

        internal static void Remove(uint ship, ushort core)
        {
            lock (Gate)
                Samples.Remove((ship, core));
        }

        internal static void Clear()
        {
            lock (Gate)
            {
                Samples.Clear();
                sequence = 0;
            }
        }
    }
}