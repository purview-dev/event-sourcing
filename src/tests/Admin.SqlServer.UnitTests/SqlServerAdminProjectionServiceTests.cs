using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Admin.Services;

namespace Purview.EventSourcing.Admin.SQLServer;

public class SQLServerAdminProjectionServiceTests
{
	[Test]
	public async Task ProjectionService_IsRegisterable_InDependencyContainer()
	{
		// Arrange
		ServiceCollection services = new();
		var options = Microsoft.Extensions.Options.Options.Create(
			new EventSourcing.SqlServer.Events.SqlServerEventStoreOptions { ConnectionString = "test" }
		);

		services.AddSingleton(options);
		services.AddTransient<SqlServerAdminProjectionService>();

		var provider = services.BuildServiceProvider();

		// Act
		var service = provider.GetRequiredService<SqlServerAdminProjectionService>();

		// Assert
		await Assert.That(service).IsNotNull();
	}

	[Test]
	public async Task ProjectionService_Implements_IAdminProjectionService()
	{
		// Arrange & Act
		var service = typeof(SqlServerAdminProjectionService);
		var interfaceType = typeof(IAdminProjectionService);

		// Assert
		await Assert.That(service.GetInterfaces()).Contains(interfaceType);
	}
}
