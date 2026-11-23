using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SurveyX.Core;

namespace SurveyX.UI
{
    /// <summary>Chat panel for the AI assistant. Tool execution and settings are injected so it can run inside AutoCAD or in tests.</summary>
    public sealed class ChatControl : UserControl
    {
        private readonly RichTextBox _log = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White,
            Font = new Font("Segoe UI", 10.5f), DetectUrls = true, ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        private readonly TextBox _input = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10.5f), ScrollBars = ScrollBars.Vertical };
        private readonly Button _send, _stop, _new, _settings;
        private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.DimGray, Padding = new Padding(6, 3, 6, 0), Font = new Font("Segoe UI", 8.5f) };
        private readonly FlowLayoutPanel _chips = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), BackColor = SxForm.Soft };

        private readonly Func<List<ToolSpec>> _tools;
        private readonly Func<string, Dictionary<string, object>, Task<string>> _runTool;
        private readonly Func<string> _system;
        private readonly Action _openSettings;
        private AssistantSession _session;
        private CancellationTokenSource _cts;

        public ChatControl(Func<List<ToolSpec>> tools, Func<string, Dictionary<string, object>, Task<string>> runTool, Func<string> system,
            Action openSettings, IClaudeTransport transport = null)
        {
            _tools = tools; _runTool = runTool; _system = system; _openSettings = openSettings;
            Font = new Font("Segoe UI", 9.75f);
            BackColor = Color.White;
            if (Lang.IsArabic) { RightToLeft = RightToLeft.Yes; _log.RightToLeft = RightToLeft.Yes; _input.RightToLeft = RightToLeft.Yes; }

            _session = new AssistantSession(transport ?? new HttpClaudeTransport(), () => Secret.Unprotect(Settings.Current.ApiKeyProtected));

            var header = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = SxForm.Accent };
            var title = new Label { Text = "✦ " + Lang.T("AiTitle"), ForeColor = Color.White, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
            _new = HeaderBtn(Lang.T("AiNew"));
            _settings = HeaderBtn("⚙");
            _new.Click += (s, e) => NewChat();
            _settings.Click += (s, e) => { _openSettings?.Invoke(); UpdateStatus(); };
            var hb = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, BackColor = SxForm.Accent, Padding = new Padding(0, 5, 4, 0) };
            hb.Controls.Add(_new); hb.Controls.Add(_settings);
            header.Controls.Add(title);
            header.Controls.Add(hb);

            foreach (var k in new[] { "AiChip1", "AiChip2", "AiChip3", "AiChip4", "AiChip5" })
            {
                var b = new Button { Text = Lang.T(k), AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(3), Cursor = Cursors.Hand, UseMnemonic = false };
                b.FlatAppearance.BorderColor = Color.FromArgb(0xC5, 0xD3, 0xE8);
                b.Click += (s, e) => { _input.Text = ((Button)s).Text; _input.Focus(); _input.SelectionStart = _input.Text.Length; };
                _chips.Controls.Add(b);
            }

            _send = new Button { Text = Lang.T("AiSend"), Dock = DockStyle.Fill, BackColor = SxForm.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            _stop = new Button { Text = Lang.T("AiStop"), Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Visible = false };
            _send.Click += async (s, e) => await Send();
            _stop.Click += (s, e) => _cts?.Cancel();
            _input.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; await Send(); }
            };
            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 84, ColumnCount = 2, Padding = new Padding(4) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            var btns = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = new Padding(0) };
            btns.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); btns.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            btns.Controls.Add(_send, 0, 0); btns.Controls.Add(_stop, 0, 1);
            bottom.Controls.Add(_input, 0, 0);
            bottom.Controls.Add(btns, 1, 0);

            var logPad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6), BackColor = Color.White };
            logPad.Controls.Add(_log);
            Controls.Add(logPad);
            Controls.Add(_chips);
            Controls.Add(header);
            Controls.Add(bottom);
            Controls.Add(_status);
            NewChat();
        }

        private static Button HeaderBtn(string text)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 28, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = SxForm.Accent, Cursor = Cursors.Hand, Margin = new Padding(2), UseMnemonic = false };
            b.FlatAppearance.BorderColor = Color.White;
            return b;
        }

        public void NewChat()
        {
            _session.Reset();
            _log.Clear();
            Append(Lang.T("AiWelcome"), Color.FromArgb(60, 60, 60), false);
            if (string.IsNullOrEmpty(Secret.Unprotect(Settings.Current.ApiKeyProtected)))
                Append(Lang.T("AiNoKey"), Color.FromArgb(0xC6, 0x28, 0x28), true);
            UpdateStatus();
        }

        public void Append(string text, Color color, bool bold, float size = 10.5f)
        {
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = color;
            _log.SelectionFont = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            _log.AppendText(text.TrimEnd() + Environment.NewLine + Environment.NewLine);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        public void AppendUser(string text) => Append("● " + Lang.T("AiYou") + ": " + text, SxForm.Accent, true);
        public void AppendAssistant(string text) => Append(text, Color.FromArgb(25, 25, 25), false);
        public void AppendTool(string name) => Append("⚙ " + ToolLabel(name) + "…", Color.Gray, false, 9f);

        private static string ToolLabel(string name)
        {
            string key = "AiTool_" + name;
            string t = Lang.T(key);
            return t == key ? name : t;
        }

        private void UpdateStatus()
        {
            string model = Settings.Current.AiModel;
            var u = _session.Usage;
            _status.Text = model + "   ·   " + string.Format(Lang.T("AiUsage"), u.InputTokens + u.OutputTokens, u.CostUsd(model).ToString("0.000"));
        }

        private Task Send() => SendText(_input.Text);

        /// <summary>Sends a message as if typed (also used by automated tests).</summary>
        public async Task SendText(string message)
        {
            string text = (message ?? "").Trim();
            if (text.Length == 0 || _cts != null) return;
            if (string.IsNullOrEmpty(Secret.Unprotect(Settings.Current.ApiKeyProtected)))
            {
                Append(Lang.T("AiNoKey"), Color.FromArgb(0xC6, 0x28, 0x28), true);
                _openSettings?.Invoke();
                return;
            }
            _input.Clear();
            AppendUser(text);
            SetBusy(true);
            _cts = new CancellationTokenSource();
            try
            {
                _session.Model = Settings.Current.AiModel;
                _session.SystemPrompt = _system();
                _session.Tools = _tools();
                await _session.RunAsync(text,
                    async (name, input) => { var r = await _runTool(name, input); UpdateStatus(); return r; },
                    t => AppendAssistant(t),
                    (name, input) => { AppendTool(name); Application.DoEvents(); },
                    _cts.Token);
            }
            catch (OperationCanceledException) { Append(Lang.T("AiStopped"), Color.Gray, false); }
            catch (ClaudeApiException ex)
            {
                Append(Lang.T("AiApiError") + " (" + ex.Status + "): " + ex.Message, Color.FromArgb(0xC6, 0x28, 0x28), false);
                if (ex.Status == 401) Append(Lang.T("AiBadKey"), Color.FromArgb(0xC6, 0x28, 0x28), true);
                RepairHistory();
            }
            catch (Exception ex)
            {
                Append(Lang.T("AiError") + ": " + ex.Message, Color.FromArgb(0xC6, 0x28, 0x28), false);
                RepairHistory();
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false);
                UpdateStatus();
            }
        }

        /// <summary>After a failure the history may end with a user message or an unanswered tool call; trim it so the next request is valid.</summary>
        private void RepairHistory()
        {
            var m = _session.Messages;
            while (m.Count > 0)
            {
                var last = m[m.Count - 1] as Dictionary<string, object>;
                if (last != null && last.Str("role") == "assistant" && !(last.List("content") ?? new List<object>()).OfType<Dictionary<string, object>>().Any(b => b.Str("type") == "tool_use"))
                    break;
                m.RemoveAt(m.Count - 1);
            }
        }

        private void SetBusy(bool busy)
        {
            _send.Enabled = !busy;
            _stop.Visible = busy;
            _input.Enabled = !busy;
            _status.Text = busy ? Lang.T("AiThinking") : _status.Text;
            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
        }
    }
}
