using System;
using System.Collections.Generic;

namespace GpuDesktopKeeper {
    internal enum KeeperMode { Verified, DeviceOnly, SmallBuffer, LargeBuffer }
    internal static class Modes {
        internal const KeeperMode Default=KeeperMode.DeviceOnly;
        internal static readonly KeeperMode[] Order={KeeperMode.DeviceOnly,KeeperMode.Verified,KeeperMode.SmallBuffer,KeeperMode.LargeBuffer};
        internal static readonly string[] Names = {
            "Эксперимент — устройство и два буфера",
            "Основной — только устройство 11.1",
            "Эксперимент — устройство и буфер 96 байт",
            "Эксперимент — устройство и буфер 8192 байта"
        };
        internal static int Buffers(KeeperMode mode) { return mode==KeeperMode.Verified ? 2 : mode==KeeperMode.DeviceOnly ? 0 : 1; }
        internal static int Bytes(KeeperMode mode) { return mode==KeeperMode.Verified ? 8288 : mode==KeeperMode.SmallBuffer ? 96 : mode==KeeperMode.LargeBuffer ? 8192 : 0; }
    }
    internal interface ILease : IDisposable {
        string Adapter { get; }
        int Buffers { get; }
        uint FeatureLevel { get; }
        int RemovedReason();
    }
    internal sealed class GpuLease : ILease {
        private GpuContextProbe.Device device;
        internal GpuLease(KeeperMode mode) {
            try {
                device=new GpuContextProbe.Device(true);
                if(mode==KeeperMode.Verified) device.InitializeHelperBuffers();
                else if(mode!=KeeperMode.DeviceOnly) device.InitializeHelperBuffers(mode==KeeperMode.SmallBuffer,mode==KeeperMode.LargeBuffer);
                if(device.FeatureLevel!=0xb100 || device.HelperBuffersCreated!=Modes.Buffers(mode))
                    throw new InvalidOperationException("Unexpected initialization result.");
            } catch { Dispose(); throw; }
        }
        public string Adapter { get { return device.AdapterName; } }
        public int Buffers { get { return device.HelperBuffersCreated; } }
        public uint FeatureLevel { get { return device.FeatureLevel; } }
        public int RemovedReason() { return device.GetRemovedReason(); }
        public void Dispose() { var old=device; device=null; if(old!=null) old.Dispose(); }
    }
    // All calls run on the UI thread. No timer, rendering, or GPU work in the engine.
    internal sealed class KeeperEngine : IDisposable {
        private readonly Func<KeeperMode,ILease> factory;
        private readonly Action<string> log;
        private ILease lease;
        private bool disposed;
        internal bool DesiredEnabled { get; private set; }
        internal bool Active { get { return lease!=null; } }
        internal KeeperMode Mode { get; private set; }
        internal string LastError { get; private set; }
        internal string Adapter { get { return Active ? lease.Adapter : "—"; } }
        internal int BufferCount { get { return Active ? lease.Buffers : 0; } }
        internal uint FeatureLevel { get { return Active ? lease.FeatureLevel : 0; } }
        internal int Generation { get; private set; }
        internal DateTime? EnabledUtc { get; private set; }
        internal event Action Changed;
        internal KeeperEngine(Func<KeeperMode,ILease> factory,Action<string> log,KeeperMode initialMode=Modes.Default) {
            if(!Enum.IsDefined(typeof(KeeperMode),initialMode)) throw new ArgumentOutOfRangeException("initialMode");
            this.factory=factory; this.log=log; Mode=initialMode;
        }
        private void Publish() { if(Changed!=null) Changed(); }
        private void Release() {
            var old=lease; lease=null; EnabledUtc=null;
            if(old!=null) { old.Dispose(); log("Released own resources"); }
        }
        internal bool Enable(KeeperMode mode,string reason) {
            if(disposed) throw new ObjectDisposedException("KeeperEngine");
            if(!Enum.IsDefined(typeof(KeeperMode),mode)) throw new ArgumentOutOfRangeException("mode");
            DesiredEnabled=true;
            Mode=mode;
            Release(); // No overlapping old/new leases during a mode comparison.
            bool success=false;
            try {
                lease=factory(mode);
                if(lease==null) throw new InvalidOperationException("Device factory returned no resources.");
                Generation++;
                LastError=null;
                EnabledUtc=DateTime.UtcNow;
                log("Enabled mode="+mode+" generation="+Generation+" adapter="+Adapter+" buffers="+BufferCount+" reason="+reason);
                success=true;
            } catch(Exception ex) {
                Release();
                LastError=ex.Message+" (0x"+ex.HResult.ToString("X8")+")";
                log("Enable failed mode="+mode+": "+ex);
            }
            Publish(); return success;
        }
        internal void Disable(string reason) {
            DesiredEnabled=false; Release(); LastError=null;
            log("Disabled reason="+reason); Publish();
        }
        internal bool CheckHealth() {
            if(!Active) return false;
            try {
                int hr=lease.RemovedReason();
                if(hr==0) return true;
                LastError="GPU недоступен: 0x"+hr.ToString("X8");
            } catch(Exception ex) { LastError=ex.Message; }
            log(LastError); Release(); Publish(); return false;
        }
        internal bool Recover(string reason) { return DesiredEnabled && Enable(Mode,reason); }
        internal Dictionary<string,object> Snapshot() {
            return new Dictionary<string,object> {
                {"DesiredEnabled",DesiredEnabled},{"Active",Active},{"Mode",Mode.ToString()},
                {"Adapter",Adapter},{"FeatureLevel",FeatureLevel},{"BufferCount",BufferCount},
                {"BufferPayloadBytes",Active ? Modes.Bytes(Mode) : 0},{"Generation",Generation},
                {"EnabledUtc",EnabledUtc.HasValue ? EnabledUtc.Value.ToString("o") : null},{"LastError",LastError}
            };
        }
        public void Dispose() { if(disposed) return; disposed=true; DesiredEnabled=false; Release(); }
    }
}
