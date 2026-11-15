using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace WarpTen.Runtime
{
    internal static class OptionalPonder
    {
        internal static void Initialize()
        {
            var ponder = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Ponder");
            PonderContract.TryRegister(ponder, LoadAdapter, MelonLogger.Msg, MelonLogger.Warning);
        }

        private static void LoadAdapter()
        {
            using var stream = typeof(OptionalPonder).Assembly.GetManifestResourceStream("WarpTen.Ponder.dll") ??
                               throw new InvalidOperationException("Embedded Ponder adapter missing.");
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var adapter = Assembly.Load(bytes.ToArray());
            adapter.GetType("WarpTen.Ponder.Adapter", true).GetMethod("Initialize").Invoke(null, new object[]
            {
                typeof(Plugin).Assembly,
                PartCatalog.Parts.Select(p => Content.ModId + ":" + p.Id).ToArray(),
                (float)DriveRuntime.Tuning.ChargeSeconds,
                (float)DriveRuntime.Tuning.MinimumFlowFraction,
                Simulation.MatrixGrade.All[0].MaxHydrogenRate
            });
        }
    }
}
