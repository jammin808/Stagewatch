using System.Net;
using System.Net.Sockets;
using System.Text;
using Stagewatch.Core;
using Stagewatch.Integrations;

/// <summary>Sensors and outputs: LTC, DMX packets, Hue, vMix parsing, triggers, rooms, assistant guard-rails, voices.</summary>
static class IntegrationTests
{
    public static async Task Run(Action<bool, string> Check)
    {
        Ltc(Check);
        Dmx(Check);
        Vmix(Check);
        Triggers(Check);
        Assistant(Check);
        Rehearsal(Check);
        await Rooms(Check);
        Voices(Check);
    }

    static void Ltc(Action<bool, string> Check)
    {
        foreach (var fps in new[] { 24, 25, 30 })
        {
            var enc = new LtcEncoder(fps);
            var t = new TimeSpan(0, 13, 47, 21).Add(TimeSpan.FromSeconds(9.0 / fps + 0.001));
            // A lead-in frame first: the decoder can't see the edge at sample 0.
            var audio = new short[enc.SamplesPerFrame * 4];
            for (int f = 0; f < 4; f++) enc.Encode(t + TimeSpan.FromSeconds((f - 1.0) / fps), audio.AsSpan(f * enc.SamplesPerFrame, enc.SamplesPerFrame));
            var decoded = DecodeLtc(audio, enc.SamplesPerFrame);
            Check(decoded.Contains((13, 47, 21, 9)) && decoded.Contains((13, 47, 21, 10)), $"ltc {fps}: decodes its own audio ({string.Join(" ", decoded)})");
            var bits = LtcEncoder.FrameBits(13, 47, 21, 9, fps);
            Check(bits.Count(b => !b) % 2 == 0, $"ltc {fps}: polarity bit keeps zeros even");
        }
    }

    /// <summary>Tiny biphase-mark decoder: transitions every bit cell, an extra one mid-cell = 1.</summary>
    static List<(int H, int M, int S, int F)> DecodeLtc(short[] audio, int spf)
    {
        var perBit = spf / 80.0;
        var edges = new List<int>();
        for (int i = 1; i < audio.Length; i++) if (Math.Sign(audio[i]) != Math.Sign(audio[i - 1])) edges.Add(i);
        var bits = new List<bool>();
        for (int e = 0; e + 1 < edges.Count;)
        {
            var gap = edges[e + 1] - edges[e];
            if (gap < perBit * 0.75) { bits.Add(true); e += 2; }
            else { bits.Add(false); e += 1; }
        }
        var frames = new List<(int, int, int, int)>();
        var sync = new[] { false, false, true, true, true, true, true, true, true, true, true, true, true, true, false, true };
        for (int i = 0; i + 80 <= bits.Count + 1; i++)
        {
            // find sync word ending a frame
            if (i + 16 > bits.Count) break;
            if (!bits.Skip(i).Take(16).SequenceEqual(sync)) continue;
            var start = i - 64;
            if (start < 0) continue;
            int Bcd(int at, int n) { var v = 0; for (int k = 0; k < n; k++) if (bits[start + at + k]) v |= 1 << k; return v; }
            frames.Add((Bcd(48, 4) + 10 * Bcd(56, 2), Bcd(32, 4) + 10 * Bcd(40, 3), Bcd(16, 4) + 10 * Bcd(24, 3), Bcd(0, 4) + 10 * Bcd(8, 2)));
        }
        return frames;
    }

    static void Dmx(Action<bool, string> Check)
    {
        var dmx = new byte[512];
        DmxFixture.Write(dmx, 10, DmxMode.Rgb, CueLight.FromHex("#F97316", FlashRate.Off), true);
        Check(dmx[9] == 0xF9 && dmx[10] == 0x73 && dmx[11] == 0x16 && dmx[8] == 0 && dmx[12] == 0, "dmx: RGB at address 10");
        DmxFixture.Write(dmx, 10, DmxMode.Rgb, CueLight.FromHex("#F97316", FlashRate.Slow), false);
        Check(dmx[9] == 0 && dmx[10] == 0, "dmx: flash off phase is dark");
        DmxFixture.Write(dmx, 511, DmxMode.RgbDimmer, CueLight.FromHex("#FFFFFF", FlashRate.Off), true);
        Check(dmx[510] == 255 && dmx[511] == 255, "dmx: clipped at the end of the universe");

        var art = ArtNetSink.Packet(dmx, 3, 7);
        Check(Encoding.ASCII.GetString(art, 0, 7) == "Art-Net" && art[8] == 0x00 && art[9] == 0x50 && art[12] == 7 && art[14] == 3 && art[16] == 2 && art[17] == 0 && art.Length == 530,
            "artnet: ArtDmx header (OpDmx, seq, universe, length 512)");
        var sacn = SacnSink.Packet(dmx, 1, 5, Guid.NewGuid(), "Stagewatch");
        Check(Encoding.ASCII.GetString(sacn, 4, 9) == "ASC-E1.17" && sacn[111] == 5 && sacn[113] == 0 && sacn[114] == 1 && sacn[117] == 0x02 && sacn[125] == 0 && sacn.Length == 638,
            "sacn: E1.31 layers, sequence, universe, start code");

        var s = new ShowSnapshot { Mode = TimerMode.Countdown, State = TimerState.Running, Phase = TimerPhase.WrapUp, ColorHex = "#F97316" };
        Check(CueLightOutput.LightFor(s) == CueLight.FromHex("#F97316", FlashRate.Off), "cue light: follows the phase colour");
        Check(CueLightOutput.LightFor(s with { Phase = TimerPhase.Normal }).Off && CueLightOutput.LightFor(s with { Blackout = true }).Off, "cue light: dark when normal or blacked out");
        Check(CueLightOutput.LightFor(s with { Phase = TimerPhase.Overtime, ColorHex = "#DC2626" }).Flash == FlashRate.Slow, "cue light: overtime flashes");
        Check(HueSink.Body(CueLight.Dark) == "{\"on\":false}" && HueSink.Body(CueLight.FromHex("#FF0000", FlashRate.Fast)).Contains("\"alert\":\"lselect\"") && HueSink.Body(CueLight.FromHex("#FF0000", FlashRate.Off)).Contains("\"hue\":0,\"sat\":254,\"bri\":254"),
            "hue: state bodies");
    }

    static void Vmix(Action<bool, string> Check)
    {
        const string xml = """
            <vmix><version>27</version>
              <inputs>
                <input key="a" number="1" type="Colour" title="Black" state="Paused" position="0" duration="0">Black</input>
                <input key="b" number="2" type="Video" title="Sizzle reel.mp4" state="Running" position="45200" duration="90000">Sizzle reel.mp4</input>
                <input key="c" number="3" type="Video" title="Award VT 3" state="Paused" position="0" duration="30000">Award VT 3</input>
              </inputs>
              <active>2</active><preview>3</preview>
            </vmix>
            """;
        var p = VmixSource.Parse(xml);
        Check(p is { Title: "Sizzle reel.mp4", Running: true } && Math.Abs(p.RemainingSeconds - 44.8) < 0.01, "vmix: program clip remaining");
        var p2 = VmixSource.Parse(xml.Replace("<active>2</active>", "<active>1</active>"));
        Check(p2 is { Title: "Award VT 3", Running: false, DurationSeconds: 30 }, "vmix: falls back to preview clip");
        Check(VmixSource.Parse("<not xml") is null, "vmix: bad XML ignored");
    }

    static void Triggers(Action<bool, string> Check)
    {
        var (addr, args) = TriggerRunner.ParseOsc("/cue/go 12 1.5 house");
        Check(addr == "/cue/go" && args[0] is int i && i == 12 && args[1] is float f && f == 1.5f && (string)args[2] == "house", "triggers: OSC payload parsing");
        var ctx = new TriggerContext(4, "Keynote", "wrapup", "00:30", "x", "Main hall");
        Check(TriggerRunner.Expand("/sw/{ROOM}/{ITEM} {title} {PHASE}", ctx) == "/sw/Main hall/4 Keynote wrapup", "triggers: placeholders");
        var ran = new List<string>();
        var rules = new List<TriggerRule>
        {
            new() { Name = "lx", Event = TriggerEvent.Phase, Filter = "wrapup", Action = TriggerAction.Command, Payload = "note \"wrap {ITEM}\"" },
            new() { Name = "off", Enabled = false, Event = TriggerEvent.Phase, Action = TriggerAction.Command, Payload = "note x" }
        };
        using var runner = new TriggerRunner(() => rules, ran.Add);
        runner.Fire(TriggerEvent.Phase, "warning", ctx);
        runner.Fire(TriggerEvent.Phase, "wrapup", ctx);
        runner.Fire(TriggerEvent.Phase, "wrapup", ctx); // debounced (1 s)
        Check(ran.Count == 1 && ran[0] == "note \"wrap 4\"", $"triggers: filter, disabled rules, debounce ({string.Join("|", ran)})");
        Check(TriggerRunner.ParseEndPoint("127.0.0.1:9000", 1).Port == 9000 && TriggerRunner.ParseEndPoint("127.0.0.1", 53000).Port == 53000, "triggers: endpoints");
    }

    static void Assistant(Action<bool, string> Check)
    {
        var json = """{"suggestions":[{"text":"Cut Q&A by 3 minutes","command":"trim 5 -180"},{"text":"Black out now","command":"blackout on"},{"text":"Tell the speaker","command":"signal message speaker \"Please wrap up\""},{"text":"Junk","command":"trim x y"}]}""";
        var a = CloudAdvisor.Parse(json);
        Check(a.Count == 4 && a[0].Command == "trim 5 -180" && a[0].CommandLabel == "Apply", "assistant: valid command kept");
        Check(a[1].Command is null && a[3].Command is null, "assistant: disallowed / unparseable commands dropped (text only)");
        Check(a[2].Command == "signal message speaker \"Please wrap up\"", "assistant: quoted signal allowed");
        Check(CloudAdvisor.Parse("not json").Count == 0 && CloudAdvisor.Parse("{\"x\":1}").Count == 0, "assistant: garbage ignored");
        Check(CloudAdvisor.Tokenize("signal message speaker \"Wrap up now\"").SequenceEqual(new[] { "signal", "message", "speaker", "Wrap up now" }), "assistant: tokenizer");
    }

    static void Rehearsal(Action<bool, string> Check)
    {
        var t = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var r = new RehearsalRecorder();
        r.Begin("Deck.pptx", 4, t);
        r.Observe(1, 4, t);
        r.Observe(2, 4, t.AddSeconds(40));
        r.Observe(1, 4, t.AddSeconds(100)); // went back
        r.Observe(3, 4, t.AddSeconds(110));
        var b = r.Finish(t.AddSeconds(170));
        Check(b is not null && b.Length == 4 && b[0] == 50 && b[1] == 60 && b[2] == 60 && b[3] == 0, $"rehearsal: per-slide seconds, revisits add up ({(b is null ? "null" : string.Join(",", b))})");
        var path = Path.Combine(Path.GetTempPath(), "sw-reh-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new RehearsalStore(path);
        store.Save("Deck.pptx", b!);
        Check(store.For("deck.PPTX")?.Length == 4, "rehearsal: store (case-insensitive)");
    }

    static async Task Rooms(Action<bool, string> Check)
    {
        var port = FreeUdp();
        RoomBeat Beat(string room, string id) => new(1, id, room, "pc", 9924, "Keynote", "04:59", "running", "normal", 120, "17:30", -60, 1);
        using var a = new RoomBeacon(port, id => Beat("Hall A", id));
        using var b = new RoomBeacon(port, id => Beat("Hall B", id));
        List<RoomView> seen = new();
        for (int i = 0; i < 30 && seen.Count == 0; i++) { await Task.Delay(100); seen = a.Rooms(); }
        if (seen.Count == 0)
        {
            Console.WriteLine("note: UDP broadcast loopback not delivered on this PC — rooms test skipped");
            return;
        }
        Check(seen.Count == 1 && seen[0].Room == "Hall B" && seen[0].OffsetSeconds == 120 && seen[0].HardOutMarginSeconds == -60 && !seen[0].Stale, "rooms: peers discovered over UDP");
        Check(a.Rooms().All(r => r.Room != "Hall A"), "rooms: self not listed as a peer");
    }

    static void Voices(Action<bool, string> Check)
    {
        var voices = SpokenCues.Voices();
        if (voices.Count == 0) { Console.WriteLine("note: no Windows voices — spoken cue test skipped"); return; }
        var pcm = new SpokenCues().Render("two minutes", "");
        Check(pcm is { Length: > SpokenCues.SampleRate / 2 }, $"voices: spoken cue rendered ({pcm?.Length ?? 0} bytes)");
    }

    static int FreeUdp()
    {
        using var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)u.Client.LocalEndPoint!).Port;
    }
}
