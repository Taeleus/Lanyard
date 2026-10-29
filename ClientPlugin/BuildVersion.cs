using System.Reflection;

[assembly: AssemblyVersion(Lanyard.BuildVersion.Version)]
[assembly: AssemblyFileVersion(Lanyard.BuildVersion.Version)]
[assembly: AssemblyInformationalVersion(Lanyard.BuildVersion.Version)]

namespace Lanyard;

// Shared by local builds and Pulsar's source compiler.
internal static class BuildVersion
{
    public const string Version = "0.1.2";
}
