using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows.Forms;

namespace GpuDesktopKeeper {
    internal static class StartupRegistration {
        private static bool AccessDenied(Exception error) {
            for(var current=error;current!=null;current=current.InnerException)
                if(current is UnauthorizedAccessException || current.HResult==unchecked((int)0x80070005)) return true;
            return false;
        }
        internal static void Apply(StartupManager manager,bool enabled) {
            try { manager.SetEnabled(enabled); return; }
            catch(Exception ex) { if(!AccessDenied(ex)) throw; }
            // Elevate only the short registration helper, never the running Keeper or its task action.
            var info=new ProcessStartInfo(Application.ExecutablePath,
                "--configure-startup "+(enabled ? "enable" : "disable")+" "+TaskStartupStore.CurrentSid()) {
                UseShellExecute=true,Verb="runas",WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory
            };
            try {
                using(var helper=Process.Start(info)) {
                    helper.WaitForExit();
                    if(helper.ExitCode!=0) throw new IOException("Не удалось изменить задачу. Причина показана в окне настройки автозапуска.");
                }
            } catch(Win32Exception ex) {
                if(ex.NativeErrorCode==1223) throw new OperationCanceledException("Изменение автозапуска отменено в запросе Windows.",ex);
                throw;
            }
        }
        internal static int RunHelper(string[] args) {
            // No mutex, tray, preferences or GPU resources are created on this code path.
            if(args.Length!=3 || (args[1]!="enable" && args[1]!="disable")) return 2;
            try {
                if(args[2]!=TaskStartupStore.CurrentSid())
                    throw new InvalidOperationException("Подтверди запрос Windows той же учётной записью, в которой запущен Keeper.");
                var manager=new StartupManager(new TaskStartupStore(),Application.ExecutablePath,File.Exists,new RegistryStartupStore());
                manager.SetEnabled(args[1]=="enable");
                return 0;
            } catch(Exception ex) {
                MessageBox.Show("Не удалось изменить автозапуск: "+ex.Message,"GPU Desktop Keeper — автозапуск",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return 1;
            }
        }
    }
    internal interface IStartupSnapshot {
        string Export();
        void Restore(string state);
    }
    internal static class StartupTaskXml {
        internal const string Namespace="http://schemas.microsoft.com/windows/2004/02/mit/task";
        internal const string Source="GpuDesktopKeeper.Autostart.v1";
        internal static void SplitCommand(string command,out string path,out string arguments) {
            int end=command==null ? -1 : command.IndexOf('"',1);
            if(String.IsNullOrEmpty(command) || command[0]!='"' || end<2) throw new ArgumentException("Некорректный путь автозапуска.");
            path=command.Substring(1,end-1); arguments=command.Substring(end+1).Trim();
        }
        internal static string Build(string command,string sid) {
            string path,arguments; SplitCommand(command,out path,out arguments);
            var text=new StringBuilder();
            using(var w=XmlWriter.Create(text,new XmlWriterSettings {OmitXmlDeclaration=true})) {
                w.WriteStartElement("Task",Namespace); w.WriteAttributeString("version","1.2");
                w.WriteStartElement("RegistrationInfo");
                w.WriteElementString("Description","GPU Desktop Keeper: запуск в трей при входе любого пользователя; выполнение в интерактивном сеансе владельца задачи.");
                w.WriteElementString("Source",Source); w.WriteEndElement();
                w.WriteStartElement("Triggers"); w.WriteStartElement("LogonTrigger");
                // An omitted trigger UserId means any user logon. The execution principal remains the owner below.
                w.WriteElementString("Enabled","true");
                w.WriteEndElement(); w.WriteEndElement();
                w.WriteStartElement("Principals"); w.WriteStartElement("Principal"); w.WriteAttributeString("id","Author");
                w.WriteElementString("UserId",sid); w.WriteElementString("LogonType","InteractiveToken"); w.WriteElementString("RunLevel","LeastPrivilege");
                w.WriteEndElement(); w.WriteEndElement();
                w.WriteStartElement("Settings");
                w.WriteElementString("MultipleInstancesPolicy","IgnoreNew");
                w.WriteElementString("DisallowStartIfOnBatteries","false"); w.WriteElementString("StopIfGoingOnBatteries","false");
                w.WriteElementString("AllowHardTerminate","false"); w.WriteElementString("StartWhenAvailable","true");
                w.WriteElementString("RunOnlyIfNetworkAvailable","false");
                w.WriteStartElement("IdleSettings"); w.WriteElementString("StopOnIdleEnd","false"); w.WriteElementString("RestartOnIdle","false"); w.WriteEndElement();
                w.WriteElementString("AllowStartOnDemand","true"); w.WriteElementString("Enabled","true"); w.WriteElementString("Hidden","false");
                w.WriteElementString("RunOnlyIfIdle","false"); w.WriteElementString("WakeToRun","false");
                w.WriteElementString("ExecutionTimeLimit","PT0S"); w.WriteElementString("Priority","7"); w.WriteEndElement();
                w.WriteStartElement("Actions"); w.WriteAttributeString("Context","Author"); w.WriteStartElement("Exec");
                w.WriteElementString("Command",path); w.WriteElementString("Arguments",arguments); w.WriteElementString("WorkingDirectory",Path.GetDirectoryName(path));
                w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();
            }
            return text.ToString();
        }
        internal static XmlDocument Load(string xml) {
            var doc=new XmlDocument {XmlResolver=null};
            using(var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null})) doc.Load(reader);
            return doc;
        }
        internal static string Value(XmlDocument doc,string path) {
            var ns=new XmlNamespaceManager(doc.NameTable); ns.AddNamespace("t",Namespace);
            var node=doc.SelectSingleNode(path,ns); return node==null ? null : node.InnerText;
        }
        internal static void CheckOwner(string xml,string sid) {
            var doc=Load(xml);
            if(Value(doc,"/t:Task/t:RegistrationInfo/t:Source")!=Source || !SameUser(Value(doc,"/t:Task/t:Principals/t:Principal/t:UserId"),sid))
                throw new InvalidOperationException("Задача с таким именем не принадлежит этой установке Keeper. Она не изменена.");
        }
        internal static bool SameUser(string identifier,string sid) {
            if(String.Equals(identifier,sid,StringComparison.OrdinalIgnoreCase)) return true;
            if(String.IsNullOrEmpty(identifier) || identifier.StartsWith("S-",StringComparison.OrdinalIgnoreCase)) return false;
            try { return ((SecurityIdentifier)new NTAccount(identifier).Translate(typeof(SecurityIdentifier))).Value==sid; }
            catch(IdentityNotMappedException) { return false; }
        }
        internal static string ReadCommand(string xml) {
            var doc=Load(xml); var ns=new XmlNamespaceManager(doc.NameTable); ns.AddNamespace("t",Namespace);
            if(Value(doc,"/t:Task/t:Settings/t:Enabled")=="false") return null;
            var actions=doc.SelectNodes("/t:Task/t:Actions/*",ns);
            if(actions.Count!=1 || actions[0].LocalName!="Exec") throw new InvalidOperationException("Неизвестное действие в задаче Keeper.");
            string path=Value(doc,"/t:Task/t:Actions/t:Exec/t:Command");
            if(String.IsNullOrEmpty(path)) throw new InvalidOperationException("У задачи Keeper отсутствует путь к EXE.");
            return "\""+path+"\" "+Value(doc,"/t:Task/t:Actions/t:Exec/t:Arguments");
        }
    }
    internal sealed class TaskStartupStore : IStartupStore,IStartupSnapshot {
        internal readonly string Sid,TaskName;
        internal static string CurrentSid() { using(var identity=WindowsIdentity.GetCurrent()) return identity.User.Value; }
        internal TaskStartupStore(string taskName=null) { Sid=CurrentSid(); TaskName=taskName ?? "GPU Desktop Keeper - "+Sid; }
        private sealed class Connection : IDisposable {
            private readonly List<object> owned=new List<object>();
            internal readonly dynamic Folder;
            internal Connection() {
                try { dynamic service=Keep(Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true))); service.Connect(); Folder=Keep(service.GetFolder("\\")); }
                catch { Dispose(); throw; }
            }
            internal dynamic Keep(object value) { owned.Add(value); return value; }
            public void Dispose() { for(int i=owned.Count-1;i>=0;i--) if(owned[i]!=null && Marshal.IsComObject(owned[i])) Marshal.ReleaseComObject(owned[i]); owned.Clear(); }
        }
        private dynamic Find(Connection connection) {
            try { return connection.Keep(connection.Folder.GetTask(TaskName)); }
            catch(Exception ex) { if(ex.HResult==unchecked((int)0x80070002)) return null; throw; }
        }
        public string Export() {
            using(var connection=new Connection()) {
                dynamic task=Find(connection); if(task==null) return null;
                string xml=task.Xml; StartupTaskXml.CheckOwner(xml,Sid); return xml;
            }
        }
        public string Read() { string xml=Export(); return xml==null ? null : StartupTaskXml.ReadCommand(xml); }
        public void Write(string command) { Restore(StartupTaskXml.Build(command,Sid)); }
        public void Restore(string xml) {
            if(xml==null) { Delete(); return; }
            StartupTaskXml.CheckOwner(xml,Sid);
            using(var connection=new Connection()) {
                dynamic existing=Find(connection);
                if(existing!=null) StartupTaskXml.CheckOwner((string)existing.Xml,Sid);
                // Interactive current-user session, least privilege, no password, no registration-trigger execution.
                connection.Keep(connection.Folder.RegisterTask(TaskName,xml,6|32,Sid,null,3,null));
            }
        }
        public void Delete() {
            using(var connection=new Connection()) {
                dynamic existing=Find(connection); if(existing==null) return;
                StartupTaskXml.CheckOwner((string)existing.Xml,Sid);
                connection.Folder.DeleteTask(TaskName,0);
            }
        }
        internal void RunForCheck() {
            using(var connection=new Connection()) {
                dynamic task=Find(connection); if(task==null) throw new InvalidOperationException("Test task not found");
                StartupTaskXml.CheckOwner((string)task.Xml,Sid); connection.Keep(task.Run(null));
            }
        }
        internal int StateForCheck(out int lastResult) {
            using(var connection=new Connection()) {
                dynamic task=Find(connection); if(task==null) throw new InvalidOperationException("Test task not found");
                lastResult=(int)task.LastTaskResult; return (int)task.State;
            }
        }
    }
}
