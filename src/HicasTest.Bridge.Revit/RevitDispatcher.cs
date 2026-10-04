using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using HicasTest.Bridge.Core;

namespace HicasTest.Bridge.Revit
{
    /// <summary>Queues work from the pipe thread and runs it in a valid Revit API context via one ExternalEvent.</summary>
    public sealed class RevitDispatcher : IExternalEventHandler, IMainThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private readonly ExternalEvent _event;

        /// <summary>Must be constructed in an API context (OnStartup).</summary>
        public RevitDispatcher()
        {
            _event = ExternalEvent.Create(this);
        }

        /// <summary>The UIApplication of the API context currently executing queued work.</summary>
        public UIApplication Current { get; private set; }

        public Task<T> InvokeAsync<T>(Func<T> work)
        {
            // RunContinuationsAsynchronously: never run the pipe thread's continuation inline on Revit's thread.
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(() =>
            {
                try
                {
                    tcs.SetResult(work());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            _event.Raise();
            return tcs.Task;
        }

        public void Execute(UIApplication app)
        {
            Current = app;
            while (_queue.TryDequeue(out var work))
                work();
        }

        public string GetName() => "HicasTest bridge dispatcher";
    }
}
