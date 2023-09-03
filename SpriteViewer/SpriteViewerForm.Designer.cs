namespace SpriteViewer
{
    partial class SpriteViewerForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.vAddressScrollBar = new System.Windows.Forms.VScrollBar();
            this.SpritePanel = new System.Windows.Forms.Panel();
            this.SpriteModeCombo = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.SprSize = new System.Windows.Forms.ComboBox();
            this.label3 = new System.Windows.Forms.Label();
            this.SprPalette = new System.Windows.Forms.ComboBox();
            this.SuspendLayout();
            // 
            // vAddressScrollBar
            // 
            this.vAddressScrollBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.vAddressScrollBar.LargeChange = 100;
            this.vAddressScrollBar.Location = new System.Drawing.Point(608, 9);
            this.vAddressScrollBar.Maximum = 1024;
            this.vAddressScrollBar.Name = "vAddressScrollBar";
            this.vAddressScrollBar.Size = new System.Drawing.Size(20, 684);
            this.vAddressScrollBar.TabIndex = 0;
            this.vAddressScrollBar.Scroll += new System.Windows.Forms.ScrollEventHandler(this.vAddressScrollBar_Scroll);
            this.vAddressScrollBar.ValueChanged += new System.EventHandler(this.vScrollBar1_ValueChanged);
            // 
            // SpritePanel
            // 
            this.SpritePanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SpritePanel.Location = new System.Drawing.Point(13, 48);
            this.SpritePanel.Name = "SpritePanel";
            this.SpritePanel.Size = new System.Drawing.Size(592, 645);
            this.SpritePanel.TabIndex = 1;
            // 
            // SpriteModeCombo
            // 
            this.SpriteModeCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.SpriteModeCombo.FormattingEnabled = true;
            this.SpriteModeCombo.Items.AddRange(new object[] {
            "256 Colour Mode",
            "16 Colour Mode"});
            this.SpriteModeCombo.Location = new System.Drawing.Point(48, 21);
            this.SpriteModeCombo.Name = "SpriteModeCombo";
            this.SpriteModeCombo.Size = new System.Drawing.Size(121, 21);
            this.SpriteModeCombo.TabIndex = 2;
            this.SpriteModeCombo.SelectedIndexChanged += new System.EventHandler(this.SpriteModeCombo_SelectedIndexChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(13, 25);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(34, 13);
            this.label1.TabIndex = 3;
            this.label1.Text = "Mode";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(451, 25);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(27, 13);
            this.label2.TabIndex = 5;
            this.label2.Text = "Size";
            // 
            // SprSize
            // 
            this.SprSize.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.SprSize.FormattingEnabled = true;
            this.SprSize.Items.AddRange(new object[] {
            "16 x16",
            "32 x 32"});
            this.SprSize.Location = new System.Drawing.Point(484, 22);
            this.SprSize.Name = "SprSize";
            this.SprSize.Size = new System.Drawing.Size(121, 21);
            this.SprSize.TabIndex = 4;
            this.SprSize.SelectedIndexChanged += new System.EventHandler(this.SprSize_SelectedIndexChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(211, 25);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(71, 13);
            this.label3.TabIndex = 7;
            this.label3.Text = "Palette Offset";
            // 
            // SprPalette
            // 
            this.SprPalette.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.SprPalette.FormattingEnabled = true;
            this.SprPalette.Items.AddRange(new object[] {
            "0",
            "1",
            "2",
            "3",
            "4",
            "5",
            "6",
            "7",
            "8",
            "9",
            "10",
            "11",
            "12",
            "13",
            "14",
            "15"});
            this.SprPalette.Location = new System.Drawing.Point(287, 21);
            this.SprPalette.Name = "SprPalette";
            this.SprPalette.Size = new System.Drawing.Size(121, 21);
            this.SprPalette.TabIndex = 6;
            this.SprPalette.SelectedIndexChanged += new System.EventHandler(this.SprPalette_SelectedIndexChanged);
            // 
            // SpriteViewerForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(637, 702);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.SprPalette);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.SprSize);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.SpriteModeCombo);
            this.Controls.Add(this.SpritePanel);
            this.Controls.Add(this.vAddressScrollBar);
            this.DoubleBuffered = true;
            this.Name = "SpriteViewerForm";
            this.Text = "Sprite Viewer";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.SpriteViewerForm_FormClosed);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResizeEnd += new System.EventHandler(this.SpriteViewerForm_ResizeEnd);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.VScrollBar vAddressScrollBar;
        public System.Windows.Forms.Panel SpritePanel;
        private System.Windows.Forms.Label label1;
        public System.Windows.Forms.ComboBox SpriteModeCombo;
        private System.Windows.Forms.Label label2;
        public System.Windows.Forms.ComboBox SprSize;
        private System.Windows.Forms.Label label3;
        public System.Windows.Forms.ComboBox SprPalette;
    }
}

