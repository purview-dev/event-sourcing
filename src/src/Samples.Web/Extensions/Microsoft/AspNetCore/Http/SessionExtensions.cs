using System.Text.Json;
using Purview.EventSourcing.Samples.Services;

namespace Microsoft.AspNetCore.Http;

static class SessionExtensions
{
	const string CartKey = "cart";

	extension(ISession session)
	{
		public List<CartItem> GetCart()
		{
			var json = session.GetString(CartKey);
			return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<CartItem>>(json) ?? [];
		}

		public void SetCart(List<CartItem> cart) => session.SetString(CartKey, JsonSerializer.Serialize(cart));

		public int GetCartCount() => session.GetCart().Sum(c => c.Quantity);
	}
}
