using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GpuDesktopKeeper {
    internal sealed class FlydigiNotice : Form {
        private readonly CheckBox hide;
        private readonly Icon ownedIcon;
        private readonly bool darkTheme;
        internal bool HideNextTime { get { return hide.Checked; } }
        internal FlydigiNotice(bool darkTheme=false) {
            this.darkTheme=darkTheme;
            Text="GPU Desktop Keeper — Flydigi";
            ownedIcon=AppIcons.Load(); Icon=ownedIcon;
            Font=new Font("Segoe UI",10f); BackColor=Color.White;
            AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(510,230); FormBorderStyle=FormBorderStyle.FixedDialog;
            MaximizeBox=false; MinimizeBox=false; StartPosition=FormStartPosition.CenterScreen;
            var panel=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=4};
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(new Label {Text="Flydigi может мешать работе обхода",AutoSize=true,Dock=DockStyle.Fill,Font=new Font(Font,FontStyle.Bold),Margin=new Padding(0,0,0,12)},0,0);
            panel.Controls.Add(new Label {Text="Flydigi Space Station может негативно влиять на этот фикс. Если лаги возвращаются, полностью закрой Flydigi через его значок в трее.",AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,12)},0,1);
            hide=new CheckBox {Text="Скрывать при последующих запусках",AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(4,0,0,12)};
            panel.Controls.Add(hide,0,2);
            var ok=new ThemeButton {Text="Понятно",AutoSize=true,Padding=new Padding(12,5,12,5),Anchor=AnchorStyles.Right,DialogResult=DialogResult.OK};
            panel.Controls.Add(ok,0,3); AcceptButton=ok; Controls.Add(panel);
            var palette=Themes.Get(darkTheme); Themes.Apply(this,palette,palette.Surface);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Themes.TitleBar(this,darkTheme); }
        protected override void Dispose(bool disposing) {
            base.Dispose(disposing); if(disposing) ownedIcon.Dispose();
        }
        internal static void CheckPreference() {
            var preferences=new Preferences();
            if(preferences.HideFlydigiWarning) throw new InvalidOperationException("New warning unexpectedly suppressed");
            using(var dialog=new FlydigiNotice()) {
                dialog.hide.Checked=true;
                preferences.HideFlydigiWarning=dialog.HideNextTime;
                var restored=Storage.Json.Deserialize<Preferences>(Storage.Json.Serialize(preferences));
                if(!restored.HideFlydigiWarning) throw new InvalidOperationException("Warning suppression not persisted");
            }
        }
        internal static void RenderPreview() {
            foreach(bool dark in new[]{false,true}) {
            using(var dialog=new FlydigiNotice(dark)) {
                var surface=dialog.Controls[0]; dialog.Controls.Remove(surface);
                using(surface) {
                    surface.Size=dialog.ClientSize; surface.Font=dialog.Font; surface.BackColor=dialog.BackColor;
                    surface.CreateControl(); surface.PerformLayout();
                    using(var bitmap=new Bitmap(surface.Width,surface.Height)) {
                        surface.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                        bitmap.Save(Path.Combine(Storage.Folder,"preview-flydigi-notice"+(dark ? "-dark" : "")+".png"));
                    }
                }
            }
            }
        }
    }
}
