using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System;
using MelonLoader;
using Newtonsoft.Json.Linq;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts;
using StellarStriderLib;

namespace WarpTen.Runtime
{
    internal static class OptionalPeripheral
    {
        internal static Action<SocketObject> ConfigureSocket { get; private set; }
        internal static bool Enabled => ConfigureSocket != null;

        private static void ValidateContract(Assembly assembly)
        {
            Type Api(string name) => assembly.GetType("Compute.Api." + name, true);

            void Method(Type type, string name, Type result, params Type[] args)
            {
                var method = type.GetMethod(name, args);
                if (method == null || !method.IsPublic || method.ReturnType != result)
                    throw new MissingMethodException(type.FullName, name);
            }

            void Property(Type type, string name, Type result, bool writable = false)
            {
                var property = type.GetProperty(name);
                if (property?.PropertyType != result || property.GetMethod?.IsPublic != true ||
                    writable && property.SetMethod?.IsPublic != true)
                    throw new MissingMemberException(type.FullName, name);
            }

            var value = Api("PeripheralValue");
            foreach (var type in new[]
                     {
                         typeof(bool),
                         typeof(double),
                         typeof(string)
                     }
                    )
                Method(value, "From", value, type);
            var values = typeof(IEnumerable<>).MakeGenericType(value);
            var list = typeof(IReadOnlyList<>).MakeGenericType(value);
            Method(value, "Array", value, values);
            Method(value, "Object", value,
                typeof(IEnumerable<>).MakeGenericType(
                    typeof(KeyValuePair<,>).MakeGenericType(typeof(string), value)));
            if (value.GetField("Nil")?.FieldType != value)
                throw new MissingFieldException(value.FullName, "Nil");
            var context = Api("PeripheralContext");
            Property(context, "Identity", typeof(PartContext));
            var device = Api("PeripheralDevice");
            Method(device, "Invoke", list, typeof(string), list);
            if (device.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .All(c => c.GetParameters().Length != 0))
                throw new MissingMethodException(device.FullName, ".ctor");
            var definition = Api("PeripheralDefinition");
            Property(definition, "TypeId", typeof(string), true);
            Property(definition, "Part", typeof(PartRef), true);
            Property(definition, "SocketId", typeof(byte), true);
            Property(definition, "Methods", typeof(IReadOnlyList<string>), true);
            Property(definition, "CreateDevice", typeof(Func<,>).MakeGenericType(context, device), true);
        }

        internal static void Initialize()
        {
            var compute = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Compute");
            TryInitialize(compute, MelonLogger.Msg, MelonLogger.Warning);
        }

        internal static void TryInitialize(Assembly compute, Action<string> info, Action<string> warning)
        {
            if (compute == null)
                return;
            try
            {
                var api = compute.GetType("Compute.Api.PeripheralRegistry", true);
                var definition = compute.GetType("Compute.Api.PeripheralDefinition", true);
                if (api.GetMethod("Register", new[] { definition })?.ReturnType != typeof(void) ||
                    api.GetMethod("ConfigureSocket", new[] { typeof(SocketObject) })?.ReturnType != typeof(void))
                    throw new MissingMethodException("Compatible public peripheral registry/socket API unavailable.");
                ValidateContract(compute);
                using var stream =
                    typeof(OptionalPeripheral).Assembly.GetManifestResourceStream("WarpTen.StellarCompute.dll");
                if (stream == null)
                    throw new InvalidOperationException("Embedded peripheral adapter missing.");
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                var adapter = Assembly.Load(bytes.ToArray());
                ConfigureSocket = (Action<SocketObject>)adapter.GetType("WarpTen.StellarCompute.Adapter", true)
                    .GetMethod("Initialize").Invoke(null,
                        new object[] { (Func<uint, ushort, JObject>)TelemetrySnapshots.Read });
                info("StellarCompute warp telemetry registered (core Peripheral socket 4).");
            }
            catch (Exception error)
            {
                ConfigureSocket = null;
                warning("Optional StellarCompute telemetry unavailable; using the normal core. " +
                        (error.InnerException ?? error).Message);
            }
        }
    }
}