using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TouchAssistant.Core;

internal sealed class InputDispatch
{
	private readonly Channel<(Action Work, bool Repeat)> queue = Channel.CreateUnbounded<(Action, bool)>(new UnboundedChannelOptions
	{
		SingleReader = true
	});

	private readonly Action<Exception> failed;

	private int repeatPending;

	public InputDispatch(Action<Exception> failed)
	{
		this.failed = failed;
		Task.Run((Func<Task?>)Run);
	}

	public bool Post(Action work, bool repeat = false)
	{
		if (repeat && Interlocked.CompareExchange(ref repeatPending, 1, 0) != 0)
		{
			return false;
		}
		queue.Writer.TryWrite((work, repeat));
		return true;
	}

	private async Task Run()
	{
		await foreach ((Action, bool) item in queue.Reader.ReadAllAsync())
		{
			try
			{
				item.Item1();
			}
			catch (Exception obj)
			{
				failed(obj);
			}
			finally
			{
				if (item.Item2)
				{
					Volatile.Write(ref repeatPending, 0);
				}
			}
		}
	}

	public Task Drain()
	{
		TaskCompletionSource done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Post(delegate
		{
			done.SetResult();
		});
		return done.Task;
	}
}
