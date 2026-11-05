using System.Reflection;

namespace Purview.EventSourcing;

/// <summary>
/// Guards the mirror between the typed and facade extension sets.
/// </summary>
/// <remarks>
/// The facade set is generated from the typed one, so these assert the property that matters to a caller -
/// that the two surfaces agree - rather than asserting how the generator produced it. They would equally
/// catch drift if either set were hand-written again.
/// </remarks>
public class EventStoreExtensionsMirrorTests
{
	static MethodInfo[] PublicExtensions(Type type) =>
		[.. type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)];

	// Signature without the receiver: the receiver is the one thing the two sets differ by.
	static string Signature(MethodInfo method) =>
		$"{method.Name}({string.Join(", ", method.GetParameters().Skip(1).Select(p => p.ParameterType.ToString()))})";

	[Test]
	public async Task FacadeExtensions_ForEveryTypedExtension_ExposeTheSameSignature()
	{
		var typed = PublicExtensions(typeof(IEventStoreCoreExtensions))
			.Select(Signature)
			.ToHashSet(StringComparer.Ordinal);
		var facade = PublicExtensions(typeof(IEventStoreExtensions))
			.Select(Signature)
			.ToHashSet(StringComparer.Ordinal);

		var missing = typed.Except(facade, StringComparer.Ordinal).ToArray();

		await Assert.That(missing).IsEmpty().Because($"the facade is missing: {string.Join(", ", missing)}");
	}

	[Test]
	public async Task FacadeExtensions_BeyondTheTypedSet_AreOnlyTheReceiverlessTransactionHelpers()
	{
		var typed = PublicExtensions(typeof(IEventStoreCoreExtensions))
			.Select(Signature)
			.ToHashSet(StringComparer.Ordinal);
		var extra = PublicExtensions(typeof(IEventStoreExtensions))
			.Where(method => !typed.Contains(Signature(method)))
			.Select(static method => method.Name)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		// Enlist has non-generic overloads that start an empty transaction; there is no typed equivalent
		// because the typed receiver already fixes an aggregate type.
		await Assert
			.That(extra)
			.IsEquivalentTo(new[] { nameof(IEventStoreExtensions.Enlist) })
			.Because($"unexpected facade-only members: {string.Join(", ", extra)}");
	}

	[Test]
	public async Task TypedExtensions_TakeNoObjectTypedIdentifiers()
	{
		// The object-id overloads were removed in the API cleanup; they silently accepted anything and
		// deferred the failure to ToString().
		var offenders = PublicExtensions(typeof(IEventStoreCoreExtensions))
			.Concat(PublicExtensions(typeof(IEventStoreExtensions)))
			.Where(static method =>
				method
					.GetParameters()
					.Any(static p =>
						p.ParameterType == typeof(object) && p.Name?.Contains("Id", StringComparison.Ordinal) == true
					)
			)
			.Select(Signature)
			.ToArray();

		await Assert.That(offenders).IsEmpty().Because(string.Join(", ", offenders));
	}
}
