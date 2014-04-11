﻿/* ux - Micro Xylph / Software Synthesizer Core Library

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
using System.Windows.Forms;
using ux;
using ux.Utils;
using ux.Utils.Midi.Sequencer;
using uxPlayer.Properties;

namespace uxPlayer
{
    partial class MasterControlDialog : Form
    {
        #region -- Private Fields --
        private Master master = null;
        private Sequencer sequencer = null;
        #endregion

        #region -- Public Properties --
        public Master Master
        {
            get
            {
                return this.master;
            }
            set
            {
                this.master = value;
                this.ApplyToMaster(value);
            }
        }

        public Sequencer Sequencer
        {
            get
            {
                return this.sequencer;
            }
            set
            {
                this.sequencer = value;
                this.ApplyToSequencer(value);
            }
        }
        #endregion

        #region -- Constructor --
        public MasterControlDialog()
        {
            InitializeComponent();

            this.trackBar_mastervolume.Value = (int)Math.Round(Settings.Default.SynthMasterVolume.Clamp(2f, 0f) * 100.0);
            this.trackBar_compressor_ratio.Value = (int)Math.Round(Settings.Default.SynthCompressorRatio.Clamp(10f, 1f) * 100.0);
            this.trackBar_compressor_threshold.Value = (int)Math.Round(Settings.Default.SynthCompressorThreshold.Clamp(1f, 0f) * 100.0);
            this.trackBar_tempo.Value = (int)Math.Round(Settings.Default.SequenceTempoFactor.Clamp(2f, 0.5f) * 100.0);
        }
        #endregion

        #region -- Public Methods --
        public void ApplyToMaster(Master master)
        {
            if (master != null)
            {
                master.MasterVolume = this.trackBar_mastervolume.Value / 100.0f;
                master.Ratio = this.trackBar_compressor_ratio.Value / 100.0f;
                master.Threshold = this.trackBar_compressor_threshold.Value / 100.0f;
            }
        }

        public void ApplyToSequencer(Sequencer sequencer)
        {
            if (sequencer != null)
                sequencer.TempoFactor = this.trackBar_tempo.Value / 100.0f;
        }
        #endregion

        #region -- Private Methods --
        private void MasterControlDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            Settings.Default.SynthMasterVolume = this.trackBar_mastervolume.Value / 100.0f;
            Settings.Default.SynthCompressorRatio = this.trackBar_compressor_ratio.Value / 100.0f;
            Settings.Default.SynthCompressorThreshold = this.trackBar_compressor_threshold.Value / 100.0f;
            Settings.Default.SequenceTempoFactor = this.trackBar_tempo.Value / 100.0f;

            Settings.Default.Save();

            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Visible = false;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.trackBar_mastervolume.Value = 100;
            this.trackBar_compressor_ratio.Value = 200;
            this.trackBar_compressor_threshold.Value = 80;
            this.trackBar_tempo.Value = 100;
        }

        private void trackBar1_ValueChanged(object sender, EventArgs e)
        {
            this.ApplyToMaster(this.master);
            this.label_mastervolume.Text = String.Format("{0:p0}", this.trackBar_mastervolume.Value / 100.0f);
        }

        private void trackBar2_ValueChanged(object sender, EventArgs e)
        {
            this.ApplyToMaster(this.master);
            this.label_compressor_ratio.Text = String.Format("1:{0:f2}", this.trackBar_compressor_ratio.Value / 100.0f);
        }

        private void trackBar3_ValueChanged(object sender, EventArgs e)
        {
            this.ApplyToMaster(this.master);
            this.label_compressor_threshold.Text = String.Format("{0:f2}", this.trackBar_compressor_threshold.Value / 100.0f);
        }

        private void trackBar4_ValueChanged(object sender, EventArgs e)
        {
            this.ApplyToSequencer(this.sequencer);
            this.label_tempo.Text = String.Format("{0:p0}", this.trackBar_tempo.Value / 100.0f);
        }
        #endregion
    }
}
