namespace catgirlwindow.Src.UI;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;
    private System.Windows.Forms.Label labelUrl;
    private System.Windows.Forms.TextBox textBoxUrl;
    private System.Windows.Forms.Label labelModel;
    private System.Windows.Forms.TextBox textBoxModel;
    private System.Windows.Forms.Label labelProvider;
    private System.Windows.Forms.ComboBox comboBoxProvider;
    private System.Windows.Forms.RichTextBox richTextChat;
    private System.Windows.Forms.Label labelPrompt;
    private System.Windows.Forms.TextBox textBoxPrompt;
    private System.Windows.Forms.Button buttonSend;
    private System.Windows.Forms.Button buttonClear;
    private System.Windows.Forms.PictureBox pictureBoxCharacter;
    private System.Windows.Forms.Timer renderTimer;

    /// <summary>
    ///  Clean up any resources being used.
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
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        this.labelUrl = new System.Windows.Forms.Label();
        this.textBoxUrl = new System.Windows.Forms.TextBox();
        this.labelModel = new System.Windows.Forms.Label();
        this.textBoxModel = new System.Windows.Forms.TextBox();
        this.labelProvider = new System.Windows.Forms.Label();
        this.comboBoxProvider = new System.Windows.Forms.ComboBox();
        this.richTextChat = new System.Windows.Forms.RichTextBox();
        this.labelPrompt = new System.Windows.Forms.Label();
        this.textBoxPrompt = new System.Windows.Forms.TextBox();
        this.buttonSend = new System.Windows.Forms.Button();
        this.pictureBoxCharacter = new System.Windows.Forms.PictureBox();
        this.renderTimer = new System.Windows.Forms.Timer();
        this.buttonClear = new System.Windows.Forms.Button();
        ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCharacter)).BeginInit();
        this.SuspendLayout();
        // 
        // labelUrl
        // 
        this.labelUrl.AutoSize = true;
        this.labelUrl.Location = new System.Drawing.Point(12, 15);
        this.labelUrl.Name = "labelUrl";
        this.labelUrl.Size = new System.Drawing.Size(99, 15);
        this.labelUrl.TabIndex = 0;
        this.labelUrl.Text = "LM Studio URL:";
        // 
        // textBoxUrl
        // 
        this.textBoxUrl.Location = new System.Drawing.Point(117, 12);
        this.textBoxUrl.Name = "textBoxUrl";
        this.textBoxUrl.Size = new System.Drawing.Size(420, 23);
        this.textBoxUrl.TabIndex = 1;
        this.textBoxUrl.Text = "http://localhost:1234";
        // 
        // labelModel
        // 
        this.labelModel.AutoSize = true;
        this.labelModel.Location = new System.Drawing.Point(12, 50);
        this.labelModel.Name = "labelModel";
        this.labelModel.Size = new System.Drawing.Size(84, 15);
        this.labelModel.TabIndex = 2;
        this.labelModel.Text = "Model name:";
        // 
        // textBoxModel
        // 
        this.textBoxModel.Location = new System.Drawing.Point(117, 47);
        this.textBoxModel.Name = "textBoxModel";
        this.textBoxModel.Size = new System.Drawing.Size(420, 23);
        this.textBoxModel.TabIndex = 3;
        this.textBoxModel.Text = "qwen4b";
        // 
        // labelProvider
        // 
        this.labelProvider.AutoSize = true;
        this.labelProvider.Location = new System.Drawing.Point(12, 80);
        this.labelProvider.Name = "labelProvider";
        this.labelProvider.Size = new System.Drawing.Size(60, 15);
        this.labelProvider.TabIndex = 4;
        this.labelProvider.Text = "Provider:";
        // 
        // comboBoxProvider
        // 
        this.comboBoxProvider.FormattingEnabled = true;
        this.comboBoxProvider.Location = new System.Drawing.Point(117, 77);
        this.comboBoxProvider.Name = "comboBoxProvider";
        this.comboBoxProvider.Size = new System.Drawing.Size(420, 23);
        this.comboBoxProvider.TabIndex = 5;
        this.comboBoxProvider.SelectedIndexChanged += new System.EventHandler(this.comboBoxProvider_SelectedIndexChanged);
        // 
        // richTextChat
        // 
        this.richTextChat.Location = new System.Drawing.Point(12, 115);
        this.richTextChat.Name = "richTextChat";
        this.richTextChat.ReadOnly = true;
        this.richTextChat.Size = new System.Drawing.Size(776, 250);
        this.richTextChat.TabIndex = 6;
        this.richTextChat.Text = "";
        this.richTextChat.WordWrap = true;
        // 
        // labelPrompt
        // 
        this.labelPrompt.AutoSize = true;
        this.labelPrompt.Location = new System.Drawing.Point(12, 378);
        this.labelPrompt.Name = "labelPrompt";
        this.labelPrompt.Size = new System.Drawing.Size(105, 15);
        this.labelPrompt.TabIndex = 7;
        this.labelPrompt.Text = "输入你的问题：";
        // 
        // textBoxPrompt
        // 
        this.textBoxPrompt.Location = new System.Drawing.Point(12, 396);
        this.textBoxPrompt.Multiline = true;
        this.textBoxPrompt.Name = "textBoxPrompt";
        this.textBoxPrompt.Size = new System.Drawing.Size(620, 42);
        this.textBoxPrompt.TabIndex = 8;
        this.textBoxPrompt.KeyDown += new System.Windows.Forms.KeyEventHandler(this.textBoxPrompt_KeyDown);
        // 
        // buttonSend
        // 
        this.buttonSend.Location = new System.Drawing.Point(648, 396);
        this.buttonSend.Name = "buttonSend";
        this.buttonSend.Size = new System.Drawing.Size(140, 42);
        this.buttonSend.TabIndex = 9;
        this.buttonSend.Text = "发送";
        this.buttonSend.UseVisualStyleBackColor = true;
        this.buttonSend.Click += new System.EventHandler(this.buttonSend_Click);
        // 
        // pictureBoxCharacter
        // 
        this.pictureBoxCharacter.BackColor = System.Drawing.Color.Transparent;
        this.pictureBoxCharacter.Location = new System.Drawing.Point(800, 12);
        this.pictureBoxCharacter.Name = "pictureBoxCharacter";
        this.pictureBoxCharacter.Size = new System.Drawing.Size(512, 689);
        this.pictureBoxCharacter.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
        this.pictureBoxCharacter.TabIndex = 11;
        this.pictureBoxCharacter.TabStop = false;
        // 
        // renderTimer
        // 
        this.renderTimer.Interval = 50;
        this.renderTimer.Tick += new System.EventHandler(this.renderTimer_Tick);
        // 
        // buttonClear
        // 
        this.buttonClear.Location = new System.Drawing.Point(648, 357);
        this.buttonClear.Name = "buttonClear";
        this.buttonClear.Size = new System.Drawing.Size(140, 33);
        this.buttonClear.TabIndex = 10;
        this.buttonClear.Text = "清除聊天记录";
        this.buttonClear.UseVisualStyleBackColor = true;
        this.buttonClear.Click += new System.EventHandler(this.buttonClear_Click);
        // 
        // Form1
        // 
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(1324, 713);
        this.Controls.Add(this.pictureBoxCharacter);
        this.Controls.Add(this.buttonClear);
        this.Controls.Add(this.buttonSend);
        this.Controls.Add(this.textBoxPrompt);
        this.Controls.Add(this.labelPrompt);
        this.Controls.Add(this.richTextChat);
        this.Controls.Add(this.comboBoxProvider);
        this.Controls.Add(this.labelProvider);
        this.Controls.Add(this.textBoxModel);
        this.Controls.Add(this.labelModel);
        this.Controls.Add(this.textBoxUrl);
        this.Controls.Add(this.labelUrl);
        this.Name = "Form1";
        this.Text = "猫娘窗口";
        ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCharacter)).EndInit();
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion
}
