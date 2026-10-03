namespace MemoryViewer
{
    partial class MemoryViewerForm
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
            this.MemoryPanel = new System.Windows.Forms.Panel();
            this.MemModeCombo = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.BankOffsetBox = new System.Windows.Forms.TextBox();
            this.WidthCombo = new System.Windows.Forms.ComboBox();
            this.ULAEnabledCheckbox = new System.Windows.Forms.CheckBox();
            this.ScaleLabel = new System.Windows.Forms.Label();
            this.ScaleCombo = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.BuffSizeBox = new System.Windows.Forms.TextBox();
            this.RealtimeCheckbox = new System.Windows.Forms.CheckBox();
            this.label5 = new System.Windows.Forms.Label();
            this.PaletteCombo = new System.Windows.Forms.ComboBox();
            this.label6 = new System.Windows.Forms.Label();
            this.OffsetCombo = new System.Windows.Forms.ComboBox();
            this.SnapShotButton = new System.Windows.Forms.Button();
            this.PauseButton = new System.Windows.Forms.Button();
            this.StepButton = new System.Windows.Forms.Button();
            this.DecreaseMemoryButton = new System.Windows.Forms.Button();
            this.IncreaseMemoryButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // MemoryPanel
            // 
            this.MemoryPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.MemoryPanel.Location = new System.Drawing.Point(13, 85);
            this.MemoryPanel.Name = "MemoryPanel";
            this.MemoryPanel.Size = new System.Drawing.Size(767, 577);
            this.MemoryPanel.TabIndex = 5;
            this.MemoryPanel.MouseEnter += new System.EventHandler(this.SpritePanel_MouseEnter);
            this.MemoryPanel.MouseLeave += new System.EventHandler(this.SpritePanel_MouseLeave);
            this.MemoryPanel.MouseMove += new System.Windows.Forms.MouseEventHandler(this.SpritePanel_MouseMove);
            // 
            // MemModeCombo
            // 
            this.MemModeCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.MemModeCombo.FormattingEnabled = true;
            this.MemModeCombo.Items.AddRange(new object[] {
            "Spectrum Screen",
            "Linear 1bit",
            "256 colour Linear",
            "256 colour stripped",
            "16 Colour Linear",
            "16 Colour stripped",
            "Raw (grayscale)"});
            this.MemModeCombo.Location = new System.Drawing.Point(48, 21);
            this.MemModeCombo.Name = "MemModeCombo";
            this.MemModeCombo.Size = new System.Drawing.Size(121, 21);
            this.MemModeCombo.TabIndex = 0;
            this.MemModeCombo.TabStop = false;
            this.MemModeCombo.SelectedIndexChanged += new System.EventHandler(this.SpriteModeCombo_SelectedIndexChanged);
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
            this.label2.Location = new System.Drawing.Point(307, 24);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(63, 13);
            this.label2.TabIndex = 5;
            this.label2.Text = "Bank:Offset";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(178, 25);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(59, 13);
            this.label3.TabIndex = 7;
            this.label3.Text = "Byte Width";
            // 
            // BankOffsetBox
            // 
            this.BankOffsetBox.Location = new System.Drawing.Point(376, 22);
            this.BankOffsetBox.Name = "BankOffsetBox";
            this.BankOffsetBox.Size = new System.Drawing.Size(74, 20);
            this.BankOffsetBox.TabIndex = 2;
            this.BankOffsetBox.TabStop = false;
            this.BankOffsetBox.TextChanged += new System.EventHandler(this.BankOffsetBox_TextChanged);
            // 
            // WidthCombo
            // 
            this.WidthCombo.FormattingEnabled = true;
            this.WidthCombo.Items.AddRange(new object[] {
            "1",
            "2",
            "4",
            "8",
            "16",
            "32",
            "64",
            "128",
            "256",
            "512",
            "1024",
            "2048",
            "4096"});
            this.WidthCombo.Location = new System.Drawing.Point(243, 21);
            this.WidthCombo.Name = "WidthCombo";
            this.WidthCombo.Size = new System.Drawing.Size(58, 21);
            this.WidthCombo.TabIndex = 9;
            this.WidthCombo.SelectedIndexChanged += new System.EventHandler(this.WidthCombo_SelectedIndexChanged);
            this.WidthCombo.SelectionChangeCommitted += new System.EventHandler(this.WidthCombo_SelectionChangeCommitted);
            this.WidthCombo.KeyUp += new System.Windows.Forms.KeyEventHandler(this.WidthCombo_KeyUp);
            // 
            // ULAEnabledCheckbox
            // 
            this.ULAEnabledCheckbox.AutoSize = true;
            this.ULAEnabledCheckbox.Checked = true;
            this.ULAEnabledCheckbox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.ULAEnabledCheckbox.Location = new System.Drawing.Point(16, 52);
            this.ULAEnabledCheckbox.Name = "ULAEnabledCheckbox";
            this.ULAEnabledCheckbox.Size = new System.Drawing.Size(83, 17);
            this.ULAEnabledCheckbox.TabIndex = 10;
            this.ULAEnabledCheckbox.Text = "ULA Enable";
            this.ULAEnabledCheckbox.UseVisualStyleBackColor = true;
            // 
            // ScaleLabel
            // 
            this.ScaleLabel.AutoSize = true;
            this.ScaleLabel.Location = new System.Drawing.Point(203, 52);
            this.ScaleLabel.Name = "ScaleLabel";
            this.ScaleLabel.Size = new System.Drawing.Size(34, 13);
            this.ScaleLabel.TabIndex = 11;
            this.ScaleLabel.Text = "Scale";
            // 
            // ScaleCombo
            // 
            this.ScaleCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ScaleCombo.FormattingEnabled = true;
            this.ScaleCombo.Items.AddRange(new object[] {
            "x1",
            "x2",
            "x3",
            "x4",
            "x5",
            "x6",
            "x7",
            "x8"});
            this.ScaleCombo.Location = new System.Drawing.Point(243, 50);
            this.ScaleCombo.Name = "ScaleCombo";
            this.ScaleCombo.Size = new System.Drawing.Size(58, 21);
            this.ScaleCombo.TabIndex = 12;
            this.ScaleCombo.TabStop = false;
            this.ScaleCombo.SelectedIndexChanged += new System.EventHandler(this.ScaleCombo_SelectedIndexChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(315, 55);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(55, 13);
            this.label4.TabIndex = 13;
            this.label4.Text = "BufferSize";
            // 
            // BuffSizeBox
            // 
            this.BuffSizeBox.Location = new System.Drawing.Point(376, 51);
            this.BuffSizeBox.Name = "BuffSizeBox";
            this.BuffSizeBox.Size = new System.Drawing.Size(74, 20);
            this.BuffSizeBox.TabIndex = 14;
            this.BuffSizeBox.TabStop = false;
            this.BuffSizeBox.TextChanged += new System.EventHandler(this.BuffSizeBox_TextChanged);
            // 
            // RealtimeCheckbox
            // 
            this.RealtimeCheckbox.AutoSize = true;
            this.RealtimeCheckbox.Checked = true;
            this.RealtimeCheckbox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.RealtimeCheckbox.Location = new System.Drawing.Point(105, 52);
            this.RealtimeCheckbox.Name = "RealtimeCheckbox";
            this.RealtimeCheckbox.Size = new System.Drawing.Size(71, 17);
            this.RealtimeCheckbox.TabIndex = 15;
            this.RealtimeCheckbox.Text = "RealTime";
            this.RealtimeCheckbox.UseVisualStyleBackColor = true;
            this.RealtimeCheckbox.CheckedChanged += new System.EventHandler(this.checkBox2_CheckedChanged);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(493, 24);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(40, 13);
            this.label5.TabIndex = 17;
            this.label5.Text = "Palette";
            // 
            // PaletteCombo
            // 
            this.PaletteCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.PaletteCombo.FormattingEnabled = true;
            this.PaletteCombo.Items.AddRange(new object[] {
            "ULA 0",
            "ULA 1",
            "Layer2 0",
            "Layer2 1",
            "Sprites 0",
            "Sprites 1",
            "Tiles 0",
            "Tiles 1"});
            this.PaletteCombo.Location = new System.Drawing.Point(539, 21);
            this.PaletteCombo.Name = "PaletteCombo";
            this.PaletteCombo.Size = new System.Drawing.Size(67, 21);
            this.PaletteCombo.TabIndex = 16;
            this.PaletteCombo.TabStop = false;
            this.PaletteCombo.SelectedIndexChanged += new System.EventHandler(this.comboBox1_SelectedIndexChanged);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(498, 54);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(35, 13);
            this.label6.TabIndex = 19;
            this.label6.Text = "Offset";
            // 
            // OffsetCombo
            // 
            this.OffsetCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.OffsetCombo.FormattingEnabled = true;
            this.OffsetCombo.Items.AddRange(new object[] {
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
            this.OffsetCombo.Location = new System.Drawing.Point(539, 50);
            this.OffsetCombo.Name = "OffsetCombo";
            this.OffsetCombo.Size = new System.Drawing.Size(67, 21);
            this.OffsetCombo.TabIndex = 20;
            this.OffsetCombo.TabStop = false;
            this.OffsetCombo.SelectedIndexChanged += new System.EventHandler(this.OffsetCombo_SelectedIndexChanged);
            // 
            // SnapShotButton
            // 
            this.SnapShotButton.Location = new System.Drawing.Point(617, 49);
            this.SnapShotButton.Name = "SnapShotButton";
            this.SnapShotButton.Size = new System.Drawing.Size(75, 23);
            this.SnapShotButton.TabIndex = 21;
            this.SnapShotButton.Text = "SnapShot";
            this.SnapShotButton.UseVisualStyleBackColor = true;
            this.SnapShotButton.Click += new System.EventHandler(this.SnapShotButton_Click);
            // 
            // PauseButton
            // 
            this.PauseButton.Location = new System.Drawing.Point(617, 21);
            this.PauseButton.Name = "PauseButton";
            this.PauseButton.Size = new System.Drawing.Size(75, 23);
            this.PauseButton.TabIndex = 22;
            this.PauseButton.Text = "Pause";
            this.PauseButton.UseVisualStyleBackColor = true;
            this.PauseButton.Click += new System.EventHandler(this.PauseButton_Click);
            // 
            // StepButton
            // 
            this.StepButton.Enabled = false;
            this.StepButton.Location = new System.Drawing.Point(698, 21);
            this.StepButton.Name = "StepButton";
            this.StepButton.Size = new System.Drawing.Size(75, 23);
            this.StepButton.TabIndex = 23;
            this.StepButton.Text = "Step";
            this.StepButton.UseVisualStyleBackColor = true;
            this.StepButton.Click += new System.EventHandler(this.StepButton_Click);
            // 
            // DecreaseMemoryButton
            // 
            this.DecreaseMemoryButton.Font = new System.Drawing.Font("Wingdings", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.DecreaseMemoryButton.Location = new System.Drawing.Point(456, 21);
            this.DecreaseMemoryButton.Name = "DecreaseMemoryButton";
            this.DecreaseMemoryButton.Size = new System.Drawing.Size(28, 23);
            this.DecreaseMemoryButton.TabIndex = 24;
            this.DecreaseMemoryButton.Text = "á";
            this.DecreaseMemoryButton.UseVisualStyleBackColor = true;
            this.DecreaseMemoryButton.Click += new System.EventHandler(this.DecreaseMemoryButton_Click);
            // 
            // IncreaseMemoryButton
            // 
            this.IncreaseMemoryButton.Font = new System.Drawing.Font("Wingdings", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.IncreaseMemoryButton.Location = new System.Drawing.Point(456, 49);
            this.IncreaseMemoryButton.Name = "IncreaseMemoryButton";
            this.IncreaseMemoryButton.Size = new System.Drawing.Size(28, 23);
            this.IncreaseMemoryButton.TabIndex = 25;
            this.IncreaseMemoryButton.Text = "â";
            this.IncreaseMemoryButton.UseVisualStyleBackColor = true;
            this.IncreaseMemoryButton.Click += new System.EventHandler(this.IncreaseMemoryButton_Click);
            // 
            // MemoryViewerForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(792, 671);
            this.Controls.Add(this.IncreaseMemoryButton);
            this.Controls.Add(this.DecreaseMemoryButton);
            this.Controls.Add(this.StepButton);
            this.Controls.Add(this.PauseButton);
            this.Controls.Add(this.SnapShotButton);
            this.Controls.Add(this.OffsetCombo);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.PaletteCombo);
            this.Controls.Add(this.RealtimeCheckbox);
            this.Controls.Add(this.BuffSizeBox);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.ScaleCombo);
            this.Controls.Add(this.ScaleLabel);
            this.Controls.Add(this.ULAEnabledCheckbox);
            this.Controls.Add(this.WidthCombo);
            this.Controls.Add(this.BankOffsetBox);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.MemModeCombo);
            this.Controls.Add(this.MemoryPanel);
            this.DoubleBuffered = true;
            this.Name = "MemoryViewerForm";
            this.Text = "Memory Viewer";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MemoryViewerForm_FormClosing);
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.SpriteViewerForm_FormClosed);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResizeEnd += new System.EventHandler(this.SpriteViewerForm_ResizeEnd);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        public System.Windows.Forms.Panel MemoryPanel;
        private System.Windows.Forms.Label label1;
        public System.Windows.Forms.ComboBox MemModeCombo;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        public System.Windows.Forms.TextBox BankOffsetBox;
        public System.Windows.Forms.ComboBox WidthCombo;
        private System.Windows.Forms.Label ScaleLabel;
        public System.Windows.Forms.ComboBox ScaleCombo;
        private System.Windows.Forms.Label label4;
        public System.Windows.Forms.TextBox BuffSizeBox;
        private System.Windows.Forms.Label label5;
        public System.Windows.Forms.ComboBox PaletteCombo;
        private System.Windows.Forms.Label label6;
        public System.Windows.Forms.ComboBox OffsetCombo;
        public System.Windows.Forms.CheckBox ULAEnabledCheckbox;
        public System.Windows.Forms.CheckBox RealtimeCheckbox;
        private System.Windows.Forms.Button DecreaseMemoryButton;
        private System.Windows.Forms.Button IncreaseMemoryButton;
        public System.Windows.Forms.Button StepButton;
        public System.Windows.Forms.Button SnapShotButton;
        public System.Windows.Forms.Button PauseButton;
    }
}

