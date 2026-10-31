using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DuckCam.Core
{
    internal sealed class PatchBinding
    {
        public string Id;
        public string Feature;
        public string Target;
        public bool Bound;
        public string Error;
    }

    internal static class PatchRegistry
    {
        private static Harmony _harmony;
        private static readonly List<PatchBinding> _bindings = new List<PatchBinding>();
        private static readonly Dictionary<string, PatchBinding> _byId = new Dictionary<string, PatchBinding>();

        public static IReadOnlyList<PatchBinding> Bindings => _bindings;

        public static void Init(string harmonyId)
        {
            _harmony = new Harmony(harmonyId);
        }

        public static bool Prefix(string id, string feature, Type type, string methodName, Type[] args, Type patchClass, string patchMethod)
        {
            var binding = new PatchBinding
            {
                Id = id,
                Feature = feature,
                Target = $"{type?.FullName ?? "<missing type>"}.{methodName}({(args == null ? "*" : string.Join(", ", Array.ConvertAll(args, a => a.Name)))})"
            };
            _bindings.Add(binding);
            _byId[id] = binding;

            try
            {
                if (type == null)
                {
                    throw new MissingMemberException("type not found");
                }

                MethodInfo target = AccessTools.Method(type, methodName, args);
                if (target == null)
                {
                    throw new MissingMethodException(type.FullName, methodName);
                }

                MethodInfo prefix = AccessTools.Method(patchClass, patchMethod);
                if (prefix == null)
                {
                    throw new MissingMethodException(patchClass.FullName, patchMethod);
                }

                _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                binding.Bound = true;
                PhotoLog.Verbose($"Bound {id}: {binding.Target}");
            }
            catch (Exception ex)
            {
                binding.Bound = false;
                binding.Error = ex.GetType().Name + ": " + ex.Message;
                PhotoLog.Warn($"Patch {id} for feature '{feature}' unavailable. Target {binding.Target}. {binding.Error}");
            }

            return binding.Bound;
        }

        public static bool IsBound(string id)
        {
            return _byId.TryGetValue(id, out PatchBinding b) && b.Bound;
        }

        public static void UnpatchAll()
        {
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch (Exception ex)
            {
                PhotoLog.Error("UnpatchSelf failed: " + ex);
            }
        }
    }
}
