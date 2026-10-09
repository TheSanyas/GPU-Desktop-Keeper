using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GpuDesktopKeeper {
    internal sealed class KeeperWindow : Form {
        internal event Action ToggleRequested,RestartRequested,CancelRequested,PreferencesChanged;
        internal event Action<KeeperMode?> ApplyRequested;
        internal event Action<bool> StartupChangeRequested;
        private readonly Preferences preferences;
        private readonly TableLayoutPanel root;
        private readonly ThemeTabs tabs;
        private readonly Label status,summary,details,feedback;
        private readonly Button toggle,restart,apply,verified,cancel;
        private readonly ThemeButton themeToggle;
        private readonly ToolTip themeTip;
        private readonly FlowLayoutPanel choices;
        private readonly RadioButton[] modes=new RadioButton[4];
        private readonly CheckBox autoRecover,watchDevice,startMinimized,startEnabled,rememberMode,startWithWindows;
        private bool refreshingStartup;
        private bool stateActive,stateDesired,stateRecovering;
        private readonly Icon ownedIcon;
        private readonly Color muted=Color.FromArgb(86,101,119);

        internal KeeperWindow(Preferences preferences) {
            this.preferences=preferences;
            Text="GPU Desktop Keeper 1.0";
            ownedIcon=AppIcons.Load(); Icon=ownedIcon;
            Font=new Font("Segoe UI",10f);
            AutoScaleDimensions=new SizeF(96,96);
            AutoScaleMode=AutoScaleMode.Dpi;
            BackColor=Color.FromArgb(247,249,252);
            ForeColor=Color.FromArgb(29,43,61);
            ClientSize=new Size(740,790);
            MinimumSize=new Size(720,680);
            StartPosition=FormStartPosition.CenterScreen;
            root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(22,18,22,16)};
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);
            var header=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RowCount=2,Margin=new Padding(0)};
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            header.Controls.Add(TextLabel("GPU Desktop Keeper",20,true),0,0);
            themeToggle=(ThemeButton)MakeButton("",delegate {
                preferences.DarkTheme=!preferences.DarkTheme; ApplyTheme(); Raise(PreferencesChanged);
            });
            themeToggle.AutoSize=false; themeToggle.Size=new Size(42,42); themeToggle.Padding=new Padding(0);
            themeTip=new ToolTip {ShowAlways=true};
            themeToggle.Anchor=AnchorStyles.Top|AnchorStyles.Right; themeToggle.Margin=new Padding(12,0,0,8);
            themeToggle.AccessibleName="Переключить светлую или тёмную тему";
            header.Controls.Add(themeToggle,1,0);
            var subtitle=TextLabel("Фикс зависаний приложений на дополнительных мониторах",10,false);
            header.Controls.Add(subtitle,0,1); header.SetColumnSpan(subtitle,2);
            root.Controls.Add(header,0,0);
            status=TextLabel("Запуск…",15,true); status.Margin=new Padding(0,16,0,14);
            root.Controls.Add(status,0,1);
            tabs=new ThemeTabs {Dock=DockStyle.Fill,Margin=new Padding(0)};
            root.Controls.Add(tabs,0,2);
            var main=new ThemePage("Главная") {BackColor=Color.White,Padding=new Padding(16),AutoScroll=true};
            var lab=new ThemePage("Эксперименты") {BackColor=Color.White,Padding=new Padding(16),AutoScroll=true};
            tabs.TabPages.Add(main); tabs.TabPages.Add(lab);
            var home=Stack(); main.Controls.Add(home);
            summary=TextLabel("",11,false); home.Controls.Add(summary);
            var mainButtons=Buttons();
            toggle=MakeButton("Выключить",delegate { Raise(ToggleRequested); });
            restart=MakeButton("Перезапустить фикс",delegate { Raise(RestartRequested); });
            mainButtons.Controls.Add(toggle); mainButtons.Controls.Add(restart);
            home.Controls.Add(mainButtons);
            var settingsTitle=TextLabel("Поведение программы",11,true); settingsTitle.Margin=new Padding(0,14,0,6);
            home.Controls.Add(settingsTitle);
            autoRecover=Check("Восстанавливать фикс после сна и при потере устройства",preferences.AutoRecover);
            watchDevice=Check("Проверять доступность GPU каждые 10 секунд",preferences.WatchDevice);
            startMinimized=Check("При запуске сразу сворачивать окно в трей",preferences.StartMinimized);
            startEnabled=Check("Включать фикс при запуске программы",preferences.StartEnabled);
            rememberMode=Check("Запоминать успешно применённый режим",preferences.RememberMode);
            startWithWindows=Check("Автозапуск при запуске системы",false);
            startWithWindows.Enabled=false;
            home.Controls.Add(autoRecover); home.Controls.Add(watchDevice); home.Controls.Add(startMinimized);
            home.Controls.Add(startEnabled); home.Controls.Add(rememberMode);
            home.Controls.Add(startWithWindows);
            var statusNote=TextLabel("Статус «включён» означает, что программа удерживает свои GPU-ресурсы. Плавность оценивается в игре.",9,false);
            statusNote.Dock=DockStyle.Bottom;
            main.Controls.Add(statusNote);
            autoRecover.CheckedChanged+=SettingsChanged;
            watchDevice.CheckedChanged+=SettingsChanged;
            startMinimized.CheckedChanged+=SettingsChanged;
            startEnabled.CheckedChanged+=SettingsChanged;
            rememberMode.CheckedChanged+=SettingsChanged;
            startWithWindows.CheckedChanged+=delegate {
                if(!refreshingStartup && StartupChangeRequested!=null) StartupChangeRequested(startWithWindows.Checked);
            };

            var experiment=Stack(); lab.Controls.Add(experiment);
            experiment.Controls.Add(TextLabel("Сравнение режимов",12,true));
            experiment.Controls.Add(TextLabel("Режим применяется сразу после нажатия кнопки. Затем верни фокус в игру и наблюдай тот же видеоряд на втором мониторе минимум 30 секунд.",10,false));
            choices=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new Padding(0,8,0,8)};
            foreach(KeeperMode mode in Modes.Order) {
                int i=(int)mode;
                modes[i]=new RadioButton {Text=Modes.Names[i],Checked=i==(int)preferences.StartupMode(),AutoSize=true,Margin=new Padding(0,0,0,6)};
                choices.Controls.Add(modes[i]);
            }
            experiment.Controls.Add(choices);
            var actions=Buttons();
            apply=MakeButton("Применить",delegate { Apply(SelectedMode()); });
            actions.Controls.Add(apply); experiment.Controls.Add(actions);
            var more=Buttons();
            verified=MakeButton("Вернуть основной режим",delegate { modes[(int)Modes.Default].Checked=true; Apply(Modes.Default); });
            cancel=MakeButton("Отменить восстановление",delegate { Raise(CancelRequested); });
            more.Controls.Add(verified); more.Controls.Add(cancel); experiment.Controls.Add(more);
            details=TextLabel("",10,false); details.Margin=new Padding(0,12,0,8); experiment.Controls.Add(details);
            feedback=TextLabel("Все изменения относятся только к работе этой программы.",9,false);
            feedback.Margin=new Padding(0,12,0,0); root.Controls.Add(feedback,0,3);
            ApplyTheme();
        }
        private TableLayoutPanel Stack() {
            var panel=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Margin=new Padding(0)};
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); return panel;
        }
        private Label TextLabel(string text,float size,bool bold) {
            return new Label {Text=text,AutoSize=true,Dock=DockStyle.Fill,Font=new Font("Segoe UI",size,bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor=bold ? ForeColor : muted,Tag=bold ? "title" : "muted",Margin=new Padding(0,0,0,8),MaximumSize=new Size(660,0)};
        }
        private static FlowLayoutPanel Buttons() { return new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,WrapContents=true,Margin=new Padding(0,4,0,6)}; }
        private static Button MakeButton(string text,Action click) {
            var button=new ThemeButton {Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(9,6,9,6),Margin=new Padding(0,0,8,6),UseVisualStyleBackColor=true};
            button.Click+=delegate { click(); }; return button;
        }
        private static CheckBox Check(string text,bool value) { return new CheckBox {Text=text,Checked=value,AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,4,0,6)}; }
        private static void Raise(Action action) { if(action!=null) action(); }
        private KeeperMode SelectedMode() {
            for(int i=0;i<modes.Length;i++) if(modes[i].Checked) return (KeeperMode)i;
            return Modes.Default;
        }
        private void Apply(KeeperMode? mode) { if(ApplyRequested!=null) ApplyRequested(mode); }
        private void SettingsChanged(object sender,EventArgs e) {
            preferences.AutoRecover=autoRecover.Checked; preferences.WatchDevice=watchDevice.Checked; preferences.StartMinimized=startMinimized.Checked;
            preferences.StartEnabled=startEnabled.Checked; preferences.RememberMode=rememberMode.Checked;
            Raise(PreferencesChanged);
        }
        private void ApplyTheme() {
            var palette=Themes.Get(preferences.DarkTheme);
            BackColor=palette.Background; ForeColor=palette.Text;
            Themes.Apply(root,palette,palette.Background);
            themeToggle.ThemeIcon=preferences.DarkTheme ? ThemeButtonIcon.Sun : ThemeButtonIcon.Moon;
            string nextTheme=preferences.DarkTheme ? "Включить светлую тему" : "Включить тёмную тему";
            themeToggle.AccessibleName=nextTheme;
            themeTip.SetToolTip(themeToggle,nextTheme); themeToggle.Invalidate();
            Themes.TitleBar(this,preferences.DarkTheme); RefreshStatusColor();
        }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e); Themes.TitleBar(this,preferences.DarkTheme);
        }
        private void RefreshStatusColor() {
            var palette=Themes.Get(preferences.DarkTheme);
            status.ForeColor=stateRecovering ? palette.Warning : stateActive ? palette.Success : stateDesired ? palette.Error : palette.Muted;
        }
        internal void RefreshState(KeeperEngine engine,string text,bool recovering) {
            status.Text=text;
            stateActive=engine.Active; stateDesired=engine.DesiredEnabled; stateRecovering=recovering; RefreshStatusColor();
            summary.Text=Modes.Names[(int)engine.Mode]+Environment.NewLine+"GPU: "+engine.Adapter;
            if(engine.LastError!=null) summary.Text+=Environment.NewLine+engine.LastError;
            toggle.Text=engine.DesiredEnabled ? "Выключить" : "Включить";
            restart.Enabled=engine.DesiredEnabled;
            apply.Enabled=verified.Enabled=choices.Enabled=!recovering;
            cancel.Enabled=recovering;
            details.Text="Сейчас: "+Modes.Names[(int)engine.Mode]+Environment.NewLine+
                (engine.Active ? "Устройство 11.1 · буферов: "+engine.BufferCount+" · данные буферов: "+Modes.Bytes(engine.Mode)+" байт" : "GPU-ресурсы освобождены")+
                Environment.NewLine+"Инициализаций в этом запуске: "+engine.Generation;
            if(engine.LastError!=null) details.Text+=Environment.NewLine+engine.LastError;
        }
        internal void Feedback(string text) { feedback.Text=text; }
        internal void RefreshStartup(StartupState state) {
            refreshingStartup=true;
            try { startWithWindows.Checked=state.Registered; startWithWindows.Enabled=state.Error==null; }
            finally { refreshingStartup=false; }
            if(state.Error!=null) Feedback("Не удалось прочитать автозапуск: "+state.Error);
            else if(state.OtherCopy) Feedback("Автозапуск указывает на другую копию Keeper. Сними и снова включи галочку, чтобы выбрать эту копию.");
            else if(state.LegacyRegistered) Feedback("Есть прежняя запись Keeper в реестре. Включи галочку, чтобы перенести автозапуск в планировщик.");
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if(disposing) { ownedIcon.Dispose(); themeTip.Dispose(); } }

        private sealed class PreviewLease : ILease {
            private readonly KeeperMode mode;
            internal PreviewLease(KeeperMode mode) { this.mode=mode; }
            public string Adapter { get { return "NVIDIA GeForce RTX 4070 SUPER"; } }
            public int Buffers { get { return Modes.Buffers(mode); } }
            public uint FeatureLevel { get { return 0xb100; } }
            public int RemovedReason() { return 0; }
            public void Dispose() { }
        }
        internal static void CheckInteractions() {
            var preferences=new Preferences();
            using(var engine=new KeeperEngine(mode=>new PreviewLease(mode),delegate { }))
            using(var window=new KeeperWindow(preferences)) {
                var surface=window.Controls[0]; window.Controls.Remove(surface);
                using(surface) {
                    surface.Dock=DockStyle.None; surface.Size=window.ClientSize;
                    surface.Font=window.Font; surface.CreateControl();
                    int switches=0,preferencesChanged=0,cancelled=0,startupChanges=0;
                    window.StartupChangeRequested+=delegate { startupChanges++; };
                    window.ApplyRequested+=delegate(KeeperMode? mode) {
                        if(mode.HasValue) { if(engine.Enable(mode.Value,"immediate UI check")) preferences.RecordAppliedMode(mode.Value); }
                        else engine.Disable("immediate UI check");
                        switches++;
                    };
                    window.PreferencesChanged+=delegate { preferencesChanged++; };
                    window.CancelRequested+=delegate { cancelled++; };
                    engine.Enable(KeeperMode.Verified,"ui check");
                    window.RefreshState(engine,"active",false);
                    window.tabs.SelectedIndex=1; window.tabs.SelectedTab.CreateControl();
                    window.apply.PerformClick();
                    Check(switches==1 && engine.Active && engine.Mode==Modes.Default && engine.BufferCount==0,"Buffer-free default not applied within click");
                    window.modes[0].Checked=true; window.apply.PerformClick();
                    Check(switches==2 && engine.Active && engine.Mode==KeeperMode.Verified && engine.BufferCount==2 && preferences.StartupMode()==KeeperMode.Verified,"Two-buffer experiment changed identity");
                    window.verified.PerformClick(); Check(switches==3 && engine.Active && engine.Mode==Modes.Default && window.modes[(int)Modes.Default].Checked,"Restore default not immediate");
                    window.RefreshState(engine,"recovering",true);
                    Check(!window.apply.Enabled && window.cancel.Enabled,"Recovery state controls");
                    window.apply.PerformClick(); Check(switches==3,"Recovery allowed blocked action");
                    window.cancel.PerformClick(); Check(cancelled==1,"Cancel recovery action");
                    engine.Disable("ui check"); window.RefreshState(engine,"off",false);
                    Check(window.toggle.Text=="Включить" && !window.restart.Enabled && !window.cancel.Enabled,"Off state controls");
                    window.tabs.SelectedIndex=0;
                    window.autoRecover.Checked=false; window.watchDevice.Checked=true; window.startMinimized.Checked=true;
                    window.startEnabled.Checked=false; window.rememberMode.Checked=false;
                    Check(preferencesChanged==5 && !preferences.AutoRecover && preferences.WatchDevice && preferences.StartMinimized && !preferences.StartEnabled && !preferences.RememberMode,"Own preferences binding");
                    window.RefreshStartup(new StartupState {Registered=true});
                    Check(window.startWithWindows.Checked && startupChanges==0,"Reading startup changed registration");
                    window.startWithWindows.Checked=false; window.startWithWindows.Checked=true;
                    Check(startupChanges==2 && preferencesChanged==5,"Startup checkbox mixed with local settings");
                    window.RefreshStartup(new StartupState {Error="simulated denied"});
                    Check(!window.startWithWindows.Enabled && !window.startWithWindows.Checked && startupChanges==2,"Failed startup read misrepresented state");
                    window.RefreshStartup(new StartupState {Registered=true,OtherCopy=true});
                    Check(window.startWithWindows.Checked && window.feedback.Text.Contains("другую копию") && startupChanges==2,"Other copy or event suppression");
                    window.RefreshStartup(new StartupState {LegacyRegistered=true});
                    Check(!window.startWithWindows.Checked && window.feedback.Text.Contains("перенести") && startupChanges==2,"Legacy registration misrepresented as a scheduler task");
                    engine.Enable(Modes.Default,"theme UI check"); window.RefreshState(engine,"active",false);
                    int generation=engine.Generation,priorChanges=preferencesChanged;
                    window.themeToggle.PerformClick();
                    Check(preferences.DarkTheme && preferencesChanged==priorChanges+1 && window.BackColor==Themes.Dark.Background && window.tabs.TabPages[0].BackColor==Themes.Dark.Surface,"Dark theme switch or settings event failed");
                    Check(window.status.ForeColor==Themes.Dark.Success && window.themeToggle.ThemeIcon==ThemeButtonIcon.Sun && window.themeToggle.Text=="","Dark theme lost status or icon action");
                    var restored=Storage.Json.Deserialize<Preferences>(Storage.Json.Serialize(preferences));
                    using(var reopened=new KeeperWindow(restored)) {
                        Check(reopened.BackColor==Themes.Dark.Background && reopened.themeToggle.ThemeIcon==ThemeButtonIcon.Sun,"Saved theme not applied on reopening");
                    }
                    window.themeToggle.PerformClick();
                    Check(!preferences.DarkTheme && preferencesChanged==priorChanges+2 && window.BackColor==Themes.Light.Background && window.status.ForeColor==Themes.Light.Success,"Light theme round trip failed");
                    Check(window.themeToggle.ThemeIcon==ThemeButtonIcon.Moon && window.themeTip.GetToolTip(window.themeToggle)=="Включить тёмную тему" && window.themeToggle.AccessibleName=="Включить тёмную тему","Light theme icon, tooltip or accessibility missing");
                    Check(engine.Generation==generation && switches==3 && startupChanges==2,"Theme changed GPU state or startup registration");
                }
            }
        }
        private static void Check(bool condition,string reason) { if(!condition) throw new InvalidOperationException("UI check failed: "+reason); }
        internal static int ShowPreview() {
            // A real window with simulated state only: no controller, tray, disk preferences,
            // GPU device, registry, task registration or system event subscriptions.
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using(var engine=new KeeperEngine(mode=>new PreviewLease(mode),delegate { }))
            using(var window=new KeeperWindow(new Preferences())) {
                window.Text="GPU Desktop Keeper "+Program.Version+" — проверка интерфейса";
                engine.Enable(Modes.Default,"live preview with fake GPU");
                window.RefreshState(engine,"Фикс включён",false);
                Application.Run(window);
            }
            return 0;
        }
        internal static int RenderPreviews() {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            foreach(bool dark in new[]{false,true}) {
            string suffix=dark ? "-dark" : "";
            using(var engine=new KeeperEngine(mode=>new PreviewLease(mode),delegate { }))
            using(var window=new KeeperWindow(new Preferences {DarkTheme=dark})) {
                engine.Enable(Modes.Default,"preview with fake GPU");
                window.RefreshState(engine,"Фикс включён",false);
                window.RefreshStartup(new StartupState());
                // Render the client panel detached from the invisible Form. No desktop window,
                // tray icon, real GPU device or SystemEvents subscription is created here.
                var surface=window.Controls[0]; window.Controls.Remove(surface);
                using(surface) {
                    surface.Dock=DockStyle.None; surface.Size=window.ClientSize;
                    surface.Font=window.Font; surface.BackColor=window.BackColor; surface.ForeColor=window.ForeColor;
                    surface.CreateControl(); surface.PerformLayout();
                    for(int i=0;i<2;i++) {
                        window.tabs.SelectedIndex=i;
                        window.tabs.SelectedTab.CreateControl(); surface.PerformLayout();
                        using(var image=new Bitmap(surface.Width,surface.Height)) {
                            surface.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                            image.Save(Path.Combine(Storage.Folder,(i==0 ? "preview-main" : "preview-experiments")+suffix+".png"));
                        }
                    }
                    engine.Disable("preview off"); window.tabs.SelectedIndex=0;
                    window.RefreshState(engine,"Фикс выключен",false);
                    using(var image=new Bitmap(surface.Width,surface.Height)) {
                        surface.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size)); image.Save(Path.Combine(Storage.Folder,"preview-off"+suffix+".png"));
                    }
                    engine.Enable(Modes.Default,"preview with fake GPU");
                    window.RefreshState(engine,"Фикс включён",false);
                    window.tabs.SelectedIndex=1;
                    window.RefreshState(engine,"Восстановление: попытка 1 из 3",true);
                    using(var image=new Bitmap(surface.Width,surface.Height)) {
                        surface.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size)); image.Save(Path.Combine(Storage.Folder,"preview-recovery"+suffix+".png"));
                    }
                }
            }
            }
            FlydigiNotice.RenderPreview();
            return 0;
        }
    }
}
