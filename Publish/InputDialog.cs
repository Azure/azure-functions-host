using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace Publish
{
    public partial class InputDialog : Form
    {
        public InputDialog()
        {
            InitializeComponent();
        }

        private void _buttonBrowseLocalDir_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dlg = new FolderBrowserDialog();

            dlg.SelectedPath = this._textBoxLocalDir.Text;

            var result = dlg.ShowDialog();

            if (result == DialogResult.OK)
            {
                this._textBoxLocalDir.Text = dlg.SelectedPath;
            }
        }

        CloudStorageAccount GetAccount()
        {
            string name = this._textBoxAccountName.Text;
            string key = this._textBoxKey.Text;

            return new CloudStorageAccount(new StorageCredentialsAccountAndKey(name, key), false);
        }

        CloudBlobClient GetClient()
        {
            return GetAccount().CreateCloudBlobClient();
        }

        private void comboBox1_DropDown(object sender, EventArgs e)
        {
            var x = this._comboBoxContainers.Items;
            x.Clear();

            try
            {
                CloudBlobClient client = GetClient();

                foreach (CloudBlobContainer container in client.ListContainers())
                {
                    x.Add(container.Name);
                }
            }                
            catch 
            {
                x.Add("<account logon is inccorect>");
            }
        }

        private void _buttonPublish_Click(object sender, EventArgs e)
        {
            // Validate controls and produce a publish data.

            // Verify account and containter

            CloudBlobClient client;
            try
            {
                client = GetClient();
            }
            catch
            {
                MessageBox.Show(this, "Account credentials are incorrect", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string containerName = this._comboBoxContainers.Text;

            CloudBlobContainer c;
            try
            {
                c = client.GetContainerReference(containerName);
            }
            catch
            {
                MessageBox.Show(this, "Illegal container name", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!DoesExist(c))
            {
                var result = MessageBox.Show(this, "Container '" + containerName + "' does not exist. Do you want to create it?", "Warning", MessageBoxButtons.YesNo);
                if (result == System.Windows.Forms.DialogResult.No)
                {
                    return;
                }
            }


            // Check local dir
            {
                string dir = this._textBoxLocalDir.Text;
                var files = Directory.EnumerateFiles(dir, "*.dll").Concat(Directory.EnumerateFiles(dir, "*.exe"));
                if (!files.Any())
                {
                    MessageBox.Show(this, "There are no assemblies (*.dll, *.exe) in the specified local directory. Is this a source directory instead?", "Error", MessageBoxButtons.OK);
                    return;
                }
            }

            try
            {
                CheckServiceUrl(this._textBoxServiceURL.Text);
            }
            catch
            {
                MessageBox.Show(this, "Service URL is not valid", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Data = new PublishData
            {
                AccountConnectionString = GetAccount().ToString(exportSecrets: true),
                Container = containerName,
                LocalDir = this._textBoxLocalDir.Text,
                ServiceUrl = this._textBoxServiceURL.Text
            };
            this.Close();
        }

        // Throw on error.
        void CheckServiceUrl(string serviceUrl)
        {
            string uri = string.Format(@"{0}/Api/Execution/Heartbeat", serviceUrl);

            WebRequest request = WebRequest.Create(uri);
            request.Method = "GET";
            request.ContentType = "application/json";
            request.ContentLength = 0;

            var response = request.GetResponse(); // throws on errors and 404
        }

        [DebuggerNonUserCode]
        bool DoesExist(CloudBlobContainer c)
        {
            try
            {
                c.FetchAttributes();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public PublishData Data;
    }
}
