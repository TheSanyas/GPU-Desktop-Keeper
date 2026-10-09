using System;
using System.IO;
using Microsoft.Win32;

namespace GpuDesktopKeeper {
    internal interface IStartupStore {
        string Read();
        void Write(string command);
        void Delete();
    }
    internal sealed class RegistryStartupStore : IStartupStore {
        internal const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string AppValue="GPU Desktop Keeper";
        private readonly string value;
        internal RegistryStartupStore(string value=AppValue) { this.value=value; }
        public string Read() {
            using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))
            using(var key=root.OpenSubKey(RunKey,false)) {
                if(key==null) return null;
                object data=key.GetValue(value,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                if(data==null) return null;
                if(!(data is string)) throw new InvalidOperationException(UiText.Get("Запись автозапуска имеет неизвестный формат."));
                return (string)data;
            }
        }
        public void Write(string command) {
            using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))
            using(var key=root.CreateSubKey(RunKey)) key.SetValue(value,command,RegistryValueKind.String);
        }
        public void Delete() {
            using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))
            using(var key=root.OpenSubKey(RunKey,true)) if(key!=null) key.DeleteValue(value,false);
        }
    }
    internal sealed class StartupState {
        internal bool Registered,OtherCopy,LegacyRegistered;
        internal string Error;
    }
    internal sealed class StartupManager {
        private readonly IStartupStore store;
        private readonly IStartupStore legacy;
        private readonly Func<string,bool> exists;
        private readonly string path;
        internal readonly string Command;
        internal StartupManager(IStartupStore store,string executable,Func<string,bool> exists,IStartupStore legacy=null) {
            if(String.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) || executable.IndexOfAny(new[]{'"','\r','\n'})>=0)
                throw new ArgumentException("Для автозапуска нужен полный путь к EXE.");
            this.store=store; this.legacy=legacy; this.exists=exists; path=executable;
            Command="\""+executable+"\" --startup";
        }
        internal StartupState Read() {
            try {
                string actual=store.Read();
                return new StartupState {Registered=!String.IsNullOrEmpty(actual),LegacyRegistered=legacy!=null && legacy.Read()!=null,OtherCopy=!String.IsNullOrEmpty(actual) && !String.Equals(actual,Command,StringComparison.OrdinalIgnoreCase)};
            } catch(Exception ex) { return new StartupState {Error=ex.Message}; }
        }
        internal void SetEnabled(bool enabled) {
            if(enabled && !exists(path)) throw new FileNotFoundException(UiText.Get("EXE не найден. Автозапуск не изменён."),path);
            var snapshot=store as IStartupSnapshot;
            string previous=snapshot!=null ? snapshot.Export() : store.Read();
            string previousLegacy=legacy==null ? null : legacy.Read();
            try {
                if(enabled) {
                    store.Write(Command);
                    if(!String.Equals(store.Read(),Command,StringComparison.Ordinal)) throw new IOException(UiText.Get("Планировщик не подтвердил создание задачи."));
                } else {
                    store.Delete();
                    if(snapshot!=null ? snapshot.Export()!=null : store.Read()!=null) throw new IOException(UiText.Get("Не удалось удалить задачу автозапуска."));
                }
                if(legacy!=null && previousLegacy!=null) {
                    legacy.Delete();
                    if(legacy.Read()!=null) throw new IOException(UiText.Get("Не удалось удалить прежнюю запись Keeper из реестра."));
                }
            } catch(Exception original) {
                string rollbackError=null;
                try {
                    if(snapshot!=null) snapshot.Restore(previous);
                    else if(previous==null) store.Delete(); else store.Write(previous);
                } catch(Exception ex) { rollbackError=ex.Message; }
                try { if(legacy!=null && previousLegacy!=null && legacy.Read()!=previousLegacy) legacy.Write(previousLegacy); }
                catch(Exception ex) { rollbackError=(rollbackError==null ? "" : rollbackError+"; ")+ex.Message; }
                if(rollbackError!=null) throw new IOException(original.Message+UiText.Get(" Ошибка возврата прежнего автозапуска: ")+rollbackError,original);
                throw;
            }
        }
    }
}
