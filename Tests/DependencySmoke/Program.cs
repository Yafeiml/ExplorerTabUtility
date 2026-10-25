using System;
using System.Net.Http;
using System.Windows.Forms;
using AutoUpdaterDotNET.ChangelogViewers;
using AutoUpdaterDotNET.Markdown;

namespace ExplorerTabUtility.DependencySmokeTests;

internal sealed class CaptureViewer : IChangelogViewer
{
    public string Html;
    public Control Control => null;
    public bool SupportsUrl => false;
    public void LoadContent(string content) => Html = content;
    public void LoadUrl(string url) => throw new NotSupportedException();
    public void Cleanup() { }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var capture = new CaptureViewer();
        var viewer = new MarkdownViewer(capture, false);
        viewer.LoadContent("# 更新说明\n\n**修复** [详情](https://example.com/fix)\n\n| 项目 | 状态 |\n| --- | --- |\n| 容器 | 正常 |\n\n```csharp\nvar tab = 1;\n```\n");
        foreach (var expected in new[] { "<h1", "更新说明", "<strong>修复</strong>", "href=\"https://example.com/fix\"", "<table>", "<td>正常</td>", "language-csharp" })
            if (capture.Html == null || !capture.Html.Contains(expected))
                throw new Exception("Markdown rendering missing: " + expected);

        using (var client = new HttpClient())
        {
            client.BaseAddress = new Uri("https://example.com/");
            if (client.BaseAddress.Host != "example.com")
                throw new Exception("HttpClient framework reference failed");
        }

        Console.WriteLine("PASS: updater binary renders Chinese, headings, emphasis, links, tables and code; HttpClient loads.");
    }
}
