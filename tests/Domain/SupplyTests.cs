namespace Snail.MCP.BuildBridge.Tests.Domain;

public sealed class SupplyTests
{
    [Fact]
    public void In_ALogWithNetSdk1147_IsTheWorkloadsItNamesEachOnce()
    {
        const string log = """
            /Users/dev/.s3l-build/dotnet/sdk/10.0.302/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.ImportWorkloads.targets(38,5): error NETSDK1147: To build this project, the following workloads must be installed: maui-android wasm-tools [/Users/dev/App.csproj]
            /Users/dev/.s3l-build/dotnet/sdk/10.0.302/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.ImportWorkloads.targets(38,5): error NETSDK1147: To build this project, the following workloads must be installed: maui-android [/Users/dev/Lib.csproj]
            """;

        var supply = Lacks.In(log);

        Assert.NotNull(supply);
        Assert.Equal(SupplyKind.Workloads, supply.Kind);
        Assert.Equal(["maui-android", "wasm-tools"], supply.Names);
    }

    [Fact]
    public void In_ALogWithXA5207_IsThePlatformBeforeAnythingElse()
    {
        var supply = Lacks.In("error NETSDK1147: To build this project, the following workloads must be installed: maui-android\nerror XA5207: Could not find android.jar for API level 37.\n");

        Assert.NotNull(supply);
        Assert.Equal(SupplyKind.AndroidPackages, supply.Kind);
        Assert.Equal(["platforms;android-37"], supply.Names);
    }

    [Fact]
    public void In_ALogNamingNothing_IsNull() => Assert.Null(Lacks.In("error CS1002: ; expected\n"));

    [Fact]
    public void Words_ByKind_SayWhatWasSupplied()
    {
        Assert.Equal(["sdk 10.0.400"], new Supply(SupplyKind.Sdk, ["10.0.400"]).Words);
        Assert.Equal(["workload maui-android"], new Supply(SupplyKind.Workloads, ["maui-android"]).Words);
        Assert.Equal(["platforms;android-37.0"], new Supply(SupplyKind.AndroidPackages, ["platforms;android-37.0"]).Words);
        Assert.Equal("dotnet/sdk", new Supply(SupplyKind.Sdk, ["10.0.400"]).Turn);
    }
}
