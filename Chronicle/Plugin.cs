using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using WaygateMods.Kit;

namespace WaygateMods.Chronicle
{
	// The server remembers what every character did on it: monsters slain, elite and boss
	// kills, deaths, bounties, time played. Players read it with /stats and /top in chat, the
	// server posts a short digest at dawn, and the numbers are kept in one JSON file.
	//
	// Server-side only. Everything is read where the server already decides it (the kill
	// credit, the death, the bounty record); nothing is written to a character or the world.
	[BepInPlugin(PluginId, "Chronicle", Version)]
	public class ChroniclePlugin : BasePlugin
	{
		public const string PluginId = "com.humangenome.waygatemods.chronicle";
		public const string ModId = "HumanGenome-Chronicle";
		public const string Version = "1.0.0";

		internal static ConfigEntry<bool> Commands;
		internal static ConfigEntry<bool> PrivateReplies;
		internal static ConfigEntry<int> TopCount;
		internal static ConfigEntry<bool> DawnDigest;
		internal static ConfigEntry<bool> Milestones;
		internal static ConfigEntry<int> WriteEverySeconds;

		public override void Load()
		{
			Commands = Config.Bind("Chat", "Commands", true, "Players can type /stats and /top in chat.");
			PrivateReplies = Config.Bind("Chat", "PrivateReplies", true, "true: only the player who asked sees the answer. false: everyone sees it.");
			TopCount = Config.Bind("Chat", "TopCount", 5, "How many players /top lists. 3 to 10.");
			DawnDigest = Config.Bind("Announcements", "DawnDigest", true, "At dawn, one chat line about the day that ended: monsters slain, deaths, the top hunter.");
			Milestones = Config.Bind("Announcements", "Milestones", true, "Announce a player's 100th, 500th, 1,000th ... kill and their first boss kill.");
			WriteEverySeconds = Config.Bind("File", "WriteEverySeconds", 60, "How often the numbers are written to disk while they are changing. 15 to 600.");

			var harmony = new Harmony(PluginId);
			ModKit.Init(Log, harmony, "Chronicle");
			ModKit.WatchConfig(Config);
			try
			{
				Store.Open(Path.Combine(Paths.ConfigPath, ModId));
				ModKit.Patch(typeof(TransitionManager), "Update", null, typeof(Hooks), null, nameof(Hooks.Tick), true, "the server clock");
				ModKit.Patch(typeof(QuestManager), "RegisterMonsterDeath", new[] { typeof(Player), typeof(Monster), typeof(InGameEvent) }, typeof(Hooks), null, nameof(Hooks.MonsterDeath), true, "kill tracking");
				ModKit.Patch(typeof(Player), "OnDeath", new[] { typeof(Damage), typeof(bool) }, typeof(Hooks), null, nameof(Hooks.PlayerDeath), false, "death tracking");
				ModKit.Patch(typeof(DeedManager), "RecordDeedCompletionForQuest", null, typeof(Hooks), null, nameof(Hooks.DeedDone), false, "bounty tracking");
				ModKit.Patch(typeof(ChatSystem), "SendMessageToServerServerRpc", null, typeof(Hooks), nameof(Hooks.Chat), null, false, "the /stats and /top chat commands");
				if (!ModKit.Off) ModKit.Say("Chronicle " + Version + " is on: " + Store.PlayerCount + " characters on record." + (Commands.Value ? " Players can type /stats and /top." : ""));
			}
			catch (Exception e) { ModKit.Dbg("load: " + e); ModKit.TurnOff("it could not start"); }
		}
	}

	internal sealed class Row
	{
		internal string Hash = "", Name = "";
		internal long Kills, Elites, Bosses, Deaths, Bounties;
		internal double Seconds;
		internal string FirstSeenUtc = "", LastSeenUtc = "";
		internal readonly Dictionary<string, long> ByType = new Dictionary<string, long>();
	}

	internal static class Store
	{
		private static string sDir = "", sFile = "";
		private static readonly Dictionary<string, Row> sRows = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
		internal static long DayKills, DayElites, DayBosses, DayDeaths;
		internal static readonly Dictionary<string, long> DayHunters = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
		internal static bool Dirty;
		private static float sNextWrite;

		internal static int PlayerCount { get { return sRows.Count; } }
		internal static IEnumerable<Row> Rows { get { return sRows.Values; } }

		internal static void Open(string dir)
		{
			sDir = dir; sFile = Path.Combine(dir, "chronicle.json");
			Directory.CreateDirectory(sDir);
			if (!File.Exists(sFile)) return;
			try
			{
				var root = MiniJson.Parse(File.ReadAllText(sFile, Encoding.UTF8)) as Dictionary<string, object>;
				if (root == null) throw new FormatException("not an object");
				var today = MiniJson.Obj(root, "today");
				if (today != null)
				{
					DayKills = MiniJson.Long(today, "kills"); DayElites = MiniJson.Long(today, "elites");
					DayBosses = MiniJson.Long(today, "bosses"); DayDeaths = MiniJson.Long(today, "deaths");
					var h = MiniJson.Obj(today, "hunters");
					if (h != null) foreach (var kv in h) DayHunters[kv.Key] = Convert.ToInt64(kv.Value, CultureInfo.InvariantCulture);
				}
				var players = root.ContainsKey("players") ? root["players"] as List<object> : null;
				if (players != null)
				{
					foreach (var o in players)
					{
						var d = o as Dictionary<string, object>;
						if (d == null) continue;
						var r = new Row { Hash = MiniJson.Str(d, "hash"), Name = MiniJson.Str(d, "name") };
						if (r.Hash.Length == 0) continue;
						r.Kills = MiniJson.Long(d, "kills"); r.Elites = MiniJson.Long(d, "elites"); r.Bosses = MiniJson.Long(d, "bosses");
						r.Deaths = MiniJson.Long(d, "deaths"); r.Bounties = MiniJson.Long(d, "bounties");
						r.Seconds = MiniJson.Num(d, "seconds");
						r.FirstSeenUtc = MiniJson.Str(d, "first_seen_utc"); r.LastSeenUtc = MiniJson.Str(d, "last_seen_utc");
						var bt = MiniJson.Obj(d, "by_type");
						if (bt != null) foreach (var kv in bt) r.ByType[kv.Key] = Convert.ToInt64(kv.Value, CultureInfo.InvariantCulture);
						sRows[r.Hash] = r;
					}
				}
			}
			catch (Exception e)
			{
				// Never lose the owner's history to a bad read: keep the file under another name.
				string kept = Path.Combine(sDir, "chronicle.unreadable-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
				try { File.Move(sFile, kept); } catch { }
				sRows.Clear(); DayHunters.Clear(); DayKills = DayElites = DayBosses = DayDeaths = 0;
				ModKit.Dbg("read failed: " + e);
				ModKit.Say("Chronicle could not read its saved numbers and started a new record. The old file was kept as " + Path.GetFileName(kept) + ".");
			}
		}

		internal static Row Get(string hash, string name)
		{
			Row r;
			if (!sRows.TryGetValue(hash, out r))
			{
				r = new Row { Hash = hash, FirstSeenUtc = UtcNow() };
				sRows[hash] = r; Dirty = true;
			}
			if (!string.IsNullOrEmpty(name) && r.Name != name) { r.Name = name; Dirty = true; }
			return r;
		}

		internal static Row Find(string name)
		{
			Row best = null;
			foreach (var r in sRows.Values)
			{
				if (!string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
				if (best == null || string.CompareOrdinal(r.LastSeenUtc, best.LastSeenUtc) > 0) best = r;
			}
			return best;
		}

		internal static string UtcNow() { return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture); }

		internal static void NewDay()
		{
			DayKills = DayElites = DayBosses = DayDeaths = 0; DayHunters.Clear(); Dirty = true;
		}

		internal static void WriteIfDue(float now, bool force)
		{
			if (!Dirty) return;
			if (!force && now < sNextWrite) return;
			int every = Mathf.Clamp(ChroniclePlugin.WriteEverySeconds.Value, 15, 600);
			sNextWrite = now + every;
			try
			{
				var sb = new StringBuilder(4096);
				long tk = 0, te = 0, tb = 0, td = 0, tq = 0;
				foreach (var r in sRows.Values) { tk += r.Kills; te += r.Elites; tb += r.Bosses; td += r.Deaths; tq += r.Bounties; }
				sb.Append("{\n  \"schema\": 1,\n  \"mod\": \"").Append(ChroniclePlugin.ModId).Append("\",\n  \"version\": \"").Append(ChroniclePlugin.Version).Append("\",\n  \"updated_utc\": \"").Append(UtcNow()).Append("\",\n");
				sb.Append("  \"totals\": {\"kills\": ").Append(tk).Append(", \"elites\": ").Append(te).Append(", \"bosses\": ").Append(tb).Append(", \"deaths\": ").Append(td).Append(", \"bounties\": ").Append(tq).Append("},\n");
				sb.Append("  \"today\": {\"kills\": ").Append(DayKills).Append(", \"elites\": ").Append(DayElites).Append(", \"bosses\": ").Append(DayBosses).Append(", \"deaths\": ").Append(DayDeaths).Append(", \"hunters\": {");
				bool first = true;
				foreach (var kv in DayHunters) { if (!first) sb.Append(", "); first = false; MiniJson.WriteString(sb, kv.Key); sb.Append(": ").Append(kv.Value); }
				sb.Append("}},\n  \"players\": [");
				first = true;
				foreach (var r in sRows.Values)
				{
					sb.Append(first ? "\n" : ",\n"); first = false;
					sb.Append("    {\"hash\": "); MiniJson.WriteString(sb, r.Hash);
					sb.Append(", \"name\": "); MiniJson.WriteString(sb, r.Name);
					sb.Append(", \"kills\": ").Append(r.Kills).Append(", \"elites\": ").Append(r.Elites).Append(", \"bosses\": ").Append(r.Bosses);
					sb.Append(", \"deaths\": ").Append(r.Deaths).Append(", \"bounties\": ").Append(r.Bounties);
					sb.Append(", \"seconds\": ").Append(((long)r.Seconds).ToString(CultureInfo.InvariantCulture));
					sb.Append(", \"first_seen_utc\": "); MiniJson.WriteString(sb, r.FirstSeenUtc);
					sb.Append(", \"last_seen_utc\": "); MiniJson.WriteString(sb, r.LastSeenUtc);
					sb.Append(", \"by_type\": {");
					bool f2 = true;
					foreach (var kv in r.ByType) { if (!f2) sb.Append(", "); f2 = false; MiniJson.WriteString(sb, kv.Key); sb.Append(": ").Append(kv.Value); }
					sb.Append("}}");
				}
				sb.Append("\n  ]\n}\n");
				string tmp = sFile + ".tmp";
				File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
				if (File.Exists(sFile)) File.Replace(tmp, sFile, null); else File.Move(tmp, sFile);
				Dirty = false;
			}
			catch (Exception e) { ModKit.Fail("write", e); }
		}
	}

	internal static class Hooks
	{
		private static float sNextTick, sLastTick = -1f;
		private static bool sClockKnown, sWasNight;
		private static readonly Dictionary<ulong, float> sSeenMonsters = new Dictionary<ulong, float>();
		private static readonly Dictionary<string, float> sLastDeath = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
		private static readonly long[] sKillMarks = { 100, 500, 1000, 2500, 5000, 10000, 25000, 50000, 100000 };

		internal static void Tick()
		{
			if (ModKit.Off) return;
			try
			{
				ModKit.Pump();
				float now = Time.realtimeSinceStartup;
				if (now < sNextTick) return;
				sNextTick = now + 1f;
				if (!ModKit.OnServer()) return;
				float dt = sLastTick < 0f ? 0f : Mathf.Clamp(now - sLastTick, 0f, 5f);
				sLastTick = now;

				string stamp = null;
				foreach (var p in ModKit.Players())
				{
					var hash = ModKit.HashOf(p);
					if (hash.Length == 0) continue;
					var row = Store.Get(hash, ModKit.NameOf(p));
					row.Seconds += dt;
					if (stamp == null) stamp = Store.UtcNow();
					row.LastSeenUtc = stamp;
					Store.Dirty = true;
				}

				bool night; GameTime time;
				if (ModKit.TryClock(out night, out time))
				{
					if (sClockKnown && sWasNight && !night) Dawn();
					sWasNight = night; sClockKnown = true;
				}
				Store.WriteIfDue(now, false);
			}
			catch (Exception e) { ModKit.Fail("tick", e); }
		}

		private static void Dawn()
		{
			try
			{
				if (ChroniclePlugin.DawnDigest.Value && (Store.DayKills > 0 || Store.DayDeaths > 0) && ModKit.Players().Count > 0)
				{
					string top = ""; long best = 0;
					foreach (var kv in Store.DayHunters)
					{
						if (kv.Value <= best) continue;
						best = kv.Value; top = kv.Key;
					}
					string topName = "";
					if (top.Length > 0) foreach (var r in Store.Rows) if (string.Equals(r.Hash, top, StringComparison.OrdinalIgnoreCase)) { topName = r.Name; break; }
					var line = "Yesterday: " + Count(Store.DayKills, "monster") + " slain";
					if (Store.DayElites > 0) line += " (" + Store.DayElites + " elite)";
					if (Store.DayBosses > 0) line += ", " + Count(Store.DayBosses, "boss", "bosses") + " felled";
					line += ", " + Count(Store.DayDeaths, "death") + ".";
					if (topName.Length > 0) line += " Top hunter: " + topName + " with " + best + ".";
					ModKit.Broadcast(line);
					ModKit.Say("Dawn digest sent: " + line);
				}
				Store.NewDay();
			}
			catch (Exception e) { ModKit.Fail("dawn", e); }
		}

		private static string Count(long n, string one, string many = null) { return n.ToString("N0", CultureInfo.InvariantCulture) + " " + (n == 1 ? one : (many ?? one + "s")); }

		// The server's death path names the player it credits with the kill (the attacker, or
		// the closest player in the scene when the attacker was a summon or an effect).
		internal static void MonsterDeath(Player killer, Monster monster)
		{
			if (ModKit.Off) return;
			try
			{
				if (killer == null || monster == null || !ModKit.OnServer()) return;
				if (killer.OwnerClientId == NetworkManager.ServerClientId) return;
				float now = Time.realtimeSinceStartup;
				ulong id = monster.NetworkObjectId;
				float seen;
				if (sSeenMonsters.TryGetValue(id, out seen) && now - seen < 10f) return;
				if (sSeenMonsters.Count > 256) sSeenMonsters.Clear();
				sSeenMonsters[id] = now;

				var hash = ModKit.HashOf(killer);
				if (hash.Length == 0) return;
				var row = Store.Get(hash, ModKit.NameOf(killer));

				string type = "Unknown"; bool boss = false, elite = false;
				try
				{
					var cfg = monster.MonsterConfiguration;
					if (cfg != null)
					{
						type = cfg.MonsterType.ToString();
						var danger = cfg.DangerLevel;
						boss = danger == DangerLevel.Boss || danger == DangerLevel.MiniBoss;
					}
				}
				catch { }
				try { elite = monster.WasEmpoweredOnDeath || monster.IsEmpowered; } catch { }
				if (!elite && type.EndsWith("Empowered", StringComparison.Ordinal)) elite = true;

				row.Kills++;
				long t; row.ByType.TryGetValue(type, out t); row.ByType[type] = t + 1;
				Store.DayKills++;
				long d; Store.DayHunters.TryGetValue(hash, out d); Store.DayHunters[hash] = d + 1;
				if (elite) { row.Elites++; Store.DayElites++; }
				if (boss) { row.Bosses++; Store.DayBosses++; }
				Store.Dirty = true;
				ModKit.Dbg("kill: " + row.Name + " -> " + type + (elite ? " (elite)" : "") + (boss ? " (boss)" : "") + " total=" + row.Kills);

				if (!ChroniclePlugin.Milestones.Value) return;
				if (boss && row.Bosses == 1) Announce(row.Name + " felled their first boss on this server.");
				foreach (var mark in sKillMarks)
					if (row.Kills == mark) { Announce(row.Name + " has slain " + mark.ToString("N0", CultureInfo.InvariantCulture) + " monsters on this server."); break; }
			}
			catch (Exception e) { ModKit.Fail("kill", e); }
		}

		private static void Announce(string line)
		{
			ModKit.Broadcast(line);
			ModKit.Say("Milestone: " + line);
		}

		internal static void PlayerDeath(Player __instance)
		{
			if (ModKit.Off) return;
			try
			{
				if (__instance == null || !ModKit.OnServer()) return;
				if (__instance.OwnerClientId == NetworkManager.ServerClientId) return;
				var hash = ModKit.HashOf(__instance);
				if (hash.Length == 0) return;
				float now = Time.realtimeSinceStartup, last;
				if (sLastDeath.TryGetValue(hash, out last) && now - last < 5f) return;
				sLastDeath[hash] = now;
				var row = Store.Get(hash, ModKit.NameOf(__instance));
				row.Deaths++; Store.DayDeaths++; Store.Dirty = true;
				ModKit.Dbg("death: " + row.Name + " total=" + row.Deaths);
			}
			catch (Exception e) { ModKit.Fail("death", e); }
		}

		internal static void DeedDone(string completingPlayerHash)
		{
			if (ModKit.Off) return;
			try
			{
				if (string.IsNullOrEmpty(completingPlayerHash) || !ModKit.OnServer()) return;
				string name = "";
				foreach (var p in ModKit.Players()) if (string.Equals(ModKit.HashOf(p), completingPlayerHash, StringComparison.OrdinalIgnoreCase)) { name = ModKit.NameOf(p); break; }
				var row = Store.Get(completingPlayerHash, name);
				row.Bounties++; Store.Dirty = true;
				ModKit.Dbg("bounty: " + row.Name + " total=" + row.Bounties);
			}
			catch (Exception e) { ModKit.Fail("bounty", e); }
		}

		// /stats [name], /top [kills|elites|bosses|deaths|time|bounties]. A line this mod
		// answers is not relayed to the other players.
		[HarmonyPriority(Priority.First)]
		internal static bool Chat(Message message)
		{
			if (ModKit.Off) return true;
			try
			{
				if (!ChroniclePlugin.Commands.Value || !ModKit.OnServer()) return true;
				string verb, rest; ulong source;
				if (!ModKit.IsCommand(message, out verb, out rest, out source)) return true;
				if (verb != "stats" && verb != "top") return true;
				bool everyone = !ChroniclePlugin.PrivateReplies.Value;
				if (verb == "stats") ModKit.Whisper(source, StatsLine(source, rest), everyone);
				else foreach (var line in TopLines(rest)) ModKit.Whisper(source, line, everyone);
				return false;
			}
			catch (Exception e) { ModKit.Fail("chat", e); return true; }
		}

		private static string StatsLine(ulong source, string who)
		{
			Row row = null;
			if (who.Length > 0)
			{
				row = Store.Find(who);
				if (row == null) return "Chronicle has no record of '" + who + "'.";
			}
			else
			{
				var p = ModKit.PlayerOf(source);
				var hash = p != null ? ModKit.HashOf(p) : "";
				if (hash.Length == 0) return "Chronicle does not know your character yet. Try again in a moment.";
				row = Store.Get(hash, ModKit.NameOf(p));
			}
			var line = row.Name + ": " + Count(row.Kills, "kill");
			if (row.Elites > 0 || row.Bosses > 0) line += " (" + row.Elites + " elite, " + row.Bosses + " boss)";
			line += ", " + Count(row.Deaths, "death") + ", " + Count(row.Bounties, "bounty", "bounties") + ", " + Played(row.Seconds) + " played.";
			return line;
		}

		private static string Played(double seconds)
		{
			long m = (long)(seconds / 60.0);
			if (m < 60) return m + "m";
			return (m / 60) + "h " + (m % 60) + "m";
		}

		private static List<string> TopLines(string what)
		{
			what = (what ?? "").Trim().ToLowerInvariant();
			string label; Func<Row, double> pick;
			switch (what)
			{
				case "": case "kills": case "kill": label = "kills"; pick = r => r.Kills; break;
				case "elites": case "elite": label = "elite kills"; pick = r => r.Elites; break;
				case "bosses": case "boss": label = "boss kills"; pick = r => r.Bosses; break;
				case "deaths": case "death": label = "deaths"; pick = r => r.Deaths; break;
				case "time": case "played": case "playtime": label = "time played"; pick = r => r.Seconds; break;
				case "bounties": case "bounty": label = "bounties"; pick = r => r.Bounties; break;
				default: return new List<string> { "Try /top kills, elites, bosses, deaths, bounties or time." };
			}
			var rows = new List<Row>();
			foreach (var r in Store.Rows) if (pick(r) > 0 && r.Name.Length > 0) rows.Add(r);
			rows.Sort((a, b) => { int c = pick(b).CompareTo(pick(a)); return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name); });
			var lines = new List<string>();
			if (rows.Count == 0) { lines.Add("Top " + label + ": nothing recorded yet."); return lines; }
			int n = Math.Min(rows.Count, Mathf.Clamp(ChroniclePlugin.TopCount.Value, 3, 10));
			var sb = new StringBuilder("Top " + label + ": ");
			for (int i = 0; i < n; i++)
			{
				string value = label == "time played" ? Played(rows[i].Seconds) : ((long)pick(rows[i])).ToString("N0", CultureInfo.InvariantCulture);
				string part = (i + 1) + ". " + rows[i].Name + " " + value;
				if (sb.Length + part.Length > 150) { lines.Add(sb.ToString().TrimEnd(' ', ',')); sb.Clear(); }
				sb.Append(part).Append(i + 1 < n ? ", " : "");
			}
			if (sb.Length > 0) lines.Add(sb.ToString().TrimEnd(' ', ','));
			return lines;
		}
	}

	// A reader and a string writer for the one file this mod owns. Small on purpose.
	internal static class MiniJson
	{
		internal static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }
		internal static Dictionary<string, object> Obj(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null; }
		internal static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v is string ? (string)v : ""; }
		internal static double Num(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v is double ? (double)v : 0.0; }
		internal static long Long(Dictionary<string, object> d, string k) { return (long)Num(d, k); }

		internal static void WriteString(StringBuilder sb, string s)
		{
			sb.Append('"');
			foreach (char c in s ?? "")
			{
				if (c == '"') sb.Append("\\\""); else if (c == '\\') sb.Append("\\\\");
				else if (c == '\n') sb.Append("\\n"); else if (c == '\r') sb.Append("\\r"); else if (c == '\t') sb.Append("\\t");
				else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
				else sb.Append(c);
			}
			sb.Append('"');
		}

		private static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

		private static object Value(string s, ref int i)
		{
			Skip(s, ref i);
			if (i >= s.Length) throw new FormatException("unexpected end");
			char c = s[i];
			if (c == '{')
			{
				var d = new Dictionary<string, object>(); i++;
				Skip(s, ref i);
				if (i < s.Length && s[i] == '}') { i++; return d; }
				while (true)
				{
					Skip(s, ref i);
					string key = String(s, ref i);
					Skip(s, ref i);
					if (i >= s.Length || s[i] != ':') throw new FormatException("expected ':'");
					i++;
					d[key] = Value(s, ref i);
					Skip(s, ref i);
					if (i < s.Length && s[i] == ',') { i++; continue; }
					if (i < s.Length && s[i] == '}') { i++; return d; }
					throw new FormatException("expected ',' or '}'");
				}
			}
			if (c == '[')
			{
				var l = new List<object>(); i++;
				Skip(s, ref i);
				if (i < s.Length && s[i] == ']') { i++; return l; }
				while (true)
				{
					l.Add(Value(s, ref i));
					Skip(s, ref i);
					if (i < s.Length && s[i] == ',') { i++; continue; }
					if (i < s.Length && s[i] == ']') { i++; return l; }
					throw new FormatException("expected ',' or ']'");
				}
			}
			if (c == '"') return String(s, ref i);
			if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
			if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
			if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
			int start = i;
			while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
			if (i == start) throw new FormatException("unexpected '" + c + "'");
			return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
		}

		private static string String(string s, ref int i)
		{
			if (i >= s.Length || s[i] != '"') throw new FormatException("expected a string");
			i++;
			var sb = new StringBuilder();
			while (i < s.Length)
			{
				char c = s[i++];
				if (c == '"') return sb.ToString();
				if (c != '\\') { sb.Append(c); continue; }
				if (i >= s.Length) break;
				char e = s[i++];
				switch (e)
				{
					case 'n': sb.Append('\n'); break; case 'r': sb.Append('\r'); break; case 't': sb.Append('\t'); break;
					case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break;
					case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
					default: sb.Append(e); break;
				}
			}
			throw new FormatException("unterminated string");
		}
	}
}
