using System.Diagnostics;
using System.IO.Compression;
using System.Security;
using NUnit.Framework;

namespace Fantasy.Net.Tests;

[NonParallelizable]
public sealed class PackageAndConfigRegressionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string FantasyNetProject = Path.Combine(
        RepositoryRoot,
        "Fantasy.Packages",
        "Fantasy.Net",
        "Fantasy.Net.csproj");
    private static readonly string TrackedPackage = Path.Combine(
        RepositoryRoot,
        "nupkg",
        "Fantasy-Net.2026.1.1002.nupkg");

    [Test]
    public void TrackedPackageContainsOnlyNet10LibraryAsset()
    {
        Assert.That(File.Exists(TrackedPackage), Is.True, $"Tracked package not found: {TrackedPackage}");

        using ZipArchive archive = ZipFile.OpenRead(TrackedPackage);
        string[] libraryAssets = archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .Where(path => path.StartsWith("lib/", StringComparison.Ordinal) && !path.EndsWith('/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.That(libraryAssets, Is.EqualTo(new[] { "lib/net10.0/Fantasy-Net.dll" }));
        Assert.That(archive.Entries.Any(entry => entry.FullName.StartsWith("analyzers/dotnet/cs/", StringComparison.Ordinal)), Is.True);
        Assert.That(archive.Entries.Any(entry => entry.FullName.StartsWith("build/Fantasy-Net", StringComparison.Ordinal)), Is.True);
        Assert.That(archive.Entries.Any(entry => entry.FullName.StartsWith("buildTransitive/Fantasy-Net", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task ProjectReferencePublishContainsOneApplicationConfig()
    {
        string root = CreateConsumerDirectory();
        try
        {
            WriteConsumerSource(root);
            File.WriteAllText(
                Path.Combine(root, "Consumer.csproj"),
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{Xml(FantasyNetProject)}}" />
                  </ItemGroup>
                  <ItemGroup>
                    <None Remove="Fantasy.config" />
                    <AdditionalFiles Include="Fantasy.config">
                      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
                    </AdditionalFiles>
                  </ItemGroup>
                </Project>
                """);

            await RunDotNetAsync(root, "publish", "Consumer.csproj", "-c", "Release", "-o", "publish", "--nologo");
            AssertSingleApplicationConfig(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task PackageReferencePublishContainsOneApplicationConfig()
    {
        string root = CreateConsumerDirectory();
        try
        {
            WriteConsumerSource(root);
            File.WriteAllText(
                Path.Combine(root, "Consumer.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Fantasy-Net" Version="2026.1.1002" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(root, "NuGet.config"),
                $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="fantasy-local" value="{{Xml(Path.GetDirectoryName(TrackedPackage)!)}}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            await RunDotNetAsync(root, "restore", "Consumer.csproj", "--configfile", "NuGet.config", "--nologo");
            await RunDotNetAsync(root, "publish", "Consumer.csproj", "-c", "Release", "-o", "publish", "--no-restore", "--nologo");
            AssertSingleApplicationConfig(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteConsumerSource(string root)
    {
        File.WriteAllText(
            Path.Combine(root, "Program.cs"),
            """
            using Fantasy.Platform.Net;

            System.Console.WriteLine("Fantasy consumer smoke test");
            _ = typeof(Fantasy.Network.KCP.KCPClientNetwork);

            static async System.Threading.Tasks.Task VerifyCancellationAwareEntry(
                System.Threading.CancellationToken cancellationToken)
            {
                await Entry.Start(cancellationToken: cancellationToken);
            }
            """);
        File.Copy(
            Path.Combine(RepositoryRoot, "Fantasy.Packages", "Fantasy.Net", "Fantasy.config"),
            Path.Combine(root, "Fantasy.config"));
    }

    private static void AssertSingleApplicationConfig(string root)
    {
        string[] configs = Directory.GetFiles(Path.Combine(root, "publish"), "Fantasy.config", SearchOption.AllDirectories);
        Assert.That(configs, Has.Length.EqualTo(1));
        Assert.That(File.ReadAllText(configs[0]), Is.EqualTo(File.ReadAllText(Path.Combine(root, "Fantasy.config"))));
    }

    private static string CreateConsumerDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "fantasy-net-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task RunDotNetAsync(string workingDirectory, params string[] arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
        await process.WaitForExitAsync(timeout.Token);

        string output = await standardOutput;
        string error = await standardError;
        Assert.That(process.ExitCode, Is.Zero, $"dotnet {string.Join(' ', arguments)} failed:{Environment.NewLine}{output}{Environment.NewLine}{error}");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Fantasy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Fantasy repository root.");
    }

    private static string Xml(string value) => SecurityElement.Escape(value) ?? value;
}
