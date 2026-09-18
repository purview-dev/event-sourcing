using System.Reflection;

namespace Purview.EventSourcing.SourceGenerator.Generators;

public sealed class AggregateSourceGeneratorValueObjectTests : AggregateSourceGeneratorTestBase
{
	[Test]
	public async Task Generate_UsesContextualValueObjectCreateAndHookMethods(CancellationToken cancellationToken)
	{
		const string source = """
			namespace Testing
			{

			public enum OrderStatusCode
			{
				Draft = 0,
				Confirmed = 1
			}

			[Purview.ValueObjects.Serialization.Scalar]
			public readonly partial record struct OrderStatus
			{
				public OrderStatusCode Value { get; }

				private OrderStatus(OrderStatusCode value) => Value = value;

				public static OrderStatus Create(OrderStatusCode value, in Purview.ValueObjects.ValueObjectContext<OrderAggregate> context)
				{
					return new(value);
				}

				public static OrderStatus Hydrate(OrderStatusCode value) => new(value);
			}

			[Purview.EventSourcing.Aggregates.Aggregate]
			public partial class OrderAggregate : Purview.EventSourcing.Aggregates.AggregateBase
			{
				public OrderStatus Status { get; private set; } = OrderStatus.Hydrate(OrderStatusCode.Draft);
				public int LineItems { get; private set; }

				[Purview.EventSourcing.Aggregates.Event(EventName = "OrderConfirmed")]
				public partial void ConfirmOrder(OrderStatusCode status);

				public void AddLineItem() => LineItems++;

				partial void OnRaisingOrderConfirmedEvent(ref OrderStatus status)
				{
					if (Status.Value != OrderStatusCode.Draft)
						throw new System.InvalidOperationException("Can only confirm draft orders.");

					if (LineItems == 0)
						throw new System.InvalidOperationException("Cannot confirm an order with no items.");
				}
			}
			}
			""";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = await Assert.That(query).HasGeneratedClass(TypeRefs.Named("OrderAggregate", "Testing"));
		var confirmOrder = await Assert
			.That(aggregate)
			.HasMethodOfType("ConfirmOrder", [TypeRefs.Named("OrderStatusCode", "Testing")]);
		var body = confirmOrder.Node.Body?.ToString() ?? string.Empty;

		await Assert.That(body).Contains("OnRaisingOrderConfirmedEvent(ref __statusValue);");
		await Assert.That(body).Contains("OnRaisedOrderConfirmedEvent(@event);");
		await Assert
			.That(body)
			.Contains(
				"OrderStatus.Create(status, new global::Purview.ValueObjects.ValueObjectContext<global::Testing.OrderAggregate>(this, MemberName: nameof(Status), Reason: nameof(global::Testing.OrderEvents.OrderConfirmed)))"
			);
		await Assert.That(body).Contains("OnStatusChanging(ref __statusValue);");
	}

	[Test]
	public async Task Generate_AllowsHookToRejectInvalidTransition(CancellationToken cancellationToken)
	{
		const string source = """
			namespace Testing
			{

			public enum OrderStatusCode
			{
				Draft = 0,
				Confirmed = 1
			}

			[Purview.ValueObjects.Serialization.Scalar]
			public readonly partial record struct OrderStatus
			{
				public OrderStatusCode Value { get; }

				private OrderStatus(OrderStatusCode value) => Value = value;

				public static OrderStatus Create(OrderStatusCode value, in Purview.ValueObjects.ValueObjectContext<OrderAggregate> context)
				{
					return new(value);
				}

				public static OrderStatus Hydrate(OrderStatusCode value) => new(value);
			}

			[Purview.EventSourcing.Aggregates.Aggregate]
			public partial class OrderAggregate : Purview.EventSourcing.Aggregates.AggregateBase
			{
				public OrderStatus Status { get; private set; } = OrderStatus.Hydrate(OrderStatusCode.Draft);
				public int LineItems { get; private set; }

				[Purview.EventSourcing.Aggregates.Event(EventName = "OrderConfirmed")]
				public partial void ConfirmOrder(OrderStatusCode status);

				public void AddLineItem() => LineItems++;

				partial void OnRaisingOrderConfirmedEvent(ref OrderStatus status)
				{
					if (LineItems == 0)
						throw new System.InvalidOperationException("Cannot confirm an order with no items.");
				}
			}
			}
			""";

		var result = await GenerateAsync(
			source,
			EventSourcingGeneratorTestOptions.Default.Compile(),
			cancellationToken
		);
		var assembly = await Assert.That(result.CompilationResult.Assembly).IsNotNull();

		var aggregateType = assembly.GetType("Testing.OrderAggregate")!;
		var aggregate = Activator.CreateInstance(aggregateType)!;
		var orderStatusCodeType = assembly.GetType("Testing.OrderStatusCode")!;
		var confirmedValue = Enum.Parse(orderStatusCodeType, "Confirmed");
		var confirmMethod = aggregateType.GetMethod("ConfirmOrder")!;
		var addLineItemMethod = aggregateType.GetMethod("AddLineItem")!;

		var throwsWithoutLineItems = false;
		try
		{
			confirmMethod.Invoke(aggregate, [confirmedValue]);
		}
		catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
		{
			throwsWithoutLineItems = true;
		}

		addLineItemMethod.Invoke(aggregate, null);
		confirmMethod.Invoke(aggregate, [confirmedValue]);

		var status = aggregateType.GetProperty("Status")!.GetValue(aggregate)!;
		var statusValue = status.GetType().GetProperty("Value")!.GetValue(status)!;

		await Assert.That(throwsWithoutLineItems).IsTrue();
		await Assert.That(statusValue.ToString()).IsEqualTo("Confirmed");
	}

	[Test]
	public async Task Generate_RunsPropertyHooksInRequiredOrderAndSkipsPreHookOnReplay(
		CancellationToken cancellationToken
	)
	{
		const string source = """
			namespace Testing
			{
			[Purview.ValueObjects.Serialization.Scalar]
			public readonly partial record struct EmailAddress
			{
				public string Value { get; }

				private EmailAddress(string value) => Value = value;

				public static int CreateCalls { get; private set; }
				public static int HydrateCalls { get; private set; }

				public static EmailAddress Create(string value)
				{
					CreateCalls++;
					return new(value);
				}

				public static EmailAddress Hydrate(string value)
				{
					HydrateCalls++;
					return new(value);
				}
			}

			[Purview.EventSourcing.Aggregates.Aggregate]
			public partial class CustomerAggregate : Purview.EventSourcing.Aggregates.AggregateBase
			{
				public EmailAddress Email { get; private set; } = EmailAddress.Hydrate("init@example.com");
				public int ChangingCalls { get; private set; }
				public int ChangedCalls { get; private set; }
				public string Trace { get; private set; } = string.Empty;
				public string? PreviousEmailValue { get; private set; }
				public string? CurrentEmailValue { get; private set; }

				[Purview.EventSourcing.Aggregates.Event(EventName = "CustomerRegistered")]
				public partial CustomerAggregate RegisterCustomer(string email);

				[Purview.EventSourcing.Aggregates.Event(EventName = "CustomerEmailChanged")]
				public partial CustomerAggregate ChangeEmail(string email);

				partial void OnEmailChanging(ref EmailAddress email)
				{
					ChangingCalls++;
					Trace += "P";
					email = EmailAddress.Hydrate(email.Value + ".mutated");
				}

				partial void OnEmailChanged(EmailAddress previous, EmailAddress current)
				{
					ChangedCalls++;
					Trace += "C";
					PreviousEmailValue = previous.Value;
					CurrentEmailValue = current.Value;
				}

				partial void OnRaisingCustomerEmailChangedEvent(ref EmailAddress email)
				{
					Trace += "E";
				}

				partial void OnAppliedCustomerEmailChangedEvent(global::Testing.CustomerEvents.CustomerEmailChanged @event)
				{
					Trace += "A";
				}
			}
			}
			""";

		var result = await GenerateAsync(
			source,
			EventSourcingGeneratorTestOptions.Default.Compile(),
			cancellationToken
		);
		var assembly = await Assert.That(result.CompilationResult.Assembly).IsNotNull();

		var aggregateType = assembly.GetType("Testing.CustomerAggregate")!;
		var emailType = assembly.GetType("Testing.EmailAddress")!;
		var eventType = assembly.GetType("Testing.CustomerEvents.CustomerEmailChanged")!;
		var aggregate = Activator.CreateInstance(aggregateType)!;

		aggregateType.GetMethod("RegisterCustomer")!.Invoke(aggregate, ["first@example.com"]);
		aggregateType.GetMethod("ChangeEmail")!.Invoke(aggregate, ["second@example.com"]);

		var traceAfterCommand = (string)aggregateType.GetProperty("Trace")!.GetValue(aggregate)!;
		var changingCalls = (int)aggregateType.GetProperty("ChangingCalls")!.GetValue(aggregate)!;
		var changedCalls = (int)aggregateType.GetProperty("ChangedCalls")!.GetValue(aggregate)!;
		var previousValue = (string?)aggregateType.GetProperty("PreviousEmailValue")!.GetValue(aggregate);
		var currentValue = (string?)aggregateType.GetProperty("CurrentEmailValue")!.GetValue(aggregate);
		var createCalls = (int)emailType.GetProperty("CreateCalls")!.GetValue(null)!;

		var hydrateMethod = emailType.GetMethod("Hydrate", BindingFlags.Public | BindingFlags.Static)!;
		var replayEvent = Activator.CreateInstance(eventType)!;
		eventType.GetProperty("Email")!.SetValue(replayEvent, hydrateMethod.Invoke(null, ["replay@example.com"]));

		var applyMethod = aggregateType.GetMethod(
			"Apply",
			BindingFlags.NonPublic | BindingFlags.Instance,
			null,
			[eventType],
			null
		)!;
		applyMethod.Invoke(aggregate, [replayEvent]);

		var changingCallsAfterReplay = (int)aggregateType.GetProperty("ChangingCalls")!.GetValue(aggregate)!;
		var changedCallsAfterReplay = (int)aggregateType.GetProperty("ChangedCalls")!.GetValue(aggregate)!;
		var createCallsAfterReplay = (int)emailType.GetProperty("CreateCalls")!.GetValue(null)!;
		var traceAfterReplay = (string)aggregateType.GetProperty("Trace")!.GetValue(aggregate)!;

		await Assert.That(traceAfterCommand).Contains("PECA");
		await Assert.That(changingCalls).IsEqualTo(2);
		await Assert.That(changedCalls).IsEqualTo(2);
		await Assert.That(previousValue).IsEqualTo("first@example.com.mutated");
		await Assert.That(currentValue).IsEqualTo("second@example.com.mutated");
		await Assert.That(createCalls).IsEqualTo(2);
		await Assert.That(changingCallsAfterReplay).IsEqualTo(2);
		await Assert.That(changedCallsAfterReplay).IsEqualTo(3);
		await Assert.That(createCallsAfterReplay).IsEqualTo(2);
		await Assert.That(traceAfterReplay.EndsWith("CA", StringComparison.Ordinal)).IsTrue();
	}
}
