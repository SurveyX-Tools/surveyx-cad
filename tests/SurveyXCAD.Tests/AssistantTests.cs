using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SurveyX.Core;
using Xunit;

namespace SurveyX.Tests
{
    /// <summary>Plays back scripted Claude responses and records the requests that were sent.</summary>
    internal sealed class FakeTransport : IClaudeTransport
    {
        public readonly Queue<string> Responses = new Queue<string>();
        public readonly List<string> Requests = new List<string>();
        public Task<string> SendAsync(string apiKey, string jsonBody, CancellationToken ct)
        {
            Requests.Add(jsonBody);
            return Task.FromResult(Responses.Dequeue());
        }
    }

    public class AssistantTests
    {
        private const string ToolUse =
            "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"stop_reason\":\"tool_use\",\"usage\":{\"input_tokens\":1200,\"output_tokens\":80}," +
            "\"content\":[{\"type\":\"text\",\"text\":\"حاضر، هحسب القطع والردم.\"},{\"type\":\"tool_use\",\"id\":\"toolu_01\",\"name\":\"cut_fill\"," +
            "\"input\":{\"ogl\":{\"saved_ogl\":true},\"survey\":{\"file\":\"today.csv\"},\"area_name\":\"Zone A\",\"cell\":0.5}}]}";
        private const string Final =
            "{\"id\":\"msg_2\",\"type\":\"message\",\"role\":\"assistant\",\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":1500,\"output_tokens\":120}," +
            "\"content\":[{\"type\":\"text\",\"text\":\"الحفر 3,664.93 م³ والردم 5,534.07 م³.\"}]}";

        [Fact]
        public async Task RunsToolsAndReturnsFinalAnswer()
        {
            var t = new FakeTransport();
            t.Responses.Enqueue(ToolUse);
            t.Responses.Enqueue(Final);
            var s = new AssistantSession(t, () => "sk-test") { SystemPrompt = "sys", Tools = { new ToolSpec("cut_fill", "d").Prop("area_name", "string", "x") } };
            var texts = new List<string>();
            string calledName = null;
            Dictionary<string, object> calledInput = null;
            await s.RunAsync("احسب قطع وردم",
                (n, i) => { calledName = n; calledInput = i; return Task.FromResult("{\"cut_m3\":3664.93,\"fill_m3\":5534.07}"); },
                texts.Add, null, CancellationToken.None);

            Assert.Equal("cut_fill", calledName);
            Assert.True(calledInput.Sub("ogl").Bool("saved_ogl"));
            Assert.Equal("today.csv", calledInput.Sub("survey").Str("file"));
            Assert.Equal(0.5, calledInput.Num("cell"));
            Assert.Equal(2, texts.Count);
            Assert.Contains("5,534.07", texts[1]);
            Assert.Equal(2700, s.Usage.InputTokens);
            Assert.Equal(200, s.Usage.OutputTokens);

            // second request must carry the assistant tool_use and our tool_result
            var req2 = MiniJson.Parse(t.Requests[1]) as Dictionary<string, object>;
            Assert.Equal("claude-sonnet-5-5", req2.Str("model"));
            Assert.Equal("sys", req2.Str("system"));
            var msgs = req2.List("messages");
            Assert.Equal(3, msgs.Count);
            var toolResult = ((msgs[2] as Dictionary<string, object>).List("content")[0]) as Dictionary<string, object>;
            Assert.Equal("tool_result", toolResult.Str("type"));
            Assert.Equal("toolu_01", toolResult.Str("tool_use_id"));
            Assert.Contains("3664.93", toolResult.Str("content"));
            var tools = req2.List("tools");
            Assert.Equal("cut_fill", (tools[0] as Dictionary<string, object>).Str("name"));
            Assert.False(req2.ContainsKey("temperature")); // removed in API version 2026-10-21
        }

        [Fact]
        public async Task ToolErrorsAreReportedToClaude()
        {
            var t = new FakeTransport();
            t.Responses.Enqueue(ToolUse);
            t.Responses.Enqueue(Final);
            var s = new AssistantSession(t, () => "k");
            await s.RunAsync("x", (n, i) => throw new FileNotFoundException("File not found: today.csv"), _ => { }, null, CancellationToken.None);
            var req2 = MiniJson.Parse(t.Requests[1]) as Dictionary<string, object>;
            var tr = ((req2.List("messages")[2] as Dictionary<string, object>).List("content")[0]) as Dictionary<string, object>;
            Assert.True(tr.Bool("is_error"));
            Assert.Contains("today.csv", tr.Str("content"));
        }

        [Fact]
        public async Task MissingKeyThrowsFriendlyError()
        {
            var s = new AssistantSession(new FakeTransport(), () => "");
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.RunAsync("x", (n, i) => Task.FromResult(""), _ => { }, null, CancellationToken.None));
        }

        [Fact]
        public void JsonRoundTripWithArabicAndEscapes()
        {
            var o = J.Obj("t", "قطع \"وردم\"\n\\ok", "n", 1.5, "b", true, "z", null, "a", J.Arr(1, "x", J.Obj("k", -2.25e-3)));
            var back = MiniJson.Parse(JsonWriter.Write(o)) as Dictionary<string, object>;
            Assert.Equal("قطع \"وردم\"\n\\ok", back.Str("t"));
            Assert.Equal(1.5, back.Num("n"));
            Assert.True(back.Bool("b"));
            Assert.Null(back["z"]);
            Assert.Equal(-2.25e-3, (back.List("a")[2] as Dictionary<string, object>).Num("k"), 12);
        }

        [Fact]
        public void SecretRoundTripOnWindows()
        {
            if (!OperatingSystem.IsWindows()) return;
            string enc = Secret.Protect("sk-ant-test-123");
            Assert.NotEqual("sk-ant-test-123", enc);
            Assert.Equal("sk-ant-test-123", Secret.Unprotect(enc));
            Assert.Equal("", Secret.Unprotect("not-base64!"));
        }
    }

    public class OfficeTests
    {
        [Fact]
        public void StationOffsetRightPositive()
        {
            var align = new List<(double, double)> { (0, 0), (100, 0), (100, 100) };
            var a = StationOffset.Compute(align, 50, -3);   // right of an eastward line
            Assert.Equal(50, a.station, 9); Assert.Equal(3, a.offset, 9);
            var b = StationOffset.Compute(align, 50, 4);    // left
            Assert.Equal(-4, b.offset, 9);
            var c = StationOffset.Compute(align, 102, 30);  // second leg going north, point to the east = right
            Assert.Equal(130, c.station, 9); Assert.Equal(2, c.offset, 9);
        }

        [Fact]
        public void CutFillLogKeepsLastPerDayAndFilters()
        {
            CutFillLog.PathOverride = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                CutFillLog.Add(new CutFillLog.Entry { Date = new DateTime(2026, 11, 20), Area = "Zone A", Cut = 100, Fill = 50 });
                CutFillLog.Add(new CutFillLog.Entry { Date = new DateTime(2026, 11, 20), Area = "zone a", Cut = 120, Fill = 55 }); // re-run same day
                CutFillLog.Add(new CutFillLog.Entry { Date = new DateTime(2026, 11, 21), Area = "Zone A", Cut = 80, Fill = 40 });
                CutFillLog.Add(new CutFillLog.Entry { Date = new DateTime(2026, 11, 21), Area = "Zone B", Cut = 10, Fill = 5 });
                var a = CutFillLog.Filter("Zone A", null, null);
                Assert.Equal(2, a.Count);
                Assert.Equal(200, a.Sum(e => e.Cut), 9);
                Assert.Equal(3, CutFillLog.Filter("", null, null).Count);
                Assert.Single(CutFillLog.Filter("", new DateTime(2026, 11, 21), new DateTime(2026, 11, 21)).Where(e => e.Area == "Zone A"));
            }
            finally { CutFillLog.PathOverride = null; }
        }

        [Fact]
        public void ExcelExportsAreReadable()
        {
            string p = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xlsx");
            var q = new List<QtoRow> { new QtoRow { Layer = "KERB", Lines = 3, Length = 120.5 }, new QtoRow { Layer = "PIPE", Polylines = 2, Length = 80.25, Blocks = 4 } };
            q[1].BlockNames["MH"] = 4;
            ExcelExport.Qto(p, q);
            var rows = XlsxReader.ReadFirstSheet(p);
            Assert.Equal("KERB", rows[2][0]);
            Assert.Equal(120.5, double.Parse(rows[2][1], System.Globalization.CultureInfo.InvariantCulture), 9);
            Assert.Equal("TOTAL", rows[4][0]);
            Assert.Equal(200.75, double.Parse(rows[4][1], System.Globalization.CultureInfo.InvariantCulture), 9);
            Assert.Contains(rows, r => r.Length > 1 && r[1] == "MH");
        }
    }
}
