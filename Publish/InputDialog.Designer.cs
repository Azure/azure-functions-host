namespace Publish
{
    partial class InputDialog
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
            this._buttonPublish = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this._textBoxAccountName = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this._textBoxKey = new System.Windows.Forms.TextBox();
            this._comboBoxContainers = new System.Windows.Forms.ComboBox();
            this.label3 = new System.Windows.Forms.Label();
            this._textBoxLocalDir = new System.Windows.Forms.TextBox();
            this._buttonBrowseLocalDir = new System.Windows.Forms.Button();
            this._textBoxServiceURL = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // _buttonPublish
            // 
            this._buttonPublish.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this._buttonPublish.Location = new System.Drawing.Point(293, 260);
            this._buttonPublish.Name = "_buttonPublish";
            this._buttonPublish.Size = new System.Drawing.Size(106, 28);
            this._buttonPublish.TabIndex = 0;
            this._buttonPublish.Text = "Publish!";
            this._buttonPublish.UseVisualStyleBackColor = true;
            this._buttonPublish.Click += new System.EventHandler(this._buttonPublish_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(-2, 113);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(81, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Account Name:";
            // 
            // _textBoxAccountName
            // 
            this._textBoxAccountName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._textBoxAccountName.Location = new System.Drawing.Point(114, 110);
            this._textBoxAccountName.Name = "_textBoxAccountName";
            this._textBoxAccountName.Size = new System.Drawing.Size(275, 20);
            this._textBoxAccountName.TabIndex = 2;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(-2, 140);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(105, 13);
            this.label2.TabIndex = 3;
            this.label2.Text = "Account Secret Key:";
            // 
            // _textBoxKey
            // 
            this._textBoxKey.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._textBoxKey.Location = new System.Drawing.Point(114, 141);
            this._textBoxKey.Name = "_textBoxKey";
            this._textBoxKey.PasswordChar = '*';
            this._textBoxKey.Size = new System.Drawing.Size(275, 20);
            this._textBoxKey.TabIndex = 4;
            // 
            // _comboBoxContainers
            // 
            this._comboBoxContainers.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._comboBoxContainers.FormattingEnabled = true;
            this._comboBoxContainers.Location = new System.Drawing.Point(114, 167);
            this._comboBoxContainers.Name = "_comboBoxContainers";
            this._comboBoxContainers.Size = new System.Drawing.Size(275, 21);
            this._comboBoxContainers.TabIndex = 5;
            this._comboBoxContainers.DropDown += new System.EventHandler(this.comboBox1_DropDown);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(-2, 168);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(55, 13);
            this.label3.TabIndex = 6;
            this.label3.Text = "Container:";
            // 
            // _textBoxLocalDir
            // 
            this._textBoxLocalDir.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._textBoxLocalDir.Location = new System.Drawing.Point(14, 52);
            this._textBoxLocalDir.Name = "_textBoxLocalDir";
            this._textBoxLocalDir.Size = new System.Drawing.Size(320, 20);
            this._textBoxLocalDir.TabIndex = 8;
            // 
            // _buttonBrowseLocalDir
            // 
            this._buttonBrowseLocalDir.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this._buttonBrowseLocalDir.Location = new System.Drawing.Point(342, 53);
            this._buttonBrowseLocalDir.Name = "_buttonBrowseLocalDir";
            this._buttonBrowseLocalDir.Size = new System.Drawing.Size(47, 19);
            this._buttonBrowseLocalDir.TabIndex = 9;
            this._buttonBrowseLocalDir.Text = "...";
            this._buttonBrowseLocalDir.UseVisualStyleBackColor = true;
            this._buttonBrowseLocalDir.Click += new System.EventHandler(this._buttonBrowseLocalDir_Click);
            // 
            // _textBoxServiceURL
            // 
            this._textBoxServiceURL.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._textBoxServiceURL.Location = new System.Drawing.Point(114, 223);
            this._textBoxServiceURL.Name = "_textBoxServiceURL";
            this._textBoxServiceURL.Size = new System.Drawing.Size(275, 20);
            this._textBoxServiceURL.TabIndex = 10;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(-2, 223);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(68, 13);
            this.label5.TabIndex = 11;
            this.label5.Text = "Service URL";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(-2, 9);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(191, 13);
            this.label6.TabIndex = 13;
            this.label6.Text = "To add a new function to SimpleBatch:";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label7.Location = new System.Drawing.Point(6, 36);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(392, 13);
            this.label7.TabIndex = 14;
            this.label7.Text = "1) Upload the assemblies from this local directory: (eg, a bin\\debug)";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.Location = new System.Drawing.Point(6, 83);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(196, 13);
            this.label4.TabIndex = 15;
            this.label4.Text = "2) upload to this Cloud container:";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label8.Location = new System.Drawing.Point(6, 193);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(216, 13);
            this.label8.TabIndex = 16;
            this.label8.Text = "3) register with this service instance:";
            // 
            // InputDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(411, 300);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this._textBoxServiceURL);
            this.Controls.Add(this._buttonBrowseLocalDir);
            this.Controls.Add(this._textBoxLocalDir);
            this.Controls.Add(this.label3);
            this.Controls.Add(this._comboBoxContainers);
            this.Controls.Add(this._textBoxKey);
            this.Controls.Add(this.label2);
            this.Controls.Add(this._textBoxAccountName);
            this.Controls.Add(this.label1);
            this.Controls.Add(this._buttonPublish);
            this.Name = "InputDialog";
            this.Text = "Input Publish information";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button _buttonPublish;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox _textBoxAccountName;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox _textBoxKey;
        private System.Windows.Forms.ComboBox _comboBoxContainers;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox _textBoxLocalDir;
        private System.Windows.Forms.Button _buttonBrowseLocalDir;
        private System.Windows.Forms.TextBox _textBoxServiceURL;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label8;
    }
}