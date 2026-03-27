namespace Profiler
{
    partial class ProfilerForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1200, 600);
            this.DoubleBuffered = true;
            this.Name = "ProfilerForm";
            this.Text = "CSpect Profiler";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.CopperDissForm_FormClosed);
            this.ResumeLayout(false);
        }
    }
}
