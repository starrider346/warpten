using System;
using System.Linq;
using System.Reflection;

namespace WarpTen.Runtime
{
    internal static class PonderContract
    {
        internal static bool TryRegister(Assembly ponder, Action register, Action<string> info, Action<string> warning)
        {
            if (ponder == null) return false;
            try
            {
                Validate(ponder);
                register();
                info("Ponder warp-drive guide registered for all Warp10 parts.");
                return true;
            }
            catch (Exception error)
            {
                warning("Optional Ponder guide unavailable. " + (error.InnerException ?? error).Message);
                return false;
            }
        }

        private static void Validate(Assembly assembly)
        {
            Type Api(string name) => assembly.GetType("Ponder." + name, true);
            void Method(Type type, string name, string result, bool isStatic, params string[] args)
            {
                if (!type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Any(m =>
                        m.Name == name && m.IsStatic == isStatic && m.ReturnType.FullName == result &&
                        m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(args)))
                    throw new MissingMethodException(type.FullName, name);
            }
            void Property(Type type, string name, Type value)
            {
                var property = type.GetProperty(name);
                if (property?.PropertyType != value || property.SetMethod?.IsPublic != true || property.SetMethod.IsStatic)
                    throw new MissingMemberException(type.FullName, name);
            }

            var registry = Api("GuideRegistry");
            var definition = Api("GuideDefinition");
            var step = Api("GuideStep");
            var scene = Api("PonderScene");
            foreach (var type in new[] { definition, step })
                if (type.GetConstructor(Type.EmptyTypes) == null) throw new MissingMethodException(type.FullName, ".ctor");
            Method(Api("PonderRegistry"), "Create", registry.FullName, true, "System.String", "System.Reflection.Assembly");
            Method(registry, "RegisterGuide", "System.Void", false, definition.FullName);
            Property(definition, "Id", typeof(string));
            Property(definition, "Title", typeof(string));
            Property(definition, "Parts", typeof(string[]));
            Property(definition, "Steps", step.MakeArrayType());
            Property(definition, "BuildScene", typeof(Action<>).MakeGenericType(scene));
            Property(definition, "Sample", typeof(Action<,>).MakeGenericType(scene, typeof(float)));
            Property(step, "Caption", typeof(string));
            Property(step, "Duration", typeof(float));
            const string vector = "UnityEngine.Vector3", color = "System.Nullable`1[[UnityEngine.Color";
            Method(scene, "AddPart", "UnityEngine.GameObject", false, "System.String", "StellarStriderLib.PartRef", vector, vector);
            Method(scene, "CreateObject", "UnityEngine.GameObject", false, "System.String");
            Method(scene, "GetObject", "UnityEngine.GameObject", false, "System.String");
            Method(scene, "GetSocket", "UnityEngine.Transform", false, "System.String", "System.Byte");
            Method(scene, "Move", "System.Void", false, "System.String", vector, vector, "System.Single", "System.Single");
            Method(scene, "Show", "System.Void", false, "System.String", "System.Single", "System.Boolean");
            Method(scene, "Callout", "System.Void", false, "System.String", vector, "System.String", "System.Single", "System.Single");
            Method(scene, "CalloutSocket", "System.Void", false, "System.String", "System.Byte", "System.String", "System.Single", "System.Single");
            Method(scene, "AddSignalCable", "UnityEngine.GameObject", false, "System.String", "System.String", "System.Byte", "System.String", "System.Byte", "System.Single", "System.Single");
            Method(scene, "SetCameraFrame", "System.Void", false, "UnityEngine.Bounds", "System.Single", "System.Single", "System.Single");
            foreach (var name in new[] { "Highlight", "HighlightSocket" })
            {
                var prefix = name == "Highlight" ? new[] { "System.String", "System.Single", "System.Single" }
                    : new[] { "System.String", "System.Byte", "System.Single", "System.Single" };
                if (!scene.GetMethods().Any(m => m.Name == name && m.ReturnType == typeof(void) &&
                        m.GetParameters().Select(p => p.ParameterType.FullName).Take(prefix.Length).SequenceEqual(prefix) &&
                        m.GetParameters().Length == prefix.Length + 1 &&
                        m.GetParameters().Last().ParameterType.FullName.StartsWith(color, StringComparison.Ordinal)))
                    throw new MissingMethodException(scene.FullName, name);
            }
            if (!scene.GetMethods().Any(m => m.Name == "Own" && m.IsGenericMethodDefinition &&
                    m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == m.GetGenericArguments()[0] &&
                    m.ReturnType == m.GetGenericArguments()[0]))
                throw new MissingMethodException(scene.FullName, "Own");
        }
    }
}
