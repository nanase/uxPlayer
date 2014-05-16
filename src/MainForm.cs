﻿/* uxPlayer / Software Synthesizer

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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using ALSharp;
using MidiUtils.IO;
using ux.Utils.Midi;
using uxPlayer.Properties;

namespace uxPlayer
{
    public partial class MainForm : Form
    {
        #region -- Private Fields --
        private SinglePlayer player;
        private MidiConnector connector;
        private const int frequencty = 44100;
        private bool mode_smf;
        private bool playing;
        private long eventCountOld;
        private long resumeTick = 0L;

        private float[] monitorBuffer = null;
        private MonitorBase monitor;
        private MonitorBase volumeMonitor;
        MasterControlDialog masterc;

        private readonly Encoding sjis = Encoding.GetEncoding(932);
        #endregion

        #region -- Constructors --
        public MainForm()
        {
            InitializeComponent();

            this.masterc = new MasterControlDialog();

            this.SwitchConnection();
        }
        #endregion

        #region -- Private Methods --
        private void Play()
        {
            if (this.playing)
                return;

            this.playing = true;

            if (this.player.AudioSource != null)
                this.player.AudioSource.Clear();

            if (this.player.BasePlayer != null && this.player.BasePlayer.Playing)
            {
                this.PlayFromFirst();
                return;
            }
            else
            {
                this.player.Play();
                this.connector.Play();
            }

            if (this.mode_smf && ((SmfConnector)this.connector).Sequencer != null)
            {
                SmfConnector smfConnector = (SmfConnector)this.connector;
                this.masterc.Sequencer = smfConnector.Sequencer;
                smfConnector.Sequencer.Tick = this.resumeTick;
            }

            Action invoke = () =>
            {
                this.toolStrip_play.Enabled = false;
                this.toolStrip_stop.Enabled = true;
                this.menu_play.Enabled = false;
                this.menu_stop.Enabled = true;

                this.label_static_play.Text = "PLAY";
            };

            if (this.InvokeRequired)
                this.Invoke(invoke);
            else
                invoke();
        }

        private void Stop(bool stopConnector = true)
        {
            if (!this.playing)
                return;

            this.playing = false;

            if (stopConnector)
            {
                if (this.mode_smf)
                {
                    if (((SmfConnector)this.connector).Sequencer != null)
                    {
                        SmfConnector smfConnector = (SmfConnector)this.connector;
                        this.masterc.Sequencer = smfConnector.Sequencer;
                        this.resumeTick = smfConnector.Sequencer.Tick;
                    }

                    if (this.resumeTick < 0L)
                        this.resumeTick = 0L;
                }

                this.player.Stop();
                this.connector.Stop();

                if (this.player.AudioSource != null)
                    this.player.AudioSource.Clear();
            }
            else
            {
                if (this.mode_smf)
                    this.resumeTick = 0L;

                this.connector.Stop();
            }

            this.connector.Master.Release();

            Action invoke = () =>
            {
                this.toolStrip_play.Enabled = true;
                this.toolStrip_stop.Enabled = false;
                this.menu_play.Enabled = true;
                this.menu_stop.Enabled = false;

                this.label_static_play.Text = "STOP";
            };

            if (this.InvokeRequired)
                this.Invoke(invoke);
            else
                invoke();
        }

        private void SwitchConnection()
        {
            this.Stop();

            this.mode_smf = !this.mode_smf;

            if (!this.mode_smf && Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                MessageBox.Show("MIDI-IN への接続は Windows のみ対応しています。",
                                null,
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                this.mode_smf = true;
                return;
            }

            if (this.connector != null)
                this.connector.Dispose();

            if (this.mode_smf)
            {
                this.connector = new SmfConnector(frequencty);
                this.label_static_connect.Text = "SMF";
            }
            else
            {
                this.connector = new MidiInConnector(frequencty, 0);
                this.label_static_connect.Text = "MIDI-IN";
                this.masterc.Sequencer = null;
            }

            this.masterc.Master = this.connector.Master;

            #region Control
            this.label_static_title.BackColor = this.label_static_resolution.BackColor =
                this.label_static_tick.BackColor = this.label_static_tempo.BackColor
                    = (this.mode_smf) ? Color.FromArgb(64, 64, 64) : Color.FromArgb(139, 229, 139);

            this.toolStrip_connect.Checked = this.menu_connect.Checked = !this.mode_smf;
            this.hScrollBar.Enabled = this.toolStrip_open.Enabled =
                                      this.menu_open.Enabled =
                                      this.menu_playFirst.Enabled =
                                      this.toolStrip_playFirst.Enabled = this.mode_smf;

            if (this.mode_smf)
            {
                this.label_sep2.Text = "/";
                this.toolStrip_connect.Image = this.menu_connect.Image = Properties.Resources.plug_disconnect;
            }
            else
            {
                this.label_title.Text = this.label_tick_left.Text = this.label_tick_right.Text =
                    this.label_tempo.Text = this.label_resolution.Text = "";
                this.label_sep2.Text = "";
                this.toolStrip_connect.Image = this.menu_connect.Image = Properties.Resources.plug_connect;
            }
            #endregion

            foreach (string item in Settings.Default.PlayerPresets)
                this.connector.AddPreset(item);
        }

        private void OpenSmfFile()
        {
            if (!this.mode_smf)
                return;

            if (this.smfFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (this.playing)
                {
                    this.connector.Stop();
                    this.connector.Reset();
                }

                SmfConnector smf = (SmfConnector)this.connector;
                smf.Load(this.smfFileDialog.FileName);
                smf.Sequencer.SequenceEnd += (sender, e) =>
                {
                    this.Stop(false);
                    this.Invoke(new Action(() => this.hScrollBar.Value = 0));
                };
                smf.Sequencer.OnTrackEvent += (sender, e) =>
                {
                    foreach (MetaEvent meta in e.Events.OfType<MetaEvent>().Where(m => m.MetaType == MetaType.Lyrics))
                    {
                        this.Invoke(new Action(() => this.label_title.Text = this.sjis.GetString(meta.Data).Trim()));
                    }
                };

                this.masterc.Sequencer = smf.Sequencer;
                this.label_resolution.Text = smf.Sequence.Resolution.ToString();
                this.label_tick_right.Text = smf.Sequence.MaxTick.ToString();
                this.hScrollBar.SmallChange = smf.Sequence.Resolution;
                this.hScrollBar.LargeChange = smf.Sequence.Resolution * 4;

                MetaEvent title = smf.Sequence.Tracks.SelectMany(t => t.Events)
                                                     .OfType<MetaEvent>()
                                                     .Where(e => e.MetaType == MetaType.TrackName)
                                                     .FirstOrDefault();

                if (title != null)
                    this.label_title.Text = this.sjis.GetString(title.Data).Trim();

                this.hScrollBar.Maximum = (int)smf.Sequence.MaxTick;

                if (this.playing)
                {
                    this.connector.Play();
                }
            }
        }

        private void RefreshPresets()
        {
            this.connector.ReloadPreset();
        }

        private void ShowVersionInfo()
        {
            Assembly current = Assembly.GetExecutingAssembly();
            MessageBox.Show("uxPlayer " + current.GetName().Version.ToString());
        }

        private void AllNoteOff()
        {
            this.connector.Master.Silence();
        }

        private void AllReset()
        {
            this.connector.Master.Reset();
            this.connector.Reset();
        }

        private void PlayFromFirst()
        {
            if (!this.mode_smf)
                return;

            this.Stop();

            if (((SmfConnector)this.connector).Sequencer != null)
                ((SmfConnector)this.connector).Sequencer.Tick = 0L;

            this.resumeTick = 0L;

            this.RefreshPresets();
            this.AllReset();
            this.Play();
        }

        #region Controls
        private void MainForm_Load(object sender, EventArgs e)
        {
            this.monitorBuffer = new float[2048];
            this.monitor = new WaveformMonitor(Color.FromArgb(139, 229, 139), this.monitorBox.Size);
            this.monitorBox.Image = this.monitor.Bitmap;

            this.volumeMonitor = new VolumeMonitor(Color.FromArgb(139, 229, 139), this.volumeMonitorBox.Size);
            this.volumeMonitorBox.Image = this.volumeMonitor.Bitmap;

            Func<float[], int, int, int> process = (buffer, offset, count) =>
            {
                int k = connector.Master.Read(buffer, offset, count);

                if (this.monitorBuffer.Length != buffer.Length)
                    this.monitorBuffer = new float[buffer.Length];

                buffer.CopyTo(this.monitorBuffer, 0);

                return k;
            };

            var setting = new PlayerSettings()
            {
                BufferSize = 1024 * 2,
                BufferCount = 16,
                BitPerSample = 16,
                SamplingFrequency = frequencty
            };

            this.player = new SinglePlayer(process, setting);

            this.playing = true;
            this.Stop();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.Stop();

            this.connector.Dispose();
            this.player.Dispose();
        }

        private void hScrollBar_Scroll(object sender, ScrollEventArgs e)
        {
            if (this.mode_smf && this.playing)
            {
                SmfConnector smf = (SmfConnector)this.connector;

                if (smf.Sequencer != null)
                {
                    smf.Sequencer.Tick = this.hScrollBar.Value;
                    this.connector.Master.Silence();
                }
            }
        }

        #region Monitor
        private void monitorTimer_Tick(object sender, EventArgs e)
        {
            if (!this.playing)
                return;

            if (this.monitor != null)
            {
                this.monitor.Draw(this.monitorBuffer);
                this.monitorBox.Refresh();
            }

            if (this.volumeMonitor != null)
            {
                this.volumeMonitor.Draw(this.monitorBuffer);
                this.volumeMonitorBox.Refresh();
            }
        }

        private void menu_noMonitor_Click(object sender, EventArgs e)
        {
            if (this.monitor != null)
                this.monitor.Dispose();

            this.monitor = null;
            this.monitorBox.Image = null;
        }

        private void menu_waveform_Click(object sender, EventArgs e)
        {
            if (this.monitor != null)
                this.monitor.Dispose();

            this.monitor = new WaveformMonitor(this.monitorBox.BackColor, this.monitorBox.Size);
            this.monitorBox.Image = this.monitor.Bitmap;
        }

        private void menu_spectrum_Click(object sender, EventArgs e)
        {
            if (this.monitor != null)
                this.monitor.Dispose();

            this.monitor = new FrequencyMonitor(this.monitorBox.BackColor, this.monitorBox.Size);
            this.monitorBox.Image = this.monitor.Bitmap;
        }

        private void menu_historyClick(object sender, EventArgs e)
        {
            if (this.monitor != null)
                this.monitor.Dispose();

            this.monitor = new FrequencySpectrumMonitor(this.monitorBox.BackColor, this.monitorBox.Size);
            this.monitorBox.Image = this.monitor.Bitmap;
        }
        #endregion

        #region ToolStrip
        private void toolStrip_play_Click(object sender, EventArgs e)
        {
            this.Play();
        }

        private void toolStrip_stop_Click(object sender, EventArgs e)
        {
            this.Stop();
        }

        private void toolStrip_connect_Click(object sender, EventArgs e)
        {
            this.SwitchConnection();
        }

        private void toolStrip_open_Click(object sender, EventArgs e)
        {
            this.OpenSmfFile();
        }

        private void toolStrip_allNoteOff_Click(object sender, EventArgs e)
        {
            this.AllNoteOff();
        }

        private void toolStrip_allReset_Click(object sender, EventArgs e)
        {
            this.AllReset();
        }

        private void toolStrip_refresh_Click(object sender, EventArgs e)
        {
            this.RefreshPresets();
        }

        private void toolStrip_playFirst_Click(object sender, EventArgs e)
        {
            this.PlayFromFirst();
        }
        #endregion

        #region Timer
        private void displayTimer_Tick(object sender, EventArgs e)
        {
            long passedEvent = this.connector.EventCount - this.eventCountOld;

            if (passedEvent < 0L)
                passedEvent = 0L;

            this.label_message.Text = passedEvent.ToString();
            this.label_tone_left.Text = this.connector.Master.ToneCount.ToString();
            this.label_tone_right.Text = this.connector.Master.PartCount.ToString();

            this.eventCountOld = this.connector.EventCount;
        }

        private void fastTimer_Tick(object sender, EventArgs e)
        {
            if (this.mode_smf)
            {
                SmfConnector smf = (SmfConnector)this.connector;

                if (smf.Sequence != null && smf.Sequencer != null)
                {
                    long tmp = smf.Sequencer.Tick;

                    if (tmp > smf.Sequence.MaxTick)
                        tmp = smf.Sequence.MaxTick;
                    else if (tmp < 0)
                        tmp = 0;

                    this.label_tick_left.Text = tmp.ToString();
                }
            }
        }

        private void slowTimer_Tick(object sender, EventArgs e)
        {
            if (this.mode_smf)
            {
                SmfConnector smf = (SmfConnector)this.connector;

                if (smf.Sequence != null && smf.Sequencer != null)
                {
                    if (smf.Sequencer.Tick >= 0 && this.hScrollBar.Maximum >= smf.Sequencer.Tick)
                        this.hScrollBar.Value = (int)smf.Sequencer.Tick;

                    this.label_tempo.Text = smf.Sequencer.Tempo.ToString("f1");
                }
            }
        }
        #endregion

        #region Menu
        private void menu_exit_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void menu_open_Click(object sender, EventArgs e)
        {
            this.OpenSmfFile();
        }

        private void menu_play_Click(object sender, EventArgs e)
        {
            this.Play();
        }

        private void menu_stop_Click(object sender, EventArgs e)
        {
            this.Stop();
        }

        private void menu_allNoteOff_Click(object sender, EventArgs e)
        {
            this.AllNoteOff();
        }

        private void menu_allReset_Click(object sender, EventArgs e)
        {
            this.AllReset();
        }

        private void menu_connect_Click(object sender, EventArgs e)
        {
            this.SwitchConnection();
        }

        private void menu_refresh_Click(object sender, EventArgs e)
        {
            this.RefreshPresets();
        }

        private void menu_versionInfo_Click(object sender, EventArgs e)
        {
            this.ShowVersionInfo();
        }

        private void menu_playFirst_Click(object sender, EventArgs e)
        {
            this.PlayFromFirst();
        }

        private void menu_export_Click(object sender, EventArgs e)
        {
            if (!this.mode_smf)
            {
                MessageBox.Show("出力は SMF モードでのみ使用できます。",
                                "",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                return;
            }

            if (!File.Exists(this.smfFileDialog.FileName))
            {
                MessageBox.Show("SMF ファイルが開かれていません。",
                                "",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                return;
            }

            ExportDialog ed = new ExportDialog(this.smfFileDialog.FileName,
                                               Settings.Default.PlayerPresets.Cast<string>(),
                                               this.masterc);

            ed.ShowDialog();
        }

        private void menu_masterControl_Click(object sender, EventArgs e)
        {
            this.masterc.Visible = true;
        }

        private void menu_preset_Click(object sender, EventArgs e)
        {
            using (PresetManageDialog pmd = new PresetManageDialog(Settings.Default.PlayerPresets.Cast<string>()))
                if (pmd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    Settings.Default.PlayerPresets.Clear();
                    Settings.Default.PlayerPresets.AddRange(pmd.FileNames.ToArray());

                    this.RefreshPresets();
                }
        }
        #endregion
        #endregion
        #endregion
    }
}
