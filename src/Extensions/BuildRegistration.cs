using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Adapters;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Extensions;

/// <summary>The hosts feature: the programs this server works through, git's view of a copy, a host over ssh, and what a tool reaches them through.</summary>
public static class BuildRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>Everything a tool reaches a host through, with the settings read from the settings file and the <c>SNAIL_MCP_BUILDBRIDGE_</c> variables only.</summary>
        public IServiceCollection AddBuilders()
        {
            services.AddOptions<BuildOptions>().Bind(BuildSettings.Read()).Validate(options => options.HasKnownSync, "Sync must be rsync or mutagen.").ValidateOnStart();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IPrograms, Programs>();
            services.AddSingleton<ICopies, GitCopies>();
            services.AddSingleton<RemoteShell>();
            services.AddSingleton<RsyncCourier>();
            services.AddSingleton<MutagenCourier>();
            services.AddSingleton<ICourier>(provider => provider.GetRequiredService<IOptions<BuildOptions>>().Value.Sync == Delivery.Mutagen
                ? provider.GetRequiredService<MutagenCourier>()
                : provider.GetRequiredService<RsyncCourier>());
            services.AddSingleton<IBuilder, SshBuilder>();
            services.AddSingleton<Hosts>();
            services.AddSingleton<Deliveries>();
            services.AddSingleton<Logs>();
            services.AddSingleton<Provisioning>();
            services.AddSingleton<Sweeps>();
            services.AddSingleton<Runs>();
            services.AddSingleton<Collections>();

            return services;
        }
    }
}
