using System.Net;
using System.Net.Sockets;

namespace Purview.EventSourcing.Faults;

/// <summary>
/// A TCP proxy that sits between a client and a container and breaks the connection on command.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FaultInjectingEventStore{T}"/> fails at the store boundary, which covers the
/// framework's own logic. It cannot reach the failures that only exist below it: a connection reset
/// part-way through a write, a response that stops arriving, a server that accepts a connection and
/// then says nothing. Those are the ones that decide whether a provider's SDK retried, and whether
/// that retry duplicated anything.
/// </para>
/// <para>
/// <strong>Written here rather than adding Toxiproxy.</strong> Toxiproxy's toxics are probabilistic,
/// and a test that fails a random write turns a real defect into an intermittent annoyance nobody
/// trusts. This faults the <em>nth</em> connection, or after an exact byte count, so a failure
/// reproduces. It is also a container and a dependency this repository then does not have.
/// </para>
/// <para>
/// <strong>It is not a general-purpose proxy.</strong> It forwards bytes in both directions and
/// knows nothing about TDS, the PostgreSQL wire protocol or HTTP. That is enough to sever a
/// connection convincingly and nowhere near enough to corrupt one in a protocol-aware way.
/// </para>
/// </remarks>
public sealed class FaultingTcpProxy : IAsyncDisposable
{
	readonly TcpListener _listener;
	readonly string _targetHost;
	readonly int _targetPort;
	readonly CancellationTokenSource _shutdown = new();
	readonly Task _accepting;

	int _connections;

	FaultingTcpProxy(string targetHost, int targetPort)
	{
		_targetHost = targetHost;
		_targetPort = targetPort;

		_listener = new TcpListener(IPAddress.Loopback, 0);
		_listener.Start();

		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		_accepting = AcceptLoopAsync(_shutdown.Token);
	}

	/// <summary>Starts a proxy in front of <paramref name="targetHost"/>:<paramref name="targetPort"/>.</summary>
	public static FaultingTcpProxy Start(string targetHost, int targetPort) => new(targetHost, targetPort);

	/// <summary>The loopback port clients should connect to.</summary>
	public int Port { get; }

	/// <summary>The number of connections accepted since the proxy started.</summary>
	public int ConnectionsAccepted => Volatile.Read(ref _connections);

	/// <summary>
	/// When set, every connection from this one onwards is severed as soon as the server starts
	/// replying. One-based; <see langword="null"/> forwards everything.
	/// </summary>
	/// <remarks>
	/// Severing on the <em>reply</em> rather than the request is what models a lost response: the
	/// server has received and may have committed the work, and the client is told only that the
	/// connection went away. That is the fault that produces duplicates, because the caller's natural
	/// reaction is to retry something that already happened.
	/// </remarks>
	public int? ResetRepliesFromConnection { get; set; }

	/// <summary>
	/// When set, a connection is severed after this many bytes have been forwarded <em>to</em> the
	/// server, modelling a half-written request.
	/// </summary>
	public int? ResetAfterBytesSent { get; set; }

	/// <summary>Stops accepting and severing, so later connections are refused outright.</summary>
	public void Stop() => _listener.Stop();

	async Task AcceptLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var client = await _listener.AcceptTcpClientAsync(cancellationToken);
				var ordinal = Interlocked.Increment(ref _connections);

				// Not awaited: each connection is pumped independently, and the listener has to get
				// back to accepting immediately or the client's next connection blocks.
				_ = PumpAsync(client, ordinal, cancellationToken);
			}
		}
		catch (OperationCanceledException) { }
		catch (ObjectDisposedException) { }
		catch (SocketException) { }
	}

	async Task PumpAsync(TcpClient client, int ordinal, CancellationToken cancellationToken)
	{
		using (client)
		{
			try
			{
				using TcpClient server = new();
				await server.ConnectAsync(_targetHost, _targetPort, cancellationToken);

				var severReplies = ResetRepliesFromConnection is { } from && ordinal >= from;

				using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

				var upstream = CopyAsync(
					client.GetStream(),
					server.GetStream(),
					ResetAfterBytesSent,
					linked,
					linked.Token
				);
				var downstream = severReplies
					// A zero budget severs on the first byte the server sends back.
					? CopyAsync(server.GetStream(), client.GetStream(), 0, linked, linked.Token)
					: CopyAsync(server.GetStream(), client.GetStream(), null, linked, linked.Token);

				await Task.WhenAny(upstream, downstream);
				await linked.CancelAsync();
			}
			// The whole point of this type is to break connections, so every way a connection can
			// break is an expected outcome here rather than an error.
			catch (OperationCanceledException) { }
			catch (IOException) { }
			catch (SocketException) { }
			catch (ObjectDisposedException) { }
		}
	}

	static async Task CopyAsync(
		NetworkStream from,
		NetworkStream to,
		int? severAfterBytes,
		CancellationTokenSource linked,
		CancellationToken cancellationToken
	)
	{
		var buffer = new byte[16 * 1024];
		var forwarded = 0;

		while (!cancellationToken.IsCancellationRequested)
		{
			var read = await from.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;

			if (severAfterBytes is { } budget && forwarded + read > budget)
			{
				// Forward whatever still fits inside the budget, then stop: a request cut off
				// mid-stream is a more realistic fault than one that never started.
				var allowed = Math.Max(0, budget - forwarded);
				if (allowed > 0)
					await to.WriteAsync(buffer.AsMemory(0, allowed), cancellationToken);

				await linked.CancelAsync();

				return;
			}

			await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
			forwarded += read;
		}
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await _shutdown.CancelAsync();
		_listener.Dispose();

		try
		{
			await _accepting;
		}
		catch (OperationCanceledException) { }

		_shutdown.Dispose();
	}
}
