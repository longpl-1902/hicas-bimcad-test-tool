using System;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using HicasTest.Bridge.Core;

[assembly: ExtensionApplication(typeof(HicasTest.Bridge.AutoCAD.BridgeExtension))]

namespace HicasTest.Bridge.AutoCAD
{
    /// <summary>Test-only extension, NETLOADed by the runner's startup script. Never ships with a product.</summary>
    public sealed class BridgeExtension : IExtensionApplication
    {
        private PipeBridgeServer _server;
        private int _pid;

        public void Initialize()
        {
            try
            {
                _pid = Process.GetCurrentProcess().Id;
                var dispatcher = new AcadDispatcher();
                var version = Convert.ToString(Application.GetSystemVariable("ACADVER"));
                var ops = new AcadHostOperations(version, _pid);

                _server = new PipeBridgeServer(ops.Info.PipeName, new RpcRouter(dispatcher, ops));
                _server.Start();
                DiscoveryFile.Write(ops.Info);
                BridgeLog.Info("autocad bridge ready, ACADVER " + version);
            }
            catch (System.Exception ex)
            {
                BridgeLog.Error("autocad bridge startup", ex);
            }
        }

        public void Terminate()
        {
            _server?.Dispose();
            DiscoveryFile.Delete(_pid);
        }
    }
}
