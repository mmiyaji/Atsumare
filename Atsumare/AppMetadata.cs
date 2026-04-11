using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Atsumare;

internal static class AppMetadata
{
    internal const string ProductName = "Atsumare";
    internal const string AuthorName = "mmiyaji";
    internal const string SupportUrl = "https://mmiyaji.github.io/Atsumare/";
    internal const string RepositoryUrl = "https://github.com/mmiyaji/Atsumare";
    internal const string PrivacyPolicyUrl = "https://mmiyaji.github.io/Atsumare/privacy-policy.html";
    internal const string TermsOfUseUrl = "https://mmiyaji.github.io/Atsumare/terms-of-use.html";
    internal const string WindowsAppSdkProjectUrl = "https://github.com/microsoft/windowsappsdk";
    internal const string DotNetRuntimeLicenseUrl = "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT";
    internal const string WebView2LicenseUrl = "https://learn.microsoft.com/microsoft-edge/webview2/";

    internal static string CopyrightText =>
        $"Copyright © {DateTime.Now.Year} {AuthorName}. All rights reserved.";

    internal static string VersionText
    {
        get
        {
            try
            {
                var package = Windows.ApplicationModel.Package.Current;
                var version = package.Id.Version;
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
            catch
            {
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            }
        }
    }

    internal static string BuildDateText
    {
        get
        {
            try
            {
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrWhiteSpace(assemblyPath))
                    assemblyPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";

                if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
                    return "unknown";

                return File.GetLastWriteTime(assemblyPath).ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
