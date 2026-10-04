using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using HicasTest.Bridge.Core;

namespace HicasTest.Bridge.AutoCAD
{
    /// <summary>
    /// Marshals work onto AutoCAD's main thread through a hidden WinForms control. Work runs in
    /// application context: lock the document before touching its database.
    /// </summary>
    public sealed class AcadDispatcher : IMainThreadDispatcher
    {
        private readonly Control _control;

        /// <summary>Must be constructed on AutoCAD's main thread (Initialize).</summary>
        public AcadDispatcher()
        {
            _control = new Control();
            _ = _control.Handle; // forces handle creation on this thread
        }

        public Task<T> InvokeAsync<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _control.BeginInvoke(new Action(() =>
            {
                try
                {
                    tcs.SetResult(work());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }));
            return tcs.Task;
        }
    }
}
