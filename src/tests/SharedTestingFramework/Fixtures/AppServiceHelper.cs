using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Purview.EventSourcing.Fixtures;

public sealed class AppServiceHelper : IServiceProvider, IAsyncDisposable
{
	readonly ServiceCollection _services = [];
	readonly ConfigurationManager _configuration = new();
	readonly Lazy<IServiceProvider> _serviceProvider;
	readonly Lazy<AsyncServiceScope> _serviceScope;

	public IConfiguration Configuration => _configuration;

	public AppServiceHelper(Action<IServiceCollection>? configure = null)
		: this((service, _) => configure?.Invoke(service)) { }

	public AppServiceHelper(Action<IConfigurationBuilder>? configure = null)
		: this((_, config) => configure?.Invoke(config)) { }

	public AppServiceHelper(Action<IServiceCollection, IConfigurationBuilder>? configure = null)
	{
		_serviceProvider = new(() =>
		{
			ConfigureServices(_services);
			configure?.Invoke(_services, _configuration);

			_services.AddSingleton(Configuration);

			return _services.BuildServiceProvider(
				new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
			);
		});
		_serviceScope = new(_serviceProvider.Value.CreateAsyncScope);
	}

	static void ConfigureServices(ServiceCollection services)
	{
		services
			.AddMetrics()
			.AddLogging(configure =>
			{
				configure.SetMinimumLevel(LogLevel.Trace);
				// Providers here so the testing infra can pick it up.
				configure.AddDebug();
			});

		services.AddDistributedMemoryCache();
	}

	public async ValueTask DisposeAsync()
	{
		if (_serviceScope.IsValueCreated)
			await _serviceScope.Value.DisposeAsync();

		_configuration.Dispose();

		GC.SuppressFinalize(this);
	}

	public object? GetService(Type serviceType) => _serviceScope.Value.ServiceProvider.GetService(serviceType);

	public IServiceProvider CloneServices(Action<IServiceCollection>? configure)
	{
		ServiceCollection clonedServices = [.. _services];
		configure?.Invoke(clonedServices);

		return clonedServices.BuildServiceProvider(
			new ServiceProviderOptions() { ValidateOnBuild = true, ValidateScopes = true }
		);
	}
}
