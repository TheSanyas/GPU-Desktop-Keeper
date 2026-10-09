using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Xml;

namespace GpuDesktopKeeper {
    internal static class Checks {
        private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
        private sealed class Tracker {
            internal int Live,MaxLive,Created,Disposed;
            internal bool Fail;
            internal FakeLease Current;
            internal ILease Create(KeeperMode mode) {
                if(Fail) throw new InvalidOperationException("simulated create failure");
                Live++; Created++; MaxLive=Math.Max(Live,MaxLive);
                Current=new FakeLease(this,mode); return Current;
            }
        }
        private sealed class FakeLease : ILease {
            private readonly Tracker tracker;
            private readonly KeeperMode mode;
            private bool disposed;
            internal int Reason;
            internal FakeLease(Tracker tracker,KeeperMode mode) { this.tracker=tracker; this.mode=mode; }
            public string Adapter { get { return "Simulated GPU"; } }
            public int Buffers { get { return Modes.Buffers(mode); } }
            public uint FeatureLevel { get { return 0xb100; } }
            public int RemovedReason() { Require(!disposed,"Health query after release"); return Reason; }
            public void Dispose() { Require(!disposed,"Lease released twice"); disposed=true; tracker.Live--; tracker.Disposed++; }
        }
        private static void Lifecycle() {
            var tracker=new Tracker(); int changes=0;
            using(var engine=new KeeperEngine(tracker.Create,delegate { })) {
                engine.Changed+=delegate { changes++; };
                Require(engine.Enable(KeeperMode.Verified,"check"),"Initial enable failed");
                foreach(KeeperMode mode in Enum.GetValues(typeof(KeeperMode))) {
                    Require(engine.Enable(mode,"switch"),"Switch failed");
                    Require(engine.Active && engine.DesiredEnabled && tracker.Live==1,"Incorrect active ownership");
                }
                Require(tracker.MaxLive==1,"Mode switch overlapped old/new resources");
                engine.Disable("manual");
                int created=tracker.Created;
                Require(!engine.Recover("resume") && tracker.Created==created && tracker.Live==0,"Manual disable was undone by recovery");
                engine.Enable(KeeperMode.SmallBuffer,"device-loss test");
                tracker.Current.Reason=unchecked((int)0x887a0005);
                Require(!engine.CheckHealth() && !engine.Active && engine.DesiredEnabled && tracker.Live==0,"Device-loss state not released");
                Require(engine.LastError.Contains("887A0005"),"Removal reason not preserved");
                Require(engine.Recover("recovery") && engine.Mode==KeeperMode.SmallBuffer && engine.BufferCount==1,"Recovery changed selected mode");
                tracker.Fail=true;
                Require(!engine.Enable(KeeperMode.LargeBuffer,"failure") && !engine.Active && tracker.Live==0 && engine.DesiredEnabled,"Failure kept stale resources");
                Require(engine.LastError!=null,"Failure not reported");
                tracker.Fail=false;
                Require(engine.Recover("retry") && engine.LastError==null && engine.Mode==KeeperMode.LargeBuffer,"Retry did not recover selected mode");
                engine.Dispose(); engine.Dispose();
                Require(!engine.Recover("after dispose"),"Disposed engine recovered");
            }
            Require(changes>=10 && tracker.Live==0 && tracker.Created==tracker.Disposed,"Lifecycle leak or missing transitions");
        }
        private static List<object> Hardware() {
            Require(IntPtr.Size==4,"Expected x86 to retain the proven initialization path");
            var results=new List<object>();
            // Each cycle releases before the next device is created; no Draw, Present or Flush.
            foreach(KeeperMode mode in Enum.GetValues(typeof(KeeperMode))) {
                for(int cycle=0;cycle<3;cycle++) {
                    var lease=new GpuLease(mode);
                    try {
                        Require(lease.FeatureLevel==0xb100 && lease.Buffers==Modes.Buffers(mode),"Incorrect native resources");
                        Require(lease.RemovedReason()==0,"Native device was removed");
                        results.Add(new Dictionary<string,object> {{"Mode",mode.ToString()},{"Cycle",cycle+1},{"Adapter",lease.Adapter},{"FeatureLevel",lease.FeatureLevel},{"Buffers",lease.Buffers}});
                    } finally { lease.Dispose(); lease.Dispose(); }
                }
            }
            return results;
        }
        private static void Features() {
            var preferences=new Preferences();
            Require(preferences.StartEnabled && preferences.RememberMode && preferences.StartupMode()==Modes.Default && !preferences.HideFlydigiWarning,"Fresh defaults");
            var legacy=Storage.Json.Deserialize<Preferences>("{\"AutoRecover\":true,\"WatchDevice\":false,\"StartMinimized\":true}");
            Require(legacy.StartEnabled && legacy.RememberMode && legacy.StartupMode()==Modes.Default,"Legacy settings lost new defaults");
            Require(!preferences.DarkTheme && !legacy.DarkTheme,"Fresh or legacy settings changed default theme");
            Require(preferences.Language=="ru" && legacy.Language=="ru","Legacy settings changed default language");
            preferences.Language="EN";
            Require(Storage.Json.Deserialize<Preferences>(Storage.Json.Serialize(preferences)).Language=="en","Language did not persist or normalize");
            preferences.Language="unsupported"; Require(preferences.Language=="ru","Invalid language not normalized");
            preferences.Language=null; Require(preferences.Language=="ru","Null language not normalized");
            preferences.DarkTheme=true;
            Require(Storage.Json.Deserialize<Preferences>(Storage.Json.Serialize(preferences)).DarkTheme,"Dark theme did not persist");
            var savedOldMode=Storage.Json.Deserialize<Preferences>("{\"SavedMode\":0}");
            Require(savedOldMode.StartupMode()==KeeperMode.Verified,"Old numeric mode silently remapped");
            preferences.RecordAppliedMode(KeeperMode.DeviceOnly);
            Require(!preferences.RecordAppliedMode(KeeperMode.DeviceOnly),"Unchanged mode reported changed");
            preferences.StartEnabled=false;
            var restored=Storage.Json.Deserialize<Preferences>(Storage.Json.Serialize(preferences));
            Require(!restored.StartEnabled && restored.StartupMode()==KeeperMode.DeviceOnly,"Settings round trip");
            restored.RememberMode=false; Require(restored.StartupMode()==Modes.Default,"Remember-off startup");
            restored.RememberMode=true; restored.SavedMode=12345;
            Require(restored.StartupMode()==Modes.Default,"Invalid saved mode not normalized");
            var tracker=new Tracker();
            using(var engine=new KeeperEngine(tracker.Create,delegate {},preferences.StartupMode())) {
                Require(engine.Mode==KeeperMode.DeviceOnly && !engine.Active && tracker.Created==0,"Start-off created native lease");
                if(engine.Enable(engine.Mode,"saved startup")) preferences.RecordAppliedMode(engine.Mode);
                Require(tracker.Created==1 && tracker.Current.Buffers==0,"Saved startup created intermediate buffer mode");
                tracker.Fail=true;
                if(engine.Enable(KeeperMode.SmallBuffer,"failed experiment")) preferences.RecordAppliedMode(engine.Mode);
                Require(preferences.StartupMode()==KeeperMode.DeviceOnly,"Failed experiment replaced saved mode");
                engine.Disable("off");
                Require(preferences.StartupMode()==KeeperMode.DeviceOnly && tracker.Live==0,"Off replaced saved mode or leaked");
            }
        }
        private sealed class MemoryStartup : IStartupStore {
            internal string Value;
            internal bool Deny,IgnoreWrite,DenyDelete;
            public string Read() { if(Deny) throw new UnauthorizedAccessException("simulated denied"); return Value; }
            public void Write(string command) { if(Deny) throw new UnauthorizedAccessException("simulated denied"); if(!IgnoreWrite) Value=command; }
            public void Delete() { if(Deny) throw new UnauthorizedAccessException("simulated denied"); if(DenyDelete) throw new IOException("simulated legacy deletion failure"); Value=null; }
        }
        private sealed class MemoryTaskSnapshot : IStartupStore,IStartupSnapshot {
            internal string Xml;
            public string Read() { return Xml==null ? null : StartupTaskXml.ReadCommand(Xml); }
            public void Write(string command) { Xml=StartupTaskXml.Build(command,"S-1-5-21-1-2-3-1001"); }
            public void Delete() { Xml=null; }
            public string Export() { return Xml; }
            public void Restore(string state) { Xml=state; }
        }
        private static void StartupLogic() {
            var store=new MemoryStartup();
            var manager=new StartupManager(store,@"C:\Папка с пробелами\GpuDesktopKeeper.exe",path=>true);
            Require(manager.Command=="\"C:\\Папка с пробелами\\GpuDesktopKeeper.exe\" --startup","Unquoted startup path or wrong arguments");
            for(int cycle=0;cycle<2;cycle++) {
                Require(!manager.Read().Registered,"Startup enabled by reading");
                manager.SetEnabled(true); manager.SetEnabled(true);
                Require(manager.Read().Registered && !manager.Read().OtherCopy,"Repeated enable failed");
                manager.SetEnabled(false); manager.SetEnabled(false);
                Require(!manager.Read().Registered,"Repeated disable failed");
            }
            store.Value="\"C:\\Old copy\\GpuDesktopKeeper.exe\" --startup";
            Require(manager.Read().OtherCopy,"Other installation not detected");
            manager.SetEnabled(true); Require(!manager.Read().OtherCopy,"Path update failed");
            store.Deny=true;
            Require(manager.Read().Error!=null,"Denied read reported disabled instead of unknown");
            bool denied=false; try { manager.SetEnabled(false); } catch(UnauthorizedAccessException) { denied=true; }
            Require(denied,"Failed disable reported success");
            store.Deny=false; store.Value=null; store.IgnoreWrite=true;
            bool ignored=false; try { manager.SetEnabled(true); } catch(IOException) { ignored=true; }
            Require(ignored,"Missing registry readback verification");
            store.IgnoreWrite=false;
            bool missing=false; try { new StartupManager(store,@"C:\missing.exe",path=>false).SetEnabled(true); } catch(FileNotFoundException) { missing=true; }
            Require(missing && store.Value==null,"Missing executable registered");
            var legacy=new MemoryStartup {Value="old registry command"};
            var migration=new StartupManager(store,@"C:\Keeper.exe",path=>true,legacy);
            Require(migration.Read().LegacyRegistered && !migration.Read().Registered,"Legacy reported as task");
            migration.SetEnabled(true);
            Require(legacy.Value==null && store.Value==migration.Command,"Migration did not replace legacy Run entry");
            migration.SetEnabled(false);
            legacy.Value="old registry command"; store.IgnoreWrite=true;
            bool failed=false; try { migration.SetEnabled(true); } catch(IOException) { failed=true; }
            Require(failed && legacy.Value=="old registry command" && store.Value==null,"Failed task creation removed legacy startup");
            store.IgnoreWrite=false; store.Value="old task command"; legacy.DenyDelete=true;
            failed=false; try { migration.SetEnabled(true); } catch(IOException) { failed=true; }
            Require(failed && legacy.Value=="old registry command" && store.Value=="old task command","Legacy deletion failure did not restore task");
            string sid="S-1-5-21-1-2-3-1001";
            string xml=StartupTaskXml.Build("\"C:\\Путь & пробелы\\Keeper.exe\" --startup",sid);
            StartupTaskXml.CheckOwner(xml,sid);
            Require(StartupTaskXml.ReadCommand(xml)=="\"C:\\Путь & пробелы\\Keeper.exe\" --startup","Task XML escaping or command round trip");
            CheckTaskXml(xml,sid);
            var doc=StartupTaskXml.Load(xml);
            var ns=new XmlNamespaceManager(doc.NameTable); ns.AddNamespace("t",StartupTaskXml.Namespace);
            doc.SelectSingleNode("/t:Task/t:Settings/t:Enabled",ns).InnerText="false";
            Require(StartupTaskXml.ReadCommand(doc.OuterXml)==null,"Disabled task reported enabled");
            var snapshotStore=new MemoryTaskSnapshot {Xml=doc.OuterXml};
            string disabledBefore=snapshotStore.Xml;
            var rollbackManager=new StartupManager(snapshotStore,@"C:\Keeper.exe",path=>true,legacy);
            failed=false; try { rollbackManager.SetEnabled(true); } catch(IOException) { failed=true; }
            Require(failed && snapshotStore.Xml==disabledBefore,"Rollback failed to preserve a disabled task definition");
            bool foreign=false; try { StartupTaskXml.CheckOwner(xml,"S-1-5-99"); } catch(InvalidOperationException) { foreign=true; }
            Require(foreign,"Foreign-user task accepted");
        }
        private static void CheckTaskXml(string xml,string sid) {
            var doc=StartupTaskXml.Load(xml);
            Require(StartupTaskXml.Value(doc,"/t:Task/t:Triggers/t:LogonTrigger/t:UserId")==null,"Logon trigger restricted to one user");
            Require(StartupTaskXml.SameUser(StartupTaskXml.Value(doc,"/t:Task/t:Principals/t:Principal/t:UserId"),sid),"Wrong execution user");
            Require(StartupTaskXml.Value(doc,"/t:Task/t:Principals/t:Principal/t:LogonType")=="InteractiveToken","Noninteractive logon");
            string runLevel=StartupTaskXml.Value(doc,"/t:Task/t:Principals/t:Principal/t:RunLevel");
            Require(runLevel==null || runLevel=="LeastPrivilege","Elevated task");
            Require(StartupTaskXml.Value(doc,"/t:Task/t:Settings/t:ExecutionTimeLimit")=="PT0S","Runtime limit may stop Keeper");
            Require(StartupTaskXml.Value(doc,"/t:Task/t:Settings/t:MultipleInstancesPolicy")=="IgnoreNew","Duplicate instances allowed");
            Require(StartupTaskXml.Value(doc,"/t:Task/t:Settings/t:DisallowStartIfOnBatteries")=="false" && StartupTaskXml.Value(doc,"/t:Task/t:Settings/t:StopIfGoingOnBatteries")=="false","Battery policy may stop Keeper");
        }
        internal static int StartupPayload(string token) {
            Guid parsed; if(!Guid.TryParseExact(token,"N",out parsed)) return 2;
            using(var identity=WindowsIdentity.GetCurrent())
            using(var process=Process.GetCurrentProcess()) {
                var result=new Dictionary<string,object> {{"Token",token},{"Sid",identity.User.Value},{"SessionId",process.SessionId},
                    {"AdministratorToken",new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)},{"Pid",process.Id},{"GpuApiCalled",false}};
                File.WriteAllText(Path.Combine(Storage.Folder,"startup-payload-"+token+".json"),Storage.Json.Serialize(result),Encoding.UTF8);
            }
            return 0;
        }
        internal static int RunStartupTask() {
            var result=new Dictionary<string,object> {{"Version",Program.Version},{"CreatedUtc",DateTime.UtcNow.ToString("o")},{"Cycles",2}};
            using(var identity=WindowsIdentity.GetCurrent()) result["CreatorAdministratorToken"]=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            string token=Guid.NewGuid().ToString("N");
            var realTask=new TaskStartupStore();
            var legacy=new RegistryStartupStore();
            string testName="GpuDesktopKeeper.Test."+token;
            var store=new TaskStartupStore(testName); bool owned=false;
            int exit=1;
            try {
                string previousTask=realTask.Export(),previousLegacy=legacy.Read();
                Require(store.Export()==null,"Test name collision"); owned=true;
                var manager=new StartupManager(store,System.Windows.Forms.Application.ExecutablePath,File.Exists);
                try {
                    for(int cycle=0;cycle<2;cycle++) {
                        manager.SetEnabled(true); manager.SetEnabled(true);
                        Require(store.Read()==manager.Command,"Task enable/readback failed");
                        string exported=store.Export();
                        File.WriteAllText(Path.Combine(Storage.Folder,"startup-check-task.xml"),exported,Encoding.UTF8);
                        CheckTaskXml(exported,store.Sid);
                        if(cycle==0) {
                            string payloadCommand="\""+System.Windows.Forms.Application.ExecutablePath+"\" --check-startup-payload "+token;
                            store.Write(payloadCommand); store.RunForCheck();
                            string payloadPath=Path.Combine(Storage.Folder,"startup-payload-"+token+".json");
                            var watch=Stopwatch.StartNew();
                            while(!File.Exists(payloadPath) && watch.ElapsedMilliseconds<15000) Thread.Sleep(100);
                            Require(File.Exists(payloadPath),"Task did not launch the non-GPU payload");
                            int last=0; int state;
                            do { state=store.StateForCheck(out last); if(state!=4) break; Thread.Sleep(100); } while(watch.ElapsedMilliseconds<20000);
                            Require(state!=4 && last==0,"Task payload did not exit successfully");
                            var payload=Storage.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(payloadPath));
                            Require((string)payload["Sid"]==store.Sid && !(bool)payload["AdministratorToken"],"Wrong user or elevated payload");
                            using(var process=Process.GetCurrentProcess()) Require(Convert.ToInt32(payload["SessionId"])==process.SessionId,"Payload ran in a different session");
                            result["InteractiveLaunchPayload"]=payload; result["LastTaskResult"]=last;
                        }
                        manager.SetEnabled(false); manager.SetEnabled(false);
                        Require(store.Export()==null,"Task deletion failed");
                    }
                } finally { if(owned) store.Delete(); }
                Require(store.Export()==null && realTask.Export()==previousTask && legacy.Read()==previousLegacy,"Cleanup failed or actual Keeper startup changed");
                result["RealKeeperRegistrationChanged"]=false;
                result["Cleanup"]="passed"; result["Result"]="passed"; exit=0;
            } catch(Exception ex) { result["Result"]="failed"; result["Error"]=ex.ToString(); }
            result["TemporaryTask"]=testName;
            File.WriteAllText(Path.Combine(Storage.Folder,"startup-check-result.json"),Storage.Json.Serialize(result),Encoding.UTF8);
            return exit;
        }
        internal static int Run(bool hardware) {
            var result=new Dictionary<string,object> {{"CreatedUtc",DateTime.UtcNow.ToString("o")},{"Version",Program.Version},{"ProcessBits",IntPtr.Size*8},
                {"Note",hardware ? "Includes real GPU resource creation/release. No game or smoothness tests." : "Simulated leases and hidden UI controls only. No D3D device, tray icon, driver reset or live app launch."}};
            int exit=1;
            try {
                Lifecycle(); result["Lifecycle"]="passed";
                Features(); result["SettingsAndStartup"]="passed";
                StartupLogic(); result["StartupLogicTwoCycles"]="passed";
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                KeeperWindow.CheckInteractions(); result["UiInteractions"]="passed";
                FlydigiNotice.CheckPreference(); result["FlydigiSuppression"]="passed";
                if(hardware) { result["HardwareCycles"]=Hardware(); result["Hardware"]="passed"; }
                else result["Hardware"]="not run";
                result["Result"]="passed"; exit=0;
            } catch(Exception ex) { result["Result"]="failed"; result["Error"]=ex.ToString(); }
            try { File.WriteAllText(Path.Combine(Storage.Folder,hardware ? "check-result.json" : "core-check-result.json"),Storage.Json.Serialize(result),Encoding.UTF8); }
            catch { return 2; }
            return exit;
        }
    }
}
