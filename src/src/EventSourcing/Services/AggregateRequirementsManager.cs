using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Services;

sealed class AggregateRequiredServiceManager(IServiceProvider serviceProvider) : IAggregateRequirementsManager
{
	// This is static, but still allows the AggregateRequiredServiceManager to be registered as scoped
	// for the sake of the IServiceProvider.
	static readonly ConcurrentDictionary<Type, AggregateRequiredServiceManagerContext> Builders = new();

	public void Fulfil(IAggregate aggregate)
	{
		var context = Builders.GetOrAdd(
			aggregate.GetType(),
			t =>
			{
				AggregateRequiredServiceManagerContext builder = new(t);
				builder.Build();

				return builder;
			}
		);

		context.Populate(aggregate, serviceProvider);
	}

	sealed class AggregateRequiredServiceManagerContext(Type aggregateType)
	{
		readonly List<Action<IAggregate, IServiceProvider>> _requiredServices = [];

		static readonly Lazy<MethodInfo> PopulateMethod = new(() =>
			typeof(AggregateRequiredServiceManagerContext).GetMethod(
				nameof(Populate),
				BindingFlags.Static | BindingFlags.NonPublic
			)!
		);

		bool _hasRequirements;

		public void Build()
		{
			var requiredServices = aggregateType
				.GetInterfaces()
				.Where(m => m.IsGenericType && m.GetGenericTypeDefinition() == typeof(IRequirement<>));
			foreach (var requiredService in requiredServices)
			{
				var serviceType = requiredService.GetGenericArguments()[0];

				var genericMethod = PopulateMethod.Value.MakeGenericMethod(serviceType);
				var aggregateParameter = Expression.Parameter(typeof(IAggregate), "aggregate");
				var serviceProviderParameter = Expression.Parameter(typeof(IServiceProvider), "serviceProvider");

				var callExpression = Expression.Call(null, genericMethod, aggregateParameter, serviceProviderParameter);

				var lambda = Expression.Lambda<Action<IAggregate, IServiceProvider>>(
					callExpression,
					aggregateParameter,
					serviceProviderParameter
				);
				var action = lambda.Compile();

				_requiredServices.Add(action);
			}

			_hasRequirements = _requiredServices.Count > 0;
		}

		public void Populate(IAggregate aggregate, IServiceProvider serviceProvider)
		{
			if (_hasRequirements)
			{
				foreach (var func in _requiredServices)
					func(aggregate, serviceProvider);
			}
		}

		static void Populate<T>(IAggregate aggregate, IServiceProvider serviceProvider)
			where T : notnull
		{
			var required = (IRequirement<T>)aggregate;
			required.SetService(serviceProvider.GetRequiredService<T>());
		}
	}
}
