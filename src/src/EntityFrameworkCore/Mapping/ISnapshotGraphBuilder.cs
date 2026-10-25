using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Purview.EventSourcing.EntityFrameworkCore.Mapping;

/// <summary>
/// The subset of the EF builder API the snapshot graph walker needs, so the same walk can configure an
/// entity root (document stores), a complex property, or an element of a complex collection.
/// </summary>
interface ISnapshotGraphBuilder
{
	/// <summary>Excludes a member from the model.</summary>
	/// <param name="propertyName">The member name.</param>
	void Ignore(string propertyName);

	/// <summary>Maps a member as a scalar property.</summary>
	/// <param name="propertyType">The member type.</param>
	/// <param name="propertyName">The member name.</param>
	void Property(Type propertyType, string propertyName);

	/// <summary>Maps a member as a scalar property converted by <paramref name="converterType"/>.</summary>
	/// <param name="propertyType">The member type.</param>
	/// <param name="propertyName">The member name.</param>
	/// <param name="converterType">The closed value-converter type.</param>
	void PropertyWithConversion(Type propertyType, string propertyName, Type converterType);

	/// <summary>Maps a member as a collection of primitives.</summary>
	/// <param name="propertyType">The collection type.</param>
	/// <param name="propertyName">The member name.</param>
	void PrimitiveCollection(Type propertyType, string propertyName);

	/// <summary>Maps a member as a nested complex type.</summary>
	/// <param name="propertyType">The member type.</param>
	/// <param name="propertyName">The member name.</param>
	/// <param name="configure">Configures the nested type.</param>
	void ComplexProperty(Type propertyType, string propertyName, Action<ISnapshotGraphBuilder> configure);

	/// <summary>Maps a member as a collection of nested complex types.</summary>
	/// <param name="propertyType">The collection type.</param>
	/// <param name="propertyName">The member name.</param>
	/// <param name="configure">Configures the element type.</param>
	void ComplexCollection(Type propertyType, string propertyName, Action<ISnapshotGraphBuilder> configure);
}

/// <summary>
/// Adapts the EF builders to <see cref="ISnapshotGraphBuilder"/>.
/// </summary>
static class SnapshotGraphBuilder
{
	/// <summary>Adapts an entity root.</summary>
	/// <param name="builder">The entity builder.</param>
	/// <returns>The adapted builder.</returns>
	public static ISnapshotGraphBuilder For(EntityTypeBuilder builder) => new EntityAdapter(builder);

	/// <summary>Adapts a complex property.</summary>
	/// <param name="builder">The complex property builder.</param>
	/// <returns>The adapted builder.</returns>
	public static ISnapshotGraphBuilder For(ComplexPropertyBuilder builder) => new ComplexPropertyAdapter(builder);

	/// <summary>Adapts a complex collection.</summary>
	/// <param name="builder">The complex collection builder.</param>
	/// <returns>The adapted builder.</returns>
	public static ISnapshotGraphBuilder For(ComplexCollectionBuilder builder) => new ComplexCollectionAdapter(builder);

	sealed class EntityAdapter(EntityTypeBuilder builder) : ISnapshotGraphBuilder
	{
		public void Ignore(string propertyName) => builder.Ignore(propertyName);

		public void Property(Type propertyType, string propertyName) => builder.Property(propertyType, propertyName);

		public void PropertyWithConversion(Type propertyType, string propertyName, Type converterType) =>
			builder.Property(propertyType, propertyName).HasConversion(converterType);

		public void PrimitiveCollection(Type propertyType, string propertyName) =>
			builder.PrimitiveCollection(propertyType, propertyName);

		public void ComplexProperty(Type propertyType, string propertyName, Action<ISnapshotGraphBuilder> configure) =>
			builder.ComplexProperty(propertyType, propertyName, nested => configure(For(nested)));

		public void ComplexCollection(
			Type propertyType,
			string propertyName,
			Action<ISnapshotGraphBuilder> configure
		) => builder.ComplexCollection(propertyType, propertyName, nested => configure(For(nested)));
	}

	sealed class ComplexPropertyAdapter(ComplexPropertyBuilder builder) : ISnapshotGraphBuilder
	{
		public void Ignore(string propertyName) => builder.Ignore(propertyName);

		public void Property(Type propertyType, string propertyName) => builder.Property(propertyType, propertyName);

		public void PropertyWithConversion(Type propertyType, string propertyName, Type converterType) =>
			builder.Property(propertyType, propertyName).HasConversion(converterType);

		public void PrimitiveCollection(Type propertyType, string propertyName) =>
			builder.PrimitiveCollection(propertyType, propertyName);

		public void ComplexProperty(Type propertyType, string propertyName, Action<ISnapshotGraphBuilder> configure) =>
			builder.ComplexProperty(propertyType, propertyName, nested => configure(For(nested)));

		public void ComplexCollection(
			Type propertyType,
			string propertyName,
			Action<ISnapshotGraphBuilder> configure
		) => builder.ComplexCollection(propertyType, propertyName, nested => configure(For(nested)));
	}

	sealed class ComplexCollectionAdapter(ComplexCollectionBuilder builder) : ISnapshotGraphBuilder
	{
		public void Ignore(string propertyName) => builder.Ignore(propertyName);

		public void Property(Type propertyType, string propertyName) => builder.Property(propertyType, propertyName);

		public void PropertyWithConversion(Type propertyType, string propertyName, Type converterType) =>
			builder.Property(propertyType, propertyName).HasConversion(converterType);

		public void PrimitiveCollection(Type propertyType, string propertyName) =>
			builder.PrimitiveCollection(propertyType, propertyName);

		public void ComplexProperty(Type propertyType, string propertyName, Action<ISnapshotGraphBuilder> configure) =>
			builder.ComplexProperty(propertyType, propertyName, nested => configure(For(nested)));

		public void ComplexCollection(
			Type propertyType,
			string propertyName,
			Action<ISnapshotGraphBuilder> configure
		) => builder.ComplexCollection(propertyType, propertyName, nested => configure(For(nested)));
	}
}
