/*
 * BSD 3-Clause License
 * 
 * Copyright (c) 2021, Sergey Parshin
 * All rights reserved.
 * 
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 * 
 * 1. Redistributions of source code must retain the above copyright notice, this
 *    list of conditions and the following disclaimer.
 * 
 * 2. Redistributions in binary form must reproduce the above copyright notice,
 *    this list of conditions and the following disclaimer in the documentation
 *    and/or other materials provided with the distribution.
 * 
 * 3. Neither the name of the copyright holder nor the names of its
 *    contributors may be used to endorse or promote products derived from
 *    this software without specific prior written permission.
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
 * FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
 * DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
 * SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 * CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
 * OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
 * OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 * 
 */
 
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PingMonitor
{
    public struct PingSummaryEntry
    {
        public bool Success;
        public uint RoundTripMillis;
        public uint TimeToLive;
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const int NUM_HISTORY_ENTRIES = 256;

        const int MAX_Y = 350;
        const int X_STEP = 3;
        const int MARGIN_LEFT = 10;
        const int PING_TIMEOUT = 3000;

        const double LOG_SCALE = 95.0;

        const uint MAX_GREEN_PING = 50;

        private object dataLock = new object();
        private PingSummaryEntry[] data = new PingSummaryEntry[NUM_HISTORY_ENTRIES];
        private int w_idx = 0;
        private int numSent = 0;
        private int numReceived = 0;
        private uint minTime = uint.MaxValue;
        private uint maxTime = 0;
        private ulong avgAcc = 0;

        private string destinationHost = "";
        private string logFile = "";

        private SolidColorBrush brushRed = new SolidColorBrush(new Color { R = 230, G = 64, B = 64, A = 220 });
        private SolidColorBrush brushGreen = new SolidColorBrush(new Color { R = 64, G = 192, B = 64, A = 220 });
        private SolidColorBrush brushBlack = new SolidColorBrush(new Color { R = 0, G = 0, B = 0, A = 255 });
        private SolidColorBrush brushBlue = new SolidColorBrush(new Color { R = 32, G = 32, B = 200, A = 190 });

        private Line[] dataViewRed = new Line[NUM_HISTORY_ENTRIES];
        private Line[] dataViewGreen = new Line[NUM_HISTORY_ENTRIES];
        private Line[] dataViewBlack= new Line[NUM_HISTORY_ENTRIES];

        private double[] grid_lines = new double[] { 1, 10, 100, 1000, 10000 };

        //private TextBlock[] gridLabels = new TextBlock[NUM_HOR_GRID_LINES];


        public MainWindow()
        {
            InitializeComponent();
            DrawGrid();
            CreateLines();
        }
        private static double ToLogScale(double val) => (LOG_SCALE * Math.Log10(Math.Max(val, 1)));

        private void DrawGrid()
        {
            double? prev_grid_pos = null;

            double x_left = MARGIN_LEFT;
            double x_right = MARGIN_LEFT + NUM_HISTORY_ENTRIES * X_STEP;

            foreach (var grid_pos in grid_lines)
            {
                if (prev_grid_pos.HasValue)
                {
                    for (double sub_grid_pos = prev_grid_pos.Value; sub_grid_pos <= grid_pos; sub_grid_pos += prev_grid_pos.Value)
                    {
                        var sub_y = MAX_Y - ToLogScale(sub_grid_pos);

                        var sub_line = new Line
                        {
                            Stroke = brushBlue,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top,
                            StrokeThickness = 0.3,
                            X1 = x_left,
                            Y1 = sub_y,
                            X2 = x_right,
                            Y2 = sub_y
                        };
                        grid.Children.Add(sub_line);
                    }
                }

                var y = MAX_Y - ToLogScale(grid_pos);

                var line = new Line
                {
                    Stroke = brushBlack,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    StrokeThickness = 0.3,
                    X1 = x_left,
                    Y1 = y,
                    X2 = x_right,
                    Y2 = y
                };
                grid.Children.Add(line);

                prev_grid_pos = grid_pos;
            }
        }

        private void CreateLines()
        {
            for (int i = 0; i < NUM_HISTORY_ENTRIES; ++i)
            {
                data[i] = new PingSummaryEntry { Success = false, RoundTripMillis = 0, TimeToLive = 0 };

                dataViewRed[i] =
                    new Line
                    {
                        Stroke = brushRed,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        StrokeThickness = (float)X_STEP,
                        X1 = MARGIN_LEFT + i * X_STEP,
                        Y1 = MAX_Y,
                        X2 = MARGIN_LEFT + i * X_STEP,
                        Y2 = MAX_Y
                    };

                dataViewGreen[i] =
                    new Line
                    {
                        Stroke = brushGreen,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        StrokeThickness = (float)X_STEP,
                        X1 = MARGIN_LEFT + i * X_STEP,
                        Y1 = MAX_Y,
                        X2 = MARGIN_LEFT + i * X_STEP,
                        Y2 = MAX_Y
                    };

                dataViewBlack[i] =
                    new Line
                    {
                        Stroke = brushBlack,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        StrokeThickness = (float)X_STEP,
                        X1 = MARGIN_LEFT + i * X_STEP,
                        Y1 = MAX_Y,
                        X2 = MARGIN_LEFT + i * X_STEP,
                        Y2 = MAX_Y
                    };


                grid.Children.Add(dataViewRed[i]);
                grid.Children.Add(dataViewGreen[i]);
                grid.Children.Add(dataViewBlack[i]);
            }
        }

        private void hideAllAt(int idx)
        {
            dataViewRed[idx].Y1 = MAX_Y;
            dataViewGreen[idx].Y1 = MAX_Y;
            dataViewBlack[idx].Y1 = MAX_Y;
        }

        private void setAt(int idx, bool success, uint timeMillis)
        {
            bool isGreen = success && timeMillis <= MAX_GREEN_PING;
            bool isRed = success && timeMillis > MAX_GREEN_PING;

            hideAllAt(idx);

            int log_rtt_time = (int)ToLogScale(timeMillis);

            if (isGreen)
            {
                dataViewGreen[idx].Y1 = Math.Max(MAX_Y - log_rtt_time, 0);
            }
            else if (isRed)
            {
                dataViewRed[idx].Y1 = Math.Max(MAX_Y - log_rtt_time, 0);
            }
            else
            {
                dataViewBlack[idx].Y1 = 0;
            }
        }

        private void buttonRun_Click(object sender, RoutedEventArgs e)
        {
            buttonRun.Visibility = Visibility.Collapsed;
            hostName.IsEnabled = false;

            destinationHost = hostName.Text;

            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folder = $"{documents}\\pingLog";
            Directory.CreateDirectory(folder);
            logFile = $"{folder}\\{DateTime.Now:yyyyMMdd-HHmmss}-{destinationHost}.csv";
            File.AppendAllText(logFile, "Date,Time,Host,Success,PingTime,Ttl\n");

            new Thread(this.PingThread).Start();

            this.Title = $"Ping {hostName.Text}, timeout {PING_TIMEOUT}";
            hostName.Visibility = Visibility.Hidden;
        }

        private void UpdateUI()
        {
            lock (dataLock)
            {
                if (w_idx < NUM_HISTORY_ENTRIES)
                {
                    for (int i = 0; i < w_idx; ++i)
                    {
                        setAt(i, data[i].Success, data[i].RoundTripMillis);
                    }
                }
                else
                {
                    for (int i = 0; i < NUM_HISTORY_ENTRIES; ++i)
                    {
                        int dataPos = (w_idx + i) % NUM_HISTORY_ENTRIES;
                        setAt(i, data[dataPos].Success, data[dataPos].RoundTripMillis);
                    }
                }

                float pctLost = 100.0f * (float)(numSent - numReceived) / (float)numSent;
                ulong avg = (numReceived > 0) ? (avgAcc / (ulong)numReceived) : 0;
                string statText = $"{numSent} packets sent, {numReceived} received ({pctLost:F03}% lost)\nMin={minTime}ms, Max={maxTime}ms, avg={avg}ms\nLog file {logFile}";
                stats.Content = statText;
            }
        }

        private void PingServer()
        {
            bool success = false;
            PingReply reply = null;

            try
            {
                Ping pingSender = new Ping();
                PingOptions options = new PingOptions();

                // Use the default Ttl value which is 128,
                // but change the fragmentation behavior.
                options.DontFragment = true;

                // Create a buffer of 32 bytes of data to be transmitted.
                string pingData = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
                byte[] buffer = Encoding.ASCII.GetBytes(pingData);
                reply = pingSender.Send(destinationHost, PING_TIMEOUT, buffer, options);

                success = reply.Status == IPStatus.Success;
            }
            catch (Exception)
            {
            }

            var res = new PingSummaryEntry
            {
                Success = success,
                RoundTripMillis = (uint)(reply?.RoundtripTime ?? 0),
                TimeToLive = (uint)(reply?.Options?.Ttl ?? 0),
            };

            lock (dataLock)
            {
                File.AppendAllText(logFile, 
                    $"{DateTime.Now:yyyy-MM-dd,HH:mm:ss}," +
                    $"{destinationHost}," +
                    $"{res.Success}," +
                    $"{reply?.RoundtripTime??10000000}," +
                    $"{reply?.Options?.Ttl ?? 0}\n"
                    );
                data[w_idx % NUM_HISTORY_ENTRIES] = res;
                w_idx++;

                numSent++;
                if (success)
                {
                    numReceived++;
                    minTime = Math.Min(minTime, (uint)reply.RoundtripTime);
                    maxTime = Math.Max(maxTime, (uint)reply.RoundtripTime);
                    avgAcc += (ulong)reply.RoundtripTime;
                }    
            }
        }

        private void PingThread()
        {
            for(; ;)
            {
                try
                {
                    PingServer();
                    Dispatcher.Invoke(() => UpdateUI());
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                Thread.Sleep(1000);
            }
        }
    }
}
