namespace Stagewatch.Companion;

/// <summary>
/// Stagewatch Companion text protocol. ASCII line-delimited commands and events.
///
/// Lines are terminated with \n (CR optional and ignored). Tokens separated by spaces.
/// Quoted strings use double quotes with backslash escape: \" \\ \n.
///
/// Commands (client → server):
///   START
///   STOP                         (= pause)
///   RESET
///   SET HH:MM:SS                 (load duration, leave stopped)
///   SET MM:SS
///   SETMS <ms>                   (load milliseconds)
///   PRESET <slot 1-12>
///   PRESET <slot> START          (load and immediately start)
///   NUDGE <±seconds>
///   MODE <countdown|countup|tod>
///   TOGGLE                       (start/pause)
///   NEXT | PREV | GOTO <n> [START]   (running order)
///   BLACKOUT [on|off|toggle]     (blank the outputs)
///   CLEARMESSAGE
///   ENDAT HH:MM:SS [elastic|realtime]  (set wall-clock end-at; "clear" or "-" to clear;
///                                pace defaults to the operator's setting)
///   MESSAGE "text"               (replace message; clears flash)
///   MESSAGE "text" <urgency> <flash>
///                                urgency = info|warn|crit, flash = off|slow|fast
///   FLASH <off|slow|fast>
///   FLASH TIMER <on|off>         (flash the timer face itself)
///   STATE                        (request a one-shot STATE event)
///   SUBSCRIBE JSON               (v2: stream SNAPSHOT/CONFIG JSON instead of TICK/STATE)
///   SNAPSHOT | CONFIG            (v2: one-shot SNAPSHOT, or CONFIG + SNAPSHOT)
///   PING [token]                 (→ PONG [token]; heartbeat. A Unix-ms token gets "PONG t1 t2 t3" for the clock offset)
///   PEER <app> "<line>"          (Show Bus §11: one line another app lists, sent born approved as a deck's — this PC or an address in
///                                Settings → Remote → May drive other show apps only; the receipt waits for what the app made of it)
///   DESK APPROVE <id> [HIGH] | DESK APPROVE NEXT | DESK REJECT <id>|NEXT ["<why>"]
///                                (a person's decision on the desk, by the same rule; a high proposal takes its id and HIGH)
///   WINDOW GRANT <window> <app id>  (Show Bus §12.4: asked of the operator, never made — it waits on the desk, by the same rule;
///                                → OK @<id> accepted p<n> waits for a person's approval, then OK|ERROR @<id> final once decided)
///   @<id> <command>              (Show Bus receipt: → OK @<id> <stage> <effect> / ERROR @<id> <why>; an id of 13.12.1's shape,
///                                <prefix><n>-<inst>, already run from this address → OK @<id> {…,"repeat":true}, not run again)
///   @<id> RECEIPTS <id> …        (13.12.6: → OK @<id> {"inst":…,"receipts":{<id>:{…}|"unknown"}}, 1–32 ids, this address's own)
///   SUBSCRIBE                    (start streaming live events; default after connect)
///   UNSUBSCRIBE
///   QUIT
///
/// Events (server → client):
///   READY <version>
///   DENIED license "<reason>"    (Free mode: the server then closes the connection)
///   SNAPSHOT {json}              (v2: full visible state, sent on change, ~1/s while running)
///   CONFIG {json}                (v2: presets, quick messages, running order, the show apps as "peers"; sent when they change)
///   ACCESS {json}                (v2: {"peer":…,"desk":…,"why":"…","grant":…} — what this connection's address may do with a
///                                person's lines; after SUBSCRIBE JSON and again whenever it changes)
///   STATE mode=<m> state=<s> ms=<ms> preset=<slot|->
///   TICK ms=<ms> formatted=<HH:MM:SS> state=<s>
///   MODE <m>
///   MESSAGE "text" urgency=<u> flash=<f>
///   FLASH <off|slow|fast>
///   ERROR <message>              (on bad command)
///   PONG
///
/// Backwards compatibility note: command shapes mirror IrisDown's verb style so existing
/// Companion modules can be ported with minimal edits.
/// </summary>
public static class CompanionProtocol
{
    public const string Version = "stagewatch/2";

    public static string EscapeQuoted(string s)
        => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    /// <summary>Parse a single line into tokens, honoring quoted strings.</summary>
    public static List<string> Tokenize(string line)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false, escape = false;
        foreach (var c in line)
        {
            if (escape)
            {
                sb.Append(c switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => c });
                escape = false;
                continue;
            }
            if (c == '\\') { escape = true; continue; }
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }
}
