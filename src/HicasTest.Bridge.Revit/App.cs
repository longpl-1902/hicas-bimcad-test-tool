using System;
using System.Diagnostics;
using Autodesk.Revit.UI;
using HicasTest.Bridge.Core;

namespace HicasTest.Bridge.Revit
{
    /// <summary>
    /// Test-only add-in. The runner writes a temporary .addin manifest for it and removes it after the run;
    /// it never ships with a product.
    /// </summary>
    public sealed class App : IExternalApplication
    {
        private PipeBridgeServer _server;
        private int _pid;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                _pid = Process.GetCurrentProcess().Id;
                var dispatcher = new RevitDispatcher();
                var ops = new RevitHostOperations(dispatcher, application.ControlledApplication.VersionNumber, _pid);

                application.ControlledApplication.DocumentChanged += ops.OnDocumentChanged;
                application.ControlledApplication.FailuresProcessing += ops.OnFailuresProcessing;
                application.DialogBoxShowing += ops.OnDialogBoxShowing;
                application.Idling += ops.OnIdling;

                _server = new PipeBridgeServer(ops.Info.PipeName, new RpcRouter(dispatcher, ops));
                _server.Start();
                DiscoveryFile.Write(ops.Info);
                BridgeLog.Info("revit bridge ready, Revit " + ops.Info.HostVersion);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                BridgeLog.Error("revit bridge startup", ex);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            _server?.Dispose();
            DiscoveryFile.Delete(_pid);
            return Result.Succeeded;
        }
    }
}
