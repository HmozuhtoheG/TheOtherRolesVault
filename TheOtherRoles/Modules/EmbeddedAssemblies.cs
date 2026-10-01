using System;
using System.Reflection;
using System.Runtime.Loader;

namespace TheOtherRoles.Modules
{
    public static class EmbeddedAssemblies
    {
        private const string ResourceName = "TheOtherRoles.NLayer.dll";
        private const string AssemblyName = "NLayer";

        private static Assembly cached;

        public static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
            try
            {
                AssemblyLoadContext.Default.Resolving += (_, name) => Matches(name) ? Load() : null;
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[EmbeddedAssemblies] alc hook failed: {ex.Message}");
            }
        }

        private static Assembly OnResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                return Matches(new AssemblyName(args.Name)) ? Load() : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool Matches(AssemblyName name)
        {
            return name != null && string.Equals(name.Name, AssemblyName, StringComparison.OrdinalIgnoreCase);
        }

        private static Assembly Load()
        {
            if (cached != null) return cached;

            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
                if (stream == null)
                {
                    TheOtherRolesPlugin.Logger.LogError($"[EmbeddedAssemblies] {ResourceName} not embedded");
                    return null;
                }

                var bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count <= 0) break;
                    read += count;
                }

                cached = Assembly.Load(bytes);
                TheOtherRolesPlugin.Logger.LogInfo($"[EmbeddedAssemblies] loaded {AssemblyName} {cached.GetName().Version}");
                return cached;
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogError($"[EmbeddedAssemblies] failed to load {AssemblyName}: {ex.Message}");
                return null;
            }
        }
    }
}
