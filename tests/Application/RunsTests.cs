using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Tools;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.Hosting.Sessions;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Application;

public sealed class RunsTests
{
    private static readonly Host Mini = new("mini", "mini.local");

    private sealed class Steps : IProgress<JobProgress>
    {
        public List<string> Said { get; } = [];

        public void Report(JobProgress value) => Said.Add(value.What ?? string.Empty);
    }

    private static (Runs Runs, FakeBuilder Builder) Of(IEnumerable<Capability>? has = null, FeedSetting[]? feeds = null, IReadOnlyList<string>? sdks = null)
    {
        var builder = new FakeBuilder();
        var toolchains = (has ?? [Capability.Dotnet]).Select(capability => new Toolchain(capability, "1"));
        builder.Up[Mini.Name] = new HostState(Mini, true, [.. toolchains], 400, null, "/Users/dev") { Sdks = sdks ?? [] };
        var options = Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = Mini.Name, Address = Mini.Address }], Feeds = feeds ?? [] });
        var hosts = new Hosts(options, builder);

        var logs = new Logs(TimeProvider.System, TestFolder.Create());

        return (new Runs(hosts, new FakeCopies(), builder, logs, new Sweeps(builder, logs, options, TimeProvider.System), new WorkspaceTurns(), new FakeSecrets(), options), builder);
    }

    [Fact]
    public async Task RunAsync_DotnetTest_DeliversRunsAndReadsWhatTheTestsSaid()
    {
        var (runs, builder) = Of();
        var steps = new Steps();

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["test", "-c", "Release"], [], [], null, steps, CancellationToken.None);

        var report = Assert.IsType<BuildReport>(answer.Value);
        Assert.True(report.IsSuccess);
        Assert.Equal(["dotnet", "test", "-c", "Release"], Assert.Single(builder.Executed).Command);
        Assert.Equal(new TestSummary(12, 0, 1, 13), report.Tests);
        Assert.Single(builder.Delivered);
        Assert.Contains(steps.Said, step => step.StartsWith("running dotnet test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_FeedsConfigured_AreMirroredBeforeTheRun()
    {
        var (runs, builder) = Of(feeds: [new FeedSetting { Name = "LocalPackages", Path = "/Users/me/nuget" }]);

        await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["build"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Equal(("/Users/me/nuget", "feeds/LocalPackages"), Assert.Single(builder.Mirrored));
    }

    [Fact]
    public async Task RunAsync_HostWithoutTheToolchain_RunsNothing()
    {
        var (runs, builder) = Of(has: [Capability.Dotnet]);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Xcode, ["build"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Null(answer.Value);
        Assert.Contains("no xcode", answer.Error, StringComparison.Ordinal);
        Assert.Equal("noToolchain", answer.Type);
        Assert.Empty(builder.Delivered);
    }

    [Fact]
    public async Task RunAsync_FailedTests_IsNotASuccessEvenWithExitCodeZero()
    {
        var (runs, builder) = Of();
        builder.LogText = "Failed!  - Failed:     1, Passed:     3, Skipped:     0, Total:     4, Duration: 1 s - A.dll (net10.0)\n";

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["test"], [], [], null, new Steps(), CancellationToken.None);

        Assert.False(answer.Value!.IsSuccess);
    }

    /// <summary>Two runs on one workspace go one after the other, the second saying it waits.</summary>
    [Fact]
    public async Task RunAsync_SameWorkspaceTwice_TakesTurns()
    {
        var (runs, builder) = Of();
        var gate = new TaskCompletionSource();
        builder.Held = gate.Task;
        var second = new Steps();

        var first = runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["build"], [], [], null, new Steps(), CancellationToken.None);
        await Task.Delay(100);
        var waiting = runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["test"], [], [], null, second, CancellationToken.None);
        await Task.Delay(100);

        Assert.Single(builder.Executed);
        Assert.Contains(second.Said, step => step.StartsWith("waiting for", StringComparison.Ordinal));
        gate.SetResult();
        await Task.WhenAll(first, waiting);
        Assert.Equal(2, builder.Executed.Count);
    }

    [Fact]
    public async Task RunAsync_XcodeTestOnASimulatorByName_GetsACloneForTheRunAndGivesItBack()
    {
        var (runs, builder) = Of(has: [Capability.Xcode]);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Xcode, ["test", "-scheme", "App", "-destination", "platform=iOS Simulator,name=iPhone 17,OS=27.1"], [], [], null, new Steps(), CancellationToken.None);

        var (device, clone) = Assert.Single(builder.Acquired);
        Assert.Equal("iPhone 17", device);
        Assert.StartsWith(XcodeCommand.SimulatorPrefix, clone, StringComparison.Ordinal);
        var command = Assert.Single(builder.Executed).Command;
        Assert.Contains("platform=iOS Simulator,id=UDID-1", command);
        Assert.Contains("-derivedDataPath", command);
        Assert.Equal(["UDID-1"], builder.Released);
        Assert.NotNull(answer.Value);
    }

    /// <summary>Two destinations by name in one run: a clone each, both pointed at by id in one xcodebuild, both given back.</summary>
    [Fact]
    public async Task RunAsync_XcodeTestOnTwoSimulatorsByName_GetsACloneEachInOneRun()
    {
        var (runs, builder) = Of(has: [Capability.Xcode]);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Xcode, ["test", "-scheme", "App", "-destination", "platform=iOS Simulator,name=iPhone 17 Pro", "-destination", "platform=iOS Simulator,name=iPad 11-inch (A16)"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Equal(["iPhone 17 Pro", "iPad 11-inch (A16)"], builder.Acquired.Select(acquired => acquired.Device));
        var command = Assert.Single(builder.Executed).Command;
        Assert.Contains("platform=iOS Simulator,id=UDID-1", command);
        Assert.Contains("platform=iOS Simulator,id=UDID-2", command);
        Assert.Equal(["UDID-1", "UDID-2"], builder.Released);
        Assert.NotNull(answer.Value);
    }

    /// <summary>A second clone refused leaves nothing behind: the first is given back, and nothing runs.</summary>
    [Fact]
    public async Task RunAsync_XcodeTestWhenASecondCloneIsRefused_GivesTheFirstBackAndRunsNothing()
    {
        var (runs, builder) = Of(has: [Capability.Xcode]);
        builder.SimulatorsAvailable = 1;

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Xcode, ["test", "-scheme", "App", "-destination", "platform=iOS Simulator,name=iPhone 17 Pro", "-destination", "platform=iOS Simulator,name=iPad 11-inch (A16)"], [], [], null, new Steps(), CancellationToken.None);

        Assert.NotNull(answer.Error);
        Assert.Empty(builder.Executed);
        Assert.Equal(["UDID-1"], builder.Released);
    }

    [Fact]
    public async Task RunAsync_XcodeWithASpecBesideTheProject_GeneratesTheProjectFirst()
    {
        var (runs, builder) = Of([Capability.Xcode]);
        string copy = TestFolder.Create();
        Directory.CreateDirectory(Path.Combine(copy, "apple"));
        await File.WriteAllTextAsync(Path.Combine(copy, "apple", "project.yml"), "name: App\n");
        var copies = new FakeCopies { Copy = new LocalCopy(copy, "Sample", "main", null, false) };
        var logs = new Logs(TimeProvider.System, TestFolder.Create());
        var options = Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = Mini.Name, Address = Mini.Address }] });
        runs = new Runs(new Hosts(options, builder), copies, builder, logs, new Sweeps(builder, logs, options, TimeProvider.System), new WorkspaceTurns(), new FakeSecrets(), options);

        var answer = await runs.RunAsync(copy, BuildKind.Xcode, ["build", "-project", "apple/App.xcodeproj", "-scheme", "App"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Null(answer.Error);
        Assert.Equal(2, builder.Executed.Count);
        Assert.Equal(["xcodegen", "generate", "--spec", "apple/project.yml", "--project", "apple"], builder.Executed[0].Command);
        Assert.Equal("xcodebuild", builder.Executed[1].Command[0]);
    }

    [Fact]
    public async Task RunAsync_AScriptOfTheCopy_RunsItAsItIs()
    {
        var (runs, builder) = Of([]);
        string copy = TestFolder.Create();
        Directory.CreateDirectory(Path.Combine(copy, "tools"));
        await File.WriteAllTextAsync(Path.Combine(copy, "tools", "pack.sh"), "#!/bin/sh\necho packed\n");
        var copies = new FakeCopies { Copy = new LocalCopy(copy, "Sample", "main", null, false) };
        var logs = new Logs(TimeProvider.System, TestFolder.Create());
        var options = Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = Mini.Name, Address = Mini.Address }] });
        runs = new Runs(new Hosts(options, builder), copies, builder, logs, new Sweeps(builder, logs, options, TimeProvider.System), new WorkspaceTurns(), new FakeSecrets(), options);

        var answer = await runs.RunAsync(copy, BuildKind.Script, ["tools/pack.sh", "Debug"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Null(answer.Error);
        Assert.Equal(["tools/pack.sh", "Debug"], Assert.Single(builder.Executed).Command);
    }

    [Theory]
    [InlineData("tools/missing.sh", Reached.Absent)]
    [InlineData("../elsewhere.sh", Reached.Outside)]
    [InlineData("/usr/bin/true", Reached.Outside)]
    public async Task RunAsync_AScriptNotOfTheCopy_RunsNothing(string script, string type)
    {
        var (runs, builder) = Of([]);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Script, [script], [], [], null, new Steps(), CancellationToken.None);

        Assert.NotNull(answer.Error);
        Assert.Equal(type, answer.Type);
        Assert.Empty(builder.Executed);
        Assert.Empty(builder.Delivered);
    }

    [Fact]
    public void SpecFolderOf_AProjectOutsideTheCopy_GeneratesNothing()
    {
        string copy = TestFolder.Create();
        var outside = new LocalCopy(copy, "Sample", "main", null, false);
        File.WriteAllText(Path.Combine(TestFolder.Create(), "project.yml"), "name: Elsewhere\n");

        Assert.Null(Runs.SpecFolderOf(outside, ["build", "-project", "../Elsewhere/App.xcodeproj"]));
        Assert.Null(Runs.SpecFolderOf(outside, ["build", "-project", "/tmp/App.xcodeproj"]));
    }

    [Fact]
    public async Task RunAsync_DockerBuild_CarriesTheOwnerLabel()
    {
        var (runs, builder) = Of(has: [Capability.Docker]);

        await runs.RunAsync("/Users/me/Sample", BuildKind.Docker, ["build", "-t", "sample", "."], [], [], null, new Steps(), CancellationToken.None);

        Assert.Equal(["docker", "build", "--label", "snail.owner=buildbridge/Sample/main", "-t", "sample", "."], Assert.Single(builder.Executed).Command);
        Assert.Empty(builder.Acquired);
    }

    [Fact]
    public async Task RunAsync_LinuxTest_RunsDotnetInsideTheDotnetImageOnTheHost()
    {
        var (runs, builder) = Of([Capability.Docker]);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Linux, ["test"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Null(answer.Error);
        var command = Assert.Single(builder.Executed).Command;
        Assert.Equal(["docker", "run"], command.Take(2));
        Assert.Contains("/Users/dev/.s3l-build/workspaces/Sample/main/Sample:/work", command);
        Assert.Contains("s3l-build-dotnet:10.0.302", command);
        Assert.Equal(["dotnet", "test"], command.TakeLast(2));
    }

    [Fact]
    public async Task RunAsync_InAContainerOnAHostThatNamedNoHome_RunsNothingAndIsNoHome()
    {
        var (runs, builder) = Of([Capability.Docker]);
        builder.Up[Mini.Name] = builder.Up[Mini.Name] with { Home = null };

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Linux, ["test"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Equal("noHome", answer.Type);
        Assert.Empty(builder.Delivered);
    }

    /// <summary>With the toolchain under the root, an Android build is dotnet on the host, the prelude naming the SDK and the JDK; without it, docker runs the Android image.</summary>
    [Fact]
    public async Task RunAsync_AndroidOnAHostWithTheToolchain_RunsDotnetOnTheHostAndInTheImageWithoutIt()
    {
        var (onHost, hostBuilder) = Of([Capability.Android, Capability.Docker]);
        var (inImage, imageBuilder) = Of([Capability.Docker]);

        await onHost.RunAsync("/Users/me/Sample", BuildKind.Android, ["build", "-f", "net10.0-android"], [], [], null, new Steps(), CancellationToken.None);
        await inImage.RunAsync("/Users/me/Sample", BuildKind.Android, ["build", "-f", "net10.0-android"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Equal(["dotnet", "build", "-f", "net10.0-android"], Assert.Single(hostBuilder.Executed).Command);
        var command = Assert.Single(imageBuilder.Executed).Command;
        Assert.Equal(["docker", "run"], command.Take(2));
        Assert.Contains("s3l-build-android:10.0.302", command);
    }

    /// <summary>What a build needs comes with the call: the packages are put under the root before anything runs, and the report names them.</summary>
    [Fact]
    public async Task RunAsync_WithNeeds_SuppliesThePackagesBeforeTheRunAndReportsThem()
    {
        var (runs, builder) = Of([Capability.Android, Capability.Dotnet]);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Android, ["build"], ["platforms;android-37", "build-tools;37.0.0"], [], null, new Steps(), CancellationToken.None);

        Assert.Null(answer.Error);
        Assert.Equal(["platforms;android-37", "build-tools;37.0.0"], Assert.Single(builder.Supplied).Names);
        Assert.Equal(["platforms;android-37", "build-tools;37.0.0"], answer.Value!.Supplied);
        Assert.Single(builder.Executed);
    }

    [Fact]
    public async Task RunAsync_WithNeedsOnAHostWithoutTheToolchain_RunsNothing()
    {
        var (runs, builder) = Of([Capability.Docker]);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Android, ["build"], ["platforms;android-37"], [], null, new Steps(), CancellationToken.None);

        Assert.Contains("no android", answer.Error, StringComparison.Ordinal);
        Assert.Equal("noToolchain", answer.Type);
        Assert.Empty(builder.Supplied);
        Assert.Empty(builder.Executed);
    }

    [Theory]
    [InlineData("platforms;android-37; rm -rf /")]
    [InlineData("$(id)")]
    public async Task RunAsync_WithANeedThatIsNoPackage_RefusesBeforeTouchingTheHost(string word)
    {
        var (runs, builder) = Of([Capability.Android]);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Android, ["build"], [word], [], null, new Steps(), CancellationToken.None);

        Assert.Contains("not an Android SDK package", answer.Error, StringComparison.Ordinal);
        Assert.Equal(ToolFailures.Arguments, answer.Type);
        Assert.Empty(builder.Probed);
    }

    /// <summary>An Android build on the host that failed for want of a platform names it in its log: the platform is supplied and the build runs again, once.</summary>
    [Fact]
    public async Task RunAsync_AndroidOnTheHostLackingAPlatform_SuppliesItAndRunsAgainOnce()
    {
        var (runs, builder) = Of([Capability.Android]);
        builder.ExitCode = 1;
        builder.LogText = "error XA5207: Could not find android.jar for API level 37. This means the Android SDK platform for API level 37 is not installed.\n";

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Android, ["build"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Equal(["platforms;android-37"], Assert.Single(builder.Supplied).Names);
        Assert.Equal(2, builder.Executed.Count);
        Assert.Equal(["platforms;android-37"], answer.Value!.Supplied);
        Assert.False(answer.Value.IsSuccess);
    }

    /// <summary>In the image the platform cannot be supplied, and any other failure is the build's own: neither runs again.</summary>
    [Fact]
    public async Task RunAsync_AndroidInTheImageOrFailingOtherwise_RunsOnce()
    {
        var (inImage, imageBuilder) = Of([Capability.Docker]);
        imageBuilder.ExitCode = 1;
        imageBuilder.LogText = "error XA5207: Could not find android.jar for API level 37.\n";
        var (otherwise, otherBuilder) = Of([Capability.Android]);
        otherBuilder.ExitCode = 1;
        otherBuilder.LogText = "error CS1002: ; expected\n";

        await inImage.RunAsync("/Users/me/Sample", BuildKind.Android, ["build"], [], [], null, new Steps(), CancellationToken.None);
        await otherwise.RunAsync("/Users/me/Sample", BuildKind.Android, ["build"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Empty(imageBuilder.Supplied);
        Assert.Single(imageBuilder.Executed);
        Assert.Empty(otherBuilder.Supplied);
        Assert.Single(otherBuilder.Executed);
    }

    /// <summary>The SDK a copy's global.json wishes for goes under the root before the run when none there meets it; one that does leaves the root alone.</summary>
    [Theory]
    [InlineData("10.0.302", "10.0.100", true)]
    [InlineData("10.0.302", "10.0.305", false)]
    public async Task RunAsync_ACopyWishingForAnSdk_SuppliesItOnlyWhenTheHostLacksOneThatMeetsIt(string wished, string held, bool expected)
    {
        var (runs, builder) = Of([Capability.Dotnet], sdks: [held]);
        string copy = TestFolder.Create();
        await File.WriteAllTextAsync(Path.Combine(copy, "global.json"), $$"""{ "sdk": { "version": "{{wished}}", "rollForward": "latestPatch" } }""");
        var copies = new FakeCopies { Copy = new LocalCopy(copy, "Sample", "main", null, false) };
        var logs = new Logs(TimeProvider.System, TestFolder.Create());
        var options = Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = Mini.Name, Address = Mini.Address }] });
        runs = new Runs(new Hosts(options, builder), copies, builder, logs, new Sweeps(builder, logs, options, TimeProvider.System), new WorkspaceTurns(), new FakeSecrets(), options);

        var answer = await runs.RunAsync(copy, BuildKind.Dotnet, ["build"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Null(answer.Error);
        Assert.Equal(expected, builder.Supplied.Count == 1);
        if (expected)
        {
            Assert.Equal(SupplyKind.Sdk, builder.Supplied[0].Kind);
            Assert.Equal([wished], builder.Supplied[0].Names);
            Assert.Equal([$"sdk {wished}"], answer.Value!.Supplied);
        }
        Assert.Single(builder.Executed);
    }

    /// <summary>A dotnet build on the host that failed for want of workloads names them: they are installed in the copy, so dotnet takes its global.json's SDK, and the build runs again.</summary>
    [Fact]
    public async Task RunAsync_DotnetLackingWorkloads_InstallsThemInTheCopyAndRunsAgainOnce()
    {
        var (runs, builder) = Of([Capability.Dotnet]);
        builder.ExitCode = 1;
        builder.LogText = "error NETSDK1147: To build this project, the following workloads must be installed: maui-android wasm-tools\n To install these workloads, run the following command: dotnet workload restore\n";

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["build"], [], [], null, new Steps(), CancellationToken.None);

        var supply = Assert.Single(builder.Supplied);
        Assert.Equal(SupplyKind.Workloads, supply.Kind);
        Assert.Equal(["maui-android", "wasm-tools"], supply.Names);
        Assert.Equal("workspaces/Sample/main/Sample", supply.Folder);
        Assert.Equal(2, builder.Executed.Count);
        Assert.Equal(["workload maui-android", "workload wasm-tools"], answer.Value!.Supplied);
    }

    /// <summary>What the call assigns and what the settings keep secret both reach the command's environment; a secret wins over an assignment of its name, so a call never stands in for one.</summary>
    [Fact]
    public async Task RunAsync_WithEnvironmentAndSecrets_GivesTheCommandBothWithTheSecretsOnTop()
    {
        var (_, builder) = Of();
        var secrets = new FakeSecrets();
        secrets.Values["TOKEN"] = "from-the-keychain";
        var logs = new Logs(TimeProvider.System, TestFolder.Create());
        var options = Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = Mini.Name, Address = Mini.Address }] });
        var runs = new Runs(new Hosts(options, builder), new FakeCopies(), builder, logs, new Sweeps(builder, logs, options, TimeProvider.System), new WorkspaceTurns(), secrets, options);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["test"], [], ["ASPNETCORE_ENVIRONMENT=Testing", "TOKEN=from-the-call"], null, new Steps(), CancellationToken.None);

        Assert.Null(answer.Error);
        Assert.Equal(["Sample"], secrets.AskedFor);
        var environment = Assert.Single(builder.Environments);
        Assert.Equal("Testing", environment["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal("from-the-keychain", environment["TOKEN"]);
        Assert.DoesNotContain(Assert.Single(builder.Executed).Command, word => word.Contains("from-the", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("TOKEN")]
    [InlineData("1TOKEN=x")]
    [InlineData("A B=c")]
    public async Task RunAsync_WithAWordThatIsNoAssignment_RefusesBeforeTouchingTheHost(string word)
    {
        var (runs, builder) = Of();

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["test"], [], [word], null, new Steps(), CancellationToken.None);

        Assert.Contains("NAME=value", answer.Error, StringComparison.Ordinal);
        Assert.Equal(ToolFailures.Arguments, answer.Type);
        Assert.Empty(builder.Probed);
    }

    [Fact]
    public async Task RunAsync_ASecretThatCannotBeRead_RunsNothingAndSaysWhich()
    {
        var (_, builder) = Of();
        var secrets = new FakeSecrets { Error = "The secret TOKEN could not be read from the keychain service anonym: not found" };
        var logs = new Logs(TimeProvider.System, TestFolder.Create());
        var options = Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = Mini.Name, Address = Mini.Address }] });
        var runs = new Runs(new Hosts(options, builder), new FakeCopies(), builder, logs, new Sweeps(builder, logs, options, TimeProvider.System), new WorkspaceTurns(), secrets, options);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["test"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Contains("TOKEN", answer.Error, StringComparison.Ordinal);
        Assert.Empty(builder.Executed);
    }

    [Fact]
    public async Task RunAsync_GradleOfACopyWithTheWrapper_RunsItFromTheRoot()
    {
        var (runs, builder) = Of([Capability.Android]);
        string copy = TestFolder.Create();
        await File.WriteAllTextAsync(Path.Combine(copy, "gradlew"), "#!/bin/sh\necho built\n");
        var copies = new FakeCopies { Copy = new LocalCopy(copy, "Sample", "main", null, false) };
        var logs = new Logs(TimeProvider.System, TestFolder.Create());
        var options = Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = Mini.Name, Address = Mini.Address }] });
        runs = new Runs(new Hosts(options, builder), copies, builder, logs, new Sweeps(builder, logs, options, TimeProvider.System), new WorkspaceTurns(), new FakeSecrets(), options);

        var answer = await runs.RunAsync(copy, BuildKind.Gradle, ["assembleDebug"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Null(answer.Error);
        Assert.Equal(["./gradlew", "assembleDebug"], Assert.Single(builder.Executed).Command);
    }

    [Fact]
    public async Task RunAsync_GradleOfACopyWithoutTheWrapper_RunsNothingAndSaysSo()
    {
        var (runs, builder) = Of([Capability.Android]);
        string copy = TestFolder.Create();
        var copies = new FakeCopies { Copy = new LocalCopy(copy, "Sample", "main", null, false) };
        var logs = new Logs(TimeProvider.System, TestFolder.Create());
        var options = Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = Mini.Name, Address = Mini.Address }] });
        runs = new Runs(new Hosts(options, builder), copies, builder, logs, new Sweeps(builder, logs, options, TimeProvider.System), new WorkspaceTurns(), new FakeSecrets(), options);

        var answer = await runs.RunAsync(copy, BuildKind.Gradle, ["assembleDebug"], [], [], null, new Steps(), CancellationToken.None);

        Assert.Contains("gradlew", answer.Error, StringComparison.Ordinal);
        Assert.Equal(Reached.Absent, answer.Type);
        Assert.Empty(builder.Executed);
    }

    [Fact]
    public async Task RunAsync_LinuxOnAHostWithoutDocker_RunsNothing()
    {
        var (runs, builder) = Of([Capability.Dotnet]);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Linux, ["test"], [], [], null, new Steps(), CancellationToken.None);

        Assert.NotNull(answer.Error);
        Assert.Empty(builder.Executed);
    }

    [Fact]
    public async Task RunAsync_StaleWorkspaceOfTheRepository_IsSweptAfterTheRun()
    {
        var (runs, builder) = Of();
        var old = DateTimeOffset.UtcNow.AddDays(-30);
        builder.Found = new Survey(Mini, [new WorkspaceState(new WorkspaceKey("Sample", "old"), old, 5), new WorkspaceState(new WorkspaceKey("Other", "old"), old, 5)], [], [], 0, 0, 400);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["build"], [], [], null, new Steps(), CancellationToken.None);

        var swept = Assert.Single(answer.Value!.Swept);
        Assert.Equal("Sample/old", swept.Item.Name);
        Assert.Equal(["Sample/old"], builder.CarriedOut.Select(item => item.Name));
    }

    [Fact]
    public async Task RunAsync_HostShortOfRoom_SweepsTheCachesFirst()
    {
        var (runs, builder) = Of();
        builder.Up[Mini.Name] = new HostState(Mini, true, [new Toolchain(Capability.Dotnet, "1")], 5, null);
        builder.Found = new Survey(Mini, [], [], [], 3.5, 0, 5);
        var steps = new Steps();

        await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["build"], [], [], null, steps, CancellationToken.None);

        Assert.Contains(builder.CarriedOut, item => item.Kind == SweepKind.BuildCache);
        Assert.Contains(steps.Said, step => step.StartsWith("swept mini", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("dotnet", BuildKind.Dotnet)]
    [InlineData("Xcode", BuildKind.Xcode)]
    [InlineData("DOCKER", BuildKind.Docker)]
    public void KindOf_AnyCase_IsTheKind(string word, BuildKind expected) => Assert.Equal(expected, Runs.KindOf(word).Value);

    [Fact]
    public void KindOf_UnknownWord_NamesTheKindsThereAre()
    {
        var answer = Runs.KindOf("make");

        Assert.Contains(ToolDescriptions.Kinds, answer.Error, StringComparison.Ordinal);
        Assert.Equal(ToolFailures.Arguments, answer.Type);
    }
}
