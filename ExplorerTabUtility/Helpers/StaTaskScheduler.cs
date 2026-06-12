using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ExplorerTabUtility.Helpers;

public sealed class StaTaskScheduler : TaskScheduler, IDisposable
{
    private readonly Thread _staThread;
    private readonly BlockingCollection<Task> _tasks = new();
    private int _disposed;

    public StaTaskScheduler()
    {
        _staThread = new Thread(Run)
        {
            IsBackground = true,
            Name = "STA Thread"
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
    }

    private void Run()
    {
        foreach (var task in _tasks.GetConsumingEnumerable())
        {
            try
            {
                TryExecuteTask(task);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in STA thread: {ex}");
            }
        }
    }

    // Called by the TPL to queue a task
    protected override void QueueTask(Task task)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(StaTaskScheduler));

        try
        {
            _tasks.Add(task);
        }
        catch (InvalidOperationException) when (_tasks.IsAddingCompleted)
        {
            throw new ObjectDisposedException(nameof(StaTaskScheduler));
        }
    }

    // (Optional) TPL calls this to see if we can run inline
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        // Since this scheduler only wants tasks on its dedicated thread, we *generally*
        // disallow inlining unless this is the STA thread itself.
        if (Thread.CurrentThread == _staThread)
        {
            return TryExecuteTask(task);
        }
        return false;
    }

    protected override IEnumerable<Task> GetScheduledTasks()
    {
        return _tasks.ToArray();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _tasks.CompleteAdding();
        if (Thread.CurrentThread != _staThread)
        {
            _staThread.Join();
            _tasks.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
