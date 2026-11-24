using System.Xml.Linq;

namespace NetBanner.Core;

/// <summary>Finds legacy NetBanner scheduled tasks in <c>schtasks /Query /XML ONE</c> output.</summary>
public static class LegacyTasks
{
    /// <summary>
    /// Task names whose action runs a NetBanner.exe other than <paramref name="ownExePath"/>. Matching on the command
    /// rather than the name, because legacy installs used different task names (e.g. <c>\Microsoft\NetBannerSchTask</c>).
    /// </summary>
    public static List<string> Find(string schtasksXml, string ownExePath)
    {
        var found = new List<string>();
        var start = schtasksXml.IndexOf("<Tasks", StringComparison.Ordinal);
        if (start < 0)
        {
            return found;
        }

        // Each task is preceded by a comment holding its full name.
        var doc = XDocument.Parse(schtasksXml[start..]);
        string? name = null;
        foreach (var node in doc.Root!.Nodes())
        {
            if (node is XComment comment)
            {
                name = comment.Value.Trim();
            }
            else if (node is XElement task && name is not null)
            {
                var commands = task.Descendants().Where(e => e.Name.LocalName == "Command").Select(e => e.Value);
                if (commands.Any(c => IsOtherNetBanner(c, ownExePath)))
                {
                    found.Add(name);
                }

                name = null;
            }
        }

        return found;
    }

    private static bool IsOtherNetBanner(string command, string ownExePath)
    {
        var path = Environment.ExpandEnvironmentVariables(command.Trim().Trim('"'));
        if (!Path.GetFileName(path).Equals("NetBanner.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(Path.GetFullPath(path), Path.GetFullPath(ownExePath), StringComparison.OrdinalIgnoreCase);
    }
}
