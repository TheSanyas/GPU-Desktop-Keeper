using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyTitle("GPU Desktop Keeper")]
[assembly: AssemblyProduct("GPU Desktop Keeper")]
[assembly: AssemblyDescription("Persistent Direct3D 11 device workaround for secondary-monitor stutter")]
[assembly: AssemblyCopyright("Copyright (c) 2026 GPU Desktop Keeper contributors")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8",FrameworkDisplayName=".NET Framework 4.8")]

namespace GpuDesktopKeeper {
    internal sealed class Preferences {
        public bool AutoRecover { get; set; }
        public bool WatchDevice { get; set; }
        public bool StartMinimized { get; set; }
        public bool StartEnabled { get; set; }
        public bool RememberMode { get; set; }
        public int SavedMode { get; set; }
        public bool HideFlydigiWarning { get; set; }
        public bool DarkTheme { get; set; }
        public Preferences() { AutoRecover=true; StartEnabled=true; RememberMode=true; SavedMode=(int)Modes.Default; }
        internal KeeperMode StartupMode() {
            return RememberMode && Enum.IsDefined(typeof(KeeperMode),SavedMode) ? (KeeperMode)SavedMode : Modes.Default;
        }
        internal bool RecordAppliedMode(KeeperMode mode) {
            if(!Enum.IsDefined(typeof(KeeperMode),mode)) throw new ArgumentOutOfRangeException("mode");
            if(SavedMode==(int)mode) return false;
            SavedMode=(int)mode; return true;
        }
    }
    internal static class Storage {
        internal static readonly string Folder=AppDomain.CurrentDomain.BaseDirectory;
        internal static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
        internal static string LastWriteError;
        internal static void Log(string text) {
            try {
                string path=Path.Combine(Folder,"keeper.log");
                if(File.Exists(path) && new FileInfo(path).Length>1024*1024) {
                    File.Copy(path,Path.Combine(Folder,"keeper.previous.log"),true);
                    File.WriteAllText(path,"",Encoding.UTF8);
                }
                File.AppendAllText(path,DateTime.UtcNow.ToString("o")+" PID="+Program.Pid+" "+text+Environment.NewLine,Encoding.UTF8);
            } catch(Exception ex) { LastWriteError=ex.Message; }
        }
        internal static Preferences Load() {
            try {
                string path=Path.Combine(Folder,"keeper-settings.json");
                return File.Exists(path) ? Json.Deserialize<Preferences>(File.ReadAllText(path)) ?? new Preferences() : new Preferences();
            } catch(Exception ex) { Log("Settings read failed; using defaults: "+ex.Message); return new Preferences(); }
        }
        internal static void Save(Preferences preferences) {
            string path=Path.Combine(Folder,"keeper-settings.json");
            string temp=path+".tmp";
            File.WriteAllText(temp,Json.Serialize(preferences),Encoding.UTF8);
            if(File.Exists(path)) File.Replace(temp,path,null);
            else File.Move(temp,path);
        }
    }
    internal static class Program {
        internal const string Version="1.0";
        internal static readonly int Pid=ReadPid();
        private static int ReadPid() { using(var process=Process.GetCurrentProcess()) return process.Id; }
        [STAThread]
        private static int Main(string[] args) {
            if(args.Length>0 && args[0]=="--configure-startup") return StartupRegistration.RunHelper(args);
            if(args.Length==1 && args[0]=="--check") return Checks.Run(true);
            if(args.Length==1 && args[0]=="--check-core") return Checks.Run(false);
            if(args.Length==1 && args[0]=="--check-startup") return Checks.RunStartupTask();
            if(args.Length==2 && args[0]=="--check-startup-payload") return Checks.StartupPayload(args[1]);
            if(args.Length==1 && args[0]=="--preview") return KeeperWindow.RenderPreviews();
            if(args.Length==1 && args[0]=="--preview-live") return KeeperWindow.ShowPreview();
            bool startup=args.Length==1 && args[0]=="--startup";
            bool minimized=startup || (args.Length==1 && args[0]=="--minimized");
            if(args.Length>0 && !minimized) return 2;
            bool created;
            // Shared with v1, preventing an unnoticed second lease from corrupting comparisons.
            using(var mutex=new Mutex(true,@"Local\GpuDesktopKeeper.Manual.v1",out created)) {
                if(!created) {
                    if(startup) return 0;
                    MessageBox.Show("GPU Desktop Keeper уже запущен.",
                        "GPU Desktop Keeper",MessageBoxButtons.OK,MessageBoxIcon.Information);
                    return 0;
                }
                try {
                    Storage.Log("Started version="+Version+" x86="+(IntPtr.Size==4));
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    using(var app=new KeeperApplication(minimized)) Application.Run(app);
                    return 0;
                } catch(Exception ex) {
                    Storage.Log("Fatal: "+ex);
                    MessageBox.Show("Не удалось продолжить работу. Собственные ресурсы освобождены.\n"+ex.Message,
                        "GPU Desktop Keeper",MessageBoxButtons.OK,MessageBoxIcon.Error);
                    return 1;
                } finally { Storage.Log("Main exiting"); mutex.ReleaseMutex(); }
            }
        }
    }
    internal sealed class KeeperApplication : ApplicationContext {
        private readonly KeeperEngine engine;
        private readonly Preferences preferences;
        private readonly KeeperWindow window;
        private readonly Control dispatch;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem trayStatus, trayToggle;
        private readonly System.Windows.Forms.Timer recoveryTimer, healthTimer;
        private readonly Icon onIcon,offIcon,errorIcon;
        private bool disposed,closing,flydigiWarningShown;
        private readonly StartupManager startup;
        private int recoveryAttempt;
        private string recoveryReason;
        private bool subscribedPower,subscribedDisplay;

        internal KeeperApplication(bool minimized) {
            preferences=Storage.Load();
            startup=new StartupManager(new TaskStartupStore(),Application.ExecutablePath,File.Exists,new RegistryStartupStore());
            engine=new KeeperEngine(mode=>new GpuLease(mode),Storage.Log,preferences.StartupMode());
            dispatch=new Control(); var handle=dispatch.Handle;
            onIcon=MakeIcon(Color.FromArgb(38,155,111));
            offIcon=MakeIcon(Color.FromArgb(127,139,153));
            errorIcon=MakeIcon(Color.FromArgb(207,116,38));
            window=new KeeperWindow(preferences);
            window.ToggleRequested+=Toggle;
            window.RestartRequested+=Restart;
            window.ApplyRequested+=Apply;
            window.CancelRequested+=CancelRecovery;
            window.PreferencesChanged+=PreferencesChanged;
            window.StartupChangeRequested+=StartupChanged;
            window.FormClosing+=delegate(object sender,FormClosingEventArgs e) {
                if(!closing && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; window.Hide(); }
            };
            menu=new ContextMenuStrip();
            trayStatus=new ToolStripMenuItem(); trayStatus.Enabled=false;
            trayToggle=new ToolStripMenuItem(); trayToggle.Click+=delegate { Toggle(); };
            var show=new ToolStripMenuItem("Открыть"); show.Click+=delegate { ShowWindow(); };
            var restart=new ToolStripMenuItem("Перезапустить обход"); restart.Click+=delegate { Restart(); };
            var exit=new ToolStripMenuItem("Выход"); exit.Click+=delegate { ExitThread(); };
            menu.Items.AddRange(new ToolStripItem[]{trayStatus,show,trayToggle,restart,new ToolStripSeparator(),exit});
            Themes.Menu(menu,preferences.DarkTheme);
            tray=new NotifyIcon {ContextMenuStrip=menu,Icon=offIcon,Text="GPU Desktop Keeper 1.0",Visible=true};
            tray.DoubleClick+=delegate { ShowWindow(); };
            recoveryTimer=new System.Windows.Forms.Timer();
            recoveryTimer.Tick+=RecoveryTick;
            healthTimer=new System.Windows.Forms.Timer {Interval=10000};
            healthTimer.Tick+=delegate {
                if(!preferences.WatchDevice || !engine.DesiredEnabled) return;
                if(engine.Active && !engine.CheckHealth() && preferences.AutoRecover) ScheduleRecovery("device lost");
            };
            engine.Changed+=Update;
            try { SystemEvents.PowerModeChanged+=PowerChanged; subscribedPower=true; }
            catch(Exception ex) { Storage.Log("Resume notifications unavailable: "+ex.Message); }
            try { SystemEvents.DisplaySettingsChanged+=DisplayChanged; subscribedDisplay=true; }
            catch(Exception ex) { Storage.Log("Display notifications unavailable: "+ex.Message); }
            healthTimer.Enabled=preferences.WatchDevice;
            RefreshStartup();
            if(preferences.StartEnabled && !Enable(preferences.StartupMode(),"startup") && preferences.AutoRecover) ScheduleRecovery("startup retry");
            Update();
            if(!minimized && !preferences.StartMinimized) window.Show();
            if(window.Visible) Post(ShowFlydigiWarning);
        }
        private void Post(Action action) {
            if(disposed) return;
            try { dispatch.BeginInvoke((MethodInvoker)delegate { if(!disposed) action(); }); }
            catch(InvalidOperationException) { }
        }
        private void PowerChanged(object sender,PowerModeChangedEventArgs e) {
            if(e.Mode==PowerModes.Resume) Post(delegate { ScheduleRecovery("resume"); });
        }
        private void DisplayChanged(object sender,EventArgs e) {
            Post(delegate {
                if(!preferences.AutoRecover || !engine.DesiredEnabled) return;
                if(!engine.Active || !engine.CheckHealth()) ScheduleRecovery("display change / device lost");
            });
        }
        private void ScheduleRecovery(string reason) {
            if(!preferences.AutoRecover || !engine.DesiredEnabled || disposed) return;
            recoveryReason=reason; recoveryAttempt=0;
            recoveryTimer.Stop(); recoveryTimer.Interval=2000; recoveryTimer.Start(); Update();
        }
        private void RecoveryTick(object sender,EventArgs e) {
            recoveryTimer.Stop();
            if(!preferences.AutoRecover || !engine.DesiredEnabled || disposed) { Update(); return; }
            recoveryAttempt++;
            bool recovered=engine.Recover(recoveryReason);
            if(recovered) RememberAppliedMode();
            if(!recovered && recoveryAttempt<3) {
                recoveryTimer.Interval=recoveryAttempt==1 ? 5000 : 15000; recoveryTimer.Start();
            }
            Update();
        }
        private void CancelRecovery() { recoveryTimer.Stop(); Update(); }
        private bool Enable(KeeperMode mode,string reason) {
            bool success=engine.Enable(mode,reason);
            if(success) RememberAppliedMode();
            Update(); return success;
        }
        private void RememberAppliedMode() {
            if(!preferences.RecordAppliedMode(engine.Mode)) return;
            if(SavePreferences()) window.Feedback(preferences.RememberMode ? "Успешно применённый режим сохранён для следующего запуска." : "Режим применён. При следующем запуске будет выбран основной режим.");
        }
        private bool SavePreferences() {
            try { Storage.Save(preferences); return true; }
            catch(Exception ex) { window.Feedback("Не удалось сохранить настройки: "+ex.Message); return false; }
        }
        private void Toggle() {
            CancelRecovery();
            if(engine.DesiredEnabled) engine.Disable("manual"); else Enable(engine.Mode,"manual");
        }
        private void Restart() {
            CancelRecovery();
            if(engine.DesiredEnabled) Enable(engine.Mode,"manual restart");
        }
        private void Apply(KeeperMode? mode) {
            CancelRecovery();
            if(mode.HasValue) Enable(mode.Value,"immediate experiment"); else engine.Disable("immediate control");
            Update();
        }
        private void PreferencesChanged() {
            Themes.Menu(menu,preferences.DarkTheme);
            healthTimer.Enabled=preferences.WatchDevice;
            if(!preferences.AutoRecover) recoveryTimer.Stop();
            if(SavePreferences()) window.Feedback("Настройки программы сохранены.");
            Update();
        }
        private void RefreshStartup() { window.RefreshStartup(startup.Read()); }
        private void StartupChanged(bool enabled) {
            string message;
            try {
                StartupRegistration.Apply(startup,enabled);
                Storage.Log("Startup registration enabled="+enabled);
                message=enabled ? "Задача создана: при входе любого пользователя, выполнение в твоём сеансе." : "Задача автозапуска удалена.";
            } catch(Exception ex) { Storage.Log("Startup change failed: "+ex.Message); message="Не удалось изменить автозапуск: "+ex.Message; }
            RefreshStartup(); window.Feedback(message);
        }
        private void ShowFlydigiWarning() {
            if(disposed || !window.Visible || flydigiWarningShown || preferences.HideFlydigiWarning) return;
            flydigiWarningShown=true;
            using(var notice=new FlydigiNotice(preferences.DarkTheme)) {
                var result=notice.ShowDialog(window);
                if(result==DialogResult.OK && notice.HideNextTime) {
                    preferences.HideFlydigiWarning=true;
                    if(!SavePreferences()) preferences.HideFlydigiWarning=false;
                }
            }
        }
        private void ShowWindow() {
            if(disposed) return;
            RefreshStartup(); window.Show(); window.WindowState=FormWindowState.Normal; window.Activate();
            Post(ShowFlydigiWarning);
        }
        private void Update() {
            if(disposed) return;
            string state=engine.Active ? "Обход включён" : engine.DesiredEnabled ? "Не удалось включить" : "Обход выключен";
            if(recoveryTimer.Enabled) state="Восстановление: попытка "+(recoveryAttempt+1)+" из 3";
            trayStatus.Text=state;
            trayToggle.Text=engine.DesiredEnabled ? "Выключить" : "Включить";
            tray.Text="GPU Desktop Keeper — "+(engine.Active ? "включено" : engine.DesiredEnabled ? "ошибка" : "выключено");
            tray.Icon=engine.Active ? onIcon : engine.DesiredEnabled ? errorIcon : offIcon;
            window.RefreshState(engine,state,recoveryTimer.Enabled);
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        private static Icon MakeIcon(Color color) {
            using(var bitmap=new Bitmap(32,32)) {
                using(var g=Graphics.FromImage(bitmap)) {
                    g.Clear(Color.Transparent); g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using(var brush=new SolidBrush(color)) g.FillEllipse(brush,2,2,28,28);
                    using(var pen=new Pen(Color.White,2)) { g.DrawRectangle(pen,8,9,16,11); g.DrawLine(pen,16,21,16,24); g.DrawLine(pen,11,24,21,24); }
                }
                IntPtr handle=bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); }
            }
        }
        protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
        protected override void Dispose(bool disposing) {
            if(disposing && !disposed) {
                disposed=true; closing=true;
                if(subscribedPower) SystemEvents.PowerModeChanged-=PowerChanged;
                if(subscribedDisplay) SystemEvents.DisplaySettingsChanged-=DisplayChanged;
                recoveryTimer.Dispose(); healthTimer.Dispose();
                engine.Changed-=Update;
                try { engine.Dispose(); }
                finally {
                    tray.Visible=false; tray.Dispose(); menu.Dispose();
                    window.Dispose(); dispatch.Dispose(); onIcon.Dispose(); offIcon.Dispose(); errorIcon.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
