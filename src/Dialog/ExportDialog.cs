/* uxPlayer / Software Synthesizer

LICENSE - The MIT License (MIT)

Copyright (c) 2013-2014 Tomona Nanase

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Permissions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SoundUtils;
using SoundUtils.Filtering;
using SoundUtils.Filtering.FIR;
using SoundUtils.IO;
using ux.Utils.Midi;

namespace uxPlayer
{
    partial class ExportDialog : Form
    {
        private string inputFile;
        private IEnumerable<string> presetFiles;
        private volatile bool reqEnd;
        private MasterControlDialog masterControlDialog;

        public ExportDialog(string inputFile, IEnumerable<string> presetFiles, MasterControlDialog masterControlDialog)
        {
            this.inputFile = inputFile;
            this.presetFiles = presetFiles;
            this.masterControlDialog = masterControlDialog;
            InitializeComponent();
        }

        protected override CreateParams CreateParams
        {
            [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
            get
            {
                const int CS_NOCLOSE = 0x200;
                CreateParams cp = base.CreateParams;
                cp.ClassStyle = cp.ClassStyle | CS_NOCLOSE;

                return cp;
            }
        }

        private void ExportDialog_Load(object sender, EventArgs e)
        {
            this.comboBox_type.SelectedIndex = 0;
            this.comboBox_samplingRate.SelectedIndex = 7;
            this.radioButton3_CheckedChanged(null, null);
        }

        private void radioButton3_CheckedChanged(object sender, EventArgs e)
        {
            this.numericUpDown_min.Enabled = this.numericUpDown_sec.Enabled = this.label_min.Enabled = this.label_sec.Enabled = this.radioButton_time.Checked;
            this.numericUpDown_filesize.Enabled = this.label_mb.Enabled = this.radioButton_filesize.Checked;
        }

        private void trackBar1_ValueChanged(object sender, EventArgs e)
        {
            if (this.trackBar_oversampling.Value == 0)
                this.label_oversampling.Text = "x1 (無効)";
            else
                this.label_oversampling.Text = String.Format("x{0:f0}", Math.Pow(2, this.trackBar_oversampling.Value));
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (this.saveFileDialog.ShowDialog()
                == System.Windows.Forms.DialogResult.OK)
                this.textBox_saveto.Text = this.saveFileDialog.FileName;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            this.SetControlState(true);

            ExportData data = new ExportData();

            data.Bit = this.radioButton_16bit.Checked ? 2 : 1;
            data.SamplingRate = int.Parse(this.comboBox_samplingRate.Text.Substring(0, this.comboBox_samplingRate.Text.IndexOf(' ')));
            data.Oversampling = (int)Math.Pow(2, this.trackBar_oversampling.Value);

            data.FileSize = (this.radioButton_unlimit.Checked) ?
                (long)Int32.MaxValue :
                    (this.radioButton_time.Checked) ?
                    (long)((double)(this.numericUpDown_min.Value * 60m + this.numericUpDown_sec.Value) * data.SamplingRate * 2.0 * data.Bit) :
                    (long)(this.numericUpDown_filesize.Value * 1024.0m * 1024.0m);

            data.Connector = new SmfConnector(data.SamplingRate * data.Oversampling);
            {
                foreach (var presetFile in this.presetFiles)
                    data.Connector.AddPreset(presetFile);

                data.Connector.Load(this.inputFile);
                data.Connector.Sequencer.SequenceEnd += (s2, e2) => { data.SequenceEnded = true; data.Connector.Master.Release(); };

                this.masterControlDialog.ApplyToMaster(data.Connector.Master);
                this.masterControlDialog.ApplyToSequencer(data.Connector.Sequencer);
            }

            if (!this.CheckFileCreate(this.textBox_saveto.Text))
            {
                button4_Click(null, null);
                return;
            }

            this.reqEnd = false;

            Task.Factory.StartNew(() => this.ExportLoop(data), TaskCreationOptions.LongRunning);
            Task.Factory.StartNew(() => this.UpdateLabelText(data), TaskCreationOptions.LongRunning);
        }

        private void ExportLoop(ExportData data)
        {
            using (FileStream fs = new FileStream(this.textBox_saveto.Text, FileMode.Create))
            using (WaveFormatWriter wfw = new WaveFormatWriter(fs, data.SamplingRate, data.Bit * 8, 2))
            {
                const int bufferSize = 512;
                const int filterSize = 4096;

                float[] buffer = new float[bufferSize];
                double[] buffer_double = new double[filterSize];
                double[] bufferOut = new double[filterSize];

                SoundFilter filter = new SoundFilter(true, filterSize);
                var filterGenerator = new LowPassFilter()
                {
                    SamplingRate = data.SamplingRate * data.Oversampling,
                    CutoffFrequency = data.SamplingRate / 2 - ImpulseResponse.GetDelta(data.SamplingRate * data.Oversampling, filterSize)
                };
                double[] impulse = filterGenerator.Generate(filterSize / 2);

                Window.Hanning(impulse);
                filter.SetFilter(impulse);

                double bufferTime = (buffer.Length / 2.0) / (data.SamplingRate * data.Oversampling);

                var filterBuffer = new FilterBuffer<float>(filterSize, da =>
                {
                    if (data.Oversampling > 1)
                    {
                        for (int i = 0; i < filterSize; i++)
                            bufferOut[i] = da[i];

                        filter.Filtering(bufferOut);

                        for (int i = 0, j = 0; i < filterSize; i += data.Oversampling * 2)
                        {
                            buffer_double[j++] = bufferOut[i];
                            buffer_double[j++] = bufferOut[i + 1];
                        }

                        wfw.Write(buffer_double, 0, (int)Math.Min(filterSize / data.Oversampling, data.FileSize - data.Output));
                    }
                    else
                        wfw.Write(da, 0, filterSize);

                    data.Output = wfw.WrittenBytes;
                });

                while (!this.reqEnd && data.FileSize > data.Output)
                {
                    data.Connector.Sequencer.Progress(bufferTime);
                    data.Connector.Master.Read(buffer, 0, bufferSize);

                    filterBuffer.Push(buffer);

                    if (data.SequenceEnded && data.Connector.Master.ToneCount == 0)
                        this.reqEnd = true;
                }

                filterBuffer.Close();

                this.reqEnd = true;
                this.Invoke(new Action(() => button4_Click(null, null)));
            }
        }

        private void UpdateLabelText(ExportData data)
        {
            TimeSpan ts;

            while (!this.reqEnd)
            {
                ts = TimeSpan.FromSeconds(data.Output / 2.0 / data.Bit / (double)data.SamplingRate);

                this.Invoke(new Action(() =>
                {
                    long position = data.Connector.Sequencer.Tick;
                    this.label_progress.Text = String.Format("{0:p0}", (double)data.Output / (double)data.FileSize);
                    this.progressBar.Value = (int)((double)data.Output / (double)data.FileSize * 100);
                    this.label_filesize.Text = String.Format("出力: {0:f0} KB", data.Output / 1024.0);
                    this.label_tick.Text = String.Format("位置: {0}", position < 0 ? 0 : position);
                    this.label_time.Text = String.Format("時間: {0}:{1:d2}", (int)ts.TotalMinutes, ts.Seconds);
                }));

                Thread.Sleep(30);
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            this.reqEnd = true;

            this.SetControlState(false);
        }

        private bool CheckFileCreate(string filename)
        {
            try
            {
                using (FileStream fs = new FileStream(filename, FileMode.Create)) { }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void SetControlState(bool start)
        {
            this.groupBox_type.Enabled = this.groupBox_oversampling.Enabled = this.groupBox_samplingRate.Enabled
                    = this.groupBox_size.Enabled = this.label_saveto.Enabled = this.textBox_saveto.Enabled
                    = this.button_open.Enabled = this.button_close.Enabled = this.button_start.Enabled
                    = !start;

            this.button_stop.Enabled = start;
            this.progressBar.Value = start ? 0 : 100;
            this.label_progress.Text =  start ? "0%" : "100%";
        }

        class ExportData
        {
            public SmfConnector Connector { get; set; }
            public long Output { get; set; }
            public int SamplingRate { get; set; }
            public int Bit { get; set; }
            public long FileSize { get; set; }
            public int Oversampling { get; set; }
            public bool SequenceEnded { get; set; }
        }
    }
}
