using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GpuDesktopKeeper {
    internal sealed class ThemePalette {
        internal readonly Color Background,Surface,Text,Muted,Border,Button,Hover,Accent,Success,Warning,Error;
        internal readonly bool Dark;
        internal ThemePalette(bool dark) {
            Dark=dark;
            Background=dark ? Color.FromArgb(23,27,34) : Color.FromArgb(247,249,252);
            Surface=dark ? Color.FromArgb(32,38,47) : Color.White;
            Text=dark ? Color.FromArgb(234,239,247) : Color.FromArgb(29,43,61);
            Muted=dark ? Color.FromArgb(176,189,207) : Color.FromArgb(86,101,119);
            Border=dark ? Color.FromArgb(70,81,98) : Color.FromArgb(205,211,219);
            Button=dark ? Color.FromArgb(44,53,66) : Color.FromArgb(253,253,254);
            Hover=dark ? Color.FromArgb(57,70,88) : Color.FromArgb(232,240,250);
            Accent=dark ? Color.FromArgb(126,181,255) : Color.FromArgb(39,105,187);
            Success=dark ? Color.FromArgb(94,215,167) : Color.FromArgb(25,131,91);
            Warning=dark ? Color.FromArgb(244,197,111) : Color.FromArgb(174,107,22);
            Error=dark ? Color.FromArgb(255,158,119) : Color.FromArgb(184,84,26);
        }
    }
    internal static class Themes {
        internal static readonly ThemePalette Light=new ThemePalette(false),Dark=new ThemePalette(true);
        internal static ThemePalette Get(bool dark) { return dark ? Dark : Light; }
        internal static void Apply(Control control,ThemePalette palette,Color background) {
            var page=control as ThemePage;
            if(page!=null) background=palette.Surface;
            control.BackColor=background;
            control.ForeColor=control.Tag as string=="muted" ? palette.Muted : palette.Text;
            var button=control as Button;
            if(button!=null) {
                button.UseVisualStyleBackColor=false; button.FlatStyle=FlatStyle.Flat;
                button.BackColor=palette.Button; button.FlatAppearance.BorderColor=palette.Border;
                button.FlatAppearance.MouseOverBackColor=palette.Hover;
                button.FlatAppearance.MouseDownBackColor=palette.Hover;
                var themed=button as ThemeButton; if(themed!=null) themed.Palette=palette;
            }
            var tabs=control as ThemeTabs;
            if(tabs!=null) tabs.SetPalette(palette);
            var themePage=control as ThemePage;
            if(themePage!=null) themePage.Palette=palette;
            foreach(Control child in control.Controls) Apply(child,palette,background);
            control.Invalidate();
        }
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        internal static void TitleBar(Form form,bool dark) {
            if(!form.IsHandleCreated) return;
            int value=dark ? 1 : 0;
            try { if(DwmSetWindowAttribute(form.Handle,20,ref value,4)!=0) DwmSetWindowAttribute(form.Handle,19,ref value,4); }
            catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }
        }
        internal static void Menu(ContextMenuStrip menu,bool dark) {
            var palette=Get(dark);
            menu.Renderer=new ThemeMenuRenderer(palette);
            menu.BackColor=palette.Surface; menu.ForeColor=palette.Text;
            foreach(ToolStripItem item in menu.Items) item.ForeColor=palette.Text;
        }
        private sealed class ThemeMenuColors : ProfessionalColorTable {
            private readonly ThemePalette palette;
            internal ThemeMenuColors(ThemePalette palette) { this.palette=palette; UseSystemColors=false; }
            public override Color ToolStripDropDownBackground { get { return palette.Surface; } }
            public override Color MenuBorder { get { return palette.Border; } }
            public override Color MenuItemBorder { get { return palette.Border; } }
            public override Color MenuItemSelected { get { return palette.Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return palette.Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return palette.Hover; } }
            public override Color ImageMarginGradientBegin { get { return palette.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return palette.Surface; } }
            public override Color ImageMarginGradientEnd { get { return palette.Surface; } }
            public override Color SeparatorDark { get { return palette.Border; } }
            public override Color SeparatorLight { get { return palette.Border; } }
        }
        private sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer {
            private readonly ThemePalette palette;
            internal ThemeMenuRenderer(ThemePalette palette) : base(new ThemeMenuColors(palette)) { this.palette=palette; }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
                e.TextColor=e.Item.Enabled ? palette.Text : palette.Muted; base.OnRenderItemText(e);
            }
        }
    }
    internal enum ThemeButtonIcon { None,Moon,Sun }
    internal sealed class ThemeButton : Button {
        internal ThemePalette Palette=Themes.Light;
        internal ThemeButtonIcon ThemeIcon;
        internal bool TabHeader,TabSelected;
        private bool hovered,pressed;
        internal ThemeButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true); }
        public override Size GetPreferredSize(Size proposedSize) {
            if(!TabHeader) return base.GetPreferredSize(proposedSize);
            var text=TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);
            return new Size(text.Width+Padding.Horizontal+8,text.Height+Padding.Vertical+4);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if(e.Button==MouseButtons.Left) pressed=true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if(e.KeyCode==Keys.Space) pressed=true; Invalidate(); base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed=false; Invalidate(); base.OnKeyUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { pressed=false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e) {
            float scale=e.Graphics.DpiX/96f;
            e.Graphics.Clear(Parent==null ? Palette.Background : Parent.BackColor);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=Rounded(new RectangleF(.5f,.5f,Width-1f,Height-1f),5f*scale)) {
                Color fill=TabHeader ? (TabSelected ? Palette.Surface : Palette.Background) : Palette.Button;
                using(var brush=new SolidBrush(Enabled && (hovered || pressed) ? Palette.Hover : fill)) e.Graphics.FillPath(brush,shape);
                using(var pen=new Pen(Palette.Border)) e.Graphics.DrawPath(pen,shape);
            }
            var bounds=Rectangle.Inflate(ClientRectangle,-4,-4);
            if(pressed && Enabled) bounds.Offset(0,1);
            if(ThemeIcon==ThemeButtonIcon.None)
                TextRenderer.DrawText(e.Graphics,Text,Font,bounds,Enabled ? Palette.Text : Palette.Muted,
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            else DrawThemeIcon(e.Graphics,bounds,scale);
            if(TabHeader && TabSelected) using(var pen=new Pen(Palette.Accent,2f*scale))
                e.Graphics.DrawLine(pen,6f*scale,Height-3f*scale,Width-6f*scale,Height-3f*scale);
            if(Focused && ShowFocusCues) {
                using(var shape=Rounded(RectangleF.Inflate(ClientRectangle,-4f,-4f),3f*scale))
                using(var pen=new Pen(Palette.Accent) {DashStyle=DashStyle.Dot}) e.Graphics.DrawPath(pen,shape);
            }
        }
        internal static GraphicsPath Rounded(RectangleF bounds,float radius) {
            float diameter=Math.Min(radius*2f,Math.Min(bounds.Width,bounds.Height));
            var path=new GraphicsPath();
            path.AddArc(bounds.Left,bounds.Top,diameter,diameter,180,90);
            path.AddArc(bounds.Right-diameter,bounds.Top,diameter,diameter,270,90);
            path.AddArc(bounds.Right-diameter,bounds.Bottom-diameter,diameter,diameter,0,90);
            path.AddArc(bounds.Left,bounds.Bottom-diameter,diameter,diameter,90,90);
            path.CloseFigure(); return path;
        }
        private void DrawThemeIcon(Graphics graphics,Rectangle bounds,float scale) {
            float cx=bounds.Left+bounds.Width/2f,cy=bounds.Top+bounds.Height/2f;
            using(var pen=new Pen(Enabled ? Palette.Text : Palette.Muted,1.7f*scale) {StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round}) {
                if(ThemeIcon==ThemeButtonIcon.Sun) {
                    float r=4.5f*scale;
                    graphics.DrawEllipse(pen,cx-r,cy-r,r*2,r*2);
                    for(int i=0;i<8;i++) {
                        double angle=i*Math.PI/4;
                        graphics.DrawLine(pen,cx+(float)Math.Cos(angle)*7.7f*scale,cy+(float)Math.Sin(angle)*7.7f*scale,
                            cx+(float)Math.Cos(angle)*10.8f*scale,cy+(float)Math.Sin(angle)*10.8f*scale);
                    }
                } else {
                    float r=9f*scale,inner=.8660254f*r;
                    using(var moon=new GraphicsPath()) {
                        moon.AddArc(cx-r,cy-r,r*2,r*2,-60,-240);
                        moon.AddArc(cx+r*.5f-inner,cy-inner,inner*2,inner*2,90,180);
                        moon.CloseFigure(); graphics.DrawPath(pen,moon);
                    }
                }
            }
        }
    }
    internal sealed class ThemePage : Panel {
        internal ThemePalette Palette=Themes.Light;
        internal ThemePage(string text) {
            Text=text; AccessibleName=text; AccessibleRole=AccessibleRole.Pane;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        }
        protected override void OnPaintBackground(PaintEventArgs e) {
            if(Width<=0 || Height<=0) return;
            // The page owns both its fill and its border, including every corner.
            e.Graphics.SmoothingMode=SmoothingMode.None;
            using(var brush=new SolidBrush(Palette.Background)) e.Graphics.FillRectangle(brush,ClientRectangle);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=ThemeButton.Rounded(new RectangleF(.5f,.5f,Width-1f,Height-1f),9f*e.Graphics.DpiX/96f)) {
                using(var brush=new SolidBrush(Palette.Surface)) e.Graphics.FillPath(brush,shape);
                using(var pen=new Pen(Palette.Border)) e.Graphics.DrawPath(pen,shape);
            }
        }
    }
    internal sealed class ThemeTabs : UserControl {
        private sealed class BufferedPanel : Panel {
            internal BufferedPanel() { DoubleBuffered=true; ResizeRedraw=true; }
        }
        private sealed class BufferedHeaders : FlowLayoutPanel {
            internal BufferedHeaders() { DoubleBuffered=true; ResizeRedraw=true; }
        }
        internal sealed class PageCollection : Collection<ThemePage> {
            private readonly ThemeTabs owner;
            internal PageCollection(ThemeTabs owner) { this.owner=owner; }
            protected override void InsertItem(int index,ThemePage page) {
                if(index!=Count) throw new NotSupportedException("Pages are added at the end.");
                base.InsertItem(index,page); owner.AddPage(page);
            }
            protected override void RemoveItem(int index) { throw new NotSupportedException("Pages are fixed for this window."); }
            protected override void SetItem(int index,ThemePage page) { throw new NotSupportedException("Pages are fixed for this window."); }
            protected override void ClearItems() { throw new NotSupportedException("Pages are fixed for this window."); }
        }
        internal readonly PageCollection TabPages;
        private readonly BufferedHeaders headers;
        private readonly BufferedPanel body;
        private readonly List<ThemeButton> buttons=new List<ThemeButton>();
        private ThemePalette palette=Themes.Light;
        private int selectedIndex=-1;
        internal int SelectedIndex {
            get { return selectedIndex; }
            set {
                if(value<0 || value>=TabPages.Count) throw new ArgumentOutOfRangeException("value");
                if(value==selectedIndex) return;
                selectedIndex=value;
                // Show the new surface before hiding the old one so the host never flashes empty.
                TabPages[value].BringToFront(); TabPages[value].Visible=true;
                for(int i=0;i<TabPages.Count;i++) {
                    if(i!=value) TabPages[i].Visible=false;
                    buttons[i].TabSelected=i==value; buttons[i].TabStop=i==value; buttons[i].Invalidate();
                }
            }
        }
        internal ThemePage SelectedTab { get { return selectedIndex<0 ? null : TabPages[selectedIndex]; } }
        internal ThemeTabs() {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            AccessibleRole=AccessibleRole.PageTabList; TabStop=false;
            TabPages=new PageCollection(this);
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(0),Padding=new Padding(0)};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            headers=new BufferedHeaders {Dock=DockStyle.Fill,AutoSize=true,WrapContents=false,Margin=new Padding(0),Padding=new Padding(0)};
            body=new BufferedPanel {Dock=DockStyle.Fill,Margin=new Padding(0),Padding=new Padding(0)};
            layout.Controls.Add(headers,0,0); layout.Controls.Add(body,0,1); Controls.Add(layout);
        }
        private void AddPage(ThemePage page) {
            int index=buttons.Count;
            var button=new ThemeButton {Text=page.Text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
                Padding=new Padding(14,5,14,5),Margin=new Padding(0,0,3,3),FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false,
                TabHeader=true,Palette=palette,AccessibleName=page.Text,AccessibleRole=AccessibleRole.PageTab};
            button.Click+=delegate { SelectedIndex=index; };
            page.TextChanged+=delegate { button.Text=page.Text; button.AccessibleName=page.Text; };
            buttons.Add(button); headers.Controls.Add(button);
            page.Dock=DockStyle.Fill; page.Visible=false; body.Controls.Add(page);
            if(selectedIndex<0) SelectedIndex=0;
        }
        internal void SetPalette(ThemePalette value) {
            palette=value; BackColor=value.Background;
            foreach(var button in buttons) { button.Palette=value; button.Invalidate(); }
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
            bool cycle=keyData==(Keys.Control|Keys.Tab) || keyData==(Keys.Control|Keys.Shift|Keys.Tab);
            bool focusedHeader=false;
            foreach(var button in buttons) if(button.Focused) focusedHeader=true;
            if(TabPages.Count>0 && (cycle || focusedHeader)) {
                int next=selectedIndex;
                if(keyData==(Keys.Control|Keys.Tab) || (focusedHeader && keyData==Keys.Right)) next=(next+1)%TabPages.Count;
                else if(keyData==(Keys.Control|Keys.Shift|Keys.Tab) || (focusedHeader && keyData==Keys.Left)) next=(next+TabPages.Count-1)%TabPages.Count;
                else if(focusedHeader && keyData==Keys.Home) next=0;
                else if(focusedHeader && keyData==Keys.End) next=TabPages.Count-1;
                else return base.ProcessCmdKey(ref msg,keyData);
                SelectedIndex=next; buttons[next].Focus(); return true;
            }
            return base.ProcessCmdKey(ref msg,keyData);
        }
    }
}
