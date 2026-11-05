namespace Purview.EventSourcing.Samples.ValueObjects;

public sealed class BlobUriTests
{
	[Test]
	public async Task BlobUri_GivenNullUri_ThrowsArgumentNullException()
	{
		// Arrange
		static BlobUri Create() => BlobUri.Create(null!);

		// Act/ Assert
		await Assert.That(Create).Throws<ArgumentNullException>();
	}

	[Test]
	[Arguments("https://localhost")]
	[Arguments("http://example.com/")]
	public async Task BlobUri_GivenNonAbsoluteUri_ThrowsArgumentException(string value)
	{
		// Arrange
		BlobUri Create() => BlobUri.Create(new Uri(value));

		// Act/ Assert
		await Assert.That(Create).Throws<ArgumentException>();
	}
}
