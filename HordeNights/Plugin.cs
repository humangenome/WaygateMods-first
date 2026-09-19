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
using UnityEngine.AI;
using WaygateMods.Kit;

namespace WaygateMods.HordeNights
{
	// Waves of monsters come for the players, on a schedule or when an admin calls one.
	//
	// The game ships a wave engine (ArenaManager) but no manager object exists in the world
	// scenes, so the mod runs its own loop with that engine's shape: a warning, a wave spawned
	// round the players, the next wave when the last one is dead or its time is up, healing
	// and a gold drop between waves, and a clean-up that removes every monster it made.
	//
	// Monsters are made by the game's own runtime spawner, SceneHandler.SpawnMonsterInstance,
	// the call its wave engine, its summons and its spawn spells use. They are ordinary
	// networked monsters: every client sees them, fights them and is paid for them by the
	// game's own kill path. They are spawned as transient, so the world save never holds one.
	//
	// Server-side only. Nothing is written to a character or to the world save.
	[BepInPlugin(PluginId, "Horde Nights", Version)]
	public class HordeNightsPlugin : BasePlugin
	{
		public const string PluginId = "com.humangenome.waygatemods.hordenights";
		public const string ModId = "HumanGenome-HordeNights";
		public const string Version = "1.0.0";

		internal static ConfigEntry<int> EveryNights, AtHour, WarnSeconds, MinPlayers;
		internal static ConfigEntry<bool> WithBloodMoon;
		internal static ConfigEntry<string> Theme, CustomWaves;
		internal static ConfigEntry<int> Waves, MaxAlive, WaveSeconds, RestSeconds, MaxMinutes;
		internal static ConfigEntry<float> Size, PerExtraPlayer, HealthPerWave, DamagePerWave, SpawnDistance;
		internal static ConfigEntry<bool> HealBetweenWaves, MonsterLoot;
		internal static ConfigEntry<int> GoldPerWave, GoldOnVictory;
		internal static ConfigEntry<string> ExcludeAreas, Admins;
		internal static ConfigEntry<string> MsgWarning, MsgWave, MsgCleared, MsgVictory, MsgWithdraw, MsgOverrun;

		public override void Load()
		{
			EveryNights = Config.Bind("Schedule", "EveryNights", 3, "A horde comes every Nth night. 0 = only when an admin calls one. Up to 30.");
			AtHour = Config.Bind("Schedule", "AtHour", 21, "The game hour the warning goes out, 0 to 23. The game calls it night from 20.");
			WarnSeconds = Config.Bind("Schedule", "WarnSeconds", 120, "Seconds between the warning and the first wave. 0 to 600.");
			MinPlayers = Config.Bind("Schedule", "MinPlayers", 1, "A scheduled horde needs this many players online. 1 to 16.");
			WithBloodMoon = Config.Bind("Schedule", "WithBloodMoon", false, "With the Dark Nights mod installed: a horde also comes on every blood moon.");

			Theme = Config.Bind("Horde", "Theme", "Droops", "Droops, Goblins, Wolves, Corrupted, or Random for a different one each time. Droops are the weakest, wolves and the corrupted the toughest.");
			CustomWaves = Config.Bind("Horde", "CustomWaves", "", "Your own waves instead of a theme. Waves are split by |, monsters by comma: GoblinRipper x4, GoblinFlaskrat x2 | GoblinBasher x3. Empty = use the theme.");
			Waves = Config.Bind("Horde", "Waves", 5, "Waves per horde, 1 to 10.");
			Size = Config.Bind("Horde", "Size", 1f, "Monsters per wave, as a multiple of the theme's own numbers. 0.5 to 3.");
			PerExtraPlayer = Config.Bind("Horde", "PerExtraPlayer", 0.5f, "Each defender after the first adds this share of monsters. 0 to 1.");
			MaxAlive = Config.Bind("Horde", "MaxAlive", 10, "Most horde monsters alive at once. The rest of a wave arrives as monsters die. Each one costs the server about 3 percent of a CPU core while it lives. 4 to 40.");
			WaveSeconds = Config.Bind("Horde", "WaveSeconds", 150, "The next wave arrives after this many seconds even when the last one still stands. 30 to 600.");
			RestSeconds = Config.Bind("Horde", "RestSeconds", 15, "The pause after a cleared wave. 5 to 120.");
			MaxMinutes = Config.Bind("Horde", "MaxMinutes", 20, "The horde gives up after this many minutes. 5 to 60.");
			HealthPerWave = Config.Bind("Horde", "HealthPerWave", 0.1f, "Monster health added per wave after the first. 0.1 = ten percent. 0 to 1.");
			DamagePerWave = Config.Bind("Horde", "DamagePerWave", 0.1f, "Monster damage added per wave after the first. 0 to 1.");
			SpawnDistance = Config.Bind("Horde", "SpawnDistance", 12f, "How far from a defender the monsters appear. 3 to 25.");
			HealBetweenWaves = Config.Bind("Horde", "HealBetweenWaves", true, "Defenders are healed when a wave is cleared.");
			MonsterLoot = Config.Bind("Horde", "MonsterLoot", true, "Horde monsters drop their normal loot. XP from kills is always the game's own.");

			GoldPerWave = Config.Bind("Rewards", "GoldPerWave", 40, "Gold dropped beside each defender when a wave is cleared. 0 to 1000.");
			GoldOnVictory = Config.Bind("Rewards", "GoldOnVictory", 250, "Gold dropped beside each defender when the last wave is cleared. 0 to 5000.");

			ExcludeAreas = Config.Bind("Areas", "Exclude", "EarlwoodVillage, GuildHall, PlayerBase", "Areas a horde never comes to, by the game's area names, split by comma.");
			Admins = Config.Bind("Commands", "Admins", "", "Character names (or character ids) allowed to type /horde start and /horde stop, split by comma. Empty = nobody.");

			MsgWarning = Config.Bind("Messages", "Warning", "{horde} is closing in on {area}. It arrives in {seconds} seconds.", "Chat line when a horde is on its way. Empty = none.");
			MsgWave = Config.Bind("Messages", "Wave", "Wave {wave} of {waves}: {name}.", "Chat line when a wave arrives. Empty = none.");
			MsgCleared = Config.Bind("Messages", "WaveCleared", "Wave {wave} cleared.", "Chat line when a wave is cleared. Empty = none.");
			MsgVictory = Config.Bind("Messages", "Victory", "The horde is broken. {area} is safe.", "Chat line when the last wave is cleared. Empty = none.");
			MsgWithdraw = Config.Bind("Messages", "Withdraw", "The horde withdraws from {area}.", "Chat line when the horde gives up or is called off. Empty = none.");
			MsgOverrun = Config.Bind("Messages", "Overrun", "The horde has overrun {area}.", "Chat line when every defender is dead. Empty = none.");

			var harmony = new Harmony(PluginId);
			ModKit.Init(Log, harmony, "Horde Nights");
			ModKit.WatchConfig(Config);
			try
			{
				State.Open(Path.Combine(Paths.ConfigPath, ModId));
				ModKit.Patch(typeof(TransitionManager), "Update", null, typeof(Horde), null, nameof(Horde.Tick), true, "the server clock");
				ModKit.Patch(typeof(QuestManager), "RegisterMonsterDeath", new[] { typeof(Player), typeof(Monster), typeof(InGameEvent) }, typeof(Horde), null, nameof(Horde.MonsterDeath), false, "naming the top slayer");
				ModKit.Patch(typeof(ChatSystem), "SendMessageToServerServerRpc", null, typeof(Horde), nameof(Horde.Chat), null, false, "the /horde chat command");
				if (!ModKit.Off)
				{
					int every = Mathf.Clamp(EveryNights.Value, 0, 30);
					ModKit.Say("Horde Nights " + Version + " is on: " + (every > 0 ? "a horde every " + every + (every == 1 ? " night" : " nights") + " at " + Mathf.Clamp(AtHour.Value, 0, 23).ToString("00", CultureInfo.InvariantCulture) + ":00 (the next one is " + Schedule.NextText(every) + ")." : "hordes come only when an admin calls one."));
					if (State.WasActive) { ModKit.Say("The server stopped during a horde. It did not carry over: horde monsters are never saved."); State.WasActive = false; State.Active = false; State.Save(); }
				}
			}
			catch (Exception e) { ModKit.Dbg("load: " + e); ModKit.TurnOff("it could not start"); }
		}
	}

	// What the mod remembers between restarts: how many horde hours it has seen, and whether
	// the server stopped in the middle of a horde. A plain text file.
	internal static class State
	{
		private static string sFile = "";
		internal static string Dir = "";
		internal static long Hours;
		internal static bool Active, WasActive;

		internal static void Open(string dir)
		{
			Directory.CreateDirectory(dir);
			Dir = dir;
			sFile = Path.Combine(dir, "horde.txt");
			if (!File.Exists(sFile)) return;
			try
			{
				foreach (var line in File.ReadAllLines(sFile))
				{
					int eq = line.IndexOf('='); if (eq <= 0) continue;
					string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
					if (k == "horde_hours") long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out Hours);
					else if (k == "horde_running") WasActive = v == "true";
				}
				if (Hours < 0) Hours = 0;
			}
			catch (Exception e) { ModKit.Dbg("state read: " + e.Message); Hours = 0; WasActive = false; }
		}

		internal static void Save()
		{
			try
			{
				string tmp = sFile + ".tmp";
				File.WriteAllLines(tmp, new[] { "horde_hours=" + Hours.ToString(CultureInfo.InvariantCulture), "horde_running=" + (Active ? "true" : "false") });
				if (File.Exists(sFile)) File.Replace(tmp, sFile, null); else File.Move(tmp, sFile);
			}
			catch (Exception e) { ModKit.Fail("state write", e); }
		}
	}

	// The themes. A row is one wave for one defender; Size and PerExtraPlayer scale it.
	internal sealed class WaveRow
	{
		internal string Name = "";
		internal readonly List<KeyValuePair<MonsterType, int>> Spawns = new List<KeyValuePair<MonsterType, int>>();
	}

	internal static class Themes
	{
		internal static readonly string[] Names = { "Droops", "Goblins", "Wolves", "Corrupted" };

		internal static string Title(string theme)
		{
			switch (theme)
			{
				case "Goblins": return "A goblin warband";
				case "Wolves": return "A wolf pack";
				case "Droops": return "A droop swarm";
				case "Corrupted": return "A corrupted host";
				default: return "A horde";
			}
		}

		private static WaveRow Row(string name, params object[] spawns)
		{
			var r = new WaveRow { Name = name };
			for (int i = 0; i + 1 < spawns.Length; i += 2) r.Spawns.Add(new KeyValuePair<MonsterType, int>((MonsterType)spawns[i], (int)spawns[i + 1]));
			return r;
		}

		internal static List<WaveRow> Rows(string theme)
		{
			var l = new List<WaveRow>();
			switch (theme)
			{
				case "Goblins":
					l.Add(Row("goblin scouts", MonsterType.GoblinRipper, 3, MonsterType.GoblinFlaskrat, 1));
					l.Add(Row("goblin raiders", MonsterType.GoblinRipper, 3, MonsterType.GoblinBasher, 2));
					l.Add(Row("goblin alchemists", MonsterType.GoblinFlaskrat, 3, MonsterType.GoblinBasher, 2));
					l.Add(Row("goblin veterans", MonsterType.GoblinRipperT2, 3, MonsterType.GoblinFlaskratT2, 2));
					l.Add(Row("the warband's champions", MonsterType.GoblinBasherT2, 2, MonsterType.GoblinRipperT2, 2, MonsterType.GoblinFlaskratT2, 1));
					break;
				case "Wolves":
					l.Add(Row("lone wolves", MonsterType.WildwoodWolf, 3));
					l.Add(Row("the pack", MonsterType.WildwoodWolf, 5));
					l.Add(Row("aetherfangs", MonsterType.WildwoodWolf, 3, MonsterType.WildwoodAetherfangWolf, 2));
					l.Add(Row("the aetherfang pack", MonsterType.WildwoodAetherfangWolf, 4));
					l.Add(Row("the alpha", MonsterType.WildwoodWolfAlpha, 1, MonsterType.WildwoodWolf, 4));
					break;
				case "Droops":
					l.Add(Row("drooplets", MonsterType.Drooplet, 5));
					l.Add(Row("droops", MonsterType.Droop, 3, MonsterType.Drooplet, 3));
					l.Add(Row("spitters", MonsterType.Droop, 3, MonsterType.DroopSpitter, 2));
					l.Add(Row("the swarm", MonsterType.DroopSpitter, 3, MonsterType.Droop, 3));
					l.Add(Row("the monstrous droop", MonsterType.MonstrousDroop, 1, MonsterType.Droop, 4));
					break;
				case "Corrupted":
					l.Add(Row("corrupted droops", MonsterType.CorruptedDroop, 4));
					l.Add(Row("corrupted wolves", MonsterType.CorruptedWildwoodWolf, 4));
					l.Add(Row("rotting treants", MonsterType.RottingTreant, 2, MonsterType.CorruptedDroop, 3));
					l.Add(Row("the rot spreads", MonsterType.CorruptedWildwoodWolf, 3, MonsterType.RottingTreant, 2));
					l.Add(Row("the corrupted treant", MonsterType.CorruptedTreant, 1, MonsterType.CorruptedDroop, 4));
					break;
			}
			return l;
		}

		// "GoblinRipper x4, GoblinFlaskrat x2 | GoblinBasher x3". A name the game does not know
		// is skipped with one line; a wave with nothing left in it is dropped.
		internal static List<WaveRow> Custom(string text)
		{
			var l = new List<WaveRow>();
			if (string.IsNullOrWhiteSpace(text)) return l;
			foreach (var waveText in text.Split('|'))
			{
				var row = new WaveRow();
				foreach (var part in waveText.Split(','))
				{
					var p = part.Trim(); if (p.Length == 0) continue;
					string name = p; int count = 1;
					int x = p.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
					if (x > 0) { int n; if (int.TryParse(p.Substring(x + 2).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { count = n; name = p.Substring(0, x).Trim(); } }
					MonsterType type;
					if (!Enum.TryParse(name, true, out type) || !Enum.IsDefined(typeof(MonsterType), type)) { ModKit.Say("Horde Nights: '" + name + "' in CustomWaves is not a monster the game knows. It was skipped."); continue; }
					row.Spawns.Add(new KeyValuePair<MonsterType, int>(type, Mathf.Clamp(count, 1, 40)));
				}
				if (row.Spawns.Count == 0) continue;
				row.Name = "wave " + (l.Count + 1);
				l.Add(row);
				if (l.Count >= 10) break;
			}
			return l;
		}

		// N waves out of a table: always the first and the last row, the others spread between.
		internal static List<WaveRow> Pick(List<WaveRow> table, int waves)
		{
			var l = new List<WaveRow>();
			if (table.Count == 0) return l;
			for (int i = 0; i < waves; i++)
			{
				int idx;
				if (waves <= 1) idx = table.Count - 1;
				else if (waves >= table.Count) idx = Mathf.Min(i, table.Count - 1);
				else idx = Mathf.RoundToInt(i * (table.Count - 1f) / (waves - 1f));
				l.Add(table[Mathf.Clamp(idx, 0, table.Count - 1)]);
			}
			return l;
		}
	}

	internal static class Schedule
	{
		private static int sLastMinutes = -1;

		internal static string NextText(int every)
		{
			if (every <= 0) return "never";
			long left = every - (State.Hours % every);
			return left == 1 ? "the coming night" : left + " nights away";
		}

		// True once each game day, when the clock passes AtHour. A clock that jumped past the
		// whole hour (players slept, or an admin set the time) counts the night but starts nothing.
		internal static bool HourCame(GameTime time, out bool jumpedPast)
		{
			jumpedPast = false;
			int hour = Mathf.Clamp(HordeNightsPlugin.AtHour.Value, 0, 23);
			int target = hour * 60, cur = time.Hour * 60 + time.Minute, prev = sLastMinutes;
			sLastMinutes = cur;
			if (prev < 0 || prev == cur) return false;
			bool crossed;
			if (cur > prev) crossed = prev < target && target <= cur;
			else if (prev - cur < 12 * 60) return false; // the clock was set back, not a new day
			else crossed = target > prev || target <= cur;
			if (!crossed) return false;
			jumpedPast = time.Hour != hour;
			return true;
		}

		internal static bool DarkNightsBloodMoon()
		{
			try
			{
				var f = Path.Combine(Paths.ConfigPath, "HumanGenome-DarkNights", "nights.txt");
				if (!File.Exists(f)) return false;
				foreach (var line in File.ReadAllLines(f)) if (line.Trim() == "in_blood_moon=true") return true;
			}
			catch (Exception e) { ModKit.Dbg("dark nights state: " + e.Message); }
			return false;
		}
	}

	internal static class Horde
	{
		private const string Marker = "HordeNights:";
		private const int MaxPerHorde = 300;
		private const float CorpseSeconds = 25f;

		private enum Phase { Idle, Warning, Wave, Rest }
		private sealed class Tracked { internal NetworkObject No; internal Monster M; internal MonsterType Type; internal ulong Id; internal float DeadAt = -1f, StillSince = -1f, WalkSince = -1f; internal Vector3 LastPos; internal bool Walking; internal ulong WalkTo; internal int Pulls; }

		private static Phase sPhase = Phase.Idle;
		private static Areas.Scene sScene = Areas.Scene.None;
		private static string sTheme = "", sTitle = "A horde";
		private static List<WaveRow> sWaves = new List<WaveRow>();
		private static int sWave, sMade, sKilled;
		private static float sPhaseEnds, sHordeEnds, sNoDefendersSince = -1f, sNextSpawn, sNextSlow, sNextFile;
		private static readonly Queue<MonsterType> sQueue = new Queue<MonsterType>();
		private static readonly List<Tracked> sAlive = new List<Tracked>();
		private static readonly List<Tracked> sCorpses = new List<Tracked>();
		private static readonly HashSet<ulong> sMine = new HashSet<ulong>();
		private static readonly HashSet<ulong> sToldHere = new HashSet<ulong>();
		private static readonly Dictionary<string, int> sSlayers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		private static float sWaveHealth = 1f, sWaveDamage = 1f;
		private static Il2CppSystem.Action<Monster> sConfigure;
		private static bool sSwept;
		private static int sCensusBefore = -1;

		internal static bool Running { get { return sPhase != Phase.Idle; } }

		// ---- the tick --------------------------------------------------------------------
		internal static void Tick()
		{
			if (ModKit.Off) return;
			try
			{
				ModKit.Pump();
				float now = Time.realtimeSinceStartup;
				if (!ModKit.OnServer()) { if (Running) Forget(); return; }
				if (Running && now >= sNextSpawn) { sNextSpawn = now + 0.5f; SpawnSome(); }
				if (now < sNextSlow) return;
				sNextSlow = now + 1f;
				if (!sSwept) { sSwept = true; SweepStrays("start"); }
				if (now >= sNextFile) { sNextFile = now + 2f; ReadCommandFile(); }
				Clock();
				if (Running) Step(now);
			}
			catch (Exception e) { ModKit.Fail("tick", e); }
		}

		private static void Clock()
		{
			bool night; GameTime time;
			if (!ModKit.TryClock(out night, out time)) return;
			bool jumped;
			if (!Schedule.HourCame(time, out jumped)) return;
			int every = Mathf.Clamp(HordeNightsPlugin.EveryNights.Value, 0, 30);
			State.Hours++; State.Save();
			bool due = every > 0 && State.Hours % every == 0;
			bool blood = !due && HordeNightsPlugin.WithBloodMoon.Value && Schedule.DarkNightsBloodMoon();
			if (!due && !blood) { ModKit.Dbg("horde hour " + State.Hours + ": not tonight"); return; }
			if (jumped) { ModKit.Say("The horde hour passed while the clock jumped ahead. No horde tonight."); return; }
			if (Running) return;
			int need = Mathf.Clamp(HordeNightsPlugin.MinPlayers.Value, 1, 16);
			if (ModKit.Players().Count < need) { ModKit.Say("The horde hour came with fewer than " + need + (need == 1 ? " player" : " players") + " online. No horde tonight."); return; }
			string why;
			if (!Start(Areas.Scene.None, "", blood ? "the blood moon" : "the schedule", out why)) ModKit.Say("No horde tonight: " + why);
		}

		// ---- starting --------------------------------------------------------------------
		private static HashSet<string> Excluded()
		{
			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var part in (HordeNightsPlugin.ExcludeAreas.Value ?? "").Split(',')) { var p = part.Trim().Replace(" ", ""); if (p.Length > 0) set.Add(p); }
			set.Add("None"); set.Add("MainMenu");
			return set;
		}

		private static string CannotHost(Areas.Scene scene)
		{
			if (Excluded().Contains(scene.ToString())) return "no hordes come to " + Words(scene);
			try
			{
				var dm = DeedManager.Singleton;
				DeedDefinition deed; Quest quest;
				if (dm != null && dm.IsSceneLockedByActiveDeed(scene, out deed, out quest)) return Words(scene) + " is locked by a bounty in progress";
			}
			catch (Exception e) { ModKit.Dbg("locked area check: " + e.Message); }
			return null;
		}

		// The area with the most living players that a horde may come to.
		private static Areas.Scene Busiest()
		{
			var count = new Dictionary<Areas.Scene, int>();
			foreach (var p in ModKit.Players())
			{
				try
				{
					if (p.Dead.Value) continue;
					var s = p.Scene.Value;
					if (CannotHost(s) != null) continue;
					int n; count.TryGetValue(s, out n); count[s] = n + 1;
				}
				catch { }
			}
			var best = Areas.Scene.None; int most = 0;
			foreach (var kv in count) if (kv.Value > most) { most = kv.Value; best = kv.Key; }
			return best;
		}

		internal static bool Start(Areas.Scene scene, string theme, string calledBy, out string why)
		{
			why = "";
			if (Running) { why = "a horde is already under way"; return false; }
			if (scene == Areas.Scene.None) scene = Busiest();
			if (scene == Areas.Scene.None) { why = "nobody is in an area a horde can reach"; return false; }
			var no = CannotHost(scene);
			if (no != null) { why = no; return false; }
			if (Defenders(scene, true).Count == 0) { why = "nobody alive is in " + Words(scene); return false; }
			SceneHandler handler = null;
			try { handler = Utility.ReturnSceneHandlerForScene(scene); } catch { }
			if (handler == null || !handler.IsSpawned) { why = Words(scene) + " is not loaded on the server"; return false; }

			// a theme named on the command wins over the owner's own waves
			bool named = false;
			foreach (var n in Themes.Names) if (string.Equals(n, (theme ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) named = true;
			List<WaveRow> table = named ? new List<WaveRow>() : Themes.Custom(HordeNightsPlugin.CustomWaves.Value);
			int waves = Mathf.Clamp(HordeNightsPlugin.Waves.Value, 1, 10);
			if (table.Count > 0) { sTheme = "Custom"; sWaves = table; }
			else
			{
				sTheme = PickTheme(string.IsNullOrWhiteSpace(theme) ? HordeNightsPlugin.Theme.Value : theme);
				sWaves = Themes.Pick(Themes.Rows(sTheme), waves);
			}
			if (sWaves.Count == 0) { why = "no waves are set up"; return false; }
			sTitle = Themes.Title(sTheme);
			sScene = scene; sWave = -1; sMade = 0; sKilled = 0; sNoDefendersSince = -1f;
			sQueue.Clear(); sSlayers.Clear(); sToldHere.Clear();
			float now = Time.realtimeSinceStartup;
			int warn = Mathf.Clamp(HordeNightsPlugin.WarnSeconds.Value, 0, 600);
			sPhase = Phase.Warning; sPhaseEnds = now + warn;
			sHordeEnds = now + warn + Mathf.Clamp(HordeNightsPlugin.MaxMinutes.Value, 5, 60) * 60f;
			State.Active = true; State.Save();
			foreach (var p in Defenders(scene, false)) sToldHere.Add(p.OwnerClientId);
			sCensusBefore = Census(scene);
			Tell(HordeNightsPlugin.MsgWarning.Value, 0, "");
			ModKit.Say(sTitle + " is on its way to " + Words(scene) + " (" + sWaves.Count + (sWaves.Count == 1 ? " wave" : " waves") + ", called by " + calledBy + "). First wave in " + warn + " seconds.");
			return true;
		}

		private static string PickTheme(string wanted)
		{
			wanted = (wanted ?? "").Trim();
			foreach (var n in Themes.Names) if (string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase)) return n;
			return Themes.Names[UnityEngine.Random.Range(0, Themes.Names.Length)];
		}

		// ---- the loop --------------------------------------------------------------------
		private static List<Player> Defenders(Areas.Scene scene, bool livingOnly)
		{
			var l = new List<Player>();
			foreach (var p in ModKit.Players())
			{
				try { if (p.Scene.Value != scene) continue; if (livingOnly && p.Dead.Value) continue; l.Add(p); } catch { }
			}
			return l;
		}

		private static void Step(float now)
		{
			Sweep(now);
			Herd(now);
			var here = Defenders(sScene, false);
			var living = Defenders(sScene, true);

			// a player who walks into the fight is told what is going on
			foreach (var p in here)
			{
				ulong id = p.OwnerClientId;
				if (!sToldHere.Add(id)) continue;
				ModKit.Whisper(id, sPhase == Phase.Warning ? sTitle + " is about to reach this area." : sTitle + " is attacking this area: wave " + (sWave + 1) + " of " + sWaves.Count + ".", false);
			}

			if (sPhase != Phase.Warning)
			{
				if (living.Count > 0) sNoDefendersSince = -1f;
				else if (sNoDefendersSince < 0f) sNoDefendersSince = now;
				else if (now - sNoDefendersSince >= 15f) { End(here.Count > 0 ? HordeNightsPlugin.MsgOverrun.Value : HordeNightsPlugin.MsgWithdraw.Value, here.Count > 0 ? "every defender is dead" : "nobody is left in the area"); return; }
			}
			if (now >= sHordeEnds) { End(HordeNightsPlugin.MsgWithdraw.Value, "its time ran out"); return; }

			switch (sPhase)
			{
				case Phase.Warning:
					if (now < sPhaseEnds) return;
					if (living.Count == 0) { End("", "nobody was there when it arrived"); return; }
					NextWave(now, living.Count);
					return;
				case Phase.Rest:
					if (now >= sPhaseEnds) NextWave(now, Mathf.Max(1, living.Count));
					return;
				case Phase.Wave:
					if (sQueue.Count == 0 && sAlive.Count == 0) { Cleared(now, living); return; }
					if (now >= sPhaseEnds && sWave + 1 < sWaves.Count) { ModKit.Say("Wave " + (sWave + 1) + " still stands (" + sAlive.Count + " left). The next wave arrives anyway."); NextWave(now, Mathf.Max(1, living.Count)); }
					return;
			}
		}

		private static void NextWave(float now, int defenders)
		{
			sWave++;
			var row = sWaves[sWave];
			float scale = Mathf.Clamp(HordeNightsPlugin.Size.Value, 0.5f, 3f) * (1f + Mathf.Clamp01(HordeNightsPlugin.PerExtraPlayer.Value) * Mathf.Max(0, defenders - 1));
			int queued = 0;
			foreach (var kv in row.Spawns)
			{
				int n = Mathf.Clamp(Mathf.RoundToInt(kv.Value * scale), 1, 60);
				for (int i = 0; i < n && sMade + sQueue.Count < MaxPerHorde; i++) { sQueue.Enqueue(kv.Key); queued++; }
			}
			sWaveHealth = 1f + Mathf.Clamp01(HordeNightsPlugin.HealthPerWave.Value) * sWave;
			sWaveDamage = 1f + Mathf.Clamp01(HordeNightsPlugin.DamagePerWave.Value) * sWave;
			sPhase = Phase.Wave;
			sPhaseEnds = now + Mathf.Clamp(HordeNightsPlugin.WaveSeconds.Value, 30, 600);
			Tell(HordeNightsPlugin.MsgWave.Value, 0, row.Name);
			ModKit.Say("Wave " + (sWave + 1) + " of " + sWaves.Count + " (" + row.Name + "): " + queued + (queued == 1 ? " monster for " : " monsters for ") + defenders + (defenders == 1 ? " defender." : " defenders."));
		}

		private static void Cleared(float now, List<Player> living)
		{
			bool last = sWave + 1 >= sWaves.Count;
			int gold = last ? Mathf.Clamp(HordeNightsPlugin.GoldOnVictory.Value, 0, 5000) : Mathf.Clamp(HordeNightsPlugin.GoldPerWave.Value, 0, 1000);
			if (HordeNightsPlugin.HealBetweenWaves.Value) foreach (var p in living) Heal(p);
			int piles = 0;
			if (gold > 0) foreach (var p in living) if (DropGold(p, gold)) piles++;
			if (last)
			{
				string top = TopSlayer();
				End(HordeNightsPlugin.MsgVictory.Value, "the last wave was cleared", (gold > 0 && piles > 0 ? " " + gold + " gold for each defender." : "") + (top.Length > 0 ? " Top slayer: " + top + "." : ""));
				return;
			}
			Tell(HordeNightsPlugin.MsgCleared.Value, gold > 0 && piles > 0 ? gold : 0, "");
			ModKit.Say("Wave " + (sWave + 1) + " cleared." + (piles > 0 ? " " + gold + " gold dropped beside " + piles + (piles == 1 ? " defender." : " defenders.") : ""));
			sPhase = Phase.Rest;
			sPhaseEnds = now + Mathf.Clamp(HordeNightsPlugin.RestSeconds.Value, 5, 120);
		}

		private static string TopSlayer()
		{
			string best = ""; int most = 0;
			foreach (var kv in sSlayers) if (kv.Value > most) { most = kv.Value; best = kv.Key; }
			return most > 0 ? best + " with " + most : "";
		}

		private static void End(string chatLine, string plainReason, string chatTail = "")
		{
			int left = sAlive.Count + sQueue.Count;
			int removed = RemoveAll();
			int census = Census(sScene);
			if (!string.IsNullOrWhiteSpace(chatLine)) ModKit.Broadcast(Fill(chatLine, 0, "") + chatTail);
			ModKit.Say("The horde in " + Words(sScene) + " is over: " + plainReason + ". " + sKilled + " of " + sMade + " monsters were killed" + (left > 0 ? ", " + left + " were still to come or standing" : "") + ", " + removed + " removed. The area held " + sCensusBefore + " monsters before the horde and holds " + census + " now." + (TopSlayer().Length > 0 ? " Top slayer: " + TopSlayer() + "." : ""));
			Forget();
		}

		private static void Forget()
		{
			sPhase = Phase.Idle; sQueue.Clear(); sAlive.Clear(); sCorpses.Clear(); sMine.Clear(); sToldHere.Clear();
			if (State.Active) { State.Active = false; State.Save(); }
		}

		// ---- monsters --------------------------------------------------------------------
		private static void SpawnSome()
		{
			if (sPhase != Phase.Wave || sQueue.Count == 0) return;
			int cap = Mathf.Clamp(HordeNightsPlugin.MaxAlive.Value, 4, 40);
			if (sAlive.Count >= cap) return;
			var living = Defenders(sScene, true);
			if (living.Count == 0) return;
			SceneHandler handler = null;
			try { handler = Utility.ReturnSceneHandlerForScene(sScene); } catch { }
			if (handler == null || !handler.IsSpawned) return;
			if (sConfigure == null) sConfigure = (Il2CppSystem.Action<Monster>)(Action<Monster>)Configure;
			for (int budget = 2; budget > 0 && sQueue.Count > 0 && sAlive.Count < cap; budget--)
			{
				var type = sQueue.Dequeue();
				var anchor = living[UnityEngine.Random.Range(0, living.Count)];
				Vector3 at = SpawnPoint(anchor, HordeNightsPlugin.SpawnDistance.Value);
				NetworkObject no = null;
				try { no = handler.SpawnMonsterInstance(type, at, Marker + type, true, true, InGameEvent.None, sConfigure, true); }
				catch (Exception e) { ModKit.Dbg("spawn " + type + ": " + e.Message); ModKit.Fail("spawning a " + type, e); continue; }
				if (no == null) { ModKit.Dbg("the game made no " + type); continue; }
				Monster m = null; try { m = no.GetComponent<Monster>(); } catch { }
				if (m == null) { try { no.Despawn(true); } catch { } continue; }
				var t = new Tracked { No = no, M = m, Type = type, Id = no.NetworkObjectId, LastPos = at };
				sAlive.Add(t); sMine.Add(t.Id); sMade++;
				Hunt(handler, t);
				ModKit.Dbg("spawned " + type + " id=" + t.Id + " at " + at + " near " + ModKit.NameOf(anchor) + " alive=" + sAlive.Count);
			}
		}

		// The game's own "attack now": nearest player in the area, a combat session, the
		// monster's in-combat behaviour. Its spawner's auto-aggro alone leaves a monster that
		// appeared outside its own sight range standing where it appeared.
		private const float HuntRange = 40f;
		private static void Hunt(SceneHandler handler, Tracked t)
		{
			try { if (handler != null && t.M != null && !t.M.IsDead()) handler.AggroMonster(t.M, HuntRange); }
			catch (Exception e) { ModKit.Dbg("hunt " + t.Id + ": " + e.Message); }
		}

		// How many monsters the area holds, for the log: the same number before a horde and
		// after it is the proof that nothing was left behind.
		private static int Census(Areas.Scene scene)
		{
			int n = 0;
			try
			{
				var all = Monster.SnapshotAll();
				for (int i = 0; all != null && i < all.Count; i++) { var m = all[i]; try { if (m != null && m.IsSpawned && m.Scene.Value == scene) n++; } catch { } }
			}
			catch (Exception e) { ModKit.Dbg("census: " + e.Message); return -1; }
			return n;
		}

		// Some monsters of the game wait for their prey to come to them: spawned a dozen steps
		// away they join the fight and never walk over. A horde monster that stands still away
		// from every defender is first walked to the nearest one with the game's own escort
		// behaviour, and if that does not move it either, it is put down a few steps from them.
		private static float sNextHerd;
		private static void Herd(float now)
		{
			if (now < sNextHerd) return;
			sNextHerd = now + 2f;
			var living = Defenders(sScene, true);
			if (living.Count == 0) return;
			SceneHandler handler = null; try { handler = Utility.ReturnSceneHandlerForScene(sScene); } catch { }
			foreach (var t in sAlive)
			{
				try
				{
					var pos = t.M.transform.position; float near = 9999f; Player target = null;
					foreach (var p in living) { float d = Vector2.Distance(pos, p.transform.position); if (d < near) { near = d; target = p; } }
					bool moved = Vector2.Distance(pos, t.LastPos) > 0.75f;
					t.LastPos = pos;
					var b = t.M.GetComponent<MonsterBehaviour>();
					ModKit.Dbg("herd id=" + t.Id + " " + t.Type + " nearest=" + near.ToString("F1", CultureInfo.InvariantCulture) + " moved=" + moved + " walking=" + t.Walking + " engaged=" + t.M.IsEngaged.Value + " hp=" + t.M.Health.Value.ToString("F0", CultureInfo.InvariantCulture) + "/" + t.M.MaxHealth.Value.ToString("F0", CultureInfo.InvariantCulture));
					// the defender it was walking to died or left: let go and start over
					if (t.Walking && target != null && target.OwnerClientId != t.WalkTo) { t.Walking = false; t.StillSince = -1f; try { if (b != null) b.ClearEscortBehaviour(); } catch { } }
					if (near <= 4.5f || target == null)
					{
						if (t.Walking) { t.Walking = false; try { if (b != null) b.ClearEscortBehaviour(); } catch { } Hunt(handler, t); }
						t.StillSince = -1f; continue;
					}
					if (moved && !t.Walking) { t.StillSince = -1f; continue; }
					if (moved && t.Walking) { t.WalkSince = now; continue; }
					if (t.StillSince < 0f) { t.StillSince = now; continue; }
					if (!t.Walking)
					{
						if (now - t.StillSince < 2f || b == null) continue;
						b.SetEscortTarget(target.transform, 2.5f, null);
						t.Walking = true; t.WalkSince = now; t.WalkTo = target.OwnerClientId;
						ModKit.Dbg("herd id=" + t.Id + " walks to " + ModKit.NameOf(target));
						continue;
					}
					if (now - t.WalkSince < 9f) continue;
					t.Walking = false; t.StillSince = -1f;
					try { if (b != null) b.ClearEscortBehaviour(); } catch { }
					PutBeside(handler, t, target);
				}
				catch (Exception e) { ModKit.Dbg("herd " + t.Id + ": " + e.Message); }
			}
		}

		private static void PutBeside(SceneHandler handler, Tracked t, Player target)
		{
			Vector3 at = SpawnPoint(target, 4.5f);
			var agent = t.M.GetComponent<NavMeshAgent>();
			bool ok = agent != null && agent.enabled && agent.Warp(at);
			if (!ok) t.M.transform.position = at;
			t.LastPos = at; t.Pulls++;
			try { if (handler != null) handler.PlayMonsterSpawnVFXClientRpc(t.Type, at, handler.GetPlayersInSceneRpcParams()); } catch (Exception e) { ModKit.Dbg("vfx: " + e.Message); }
			Hunt(handler, t);
			ModKit.Dbg("herd id=" + t.Id + " put beside " + ModKit.NameOf(target) + " (agent warp " + ok + ")");
		}

		// Runs inside the game's spawner before the monster goes on the network: the same
		// place its own wave engine sets a wave's health and damage.
		private static void Configure(Monster m)
		{
			try
			{
				if (m == null) return;
				if (!HordeNightsPlugin.MonsterLoot.Value) m.SuppressNormalLoot = true;
				if (sWaveHealth > 1.001f || sWaveDamage > 1.001f) m.ApplySpawnStatProfile(sWaveHealth, sWaveDamage, 0, new Il2CppSystem.Collections.Generic.List<MonsterStartingEffect>());
				ModKit.Dbg("configured: health x" + sWaveHealth.ToString("0.##", CultureInfo.InvariantCulture) + " damage x" + sWaveDamage.ToString("0.##", CultureInfo.InvariantCulture) + " loot " + (m.SuppressNormalLoot ? "off" : "on"));
			}
			catch (Exception e) { ModKit.Dbg("configure: " + e.Message); }
		}

		// A point on walkable ground in a ring round the defender, with a clear walk line
		// between the two, so the monster can come straight at them. The ring shrinks when the
		// place is too tight for it; the defender stands on the area's walk mesh, so the last
		// resort is a step away from them.
		private static Vector3 SpawnPoint(Player anchor, float far)
		{
			Vector3 c = anchor.transform.position;
			far = Mathf.Clamp(far, 3f, 25f);
			int agent = 0; try { agent = Utility.ConvertSceneNameToAgentType(sScene); } catch { }
			try
			{
				NavMeshHit feet;
				Vector3 from = NavMesh.SamplePositionFilter(c, out feet, 2f, agent, NavMesh.AllAreas) ? feet.position : c;
				for (float ring = far; ring >= 2.5f; ring *= 0.6f)
				{
					for (int i = 0; i < 8; i++)
					{
						float a = UnityEngine.Random.value * Mathf.PI * 2f, d = ring * (0.6f + 0.4f * UnityEngine.Random.value);
						var want = new Vector3(c.x + Mathf.Cos(a) * d, c.y + Mathf.Sin(a) * d, from.z);
						NavMeshHit hit, wall;
						if (!NavMesh.SamplePositionFilter(want, out hit, 2f, agent, NavMesh.AllAreas)) continue;
						var p = hit.position;
						if (((Vector2)(p - c)).magnitude < ring * 0.35f) continue;
						if (NavMesh.RaycastFilter(from, p, out wall, agent, NavMesh.AllAreas)) continue; // something is in the way
						ModKit.Dbg("spawn point ring=" + ring.ToString("F1", CultureInfo.InvariantCulture) + " try=" + (i + 1) + " agentType=" + agent);
						return new Vector3(p.x, p.y, c.z);
					}
				}
			}
			catch (Exception e) { ModKit.Dbg("walk mesh: " + e.Message); }
			ModKit.Dbg("spawn point: no clear ring point, beside the defender");
			float b = UnityEngine.Random.value * Mathf.PI * 2f;
			return new Vector3(c.x + Mathf.Cos(b) * 2.5f, c.y + Mathf.Sin(b) * 2.5f, c.z);
		}

		// Moves the dead out of the living list and removes a corpse after a while. A monster
		// the game itself removed (the area emptied and went to sleep) just drops off the list.
		private static void Sweep(float now)
		{
			for (int i = sAlive.Count - 1; i >= 0; i--)
			{
				var t = sAlive[i]; int s = StateOf(t);
				if (s == 0) continue;
				sAlive.RemoveAt(i);
				if (s == 1) { t.DeadAt = now; sCorpses.Add(t); sKilled++; }
			}
			for (int i = sCorpses.Count - 1; i >= 0; i--)
			{
				var t = sCorpses[i];
				if (StateOf(t) == 2) { sCorpses.RemoveAt(i); continue; }
				if (now - t.DeadAt < CorpseSeconds) continue;
				Remove(t); sCorpses.RemoveAt(i);
			}
		}

		// 0 alive, 1 dead, 2 gone
		private static int StateOf(Tracked t)
		{
			try
			{
				if (t.M == null || t.No == null || !t.No.IsSpawned) return 2;
				return t.M.IsDead() ? 1 : 0;
			}
			catch { return 2; }
		}

		private static bool Remove(Tracked t)
		{
			try
			{
				if (t.No == null || !t.No.IsSpawned) return false;
				try { var b = t.M != null ? t.M.GetComponent<MonsterBehaviour>() : null; if (b != null) b.UnregisterMonsterToTickServer(); } catch { }
				try { var h = Utility.ReturnSceneHandlerForScene(sScene); if (h != null && h._monstersSpawned != null) h._monstersSpawned.Remove(t.No); } catch { }
				t.No.Despawn(true);
				return true;
			}
			catch (Exception e) { ModKit.Dbg("remove " + t.Id + ": " + e.Message); return false; }
		}

		private static int RemoveAll()
		{
			int n = 0;
			foreach (var t in sAlive) if (Remove(t)) n++;
			foreach (var t in sCorpses) if (Remove(t)) n++;
			sAlive.Clear(); sCorpses.Clear();
			n += SweepStrays("end");
			return n;
		}

		// Anything that still carries the mod's name and is not on its list.
		private static int SweepStrays(string when)
		{
			int n = 0;
			try
			{
				var all = Monster.SnapshotAll();
				if (all == null) return 0;
				for (int i = 0; i < all.Count; i++)
				{
					var m = all[i];
					try
					{
						if (m == null || !m.IsSpawned) continue;
						if (!m.gameObject.name.StartsWith(Marker, StringComparison.Ordinal)) continue;
						var t = new Tracked { M = m, No = m.NetworkObject, Id = m.NetworkObjectId };
						if (Remove(t)) n++;
					}
					catch { }
				}
				if (n > 0) ModKit.Say("Horde Nights removed " + n + " stray horde " + (n == 1 ? "monster" : "monsters") + " (" + when + ").");
			}
			catch (Exception e) { ModKit.Dbg("stray sweep: " + e.Message); }
			return n;
		}

		// After the game's own kill credit: who killed a horde monster.
		internal static void MonsterDeath(Player __0, Monster __1)
		{
			if (ModKit.Off || !Running) return;
			try
			{
				if (__1 == null || !sMine.Contains(__1.NetworkObjectId)) return;
				string name = __0 != null ? ModKit.NameOf(__0) : "";
				if (name.Length == 0) return;
				int n; sSlayers.TryGetValue(name, out n); sSlayers[name] = n + 1;
			}
			catch (Exception e) { ModKit.Fail("kill credit", e); }
		}

		// ---- rewards ---------------------------------------------------------------------
		private static void Heal(Player p)
		{
			try { p.SetHealth(p.MaxHealth.Value); } catch (Exception e) { ModKit.Dbg("heal: " + e.Message); return; }
			try { p.SetConcentration(p.MaxConcentration.Value); } catch { }
			try { p.SetStamina(p.MaxStamina.Value); } catch { }
		}

		// A gold pile on the ground beside the player, through the game's own world-gold call
		// (the one its wave engine pays rewards with). Whoever picks it up is paid by the game.
		private static bool DropGold(Player p, int amount)
		{
			try
			{
				var rpc = p.GetComponent<ServerRPC>();
				if (rpc == null || !rpc.IsSpawned) return false;
				var pos = p.transform.position;
				float a = UnityEngine.Random.value * Mathf.PI * 2f;
				var at = new Vector2(pos.x + Mathf.Cos(a) * 1.2f, pos.y + Mathf.Sin(a) * 1.2f);
				var stage = rpc.__rpc_exec_stage;
				try { rpc.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.Server; rpc.AddGoldToWorldServerRpc(amount, sScene, at, 0UL, false); }
				finally { rpc.__rpc_exec_stage = stage; }
				return true;
			}
			catch (Exception e) { ModKit.Dbg("gold drop: " + e.Message); return false; }
		}

		// ---- words -----------------------------------------------------------------------
		internal static string Words(Areas.Scene scene)
		{
			var s = scene.ToString(); var sb = new StringBuilder();
			for (int i = 0; i < s.Length; i++)
			{
				if (i > 0 && char.IsUpper(s[i]) && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1])))) sb.Append(' ');
				sb.Append(s[i]);
			}
			return sb.ToString();
		}

		private static string Fill(string template, int gold, string waveName)
		{
			int seconds = Mathf.Max(0, Mathf.RoundToInt(sPhaseEnds - Time.realtimeSinceStartup));
			return (template ?? "")
				.Replace("{horde}", sTitle).Replace("{area}", Words(sScene))
				.Replace("{seconds}", seconds.ToString(CultureInfo.InvariantCulture))
				.Replace("{wave}", (sWave + 1).ToString(CultureInfo.InvariantCulture)).Replace("{waves}", sWaves.Count.ToString(CultureInfo.InvariantCulture))
				.Replace("{name}", waveName ?? "").Replace("{gold}", gold.ToString(CultureInfo.InvariantCulture));
		}

		private static void Tell(string template, int gold, string waveName)
		{
			if (string.IsNullOrWhiteSpace(template)) return;
			string line = Fill(template, gold, waveName);
			if (gold > 0 && template.IndexOf("{gold}", StringComparison.Ordinal) < 0) line += " " + gold + " gold for each defender.";
			ModKit.Broadcast(line);
		}

		// ---- commands --------------------------------------------------------------------
		private static bool IsAdmin(Player p)
		{
			string name = ModKit.NameOf(p), hash = ModKit.HashOf(p);
			foreach (var part in (HordeNightsPlugin.Admins.Value ?? "").Split(','))
			{
				var a = part.Trim(); if (a.Length == 0) continue;
				if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase) || (hash.Length > 0 && string.Equals(a, hash, StringComparison.OrdinalIgnoreCase))) return true;
			}
			return false;
		}

		private static string Status()
		{
			if (sPhase == Phase.Warning) return sTitle + " reaches " + Words(sScene) + " in " + Mathf.Max(0, Mathf.RoundToInt(sPhaseEnds - Time.realtimeSinceStartup)) + " seconds.";
			if (Running) return sTitle + " is attacking " + Words(sScene) + ": wave " + (sWave + 1) + " of " + sWaves.Count + ", " + (sAlive.Count + sQueue.Count) + (sAlive.Count + sQueue.Count == 1 ? " monster" : " monsters") + " left in it.";
			int every = Mathf.Clamp(HordeNightsPlugin.EveryNights.Value, 0, 30);
			if (every <= 0) return "No horde is on its way. Hordes come only when an admin calls one.";
			return "No horde is on its way. The next one is " + Schedule.NextText(every) + ", at " + Mathf.Clamp(HordeNightsPlugin.AtHour.Value, 0, 23).ToString("00", CultureInfo.InvariantCulture) + ":00.";
		}

		// A typed /horde line is answered and not relayed to the other players.
		[HarmonyPriority(Priority.First)]
		internal static bool Chat(Message message)
		{
			if (ModKit.Off) return true;
			try
			{
				if (!ModKit.OnServer()) return true;
				string verb, rest; ulong source;
				if (!ModKit.IsCommand(message, out verb, out rest, out source)) return true;
				if (verb != "horde") return true;
				var me = ModKit.PlayerOf(source);
				if (me == null) return false;
				var words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				string sub = words.Length > 0 ? words[0].ToLowerInvariant() : "";
				if (sub == "start" || sub == "stop")
				{
					if (!IsAdmin(me)) { ModKit.Whisper(source, "Only this server's horde admins can do that.", false); return false; }
					if (sub == "stop")
					{
						if (!Running) { ModKit.Whisper(source, "No horde is under way.", false); return false; }
						End(HordeNightsPlugin.MsgWithdraw.Value, "it was called off by " + ModKit.NameOf(me));
						return false;
					}
					Areas.Scene scene = Areas.Scene.None; try { scene = me.Scene.Value; } catch { }
					string why;
					if (!Start(scene, words.Length > 1 ? words[1] : "", ModKit.NameOf(me), out why)) ModKit.Whisper(source, "No horde: " + why + ".", false);
					return false;
				}
				ModKit.Whisper(source, Status() + (IsAdmin(me) ? " Admins: /horde start [goblins|wolves|droops|corrupted], /horde stop." : ""), false);
				return false;
			}
			catch (Exception e) { ModKit.Fail("chat", e); return true; }
		}

		// BepInEx\config\HumanGenome-HordeNights\command.txt: "start", "start wolves",
		// "start wolves TheLostCaverns" or "stop". Read once and deleted. For an owner with
		// file access and no character on the server.
		private static void ReadCommandFile()
		{
			string f = Path.Combine(State.Dir, "command.txt");
			if (!File.Exists(f)) return;
			string[] lines;
			try { lines = File.ReadAllLines(f); File.Delete(f); } catch (Exception e) { ModKit.Dbg("command file: " + e.Message); return; }
			foreach (var raw in lines)
			{
				var words = raw.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (words.Length == 0) continue;
				string sub = words[0].ToLowerInvariant();
				if (sub == "stop") { if (Running) End(HordeNightsPlugin.MsgWithdraw.Value, "it was called off from the command file"); else ModKit.Say("Command file: no horde is under way."); continue; }
				if (sub != "start") { ModKit.Say("Command file: '" + raw.Trim() + "' is not a horde command. Use start or stop."); continue; }
				Areas.Scene scene = Areas.Scene.None;
				if (words.Length > 2) { Areas.Scene s; if (Enum.TryParse(words[2], true, out s) && Enum.IsDefined(typeof(Areas.Scene), s)) scene = s; else { ModKit.Say("Command file: '" + words[2] + "' is not an area the game knows."); continue; } }
				string why;
				if (!Start(scene, words.Length > 1 ? words[1] : "", "the command file", out why)) ModKit.Say("Command file: no horde, " + why + ".");
			}
		}
	}
}
