using System;
using System.Threading.Tasks;
using HicasTest.Protocol;

namespace HicasTest.Bridge.Core
{
    /// <summary>Runs work on the host's main (API) thread.</summary>
    public interface IMainThreadDispatcher
    {
        Task<T> InvokeAsync<T>(Func<T> work);
    }

    /// <summary>
    /// Host-specific operations. Every member except <see cref="Info"/> is called on the host's
    /// main thread through <see cref="IMainThreadDispatcher"/>, so implementations need no locking.
    /// </summary>
    public interface IHostOperations
    {
        BridgeInfo Info { get; }

        DocumentInfo OpenDocument(OpenDocumentRequest request);

        void CloseDocument(CloseDocumentRequest request);

        void StartRecording();

        ChangeSet StopRecording();

        /// <summary>Starts the command on the main thread; the task completes when the host reports it finished.</summary>
        Task<CommandResult> RunCommandAsync(RunCommandRequest request);

        QueryResult Query(QueryRequest request);

        ExportImageResult ExportImage(ExportImageRequest request);

        void SetDialogRules(DialogRulesRequest request);

        DialogEventList TakeDialogEvents();
    }
}
