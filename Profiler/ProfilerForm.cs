// ********************************************************************************************************************************************
//      CSpect Copper Disassembler extension, shows the copper in realtime
//      Written by:
//                  Mike Dailly
//
//      contributions by: Ben Baker (FlameGraph)
//                  
//      Released under the GNU 3 license - please see license file for more details
//
//      This extension uses the KEY extension method to start. then thje "tick()" to update
//
// ********************************************************************************************************************************************
using Plugin;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Profiler
{
    public partial class ProfilerForm : Form
    {
        iCSpect CSpect;
        internal ProfilerPlugin plugin;

        // Toolbar
        Button btnStartStop;
        TabControl tabControl;

        // Status bar
        StatusStrip statusStrip;
        ToolStripStatusLabel sampleCountLabel;
        long lastDisplayedSampleCount = -1;

        // Original profiler tab
        DoubleBufferedPanel profilePanel;
        int[] profileData;
        long profileMaxVal = 1;  // global max (computed once on stop)
        int profileZoomStart = 0;
        int profileZoomEnd = 0x10000;

        // FlameGraph tab
        Panel flamePanel;
        FlameNode rootNode;
        FlameNode zoomNode;
        long totalSamples;
        float xScale = 1.0f;
        List<FlameFrame> drawnFrames = new List<FlameFrame>();
        FlameFrame hoveredFrame;
        ToolTip toolTip;

        const int FRAME_HEIGHT = 16;
        const int TOOLBAR_HEIGHT = 34;
        const int STATUS_HEIGHT = 22;

        public static readonly string DEFAULT_EXCLUDE = "WAIT, HALT, IDLE, VSYNC, VBLANK, VLINE";

        internal ProfilerForm(iCSpect _CSpect, ProfilerPlugin _plugin)
        {
            CSpect = _CSpect;
            plugin = _plugin;
            InitializeComponent();
            this.Text = "CSpect Profiler";
            this.ClientSize = new Size(1200, 650);
            this.DoubleBuffered = true;

            toolTip = new ToolTip();
            toolTip.InitialDelay = 100;
            toolTip.ReshowDelay = 50;

            // Row 1: Start/Stop + Status
            btnStartStop = new Button();
            btnStartStop.Text = "Start";
            btnStartStop.Location = new Point(4, 4);
            btnStartStop.Size = new Size(75, 24);
            btnStartStop.Click += BtnStartStop_Click;
            this.Controls.Add(btnStartStop);



            // Tab control with two tabs (sized to leave room for status strip)
            tabControl = new TabControl();
            tabControl.Location = new Point(0, TOOLBAR_HEIGHT);
            tabControl.Size = new Size(1200, 650 - TOOLBAR_HEIGHT - STATUS_HEIGHT);
            tabControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // Tab 1: Profile Overview (original)
            var tabProfile = new TabPage("Profile Overview");
            profilePanel = new DoubleBufferedPanel();
            profilePanel.Dock = DockStyle.Fill;
            profilePanel.BackColor = Color.Black;
            profilePanel.Cursor = Cursors.Cross;
            profilePanel.Paint += ProfilePanel_Paint;
            profilePanel.MouseMove += ProfilePanel_MouseMove;
            profilePanel.MouseDown += ProfilePanel_MouseDown;
            profilePanel.MouseUp += ProfilePanel_MouseUp;
            profilePanel.MouseWheel += ProfilePanel_MouseWheel;
            tabProfile.Controls.Add(profilePanel);
            tabControl.TabPages.Add(tabProfile);

            // Tab 2: FlameGraph
            var tabFlame = new TabPage("FlameGraph");
            flamePanel = new Panel();
            flamePanel.Dock = DockStyle.Fill;
            flamePanel.BackColor = Color.FromArgb(0xEE, 0xEE, 0xEE);
            flamePanel.Paint += FlamePanel_Paint;
            flamePanel.MouseClick += FlamePanel_MouseClick;
            flamePanel.MouseMove += FlamePanel_MouseMove;
            tabFlame.Controls.Add(flamePanel);
            tabControl.TabPages.Add(tabFlame);

            tabControl.SelectedIndexChanged += (s, ev) => {
                toolTip.SetToolTip(profilePanel, "");
            };

            this.Controls.Add(tabControl);

            // Status strip with sizing grip and live sample count label
            statusStrip = new StatusStrip();
            statusStrip.SizingGrip = true;
            sampleCountLabel = new ToolStripStatusLabel();
            sampleCountLabel.Text = "Samples: 0";
            sampleCountLabel.Spring = true;
            sampleCountLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusStrip.Items.Add(sampleCountLabel);
            this.Controls.Add(statusStrip);
        }

        // Called from the OS thread (via ProfilerPlugin.OSTick) to update the
        // status strip with the current sample count while sampling is active.
        // Updates only when the value actually changes to avoid wasted work.
        public void UpdateSampleCount(long count)
        {
            if (sampleCountLabel == null) return;
            if (count == lastDisplayedSampleCount) return;
            lastDisplayedSampleCount = count;
            sampleCountLabel.Text = "Samples: " + count.ToString("N0");
        }

        void BtnStartStop_Click(object sender, EventArgs e)
        {
            if (plugin.IsSampling)
            {
                plugin.StopSampling();
                btnStartStop.Text = "Start";

                // Update profile overview
                try
                {
                    int[] liveData = (int[])CSpect.GetGlobal(eGlobal.profile_exe);
                    if (liveData != null)
                    {
                        profileData = new int[liveData.Length];
                        Array.Copy(liveData, profileData, liveData.Length);
                    }
                }
                catch { profileData = null; }
                profileZoomStart = 0;
                profileZoomEnd = profileData != null ? Math.Min(0x10000, profileData.Length) : 0x10000;
                profileMaxVal = 1;
                if (profileData != null)
                {
                    for (int i = 0; i < profileData.Length && i < 0x10000; i++)
                        if (profileData[i] > profileMaxVal) profileMaxVal = profileData[i];
                }
                profilePanel.Invalidate();

                // Update FlameGraph
                var resolved = plugin.GetResolvedStacks();
                if (resolved != null)
                    LoadFlameData(resolved, plugin.TotalSamples);
            }
            else
            {
                plugin.StartSampling();
                btnStartStop.Text = "Stop";
            }
        }

        // ==================== Profile Overview Tab ====================

        // Drag state
        bool profileDragging = false;
        int profileDragStartX;
        int profileDragStartAddr;

        int AddrFromX(int x)
        {
            int w = profilePanel.Width;
            int range = profileZoomEnd - profileZoomStart;
            return profileZoomStart + (int)((float)x / w * range);
        }

        void ProfilePanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = profilePanel.Width;
            int h = profilePanel.Height;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            g.Clear(Color.Black);

            if (profileData == null)
            {
                g.DrawString("No profile data. Click Start to begin.",
                    SystemFonts.DefaultFont, Brushes.White, 10, 10);
                return;
            }

            int range = profileZoomEnd - profileZoomStart;
            if (range <= 0) return;

            float xStep = (float)w / range;
            float barW = Math.Max(1.0f, xStep);
            bool wideMode = barW >= 4.0f;
            Font labelFont = wideMode ? new Font("Courier New", 9, FontStyle.Bold) : null;

            // Bars
            for (int i = profileZoomStart; i < profileZoomEnd && i < profileData.Length; i++)
            {
                if (profileData[i] == 0) continue;
                float x = (i - profileZoomStart) * xStep;
                float frac = (float)profileData[i] / profileMaxVal;
                float barH = frac * (h - 20);
                int cr = Math.Max(0, Math.Min(255, 200 + (int)(55 * frac)));
                int cg = Math.Max(0, Math.Min(255, (int)(200 * (1.0f - frac))));

                if (!wideMode)
                {
                    using (var pen = new Pen(Color.FromArgb(cr, cg, 0)))
                        g.DrawLine(pen, x, h - barH, x, h);
                }
                else
                {
                    using (var brush = new SolidBrush(Color.FromArgb(cr, cg, 0)))
                        g.FillRectangle(brush, x, h - barH, barW, barH);

                    if (barH > 50 && labelFont != null)
                    {
                        string sym = plugin.LookupSymbol(i);
                        string label = string.Format("${0:X4} {1}", i, sym);
                        var state = g.Save();
                        g.TranslateTransform(x + barW / 2, h - 4);
                        g.RotateTransform(-90);
                        g.DrawString(label, labelFont, Brushes.Black, 2, -labelFont.GetHeight(g) / 2);
                        g.Restore(state);
                    }
                }
            }

            // Page/bank boundary lines
            using (var pagePen = new Pen(Color.FromArgb(60, 60, 60)) { DashStyle = DashStyle.Dot })
            using (var bankPen = new Pen(Color.FromArgb(100, 100, 100)) { DashStyle = DashStyle.Dash })
            using (var bFont = new Font("Consolas", 7))
            {
                for (int addr = ((profileZoomStart / 0x2000) + 1) * 0x2000;
                     addr < profileZoomEnd; addr += 0x2000)
                {
                    float bx = (addr - profileZoomStart) * xStep;
                    bool isBank = (addr % 0x4000) == 0;
                    g.DrawLine(isBank ? bankPen : pagePen, bx, 0, bx, h);
                    if (range < 0x8000)
                        g.DrawString(string.Format("${0:X4}", addr), bFont, Brushes.DarkGray, bx + 2, 2);
                }
            }

            // Edge labels
            using (var font = new Font("Consolas", 9))
            {
                g.DrawString(string.Format("${0:X4}", profileZoomStart), font, Brushes.Gray, 2, h - 14);
                string endStr = string.Format("${0:X4}", profileZoomEnd);
                var sz = g.MeasureString(endStr, font);
                g.DrawString(endStr, font, Brushes.Gray, w - sz.Width - 2, h - 14);
            }

            if (labelFont != null) labelFont.Dispose();
        }

        void ProfilePanel_MouseDown(object sender, MouseEventArgs e)
        {
            profileDragging = true;
            profileDragStartX = e.X;
            profileDragStartAddr = profileZoomStart;
            profilePanel.Cursor = Cursors.SizeAll;
            profilePanel.Focus();
        }

        void ProfilePanel_MouseUp(object sender, MouseEventArgs e)
        {
            profileDragging = false;
            profilePanel.Cursor = Cursors.Cross;
        }

        void ProfilePanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (profileData == null) return;

            if (profileDragging)
            {
                int range = profileZoomEnd - profileZoomStart;
                int maxAddr = Math.Min(0x10000, profileData.Length);
                int addrDelta = (int)((float)(profileDragStartX - e.X) / profilePanel.Width * range);
                int newStart = Math.Max(0, Math.Min(maxAddr - range, profileDragStartAddr + addrDelta));
                if (newStart != profileZoomStart)
                {
                    profileZoomStart = newStart;
                    profileZoomEnd = newStart + range;
                    profilePanel.Invalidate();
                }
                return;
            }

            int addr = AddrFromX(e.X);
            if (addr >= 0 && addr < profileData.Length && profileData[addr] > 0)
            {
                string sym = plugin.LookupSymbol(addr);
                int count = profileData[addr];
                toolTip.SetToolTip(profilePanel,
                    string.Format("${0:X4}  {1}\n{2} samples", addr, sym, count));
            }
            else
            {
                toolTip.SetToolTip(profilePanel, "");
            }
        }

        void ProfilePanel_MouseWheel(object sender, MouseEventArgs e)
        {
            if (profileData == null) return;

            int maxAddr = Math.Min(0x10000, profileData.Length);
            int range = profileZoomEnd - profileZoomStart;

            // Address under cursor
            int anchorAddr = AddrFromX(e.X);
            // Fraction of panel width where cursor is
            float cursorFrac = (float)e.X / profilePanel.Width;

            int newRange;
            if (e.Delta > 0)
                newRange = Math.Max(32, range * 2 / 3);
            else
                newRange = Math.Min(maxAddr, range * 3 / 2);

            // Keep anchorAddr at the same screen fraction
            int newStart = anchorAddr - (int)(newRange * cursorFrac);
            newStart = Math.Max(0, newStart);
            if (newStart + newRange > maxAddr)
                newStart = maxAddr - newRange;
            if (newStart < 0) newStart = 0;

            profileZoomStart = newStart;
            profileZoomEnd = Math.Min(maxAddr, newStart + newRange);

            profilePanel.Invalidate();
        }

        // ==================== FlameGraph Tab ====================

        public void LoadFlameData(Dictionary<string, long> stacks, long total)
        {
            totalSamples = total;
            rootNode = new FlameNode("all");
            rootNode.Self = 0;

            foreach (var kv in stacks)
            {
                string[] frames = kv.Key.Split(';');
                FlameNode node = rootNode;
                foreach (string frame in frames)
                {
                    if (!node.Children.ContainsKey(frame))
                        node.Children[frame] = new FlameNode(frame);
                    node = node.Children[frame];
                }
                node.Self += kv.Value;
            }

            ComputeTotals(rootNode);
            rootNode.Total = total;
            zoomNode = null;
            flamePanel.Invalidate();
        }

        void ComputeTotals(FlameNode node)
        {
            long childTotal = 0;
            foreach (var child in node.Children.Values)
            {
                ComputeTotals(child);
                childTotal += child.Total;
            }
            node.Total = node.Self + childTotal;
        }

        void FlamePanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighSpeed;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            drawnFrames.Clear();
            int w = flamePanel.Width;
            int h = flamePanel.Height;

            FlameNode viewRoot = zoomNode ?? rootNode;
            if (viewRoot == null || viewRoot.Total == 0)
            {
                g.DrawString("No FlameGraph data. Click Start to begin sampling.",
                    SystemFonts.DefaultFont, Brushes.Black, 10, 10);
                return;
            }

            xScale = (float)w / viewRoot.Total;

            using (var titleFont = new Font("Segoe UI", 10, FontStyle.Bold))
            {
                string title = zoomNode != null
                    ? string.Format("Zoomed: {0} ({1} samples, {2:F1}%)",
                        zoomNode.Name, zoomNode.Total, zoomNode.Total * 100.0 / totalSamples)
                    : string.Format("FlameGraph ({0} samples) - Click to zoom, Right-click to reset",
                        totalSamples);
                g.DrawString(title, titleFont, Brushes.Black, 4, 2);
            }

            int y0 = h - 20 - FRAME_HEIGHT;
            DrawFrame(g, viewRoot, 0, y0, w, 0);
            DrawChildren(g, viewRoot, 0, y0 - FRAME_HEIGHT, w, 1);
        }

        void DrawChildren(Graphics g, FlameNode parent, float x, int y, float parentWidth, int depth)
        {
            if (y < 24) return;
            if (parentWidth < 2) return;

            // Reserve the parent's self-time on the LEFT of the children
            // band. This matches the SVG, which sorts entire chain strings:
            // a chain that ends at this parent (no further children) sorts
            // before any chain that descends into a named child, so the
            // exposed parent bar always sits on the left and named children
            // on the right. Without this offset, children would draw flush-
            // left and the exposed self-time bar would appear on the right —
            // a mirror image of flamegraph.svg.
            float cx = x + (float)parent.Self * xScale;
            // Sort alphabetically (Brendan Gregg's flame graph convention) so
            // sibling layout is stable and matches the SVG output. Sorting by
            // sample count instead would put hot frames on the left, which
            // moves around between runs and mismatches flamegraph.svg.
            var sorted = parent.Children.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
            foreach (var child in sorted)
            {
                float childWidth = (float)child.Total * xScale;
                if (childWidth < 0.5f) continue;
                DrawFrame(g, child, cx, y, childWidth, depth);
                DrawChildren(g, child, cx, y - FRAME_HEIGHT, childWidth, depth + 1);
                cx += childWidth;
            }
        }

        void DrawFrame(Graphics g, FlameNode node, float x, int y, float width, int depth)
        {
            if (width < 0.5f) return;
            var rect = new RectangleF(x, y, width, FRAME_HEIGHT - 1);
            var color = HotColor(node.Name);

            using (var brush = new SolidBrush(color))
                g.FillRectangle(brush, rect);

            if (width > 2)
                using (var pen = new Pen(Color.FromArgb(80, 0, 0, 0), 0.5f))
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

            if (width > 30)
                using (var font = new Font("Consolas", 9))
                    g.DrawString(node.Name, font, Brushes.Black,
                        new RectangleF(x + 2, y + 1, width - 4, FRAME_HEIGHT - 2),
                        new StringFormat { Trimming = StringTrimming.EllipsisCharacter });

            drawnFrames.Add(new FlameFrame { Rect = rect, Node = node });
        }

        Color HotColor(string name)
        {
            int hash = 0;
            foreach (char c in name) hash = hash * 31 + c;
            hash = Math.Abs(hash);
            return Color.FromArgb(200 + hash % 55, 50 + hash / 3 % 150, hash / 7 % 55);
        }

        void FlamePanel_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                zoomNode = null;
                flamePanel.Invalidate();
                return;
            }
            for (int i = drawnFrames.Count - 1; i >= 0; i--)
            {
                if (drawnFrames[i].Rect.Contains(e.Location))
                {
                    zoomNode = drawnFrames[i].Node;
                    flamePanel.Invalidate();
                    return;
                }
            }
        }

        void FlamePanel_MouseMove(object sender, MouseEventArgs e)
        {
            for (int i = drawnFrames.Count - 1; i >= 0; i--)
            {
                if (drawnFrames[i].Rect.Contains(e.Location))
                {
                    var node = drawnFrames[i].Node;
                    flamePanel.Cursor = Cursors.Hand;
                    if (hoveredFrame == null || hoveredFrame.Node != node)
                    {
                        hoveredFrame = drawnFrames[i];
                        double pct = totalSamples > 0 ? node.Total * 100.0 / totalSamples : 0;
                        toolTip.SetToolTip(flamePanel, string.Format("{0}\n{1} samples ({2:F1}%)",
                            node.Name, node.Total, pct));
                    }
                    return;
                }
            }
            flamePanel.Cursor = Cursors.Default;
            hoveredFrame = null;
            toolTip.SetToolTip(flamePanel, "");
        }

        private void CopperDissForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (plugin.IsSampling)
                plugin.StopSampling();
            ProfilerPlugin.Active = false;
            ProfilerPlugin.form = null;
        }
    }

    class FlameNode
    {
        public string Name;
        public long Self;
        public long Total;
        public Dictionary<string, FlameNode> Children = new Dictionary<string, FlameNode>();
        public FlameNode(string name) { Name = name; }
    }

    class FlameFrame
    {
        public RectangleF Rect;
        public FlameNode Node;
    }

    class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.ResizeRedraw, true);
            this.UpdateStyles();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Don't paint background separately - Paint handler does g.Clear()
        }
    }
}
