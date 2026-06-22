using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace User.OXRMCBridge
{
    public class SettingsControl : UserControl
    {
        private OXRMCBridgePlugin _plugin;
        private TextBlock _modeText;
        private TextBlock _sensorText;
        private TextBlock _rollText;
        private TextBlock _pitchText;
        private TextBlock _rollGainText;
        private TextBlock _pitchGainText;
        private TextBlock _blendText;
        private TextBlock _telRollText;
        private TextBlock _telPitchText;
        private TextBlock _overrideText;
        private TextBlock _modeDescText;
        private TextBlock _maxPitchText;
        private TextBlock _maxRollText;
        private TextBlock _mountText;
        private TextBlock _yawText;
        private TextBox _overridePitchBox;
        private TextBox _overrideRollBox;
        private TextBox _rigLengthBox;
        private TextBox _rigWidthBox;
        private TextBox _strokeBox;
        private TextBlock _postConfigText;
        private Border _diagramContainer;
        private int _lastPostConfig = -1;
        private System.Windows.Threading.DispatcherTimer _timer;

        public SettingsControl(OXRMCBridgePlugin plugin)
        {
            _plugin = plugin;
            BuildUI();

            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += (s, e) => UpdateDisplay();
            _timer.Start();

            this.Unloaded += (s, e) => _timer.Stop();
        }

        private void BuildUI()
        {
            var mainStack = new StackPanel();
            mainStack.Margin = new Thickness(10);

            // Title
            var title = new TextBlock();
            title.Text = "OXRMC Bridge — VR Motion Compensation";
            title.FontSize = 18;
            title.FontWeight = FontWeights.Bold;
            title.Foreground = Brushes.White;
            title.Margin = new Thickness(0, 0, 0, 5);
            mainStack.Children.Add(title);

            var subtitle = new TextBlock();
            subtitle.Text = "Controller-free VR motion compensation for any motion rig. Uses WitMotion sensor, game telemetry, or both blended.";
            subtitle.Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160));
            subtitle.TextWrapping = TextWrapping.Wrap;
            subtitle.Margin = new Thickness(0, 0, 0, 15);
            mainStack.Children.Add(subtitle);

            // Status section
            mainStack.Children.Add(CreateSectionHeader("Status"));

            // Mode selector with description
            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            _overrideText = CreateValueText("AUTO");
            _overrideText.FontSize = 15;
            _overrideText.FontWeight = FontWeights.Bold;
            _overrideText.MinWidth = 100;
            modeRow.Children.Add(_overrideText);
            modeRow.Children.Add(CreateButton("Change", (s, e) => _plugin.CycleMode()));
            mainStack.Children.Add(modeRow);

            _modeDescText = new TextBlock();
            _modeDescText.Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160));
            _modeDescText.TextWrapping = TextWrapping.Wrap;
            _modeDescText.FontSize = 11;
            _modeDescText.Margin = new Thickness(0, 0, 0, 8);
            mainStack.Children.Add(_modeDescText);

            // Sensor status
            _modeText = CreateValueText("--");
            var sensorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            sensorRow.Children.Add(new TextBlock { Text = "Sensor: ", Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)), VerticalAlignment = VerticalAlignment.Center });
            _sensorText = CreateValueText("--");
            sensorRow.Children.Add(_sensorText);
            mainStack.Children.Add(sensorRow);

            // Live values
            mainStack.Children.Add(CreateSectionHeader("Live Values"));
            var valGrid = new Grid();
            valGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            valGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            valGrid.RowDefinitions.Add(new RowDefinition());
            valGrid.RowDefinitions.Add(new RowDefinition());

            _rollText = CreateValueText("0.00°");
            AddGridRow(valGrid, 0, "Output Roll:", _rollText);
            _pitchText = CreateValueText("0.00°");
            AddGridRow(valGrid, 1, "Output Pitch:", _pitchText);
            valGrid.RowDefinitions.Add(new RowDefinition());
            valGrid.RowDefinitions.Add(new RowDefinition());
            _telRollText = CreateValueText("0.00°");
            AddGridRow(valGrid, 2, "Telemetry Roll:", _telRollText);
            _telPitchText = CreateValueText("0.00°");
            AddGridRow(valGrid, 3, "Telemetry Pitch:", _telPitchText);
            mainStack.Children.Add(WrapInBorder(valGrid));

            // Telemetry tuning
            mainStack.Children.Add(CreateSectionHeader("Telemetry Tuning"));
            var tuneDesc = new TextBlock();
            tuneDesc.Text = "How strongly the VR view reacts to in-game forces. Increase if compensation feels too weak, decrease if it feels too strong. Used in Telemetry and Blended modes.";
            tuneDesc.Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140));
            tuneDesc.TextWrapping = TextWrapping.Wrap;
            tuneDesc.FontSize = 11;
            tuneDesc.Margin = new Thickness(0, 0, 0, 8);
            mainStack.Children.Add(tuneDesc);

            var gainGrid = new Grid();
            gainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            gainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gainGrid.RowDefinitions.Add(new RowDefinition());
            gainGrid.RowDefinitions.Add(new RowDefinition());
            gainGrid.RowDefinitions.Add(new RowDefinition());

            _rollGainText = CreateValueText("0.040");
            var rollGainPanel = new StackPanel { Orientation = Orientation.Horizontal };
            rollGainPanel.Children.Add(_rollGainText);
            rollGainPanel.Children.Add(CreateButton(" - ", (s, e) => _plugin.AdjustRollGain(-0.005)));
            rollGainPanel.Children.Add(CreateButton(" + ", (s, e) => _plugin.AdjustRollGain(0.005)));
            AddGridRow(gainGrid, 0, "Roll strength:", rollGainPanel);

            _pitchGainText = CreateValueText("0.040");
            var pitchGainPanel = new StackPanel { Orientation = Orientation.Horizontal };
            pitchGainPanel.Children.Add(_pitchGainText);
            pitchGainPanel.Children.Add(CreateButton(" - ", (s, e) => _plugin.AdjustPitchGain(-0.005)));
            pitchGainPanel.Children.Add(CreateButton(" + ", (s, e) => _plugin.AdjustPitchGain(0.005)));
            AddGridRow(gainGrid, 1, "Pitch strength:", pitchGainPanel);

            var invertPanel = new StackPanel { Orientation = Orientation.Horizontal };
            invertPanel.Children.Add(CreateButton("Invert Roll", (s, e) => _plugin.ToggleInvertRoll()));
            invertPanel.Children.Add(CreateButton("Invert Pitch", (s, e) => _plugin.ToggleInvertPitch()));
            AddGridRow(gainGrid, 2, "Flip direction:", invertPanel);
            mainStack.Children.Add(WrapInBorder(gainGrid));

            // Blend controls
            mainStack.Children.Add(CreateSectionHeader("Blend (sensor + telemetry)"));
            var blendInfo = new TextBlock();
            blendInfo.Text = "When both sensor and game are active, blends both sources. 0% = pure telemetry, 100% = pure sensor.";
            blendInfo.Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160));
            blendInfo.TextWrapping = TextWrapping.Wrap;
            blendInfo.Margin = new Thickness(0, 0, 0, 5);
            mainStack.Children.Add(blendInfo);

            var blendPanel = new StackPanel { Orientation = Orientation.Horizontal };
            blendPanel.Margin = new Thickness(0, 0, 0, 10);
            _blendText = CreateValueText("80%");
            blendPanel.Children.Add(new TextBlock { Text = "Sensor weight: ", Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            blendPanel.Children.Add(_blendText);
            blendPanel.Children.Add(CreateButton(" - ", (s, e) => _plugin.AdjustBlendAlpha(-0.05)));
            blendPanel.Children.Add(CreateButton(" + ", (s, e) => _plugin.AdjustBlendAlpha(0.05)));
            mainStack.Children.Add(blendPanel);

            // Sensor controls
            mainStack.Children.Add(CreateSectionHeader("Sensor"));

            var sensorHelp = new TextBlock();
            sensorHelp.Text = "Mount the sensor any way you like. Set the mounting mode and nudge the yaw alignment until Output Roll/Pitch move the right way when you tilt the rig, then press Calibrate Sensor with the rig level. Invert Roll/Pitch (above) also apply to the sensor.";
            sensorHelp.Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140));
            sensorHelp.TextWrapping = TextWrapping.Wrap;
            sensorHelp.FontSize = 11;
            sensorHelp.Margin = new Thickness(0, 0, 0, 8);
            mainStack.Children.Add(sensorHelp);

            // Mounting orientation: coarse mode + fine yaw alignment
            var mountGrid = new Grid();
            mountGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            mountGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mountGrid.RowDefinitions.Add(new RowDefinition());
            mountGrid.RowDefinitions.Add(new RowDefinition());

            _mountText = CreateValueText("Standard (0°)");
            var mountPanel = new StackPanel { Orientation = Orientation.Horizontal };
            mountPanel.Children.Add(_mountText);
            mountPanel.Children.Add(CreateButton("Change", (s, e) => _plugin.CycleMountMode()));
            AddGridRow(mountGrid, 0, "Mounting mode:", mountPanel);

            _yawText = CreateValueText("0°");
            var yawPanel = new StackPanel { Orientation = Orientation.Horizontal };
            yawPanel.Children.Add(_yawText);
            yawPanel.Children.Add(CreateButton(" - ", (s, e) => _plugin.AdjustSensorYawOffset(-5)));
            yawPanel.Children.Add(CreateButton(" + ", (s, e) => _plugin.AdjustSensorYawOffset(5)));
            AddGridRow(mountGrid, 1, "Yaw align:", yawPanel);
            mainStack.Children.Add(WrapInBorder(mountGrid));

            var sensorPanel = new StackPanel { Orientation = Orientation.Horizontal };
            sensorPanel.Margin = new Thickness(0, 6, 0, 10);
            sensorPanel.Children.Add(CreateButton("Calibrate Sensor", (s, e) => _plugin.CalibrateSensor()));
            sensorPanel.Children.Add(CreateButton("Reconnect Sensor", (s, e) => _plugin.ReconnectSensor()));
            mainStack.Children.Add(sensorPanel);

            // Rig Dimensions — visual diagram + dimension inputs
            mainStack.Children.Add(CreateSectionHeader("Rig Dimensions"));

            var rigLayout = new Grid();
            rigLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            rigLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: rig diagram (updates when post config changes)
            _diagramContainer = new Border();
            _diagramContainer.Child = BuildRigDiagram(_plugin.GetPostConfig());
            _lastPostConfig = _plugin.GetPostConfig();
            Grid.SetColumn(_diagramContainer, 0);
            rigLayout.Children.Add(_diagramContainer);

            // Right: dimension inputs + calculated angles
            var rigControls = new StackPanel();
            rigControls.Margin = new Thickness(15, 0, 0, 0);

            // Post configuration
            _postConfigText = CreateValueText("4-post");
            var postPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            postPanel.Children.Add(new TextBlock { Text = "Configuration", Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)), VerticalAlignment = VerticalAlignment.Center, Width = 95 });
            postPanel.Children.Add(_postConfigText);
            postPanel.Children.Add(CreateButton("Change", (s, e) => _plugin.CyclePostConfig()));
            rigControls.Children.Add(postPanel);

            // Length
            _rigLengthBox = CreateInputBox("862", (val) => _plugin.SetRigLength(val));
            var lengthPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            lengthPanel.Children.Add(new TextBlock { Text = "Length (F↔R)", Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 0)), VerticalAlignment = VerticalAlignment.Center, Width = 95 });
            lengthPanel.Children.Add(_rigLengthBox);
            lengthPanel.Children.Add(new TextBlock { Text = " mm", Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)), VerticalAlignment = VerticalAlignment.Center });
            rigControls.Children.Add(lengthPanel);

            // Width
            _rigWidthBox = CreateInputBox("748", (val) => _plugin.SetRigWidth(val));
            var widthPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            widthPanel.Children.Add(new TextBlock { Text = "Width (L↔R)", Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 0)), VerticalAlignment = VerticalAlignment.Center, Width = 95 });
            widthPanel.Children.Add(_rigWidthBox);
            widthPanel.Children.Add(new TextBlock { Text = " mm", Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)), VerticalAlignment = VerticalAlignment.Center });
            rigControls.Children.Add(widthPanel);

            // Stroke
            _strokeBox = CreateInputBox("50", (val) => _plugin.SetStroke(val));
            var strokePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            strokePanel.Children.Add(new TextBlock { Text = "Actuator stroke", Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 0)), VerticalAlignment = VerticalAlignment.Center, Width = 95 });
            strokePanel.Children.Add(_strokeBox);
            strokePanel.Children.Add(new TextBlock { Text = " mm", Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)), VerticalAlignment = VerticalAlignment.Center });
            rigControls.Children.Add(strokePanel);

            // Separator
            var sep = new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), Margin = new Thickness(0, 8, 0, 8) };
            rigControls.Children.Add(sep);

            // Calculated angles (read-only)
            var calcLabel = new TextBlock { Text = "Calculated max angles:", Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 220)), FontSize = 12, Margin = new Thickness(0, 0, 0, 5) };
            rigControls.Children.Add(calcLabel);

            _maxPitchText = CreateValueText("--");
            var pitchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            pitchRow.Children.Add(new TextBlock { Text = "Max Pitch", Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)), VerticalAlignment = VerticalAlignment.Center, Width = 95 });
            pitchRow.Children.Add(_maxPitchText);
            rigControls.Children.Add(pitchRow);

            _maxRollText = CreateValueText("--");
            var rollRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            rollRow.Children.Add(new TextBlock { Text = "Max Roll", Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)), VerticalAlignment = VerticalAlignment.Center, Width = 95 });
            rollRow.Children.Add(_maxRollText);
            rigControls.Children.Add(rollRow);

            // Manual override
            var overrideLabel = new TextBlock { Text = "Override (optional):", Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 220)), FontSize = 12, Margin = new Thickness(0, 10, 0, 2) };
            rigControls.Children.Add(overrideLabel);
            var overrideNote = new TextBlock();
            overrideNote.Text = "If your rig software shows different max angles, enter them here. Leave 0 to use calculated values.";
            overrideNote.Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120));
            overrideNote.TextWrapping = TextWrapping.Wrap;
            overrideNote.FontSize = 10;
            overrideNote.Margin = new Thickness(0, 0, 0, 5);
            rigControls.Children.Add(overrideNote);

            _overridePitchBox = CreateInputBox("0", (val) => _plugin.SetOverridePitch(val));
            var oPitchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            oPitchRow.Children.Add(new TextBlock { Text = "Max Pitch", Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)), VerticalAlignment = VerticalAlignment.Center, Width = 95 });
            oPitchRow.Children.Add(_overridePitchBox);
            oPitchRow.Children.Add(new TextBlock { Text = " °", Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)), VerticalAlignment = VerticalAlignment.Center });
            rigControls.Children.Add(oPitchRow);

            _overrideRollBox = CreateInputBox("0", (val) => _plugin.SetOverrideRoll(val));
            var oRollRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            oRollRow.Children.Add(new TextBlock { Text = "Max Roll", Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)), VerticalAlignment = VerticalAlignment.Center, Width = 95 });
            oRollRow.Children.Add(_overrideRollBox);
            oRollRow.Children.Add(new TextBlock { Text = " °", Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)), VerticalAlignment = VerticalAlignment.Center });
            rigControls.Children.Add(oRollRow);

            Grid.SetColumn(rigControls, 1);
            rigLayout.Children.Add(rigControls);
            mainStack.Children.Add(WrapInBorder(rigLayout));

            // OXRMC Setup — one-click config
            mainStack.Children.Add(CreateSectionHeader("OXRMC Setup"));
            var oxrmcDesc = new TextBlock();
            oxrmcDesc.Text = "Sets OpenXR-MotionCompensation's tracker type to \"flypt\" so it reads this plugin, and enables auto-activate so compensation starts on its own. Install and run OXRMC at least once first. Your other OXRMC settings are kept, and a backup is saved.";
            oxrmcDesc.Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140));
            oxrmcDesc.TextWrapping = TextWrapping.Wrap;
            oxrmcDesc.FontSize = 11;
            oxrmcDesc.Margin = new Thickness(0, 0, 0, 8);
            mainStack.Children.Add(oxrmcDesc);

            var oxrmcPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            oxrmcPanel.Children.Add(CreateButton("Auto-configure OXRMC (flypt)", (s, e) => ConfigureOXRMC()));
            oxrmcPanel.Children.Add(CreateButton("Open OXRMC Config", (s, e) => OpenOXRMCConfig()));
            mainStack.Children.Add(oxrmcPanel);

            // Config file location (read-only, selectable so it can be copied)
            var pathLabel = new TextBlock { Text = "Config file:", Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)), FontSize = 11, Margin = new Thickness(0, 0, 0, 2) };
            mainStack.Children.Add(pathLabel);

            var pathBox = new TextBox();
            pathBox.Text = System.IO.Path.Combine(_plugin.GetOXRMCConfigDir(), "OpenXR-MotionCompensation.ini");
            pathBox.IsReadOnly = true;
            pathBox.Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 220));
            pathBox.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            pathBox.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            pathBox.BorderThickness = new Thickness(1);
            pathBox.Padding = new Thickness(5, 3, 5, 3);
            pathBox.FontSize = 11;
            pathBox.Margin = new Thickness(0, 0, 0, 6);
            pathBox.TextWrapping = TextWrapping.Wrap;
            mainStack.Children.Add(pathBox);

            // Smoothing tip — for the common "shaky view" question
            var shakeTip = new TextBlock();
            shakeTip.Text = "View shaky? Open the config and set [input_stabilizer] enabled = 1 (strength 0.5 is a good start; higher = smoother but more latency). Telemetry mode is rougher than a mounted WitMotion sensor (Sensor mode).";
            shakeTip.Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 0));
            shakeTip.TextWrapping = TextWrapping.Wrap;
            shakeTip.FontSize = 11;
            shakeTip.Margin = new Thickness(0, 0, 0, 10);
            mainStack.Children.Add(shakeTip);

            // Tools
            mainStack.Children.Add(CreateSectionHeader("Tools"));
            var toolsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            toolsPanel.Children.Add(CreateButton("Open MMF Reader", (s, e) => LaunchMmfReader()));
            mainStack.Children.Add(toolsPanel);

            var scrollViewer = new ScrollViewer();
            scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scrollViewer.Content = mainStack;
            this.Content = scrollViewer;
        }

        private void UpdateDisplay()
        {
            string mode = _plugin.GetMode();
            _modeText.Text = mode;
            if (mode == "BLENDED")
                _modeText.Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 255));
            else if (mode == "SENSOR")
                _modeText.Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 80));
            else
                _modeText.Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 0));

            _sensorText.Text = _plugin.GetSensorStatus();
            _rollText.Text = _plugin.GetCurrentRollDeg().ToString("F2") + "°";
            _pitchText.Text = _plugin.GetCurrentPitchDeg().ToString("F2") + "°";
            _telRollText.Text = _plugin.GetTelemetryRollDeg().ToString("F2") + "°";
            _telPitchText.Text = _plugin.GetTelemetryPitchDeg().ToString("F2") + "°";
            _rollGainText.Text = _plugin.GetRollGain().ToString("F3");
            _pitchGainText.Text = _plugin.GetPitchGain().ToString("F3");
            _blendText.Text = ((int)(_plugin.GetBlendAlpha() * 100)).ToString() + "%";
            _mountText.Text = _plugin.GetMountModeName();
            _yawText.Text = _plugin.GetSensorYawOffsetDeg().ToString("F0") + "°";
            string modeSetting = _plugin.GetModeOverrideName();
            _overrideText.Text = modeSetting;
            if (modeSetting == "TELEMETRY")
                _modeDescText.Text = "No sensor needed. Estimates rig position from in-game g-forces. Tune Roll/Pitch strength below to match your rig's feel.";
            else if (modeSetting == "SENSOR")
                _modeDescText.Text = "Reads actual rig tilt from the WitMotion sensor. Most accurate. Set the mounting mode / yaw align below so it matches your rig, then calibrate.";
            else
                _modeDescText.Text = "Combines sensor + game data. Sensor gives accuracy, game data adds faster response. Best quality. Adjust blend weight below.";
            _postConfigText.Text = _plugin.GetPostConfig() + "-post";
            if (!_rigLengthBox.IsFocused) _rigLengthBox.Text = _plugin.GetRigLengthMm().ToString("F0");
            if (!_rigWidthBox.IsFocused) _rigWidthBox.Text = _plugin.GetRigWidthMm().ToString("F0");
            if (!_strokeBox.IsFocused) _strokeBox.Text = _plugin.GetStrokeMm().ToString("F0");
            double calcPitch = _plugin.GetCalculatedPitchDeg();
            double calcRoll = _plugin.GetCalculatedRollDeg();
            bool pitchOverridden = _plugin.GetOverridePitchDeg() > 0;
            bool rollOverridden = _plugin.GetOverrideRollDeg() > 0;

            _maxPitchText.Text = calcPitch.ToString("F2") + "°" + (pitchOverridden ? "  (overridden)" : "  ← active");
            _maxRollText.Text = calcRoll.ToString("F2") + "°" + (rollOverridden ? "  (overridden)" : "  ← active");
            _maxPitchText.Foreground = pitchOverridden ? new SolidColorBrush(Color.FromRgb(120, 120, 120)) : Brushes.White;
            _maxRollText.Foreground = rollOverridden ? new SolidColorBrush(Color.FromRgb(120, 120, 120)) : Brushes.White;

            if (!_overridePitchBox.IsFocused) _overridePitchBox.Text = _plugin.GetOverridePitchDeg().ToString("F1");
            if (!_overrideRollBox.IsFocused) _overrideRollBox.Text = _plugin.GetOverrideRollDeg().ToString("F1");

            // Rebuild diagram if post config changed
            int currentConfig = _plugin.GetPostConfig();
            if (currentConfig != _lastPostConfig)
            {
                _lastPostConfig = currentConfig;
                _diagramContainer.Child = BuildRigDiagram(currentConfig);
            }
        }

        private void ConfigureOXRMC()
        {
            string result = _plugin.ConfigureOXRMC();
            bool ok = !result.StartsWith("Could not") && !result.StartsWith("OXRMC config folder not found") && !result.StartsWith("No OXRMC");
            MessageBox.Show(result, "OXRMC Bridge — Auto-configure",
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void OpenOXRMCConfig()
        {
            string dir = _plugin.GetOXRMCConfigDir();
            string mainIni = System.IO.Path.Combine(dir, "OpenXR-MotionCompensation.ini");
            try
            {
                if (File.Exists(mainIni))
                {
                    // Open Explorer with the .ini highlighted, so the user can choose
                    // how to open it (right-click -> Open with) instead of the default app.
                    Process.Start("explorer.exe", "/select,\"" + mainIni + "\"");
                }
                else if (Directory.Exists(dir))
                {
                    // No main config yet — open the folder so the user can see per-app configs
                    Process.Start("explorer.exe", "\"" + dir + "\"");
                }
                else
                {
                    MessageBox.Show("OXRMC config not found at:\n" + dir +
                        "\n\nInstall OpenXR-MotionCompensation and run it once first.",
                        "OXRMC Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open the config:\n" + ex.Message,
                    "OXRMC Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LaunchMmfReader()
        {
            string path = @"C:\Program Files\OpenXR-MotionCompensation\MmfReader.exe";
            try
            {
                Process.Start(path);
            }
            catch (Exception)
            {
                MessageBox.Show("MMF Reader not found at:\n" + path + "\n\nPlease install OpenXR-MotionCompensation.", "OXRMC Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private Canvas BuildRigDiagram(int postConfig)
        {
            var canvas = new Canvas();
            canvas.Width = 190;
            canvas.Height = 220;

            var dimColor = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            var postColor = new SolidColorBrush(Color.FromRgb(0, 180, 220));
            var rigColor = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            var accentColor = new SolidColorBrush(Color.FromRgb(255, 180, 0));
            var measureColor = new SolidColorBrush(Color.FromRgb(255, 100, 100));
            double postR = 9;

            // Rig outline
            double rigL = 30, rigT = 35, rigW = 110, rigH = 140;
            var rigRect = new Rectangle { Width = rigW, Height = rigH, Stroke = rigColor, StrokeThickness = 2 };
            Canvas.SetLeft(rigRect, rigL);
            Canvas.SetTop(rigRect, rigT);
            canvas.Children.Add(rigRect);

            // Seat
            double seatL = rigL + 25, seatT = rigT + 25, seatW = 60, seatH = 90;
            var seat = new Rectangle { Width = seatW, Height = seatH, Stroke = rigColor, StrokeThickness = 1, StrokeDashArray = new DoubleCollection(new double[] { 3, 2 }) };
            Canvas.SetLeft(seat, seatL);
            Canvas.SetTop(seat, seatT);
            canvas.Children.Add(seat);
            var seatLabel = new TextBlock { Text = "seat", Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 60)), FontSize = 9 };
            Canvas.SetLeft(seatLabel, seatL + 18);
            Canvas.SetTop(seatLabel, seatT + 38);
            canvas.Children.Add(seatLabel);

            // FRONT label
            var frontLabel = new TextBlock { Text = "FRONT", Foreground = dimColor, FontSize = 9, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(frontLabel, rigL + rigW / 2 - 18);
            Canvas.SetTop(frontLabel, rigT - 16);
            canvas.Children.Add(frontLabel);

            // Arrow up for front
            canvas.Children.Add(new Line { X1 = rigL + rigW / 2, Y1 = rigT - 2, X2 = rigL + rigW / 2, Y2 = rigT - 14, Stroke = dimColor, StrokeThickness = 1 });

            // Post positions depend on config
            if (postConfig == 3)
            {
                // 3-post: one front center, two rear corners
                double frontX = rigL + rigW / 2, frontY = rigT;
                double rearLX = rigL, rearLY = rigT + rigH;
                double rearRX = rigL + rigW, rearRY = rigT + rigH;

                AddPost(canvas, frontX, frontY, "1", postR, postColor);
                AddPost(canvas, rearLX, rearLY, "2", postR, postColor);
                AddPost(canvas, rearRX, rearRY, "3", postR, postColor);

                // Lines connecting posts
                canvas.Children.Add(new Line { X1 = frontX, Y1 = frontY, X2 = rearLX, Y2 = rearLY, Stroke = rigColor, StrokeThickness = 1, StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }) });
                canvas.Children.Add(new Line { X1 = frontX, Y1 = frontY, X2 = rearRX, Y2 = rearRY, Stroke = rigColor, StrokeThickness = 1, StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }) });
                canvas.Children.Add(new Line { X1 = rearLX, Y1 = rearLY, X2 = rearRX, Y2 = rearRY, Stroke = rigColor, StrokeThickness = 1, StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }) });

                // Length: front post to rear post line (vertical)
                AddDimensionV(canvas, rigL + rigW + 18, frontY, rearLY, "Length", accentColor);

                // Width: between two rear posts (horizontal)
                AddDimensionH(canvas, rearLX, rearRX, rearLY + 18, "Width", accentColor);
            }
            else
            {
                // 4-post: four corners
                double flX = rigL, flY = rigT;           // front left
                double frX = rigL + rigW, frY = rigT;    // front right
                double rrX = rigL + rigW, rrY = rigT + rigH; // rear right
                double rlX = rigL, rlY = rigT + rigH;    // rear left

                AddPost(canvas, flX, flY, "1", postR, postColor);
                AddPost(canvas, frX, frY, "2", postR, postColor);
                AddPost(canvas, rrX, rrY, "3", postR, postColor);
                AddPost(canvas, rlX, rlY, "4", postR, postColor);

                // Length: vertical right side
                AddDimensionV(canvas, rigL + rigW + 18, frY, rrY, "Length", accentColor);

                // Width: horizontal bottom
                AddDimensionH(canvas, rlX, rrX, rlY + 18, "Width", accentColor);
            }

            return canvas;
        }

        private static void AddPost(Canvas canvas, double x, double y, string label, double r, Brush color)
        {
            var circle = new Ellipse { Width = r * 2, Height = r * 2, Fill = color };
            Canvas.SetLeft(circle, x - r);
            Canvas.SetTop(circle, y - r);
            canvas.Children.Add(circle);

            var lbl = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(lbl, x - 4);
            Canvas.SetTop(lbl, y - 7);
            canvas.Children.Add(lbl);
        }

        private static void AddDimensionH(Canvas canvas, double x1, double x2, double y, string label, Brush color)
        {
            // Horizontal dimension line with end ticks
            canvas.Children.Add(new Line { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = color, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = x1, Y1 = y - 4, X2 = x1, Y2 = y + 4, Stroke = color, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = x2, Y1 = y - 4, X2 = x2, Y2 = y + 4, Stroke = color, StrokeThickness = 1 });
            // Arrows
            canvas.Children.Add(new Line { X1 = x1, Y1 = y, X2 = x1 + 6, Y2 = y - 3, Stroke = color, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = x1, Y1 = y, X2 = x1 + 6, Y2 = y + 3, Stroke = color, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = x2, Y1 = y, X2 = x2 - 6, Y2 = y - 3, Stroke = color, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = x2, Y1 = y, X2 = x2 - 6, Y2 = y + 3, Stroke = color, StrokeThickness = 1 });
            var lbl = new TextBlock { Text = "← " + label + " →", Foreground = color, FontSize = 9 };
            Canvas.SetLeft(lbl, (x1 + x2) / 2 - 25);
            Canvas.SetTop(lbl, y + 2);
            canvas.Children.Add(lbl);
        }

        private static void AddDimensionV(Canvas canvas, double x, double y1, double y2, string label, Brush color)
        {
            // Vertical dimension line with end ticks
            canvas.Children.Add(new Line { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = color, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = x - 4, Y1 = y1, X2 = x + 4, Y2 = y1, Stroke = color, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = x - 4, Y1 = y2, X2 = x + 4, Y2 = y2, Stroke = color, StrokeThickness = 1 });
            // Arrows
            canvas.Children.Add(new Line { X1 = x, Y1 = y1, X2 = x - 3, Y2 = y1 + 6, Stroke = color, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = x, Y1 = y1, X2 = x + 3, Y2 = y1 + 6, Stroke = color, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = x, Y1 = y2, X2 = x - 3, Y2 = y2 - 6, Stroke = color, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = x, Y1 = y2, X2 = x + 3, Y2 = y2 - 6, Stroke = color, StrokeThickness = 1 });
            var lbl = new TextBlock { Text = label, Foreground = color, FontSize = 9, RenderTransform = new RotateTransform(90) };
            Canvas.SetLeft(lbl, x + 5);
            Canvas.SetTop(lbl, (y1 + y2) / 2 - 12);
            canvas.Children.Add(lbl);
        }

        private static TextBox CreateInputBox(string initial, Action<double> onChanged)
        {
            var box = new TextBox();
            box.Text = initial;
            box.Width = 60;
            box.Foreground = Brushes.White;
            box.Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));
            box.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(4, 2, 4, 2);
            box.VerticalAlignment = VerticalAlignment.Center;
            box.HorizontalContentAlignment = HorizontalAlignment.Right;

            box.LostFocus += (s, e) =>
            {
                double val;
                if (double.TryParse(box.Text, out val))
                {
                    onChanged(val);
                }
            };
            box.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    double val;
                    if (double.TryParse(box.Text, out val))
                    {
                        onChanged(val);
                    }
                    System.Windows.Input.Keyboard.ClearFocus();
                }
            };
            return box;
        }

        private static TextBlock CreateSectionHeader(string text)
        {
            var tb = new TextBlock();
            tb.Text = text;
            tb.FontSize = 14;
            tb.FontWeight = FontWeights.SemiBold;
            tb.Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 220));
            tb.Margin = new Thickness(0, 10, 0, 5);
            return tb;
        }

        private static TextBlock CreateValueText(string initial)
        {
            var tb = new TextBlock();
            tb.Text = initial;
            tb.Foreground = Brushes.White;
            tb.FontSize = 13;
            tb.VerticalAlignment = VerticalAlignment.Center;
            tb.MinWidth = 80;
            tb.Margin = new Thickness(0, 0, 10, 0);
            return tb;
        }

        private static Button CreateButton(string text, RoutedEventHandler handler)
        {
            var btn = new Button();
            btn.Content = text;
            btn.Margin = new Thickness(0, 2, 8, 2);
            btn.Padding = new Thickness(12, 4, 12, 4);
            btn.Background = new SolidColorBrush(Color.FromRgb(0, 140, 180));
            btn.Foreground = Brushes.White;
            btn.BorderThickness = new Thickness(0);
            btn.Cursor = System.Windows.Input.Cursors.Hand;
            btn.Click += handler;
            return btn;
        }

        private static void AddGridRow(Grid grid, int row, string label, UIElement value)
        {
            var lb = new TextBlock();
            lb.Text = label;
            lb.Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180));
            lb.VerticalAlignment = VerticalAlignment.Center;
            lb.Margin = new Thickness(0, 3, 0, 3);
            Grid.SetRow(lb, row);
            Grid.SetColumn(lb, 0);
            grid.Children.Add(lb);

            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
        }

        private static Border WrapInBorder(UIElement content)
        {
            var border = new Border();
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            border.BorderThickness = new Thickness(1);
            border.Padding = new Thickness(10, 5, 10, 5);
            border.Margin = new Thickness(0, 0, 0, 5);
            border.Child = content;
            return border;
        }
    }
}
