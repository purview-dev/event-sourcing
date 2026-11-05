using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.Samples.Domain;
using Purview.Results;

namespace Purview.EventSourcing.Samples.QuickStart;

sealed class ValidatorScenarios() : IAsyncDisposable
{
	readonly List<AsyncServiceScope> _scopes = [];

	IServiceProvider BuildServices(Action<IServiceCollection>? action = null)
	{
		ServiceCollection services = [];

		services.AddLogging();

		services.AddInMemoryEventStore().AddInMemorySnapshotEventStore();

		action?.Invoke(services);

		var scope = services.BuildServiceProvider().CreateAsyncScope();
		_scopes.Add(scope);

		return scope.ServiceProvider;
	}

	// Registered first so the phone number is the only thing wrong with the aggregate: ZodSharp validates
	// the whole graph, and an unregistered customer also fails on its null name and email.
	static CustomerAggregate CreateAggregate(int numberOfChars = 200)
	{
		CustomerAggregate agg = new();
		agg.RegisterCustomer("Quick Start", "quickstart@example.com");

		return agg.ChangePhoneNumber(new string('1', numberOfChars));
	}

	public async Task ValidateAsync(CancellationToken cancellationToken)
	{
		await ValidateBuiltInAsync(cancellationToken);
		await ValidateFluentValidatorAsync(cancellationToken);
		await ValidateZodSharpAsync(cancellationToken);
	}

	async Task ValidateBuiltInAsync(CancellationToken cancellationToken)
	{
		var sp = BuildServices();
		var store = sp.GetRequiredService<IQueryableEventStore>();

		var agg = CreateAggregate();

		var result = await store.SaveAsync(agg, cancellationToken);

		DisplaySummary("BuiltIn", result);
	}

	async Task ValidateFluentValidatorAsync(CancellationToken cancellationToken)
	{
		var sp = BuildServices(services => services.AddDomainFluentValidators());
		var store = sp.GetRequiredService<IQueryableEventStore>();

		var agg = CreateAggregate();

		var result = await store.SaveAsync(agg, cancellationToken);

		DisplaySummary("FluentValidation", result);
	}

	async Task ValidateZodSharpAsync(CancellationToken cancellationToken)
	{
		var sp = BuildServices(services => services.AddDomainZodValidators());
		var store = sp.GetRequiredService<IQueryableEventStore>();

		var agg = CreateAggregate();

		var result = await store.SaveAsync(agg, cancellationToken);

		DisplaySummary("ZodSharp", result);
	}

	static void DisplaySummary(string type, Result<SaveOutcome<CustomerAggregate>, AggregateSaveFailure> result)
	{
		Console.WriteLine($"Validated '{type}':");

		if (result.IsSuccess || result.Error is not AggregateInvalid invalid)
			Console.WriteLine("  FAILED: Did not validate correctly!!");
		else
		{
			var failures = invalid.ValidationResult.Failures;
			if (failures.Length != 1)
				Console.WriteLine(
					$"  FAILED: Should be exactly 1 error, but found {failures.Length}: {string.Join(" | ", failures.Select(f => $"{f.PropertyName}={f.ErrorMessage}"))}"
				);
			else if (failures[0].PropertyName != nameof(CustomerAggregate.PhoneNumber))
				Console.WriteLine(
					$"   FAILED: Error should have validated '{nameof(CustomerAggregate.PhoneNumber)}', but found '{failures[0].PropertyName}'"
				);
			else
				Console.WriteLine($"  SUCCESS: Found {failures[0].ErrorMessage}");
		}

		Console.WriteLine();
	}

	public async ValueTask DisposeAsync()
	{
		foreach (var scope in _scopes)
			await scope.DisposeAsync();
	}
}
