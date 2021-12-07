namespace CopperDissassembler
{
    partial class CopperDissForm
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
            this.SuspendLayout();
            // 
            // vAddressScrollBar
            // 
            this.vAddressScrollBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.vAddressScrollBar.LargeChange = 100;
            this.vAddressScrollBar.Location = new System.Drawing.Point(258, 9);
            this.vAddressScrollBar.Maximum = 1024;
            this.vAddressScrollBar.Name = "vAddressScrollBar";
            this.vAddressScrollBar.Size = new System.Drawing.Size(20, 597);
            this.vAddressScrollBar.TabIndex = 0;
            this.vAddressScrollBar.Scroll += new System.Windows.Forms.ScrollEventHandler(this.vAddressScrollBar_Scroll);
            this.vAddressScrollBar.ValueChanged += new System.EventHandler(this.vScrollBar1_ValueChanged);
            // 
            // CopperDissForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(287, 615);
            this.Controls.Add(this.vAddressScrollBar);
            this.Name = "CopperDissForm";
            this.Text = "Copper Disassembler";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.CopperDissForm_FormClosed);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResizeEnd += new System.EventHandler(this.CopperDissForm_ResizeEnd);
            this.Paint += new System.Windows.Forms.PaintEventHandler(this.CopperDissForm_Paint);
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.VScrollBar vAddressScrollBar;
    }
}

