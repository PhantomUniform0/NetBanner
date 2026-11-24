using NetBanner.Core;

namespace NetBanner.Core.Tests;

public class LegacyTasksTests
{
    private const string Own = @"C:\Program Files\NetBanner\NetBanner.exe";

    private static string Task(string name, string command) => $"""
        <!-- {name} -->
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Actions Context="Author"><Exec><Command>{command}</Command></Exec></Actions>
        </Task>
        """;

    private static string Export(params string[] tasks) =>
        "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<Tasks>\r\n" + string.Join("\r\n", tasks) + "\r\n</Tasks>";

    [Fact]
    public void FindsLegacyTask_ByCommand_WhateverItsName()
    {
        var xml = Export(
            Task(@"\Microsoft\NetBannerSchTask", @"C:\Program Files (x86)\Microsoft\NetBanner\NetBanner.exe"),
            Task(@"\NetBanner\NetBannerLogon", "\"" + Own + "\""),
            Task(@"\Adobe Acrobat Update Task", @"C:\Program Files\Adobe\ARM\AdobeARM.exe"),
            Task(@"\Custom\Banner", "\"%ProgramFiles(x86)%\\Microsoft\\NetBanner\\netbanner.exe\""));

        Assert.Equal([@"\Microsoft\NetBannerSchTask", @"\Custom\Banner"], LegacyTasks.Find(xml, Own));
    }

    [Fact]
    public void NoTasksElement_FindsNothing() => Assert.Empty(LegacyTasks.Find("ERROR: Access denied", Own));
}
