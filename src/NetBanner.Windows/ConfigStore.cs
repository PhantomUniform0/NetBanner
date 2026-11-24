using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace NetBanner.Windows;

/// <summary>Where the local config file lives, and how it's written safely.</summary>
public static class ConfigStore
{
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetBanner", "config.json");

    /// <summary>
    /// Server sync state (pinned key, cached config): the config folder, which Administrators/SYSTEM can write and
    /// Users can only read. Written by the SYSTEM sync task.
    /// </summary>
    public static string StateDir => Path.GetDirectoryName(DefaultPath)!;

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// True if this user is an administrator running with a UAC-filtered token, i.e. a UAC prompt can elevate
    /// without asking for other credentials. False for standard users and for already elevated processes.
    /// </summary>
    public static bool IsLimitedAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return Native.GetTokenInformation(id.Token, Native.TokenElevationTypeClass, out int type, sizeof(int), out _)
            && type == Native.TokenElevationTypeLimited;
    }

    /// <summary>
    /// Writes the file in one step: a temporary file, then a rename over the old one, so running
    /// banners never read a half-written file. If <paramref name="secureFolder"/> is true, the folder's
    /// permissions are reset to Administrators/SYSTEM full control and Users read-only.
    /// </summary>
    public static void Save(string path, string json, bool secureFolder)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        if (secureFolder)
        {
            SecureFolder(dir);
        }

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmp, path, overwrite: true);
    }

    public static void SecureFolder(string dir)
    {
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(dir).SetAccessControl(sec);
    }
}
