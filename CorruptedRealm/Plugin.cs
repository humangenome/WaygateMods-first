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

namespace WaygateMods.CorruptedRealm
{
	// An elite hunt. Empowered monsters are several times as common, every one of them gains
	// levels and extra effects and pays more, a kill is announced in chat, and the server can
	// send a named champion after a random player every so often. Monster damage can rise
	// with the number of players connected.
	//
	// Server-side only. The game rolls for an empowerment whenever a monster is set up or
	// returns from death; its roll is left alone, and a moment later the mod rolls once more
	// for a monster that stayed ordinary, so that the two rolls together come to the game's
	// chance times the setting. The pay is the game's own difficulty dials, multiplied at the
	// point where the server reads them. Levels and effects are put on a monster at the moment
	// it is empowered and are taken off by the game's own clean-up when the empowerment ends.
	// A champion is spawned through the game's own runtime spawner as a transient monster, so
	// it is never written to the world. Nothing is written to a character.
	[BepInPlugin(PluginId, "Corrupted Realm", Version)]
	public class CorruptedRealmPlugin : BasePlugin
	{
		public const string PluginId = "com.humangenome.waygatemods.corruptedrealm";
		public const string ModId = "HumanGenome-CorruptedRealm";
		public const string Version = "1.0.0";

		internal static ConfigEntry<float> EliteChance, WildChance, EliteAttackSpeed, EliteArmor, EliteXp, EliteGold, EliteLoot, PerPlayer;
		internal static ConfigEntry<int> EliteLevels, ChampionEvery, ChampionMaxAlive, ChampionLeaveAfter, DropsAfterMinutes;
		internal static ConfigEntry<bool> AnnounceKills, AnnounceChampions, Commands;
		internal static ConfigEntry<string> EliteExtraEffects, WildSkipScenes, ChampionTypes, ChampionSafeScenes, AdminNames;
		internal static ConfigEntry<string> MsgEliteKill, MsgChampionAppears, MsgChampionSlain, MsgChampionLeaves;

		public override void Load()
		{
			EliteChance = Config.Bind("Elites", "Chance", 3f, "How much more often a monster the game lets be empowered spawns empowered. 1 = the game's own chance. 1 to 10, and never past 100%.");
			WildChance = Config.Bind("Elites", "WildChance", 5f, "Chance in percent that an ordinary monster, one the game never empowers, spawns empowered. 0 = never. Up to 25. Bosses, minibosses, monsters tied to the world's story, harmless animals and things that do not move are left out.");
			WildSkipScenes = Config.Bind("Elites", "WildSkipScenes", "TheLostCaverns", "Areas where ordinary monsters stay ordinary, separated by commas. Names are the game's scene names. The default is the area new characters start in.");
			EliteLevels = Config.Bind("Elites", "BonusLevel", 3, "Levels an empowered monster gains. 0 to 20.");
			EliteArmor = Config.Bind("Elites", "Armor", 1.25f, "The game's armor-up effect on an empowered monster. 1 = none. 1 to 2.");
			EliteAttackSpeed = Config.Bind("Elites", "AttackSpeed", 1.2f, "How much faster an empowered monster attacks. 1 = no change. 1 to 2.");
			EliteExtraEffects = Config.Bind("Elites", "ExtraEffects", "", "More effects for an empowered monster, as Effect:multiplier pairs separated by commas, for example ResistanceUp:1.25,SpeedUp:1.1 (Effect:multiplier:additive is also read). Names are the game's Effect names. Empty = none.");
			EliteXp = Config.Bind("Elites", "XP", 1.5f, "XP from killing an empowered monster. 1 to 5.");
			EliteGold = Config.Bind("Elites", "Gold", 2f, "Gold from an empowered monster. 1 to 5.");
			EliteLoot = Config.Bind("Elites", "Loot", 2f, "Drop chance from an empowered monster, on top of the game's own bonus for them. 1 to 5.");

			ChampionEvery = Config.Bind("Champion", "EveryMinutes", 0, "Every this many minutes a champion comes for a random player. 0 = never. Up to 240. The clock runs only while somebody is connected.");
			ChampionTypes = Config.Bind("Champion", "Types", "GoblinBasher,GoblinBasherT2,GoblinRipperT2,CorruptedWildwoodWolf", "Monster types a champion can be, separated by commas. Names are the game's MonsterType names. Bosses, minibosses and monsters tied to the world's story are refused.");
			ChampionMaxAlive = Config.Bind("Champion", "MaxAlive", 2, "Champions alive at the same time. 1 to 5.");
			ChampionLeaveAfter = Config.Bind("Champion", "LeaveAfterMinutes", 20, "A champion nobody is fighting leaves after this many minutes. 5 to 120.");
			ChampionSafeScenes = Config.Bind("Champion", "SafeScenes", "EarlwoodVillage,GuildHall,PlayerBase", "A champion never comes for a player standing in one of these areas. Names are the game's scene names.");

			PerPlayer = Config.Bind("Scaling", "PerPlayer", 0.1f, "Monster damage added for each connected player beyond the first. 0.1 = 10% each. 0 to 0.5. 0 = off.");

			DropsAfterMinutes = Config.Bind("Cleanup", "DropsAfterMinutes", 10, "Loot and gold an empowered monster dropped and nobody picked up is removed after this many minutes. Every drop left on the ground costs the server CPU. 0 = leave it to the game, which removes a drop after 30 minutes. Up to 30.");

			AnnounceKills = Config.Bind("Announce", "EliteKills", true, "A chat line when a player kills an empowered monster.");
			AnnounceChampions = Config.Bind("Announce", "Champions", true, "Chat lines when a champion appears, dies or leaves.");

			Commands = Config.Bind("Chat", "Commands", true, "Players can type /realm. The characters named in AdminNames can type /champion.");
			AdminNames = Config.Bind("Chat", "AdminNames", "", "Character names that may call a champion with /champion, separated by commas.");

			MsgEliteKill = Config.Bind("Messages", "EliteKill", "{player} slew {elite}.", "Chat line for an empowered kill. {player} and {elite} are filled in. Empty = none.");
			MsgChampionAppears = Config.Bind("Messages", "ChampionAppears", "A champion has come for {player} in {area}: {champion}, {monster}.", "Chat line when a champion appears. {player}, {area}, {champion} and {monster} are filled in. Empty = none.");
			MsgChampionSlain = Config.Bind("Messages", "ChampionSlain", "{player} slew the champion {champion}.", "Chat line when a champion dies. {player} and {champion} are filled in. Empty = none.");
			MsgChampionLeaves = Config.Bind("Messages", "ChampionLeaves", "{champion} has gone back into the wild.", "Chat line when a champion leaves unfought. {champion} is filled in. Empty = none.");

			var harmony = new Harmony(PluginId);
			ModKit.Init(Log, harmony, "Corrupted Realm");
			ModKit.WatchConfig(Config);
			try
			{
				State.Open(Path.Combine(Paths.ConfigPath, ModId));
				ModKit.Patch(typeof(TransitionManager), "Update", null, typeof(Realm), null, nameof(Realm.Tick), true, "the server clock");
				var dm = typeof(DifficultyManager);
				Elites.SeesTheEnd = ModKit.Patch(typeof(Monster), "ClearEmpowerment", null, typeof(Elites), nameof(Elites.Clearing), null, false, "more empowered monsters, and levels for them");
				ModKit.Patch(typeof(Monster), "ApplyEmpowerment", null, typeof(Elites), null, nameof(Elites.Applied), false, "levels and effects for empowered monsters");
				ModKit.Patch(typeof(XP), "CalculateXPGained", null, typeof(Pay), nameof(Pay.XpBegin), nameof(Pay.XpEnd), false, "telling an empowered kill from an ordinary one when XP is paid");
				ModKit.Patch(typeof(MonsterUtils), "GoldDropCheck", null, typeof(Pay), nameof(Pay.GoldBegin), nameof(Pay.GoldEnd), false, "telling an empowered kill from an ordinary one when gold drops");
				ModKit.Patch(typeof(MonsterUtils), "ApplyDropChanceModifiers", null, typeof(Pay), nameof(Pay.LootBegin), nameof(Pay.LootEnd), false, "telling an empowered kill from an ordinary one when items drop");
				ModKit.Patch(dm, "GetXPMultiplier", null, typeof(Dials), null, nameof(Dials.Xp), false, "empowered XP");
				ModKit.Patch(dm, "GetGoldMultiplier", null, typeof(Dials), null, nameof(Dials.Gold), false, "empowered gold");
				ModKit.Patch(dm, "GetLootChanceMultiplier", null, typeof(Dials), null, nameof(Dials.Loot), false, "empowered drops");
				ModKit.Patch(dm, "GetMonsterDamageDealtMultiplier", null, typeof(Dials), null, nameof(Dials.Damage), false, "damage that rises with the player count");
				ModKit.Patch(typeof(QuestManager), "RegisterMonsterDeath", new[] { typeof(Player), typeof(Monster), typeof(InGameEvent) }, typeof(Realm), null, nameof(Realm.MonsterDeath), false, "kill announcements");
				ModKit.Patch(typeof(ChatSystem), "SendMessageToServerServerRpc", null, typeof(Realm), nameof(Realm.Chat), null, false, "the /realm and /champion chat commands");
				Story.Guarded = ModKit.Patch(typeof(Monster), "OnDeath", new[] { typeof(Damage), typeof(bool) }, typeof(Story), nameof(Story.DeathBegins), nameof(Story.Ends), false, "champions (keeping a champion's death out of the world's story)")
					& ModKit.Patch(typeof(MonsterBehaviour), "ActivateReaction", null, typeof(Story), nameof(Story.ReactionBegins), nameof(Story.Ends), false, "champions (keeping a champion's battle cries out of the world's story)")
					& ModKit.Patch(typeof(EventsManager), "ActivateEventServerRpc", null, typeof(Story), nameof(Story.Activate), null, false, "champions (keeping them out of the world's story)");
				Drops.CanTrack = ModKit.Patch(typeof(MonsterUtils), "RuneDropCheck", Type.EmptyTypes, typeof(Drops), nameof(Drops.Before), nameof(Drops.After), false, "clearing up what empowered monsters drop (runes)")
					& ModKit.Patch(typeof(MonsterUtils), "ItemDropCheck", Type.EmptyTypes, typeof(Drops), nameof(Drops.Before), nameof(Drops.After), false, "clearing up what empowered monsters drop (items)");
				if (!ModKit.Off)
				{
					int every = Mathf.Clamp(ChampionEvery.Value, 0, 240);
					ModKit.Say("Corrupted Realm " + Version + " is on: empowered monsters are " + Dials.Clamp(EliteChance.Value, 1f, 10f).ToString("0.##", CultureInfo.InvariantCulture) + "x as common" + (Dials.Clamp(WildChance.Value, 0f, 25f) > 0f ? ", " + Dials.Clamp(WildChance.Value, 0f, 25f).ToString("0.##", CultureInfo.InvariantCulture) + "% of ordinary monsters are empowered too" : "") + ", each gains " + Mathf.Clamp(EliteLevels.Value, 0, 20) + " levels"
						+ (every > 0 && Story.Guarded ? ", a champion every " + every + (every == 1 ? " minute" : " minutes") : ", champions off")
						+ ". Slain on this server so far: " + Realm.Count(State.ElitesSlain, "empowered monster") + ", " + Realm.Count(State.ChampionsSlain, "champion") + ".");
				}
			}
			catch (Exception e) { ModKit.Dbg("load: " + e); ModKit.TurnOff("it could not start"); }
		}
	}

	// What the mod remembers between restarts: how many empowered monsters and champions the
	// server's players have killed, and how long until the next champion. A plain text file.
	internal static class State
	{
		private static string sFile = "";
		internal static long ElitesSlain, ChampionsSlain, ChampionsSent;
		internal static float NextChampionIn = -1f;
		internal static bool Dirty;
		private static float sNextWrite;

		internal static void Open(string dir)
		{
			Directory.CreateDirectory(dir);
			sFile = Path.Combine(dir, "realm.txt");
			if (!File.Exists(sFile)) return;
			try
			{
				foreach (var line in File.ReadAllLines(sFile))
				{
					int eq = line.IndexOf('='); if (eq <= 0) continue;
					string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
					if (k == "elites_slain") long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out ElitesSlain);
					else if (k == "champions_slain") long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out ChampionsSlain);
					else if (k == "champions_sent") long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out ChampionsSent);
					else if (k == "next_champion_in_seconds") float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out NextChampionIn);
				}
				if (ElitesSlain < 0) ElitesSlain = 0;
				if (ChampionsSlain < 0) ChampionsSlain = 0;
				if (ChampionsSent < 0) ChampionsSent = 0;
			}
			catch (Exception e) { ModKit.Dbg("state read: " + e.Message); ElitesSlain = ChampionsSlain = ChampionsSent = 0; NextChampionIn = -1f; }
		}

		internal static void SaveIfDue(float now)
		{
			if (!Dirty || now < sNextWrite) return;
			sNextWrite = now + 30f;
			Save();
		}

		internal static void Save()
		{
			Dirty = false;
			try
			{
				string tmp = sFile + ".tmp";
				File.WriteAllLines(tmp, new[]
				{
					"elites_slain=" + ElitesSlain.ToString(CultureInfo.InvariantCulture),
					"champions_slain=" + ChampionsSlain.ToString(CultureInfo.InvariantCulture),
					"champions_sent=" + ChampionsSent.ToString(CultureInfo.InvariantCulture),
					"next_champion_in_seconds=" + Mathf.Max(0f, NextChampionIn).ToString("0", CultureInfo.InvariantCulture),
				});
				if (File.Exists(sFile)) File.Replace(tmp, sFile, null); else File.Move(tmp, sFile);
			}
			catch (Exception e) { ModKit.Fail("state write", e); }
		}
	}

	// Which living monsters are empowered right now, what the mod put on each, and which of
	// them died a moment ago (the game ends the empowerment at death, before it pays out).
	internal sealed class Mark
	{
		internal string Kind = "Empowered";
		internal int Levels;
		internal float ClearedAt = -1f;
	}

	internal static class Elites
	{
		private static readonly Dictionary<long, Mark> sMarks = new Dictionary<long, Mark>();
		private const float CorpseSeconds = 180f;
		// False when the game's end-of-empowerment call could not be patched: levels are then
		// not given, because they could not be taken back.
		internal static bool SeesTheEnd;

		internal static long Key(Monster m) { return m.Pointer.ToInt64(); }

		internal static Mark MarkOf(Monster m)
		{
			Mark k;
			return sMarks.TryGetValue(Key(m), out k) ? k : null;
		}

		// True for a monster that is empowered, or that was when it died a moment ago.
		internal static bool IsElite(Monster m, bool payingOut)
		{
			if (m == null) return false;
			// A boss, a miniboss or a monster tied to the world's story is left exactly as the game
			// made it, empowered or not: no levels, no effects, no extra pay, no kill line.
			try { if (Kinds.StoryOrBoss(m.MonsterConfiguration)) return false; } catch { return false; }
			try { if (m.IsEmpowered) return true; } catch { }
			var k = MarkOf(m);
			if (k != null && (k.ClearedAt < 0f || Time.realtimeSinceStartup - k.ClearedAt < CorpseSeconds)) return true;
			// The game's own note for its loot code, written when an empowered monster dies.
			if (payingOut) { try { if (m.IsDead() && m.WasEmpoweredOnDeath) return true; } catch { } }
			return false;
		}

		// After the game has empowered a monster: the levels and the effects. The effects go
		// into the monster's own list of empowerment effects, so the game removes them itself
		// when the empowerment ends.
		internal static void Applied(Monster __instance)
		{
			if (ModKit.Off) return;
			try
			{
				var m = __instance;
				if (m == null || !ModKit.OnServer() || !m.IsEmpowered) return;
				try { if (m.IsPlayerAllied) return; } catch { }
				try { if (Kinds.StoryOrBoss(m.MonsterConfiguration)) return; } catch { return; }
				long key = Key(m);
				Mark k;
				if (sMarks.TryGetValue(key, out k) && k.ClearedAt < 0f) return;
				k = new Mark();
				try { var mod = m.ActiveEmpowermentModifier; if (mod != null && !string.IsNullOrWhiteSpace(mod.DisplayName)) k.Kind = mod.DisplayName.Trim(); } catch { }
				sMarks[key] = k;

				int levels = Mathf.Clamp(CorruptedRealmPlugin.EliteLevels.Value, 0, 20);
				if (levels > 0 && SeesTheEnd)
				{
					try { var lv = m.Level; if (lv != null) { lv.Value = lv.Value + levels; k.Levels = levels; } }
					catch (Exception e) { ModKit.Dbg("levels: " + e.Message); }
				}
				int added = 0;
				var fx = m.Effects; var mine = m._empowermentEffects;
				if (fx != null && mine != null)
				{
					foreach (var e in Extras.Current())
					{
						var v = new EffectValues(e.Effect, (Spell)0, float.PositiveInfinity, 999, e.Multiplier, e.Additive, 0UL, -1);
						fx.AddEffectToObject(v);
						mine.Add(v);
						added++;
					}
				}
				Realm.Made++;
				ModKit.Dbg("elite: " + Realm.TypeOf(m) + " became " + k.Kind + ", +" + k.Levels + " levels, " + added + " extra effects");
			}
			catch (Exception e) { ModKit.Fail("empowering", e); }
		}

		// Before the game ends an empowerment (at death, and again before every new roll): the
		// levels go back. The effects are in the game's own list and leave with it.
		internal static void Clearing(Monster __instance)
		{
			if (ModKit.Off) return;
			try
			{
				var m = __instance;
				if (m == null || !ModKit.OnServer()) return;
				long key = Key(m);
				float now = Time.realtimeSinceStartup;
				// The game ends any empowerment right before each of its own rolls (when a monster
				// is set up, and when it returns from death), so this is also the cue for the
				// mod's second roll a moment later.
				Rolls.Schedule(key, m, now);
				Mark k;
				if (!sMarks.TryGetValue(key, out k)) return;
				if (k.ClearedAt >= 0f)
				{
					// The second call for the same empowerment comes when the monster returns.
					if (now - k.ClearedAt > 5f) sMarks.Remove(key);
					return;
				}
				k.ClearedAt = now;
				if (k.Levels > 0)
				{
					try { var lv = m.Level; if (lv != null) lv.Value = Mathf.Max(1, lv.Value - k.Levels); } catch (Exception e) { ModKit.Dbg("levels back: " + e.Message); }
					k.Levels = 0;
				}
			}
			catch (Exception e) { ModKit.Fail("ending an empowerment", e); }
		}

		internal static void Prune(float now)
		{
			if (sMarks.Count < 64) return;
			var gone = new List<long>();
			foreach (var kv in sMarks) if (kv.Value.ClearedAt >= 0f && now - kv.Value.ClearedAt > CorpseSeconds) gone.Add(kv.Key);
			foreach (var key in gone) sMarks.Remove(key);
			if (sMarks.Count > 4096) sMarks.Clear();
		}
	}

	// The second roll. The game's own roll reads its chance dial in a way a mod cannot reach,
	// so the mod leaves that roll alone and adds one of its own: a monster the game could have
	// empowered (its settings allow it and name a pool of empowerments) and did not gets one
	// more chance, sized so that both rolls together come to the game's chance times the
	// setting. The empowerment is one from the monster's own pool, applied by the game's own
	// call, so players see exactly what they see on any empowered monster.
	internal static class Rolls
	{
		private sealed class Due { internal Monster Monster; internal float At; }
		private static readonly Dictionary<long, Due> sDue = new Dictionary<long, Due>();
		private static readonly List<long> sReady = new List<long>();
		private static readonly System.Random sRandom = new System.Random();
		internal static long Rolled, Won;

		internal static void Schedule(long key, Monster m, float now)
		{
			if (Dials.Clamp(CorruptedRealmPlugin.EliteChance.Value, 1f, 10f) <= 1f && Dials.Clamp(CorruptedRealmPlugin.WildChance.Value, 0f, 25f) <= 0f) return;
			if (sDue.Count > 2048) sDue.Clear();
			sDue[key] = new Due { Monster = m, At = now + 0.75f };
		}

		internal static void Tick(float now)
		{
			if (sDue.Count == 0) return;
			sReady.Clear();
			foreach (var kv in sDue) if (now >= kv.Value.At) sReady.Add(kv.Key);
			foreach (var key in sReady)
			{
				var due = sDue[key];
				sDue.Remove(key);
				try { Roll(due.Monster); } catch (Exception e) { ModKit.Dbg("second roll: " + e.Message); }
			}
		}

		private static void Roll(Monster m)
		{
			if (m == null || !m.IsSpawned || m.IsDead() || m.IsEmpowered || m.IsPlayerAllied) return;
			if (m.RemainingDefeatTime > 0f || m.IsEngaged.Value) return;
			if (Champions.Find(m) != null) return;
			var cfg = m.MonsterConfiguration;
			if (cfg == null || Kinds.StoryOrBoss(cfg)) return;
			var pool = cfg.EmpowermentPool;
			if (!cfg.CanBeEmpowered || cfg.EmpowermentChance <= 0f || pool == null || pool.Count == 0) { Wild(m, cfg); return; }

			// The chance is a percentage. What the game's roll stood at, difficulty tier and any
			// other mod's share included:
			float tier = 1f; try { tier = DifficultyManager.GetEmpowermentChanceMultiplier(cfg); } catch { }
			float game = Mathf.Clamp(cfg.EmpowermentChance * tier, 0f, 100f);
			float want = Mathf.Clamp(game * Dials.Clamp(CorruptedRealmPlugin.EliteChance.Value, 1f, 10f), 0f, 100f);
			if (game >= 100f || want <= game) return;
			// P(second roll) so that P(game) + (1 - P(game)) * P(second) = want
			double second = (want - game) / (100.0 - game);
			Rolled++;
			if (sRandom.NextDouble() >= second) return;

			var em = EmpowermentManager.Singleton;
			if (em == null) return;
			var ok = new List<EmpowermentModifier>();
			for (int i = 0; i < pool.Count; i++)
			{
				var mod = em.GetModifier(pool[i]);
				try { if (mod != null && mod.InProduction) ok.Add(mod); } catch { }
			}
			if (ok.Count == 0) return;
			Won++;
			m.ApplyEmpowerment(ok[sRandom.Next(ok.Count)]);
		}

		// An ordinary monster: the game never rolls for it. The mod gives it a small chance of
		// its own and draws from every empowerment the game has in production.
		internal static long WildRolled, WildWon;
		private static readonly Dictionary<int, bool> sWildTypes = new Dictionary<int, bool>();

		private static bool WildType(MonsterConfiguration cfg)
		{
			int t = (int)cfg.MonsterType; bool ok;
			if (sWildTypes.TryGetValue(t, out ok)) return ok;
			string name = cfg.MonsterType.ToString();
			ok = !Kinds.StoryOrBoss(cfg) && cfg.XpOnKill > 1 && !cfg.AgentPermanentlyDisabled;
			foreach (var word in new[] { "Dummy", "GodMode", "Nest", "Heart", "Sleeping", "Fish" }) if (name.IndexOf(word, StringComparison.Ordinal) >= 0) ok = false;
			sWildTypes[t] = ok;
			return ok;
		}

		private static void Wild(Monster m, MonsterConfiguration cfg)
		{
			float wild = Dials.Clamp(CorruptedRealmPlugin.WildChance.Value, 0f, 25f);
			if (wild <= 0f || !WildType(cfg)) return;
			try { if (m.GetComponentInChildren<BossInvulnerability>(true) != null) return; } catch { }
			string scene = ""; try { scene = m.Scene.Value.ToString(); } catch { }
			foreach (var part in (CorruptedRealmPlugin.WildSkipScenes.Value ?? "").Split(','))
				if (part.Trim().Length > 0 && string.Equals(part.Trim(), scene, StringComparison.OrdinalIgnoreCase)) return;
			WildRolled++;
			if (sRandom.NextDouble() * 100.0 >= wild) return;
			var mod = Champions.PickModifier();
			if (mod == null) return;
			WildWon++;
			m.ApplyEmpowerment(mod);
		}
	}

	// The extra effects an empowered monster carries, read from the settings and kept until
	// the settings change.
	internal struct Extra { internal Effect Effect; internal float Multiplier, Additive; }

	internal static class Extras
	{
		private static string sBuiltFrom;
		private static List<Extra> sList = new List<Extra>();

		internal static List<Extra> Current()
		{
			string sig = CorruptedRealmPlugin.EliteArmor.Value.ToString("R", CultureInfo.InvariantCulture) + "|" + CorruptedRealmPlugin.EliteAttackSpeed.Value.ToString("R", CultureInfo.InvariantCulture) + "|" + (CorruptedRealmPlugin.EliteExtraEffects.Value ?? "");
			if (sig == sBuiltFrom) return sList;
			var list = new List<Extra>();
			float speed = Dials.Clamp(CorruptedRealmPlugin.EliteAttackSpeed.Value, 1f, 2f);
			if (speed > 1.001f) list.Add(new Extra { Effect = Effect.AttackSpeedUp, Multiplier = speed });
			float armor = Dials.Clamp(CorruptedRealmPlugin.EliteArmor.Value, 1f, 2f);
			if (armor > 1.001f) list.Add(new Extra { Effect = Effect.ArmorUp, Multiplier = armor });
			foreach (var part in (CorruptedRealmPlugin.EliteExtraEffects.Value ?? "").Split(','))
			{
				var kv = part.Trim().Split(':');
				if (kv.Length < 2 || kv.Length > 3 || list.Count >= 6) continue;
				Effect effect; float mult;
				if (!Enum.TryParse(kv[0].Trim(), true, out effect) || effect == Effect.None) { ModKit.Say("Corrupted Realm: '" + kv[0].Trim() + "' in Elites/ExtraEffects is not an effect the game knows. It was left out."); continue; }
				if (!float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out mult) || float.IsNaN(mult)) continue;
				// Not for this list: effects that would make a monster unbeatable or unseen, and the
				// effects whose value the game works out from the time they have left. An effect here
				// never ends, and with no end the game's shield sum came out as not-a-number on a
				// test server; the plain Shield effect set no shield on a monster at all.
				string en = effect.ToString();
				if (effect == Effect.Invincibility || effect == Effect.Unkillable || effect == Effect.Invisibility || effect == Effect.Shield || en.EndsWith("OverTime", StringComparison.Ordinal))
				{ ModKit.Say("Corrupted Realm: " + effect + " is not allowed in Elites/ExtraEffects. It was left out."); continue; }
				float add = 0f;
				if (kv.Length == 3 && (!float.TryParse(kv[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out add) || float.IsNaN(add))) continue;
				list.Add(new Extra { Effect = effect, Multiplier = Mathf.Clamp(mult, 0.1f, 5f), Additive = Mathf.Clamp(add, 0f, 100000f) });
			}
			sList = list; sBuiltFrom = sig;
			return sList;
		}
	}

	// The pay-out dials take a monster's settings, not the monster, so the three places where
	// the server pays for a kill note which monster is being paid for.
	internal static class Pay
	{
		internal static bool XpForElite, GoldForElite, LootForElite;

		internal static void XpBegin(Damage __0)
		{
			XpForElite = false;
			if (ModKit.Off) return;
			try
			{
				if (__0 == null || !ModKit.OnServer()) return;
				var victim = Utility.ReturnObjectsCommonById(__0.VictimId);
				var m = victim != null ? victim.TryCast<Monster>() : null;
				XpForElite = m != null && Elites.IsElite(m, false);
			}
			catch (Exception e) { ModKit.Fail("xp", e); }
		}
		internal static void XpEnd() { XpForElite = false; }

		internal static void GoldBegin(MonsterUtils __instance) { GoldForElite = Of(__instance); if (GoldForElite) Drops.Begin(); }
		internal static void GoldEnd() { GoldForElite = false; Drops.After(); }
		internal static void LootBegin(MonsterUtils __instance) { LootForElite = Of(__instance); }
		internal static void LootEnd() { LootForElite = false; }

		private static bool Of(MonsterUtils utils)
		{
			if (ModKit.Off) return false;
			try
			{
				if (utils == null || !ModKit.OnServer()) return false;
				return Elites.IsElite(utils._monster, true);
			}
			catch (Exception e) { ModKit.Fail("drops", e); return false; }
		}
	}

	internal static class Dials
	{
		internal static float Clamp(float v, float min, float max) { if (float.IsNaN(v) || v < min) return min; return v > max ? max : v; }

		// One Debug line per dial every 30 s, for whoever is proving the mod in a lab.
		private static readonly Dictionary<string, float> sTraced = new Dictionary<string, float>();
		private static void Trace(string what, float was, float now)
		{
			float t = Time.realtimeSinceStartup, last;
			if (sTraced.TryGetValue(what, out last) && t - last < 30f) return;
			sTraced[what] = t;
			ModKit.Dbg("dial " + what + " " + was.ToString("0.###", CultureInfo.InvariantCulture) + " -> " + now.ToString("0.###", CultureInfo.InvariantCulture));
		}

		private static void Scale(ref float result, float k, string what)
		{
			if (k == 1f) return;
			float was = result;
			result *= k;
			Trace(what, was, result);
		}

		internal static void Xp(ref float __result)
		{
			if (ModKit.Off) return;
			try { if (Pay.XpForElite && ModKit.OnServer()) Scale(ref __result, Clamp(CorruptedRealmPlugin.EliteXp.Value, 1f, 5f), "elite xp"); }
			catch (Exception e) { ModKit.Fail("elite xp", e); }
		}

		internal static void Gold(ref float __result)
		{
			if (ModKit.Off) return;
			try { if (Pay.GoldForElite && ModKit.OnServer()) Scale(ref __result, Clamp(CorruptedRealmPlugin.EliteGold.Value, 1f, 5f), "elite gold"); }
			catch (Exception e) { ModKit.Fail("elite gold", e); }
		}

		internal static void Loot(ref float __result)
		{
			if (ModKit.Off) return;
			try { if ((Pay.LootForElite || Pay.GoldForElite) && ModKit.OnServer()) Scale(ref __result, Clamp(CorruptedRealmPlugin.EliteLoot.Value, 1f, 5f), "elite drops"); }
			catch (Exception e) { ModKit.Fail("elite drops", e); }
		}

		// Every connected player beyond the first adds to what hostile monsters deal.
		internal static void Damage(Monster __0, ref float __result)
		{
			if (ModKit.Off) return;
			try
			{
				float per = Clamp(CorruptedRealmPlugin.PerPlayer.Value, 0f, 0.5f);
				if (per <= 0f || Realm.PlayerCount < 2 || !ModKit.OnServer()) return;
				try { if (__0 != null && __0.IsPlayerAllied) return; } catch { }
				Scale(ref __result, 1f + per * (Realm.PlayerCount - 1), "damage for " + Realm.PlayerCount + " players");
			}
			catch (Exception e) { ModKit.Fail("player scaling", e); }
		}
	}

	// What the game ties to the KIND of monster, not to the one it placed: an event when it
	// dies, boss stages, and the rank that brings story pop-ups and kill credit for "defeat X".
	// The mod never makes such a monster and never empowers one the game would not.
	internal static class Kinds
	{
		internal static bool StoryOrBoss(MonsterConfiguration cfg)
		{
			if (cfg == null) return true;
			if (cfg.EventOnDeath != InGameEvent.None) return true;
			if (cfg.DangerLevel == DangerLevel.Boss || cfg.DangerLevel == DangerLevel.MiniBoss) return true;
			return cfg.MonsterRank == MonsterRank.Boss || cfg.MonsterRank == MonsterRank.MiniBoss;
		}

		// The kind's own settings, read from the game's prefab for it: nothing is spawned.
		// found = false when the game has no prefab for the type on this version.
		internal static MonsterConfiguration Of(MonsterType type, out bool found, out bool bossStages)
		{
			found = false; bossStages = false;
			try
			{
				var npm = NetworkPrefabManager.Singleton;
				var dict = npm != null ? npm.MonsterPrefabs : null;
				NetworkObject prefab;
				if (dict == null || !dict.TryGetValue(type, out prefab) || prefab == null) return null;
				found = true;
				bossStages = prefab.GetComponentInChildren<BossInvulnerability>(true) != null;
				var cfg = prefab.GetComponent<MonsterConfiguration>();
				return cfg != null ? cfg : prefab.GetComponentInChildren<MonsterConfiguration>(true);
			}
			catch (Exception e) { ModKit.Dbg("kind " + type + ": " + e.Message); return null; }
		}
	}

	// A champion is a monster the mod ADDS, so it must not move the world's story along. The
	// game starts a story event on behalf of one monster in its death, in one of its reactions
	// (a monster can call an event in when its health falls) and in a boss's stages. Kinds with
	// an event on death, boss stages or a boss rank are never champions; for the rest, while
	// the game runs a champion's death or reaction, its call that starts an event is skipped.
	// Every other monster, the empowered ones included, does all of that as always: the mod
	// empowers a world's own monsters in place and changes nothing about how they die.
	internal static class Story
	{
		internal static bool Guarded;
		// one entry per death or reaction the game is inside of right now (one can cause another)
		private static readonly List<bool> sInside = new List<bool>();
		private static readonly HashSet<InGameEvent> sTold = new HashSet<InGameEvent>();

		private static void Begins(NetworkBehaviour on)
		{
			bool mine = false;
			try { mine = !ModKit.Off && on != null && Champions.IsMine(on.NetworkObjectId); } catch { }
			if (sInside.Count < 64) sInside.Add(mine);
		}

		internal static void DeathBegins(Monster __instance) { Begins(__instance); }
		internal static void ReactionBegins(MonsterBehaviour __instance) { Begins(__instance); }
		internal static void Ends() { if (sInside.Count > 0) sInside.RemoveAt(sInside.Count - 1); }

		// The server's tick is never inside a death: whatever is still on the list is stale.
		internal static void BetweenFrames() { if (sInside.Count > 0) sInside.Clear(); }

		internal static bool Activate(InGameEvent inGameEvent)
		{
			if (ModKit.Off || sInside.Count == 0 || !sInside[sInside.Count - 1]) return true;
			if (sTold.Add(inGameEvent)) ModKit.Say("A champion tried to start the story event " + inGameEvent + ". It was not started: champions stay out of the world's story.");
			return false;
		}
	}

	// What empowered monsters leave on the ground. The mod raises their drop chance and gold,
	// the game removes a drop only after 30 minutes, and every drop costs the server CPU for
	// as long as it lies there. So a drop that appears inside an empowered monster's own drop
	// checks (the three calls the game's death routine makes: gold, rune, item) is noted, and
	// what is left of it after Cleanup/DropsAfterMinutes is removed the way the game removes
	// one. A drop from any other monster is never on the list.
	internal static class Drops
	{
		private sealed class Drop { internal UnityEngine.Object Obj; internal float Due; }
		internal static bool CanTrack;
		private static readonly List<Drop> sDrops = new List<Drop>();
		private static HashSet<int> sBefore;
		private static float sNextLook;
		private const int MaxTracked = 2000;

		private static int Minutes { get { return Mathf.Clamp(CorruptedRealmPlugin.DropsAfterMinutes.Value, 0, 30); } }

		private static HashSet<int> Snapshot()
		{
			var set = new HashSet<int>();
			try { var e = SimpleObject.ActiveInstances.GetEnumerator(); while (e.MoveNext()) { var o = e.Current; if (o != null) set.Add(o.GetInstanceID()); } } catch (Exception ex) { ModKit.Dbg("drop list (items): " + ex.Message); }
			try { var e = RuneObject.ActiveInstances.GetEnumerator(); while (e.MoveNext()) { var o = e.Current; if (o != null) set.Add(o.GetInstanceID()); } } catch (Exception ex) { ModKit.Dbg("drop list (runes): " + ex.Message); }
			try { var e = GoldDropPickup.ActiveInstances.GetEnumerator(); while (e.MoveNext()) { var o = e.Current; if (o != null) set.Add(o.GetInstanceID()); } } catch (Exception ex) { ModKit.Dbg("drop list (gold): " + ex.Message); }
			return set;
		}

		internal static void Begin() { try { sBefore = CanTrack && Minutes > 0 ? Snapshot() : null; } catch { sBefore = null; } }

		// Round the game's rune and item drop checks on a monster (the gold check calls Begin itself).
		internal static void Before(MonsterUtils __instance)
		{
			sBefore = null;
			if (ModKit.Off) return;
			try
			{
				if (__instance == null || !ModKit.OnServer() || Minutes == 0) return;
				if (Elites.IsElite(__instance._monster, true)) Begin();
			}
			catch (Exception e) { sBefore = null; ModKit.Fail("drop check", e); }
		}

		internal static void After()
		{
			if (sBefore == null) return;
			var before = sBefore; sBefore = null;
			try
			{
				float due = Time.realtimeSinceStartup + Minutes * 60f; int added = 0;
				try { var e = SimpleObject.ActiveInstances.GetEnumerator(); while (e.MoveNext()) { var o = e.Current; if (o != null && !before.Contains(o.GetInstanceID())) { Add(o, due); added++; } } } catch { }
				try { var e = RuneObject.ActiveInstances.GetEnumerator(); while (e.MoveNext()) { var o = e.Current; if (o != null && !before.Contains(o.GetInstanceID())) { Add(o, due); added++; } } } catch { }
				try { var e = GoldDropPickup.ActiveInstances.GetEnumerator(); while (e.MoveNext()) { var o = e.Current; if (o != null && !before.Contains(o.GetInstanceID())) { Add(o, due); added++; } } } catch { }
				if (added > 0) ModKit.Dbg("noted " + added + " drop(s) of an empowered monster, " + sDrops.Count + " on the list");
			}
			catch (Exception e) { ModKit.Fail("drop check", e); }
		}

		private static void Add(UnityEngine.Object o, float due) { if (sDrops.Count < MaxTracked) sDrops.Add(new Drop { Obj = o, Due = due }); }

		// The game's own way of taking a drop off the ground, on a pick-up and when its 30
		// minutes are up, is to destroy the drop's object on the server; the network layer
		// removes it from every client. The same call is made here.
		internal static void Tick(float now)
		{
			if ((sDrops.Count == 0 && sClearedUnsaid == 0) || now < sNextLook) return;
			sNextLook = now + 5f;
			int cleared = 0;
			for (int i = sDrops.Count - 1; i >= 0; i--)
			{
				var d = sDrops[i];
				if (now < d.Due) continue;
				sDrops.RemoveAt(i);
				try
				{
					if (d.Obj == null) continue;
					var c = d.Obj.TryCast<Component>();
					if (c == null || c.gameObject == null) continue;
					UnityEngine.Object.Destroy(c.gameObject);
					cleared++;
				}
				catch (Exception e) { ModKit.Dbg("clearing a drop: " + e.Message); }
			}
			// One line a minute at most, however many rounds the clean-up took.
			sClearedUnsaid += cleared;
			if (sClearedUnsaid > 0 && now >= sNextSay)
			{
				sNextSay = now + 60f;
				ModKit.Say("Corrupted Realm cleared " + sClearedUnsaid + (sClearedUnsaid == 1 ? " drop" : " drops") + " of empowered monsters that nobody picked up.");
				sClearedUnsaid = 0;
			}
		}
		private static int sClearedUnsaid;
		private static float sNextSay;
	}

	internal sealed class Champion
	{
		internal long Key;
		internal ulong Id;
		internal Monster Monster;
		internal NetworkObject Object;
		internal string Name = "", Title = "", MonsterName = "", Target = "", Area = "";
		internal EmpowermentModifier Modifier;
		internal float SentAt, EmpowerAt;
		internal bool Empowered;
	}

	internal static class Champions
	{
		private static readonly List<Champion> sAlive = new List<Champion>();
		private static readonly System.Random sRandom = new System.Random();
		private static float sLastTick = -1f;
		private static Dictionary<int, SceneHandler> sHandlers;
		private static float sHandlersAt;
		private static readonly string[] sNames =
		{
			"Morgath", "Skarn", "Veshka", "Old Hollow", "Grimjaw", "Thessaly", "Rook", "Ashmaw", "Brannoch", "Sable",
			"Kettlebone", "Ysolde", "Varr", "Mirefang", "Dunmoor", "Hask",
		};

		internal static int AliveCount { get { return sAlive.Count; } }

		// The network ids of the monsters the mod added, kept two minutes past their end so the
		// game's death routine, which runs after the kill is credited, still finds them.
		private static readonly Dictionary<ulong, float> sMine = new Dictionary<ulong, float>();
		internal static bool IsMine(ulong id) { return sMine.ContainsKey(id); }
		private static void Ended(Champion c) { try { if (c.Id != 0) sMine[c.Id] = Time.realtimeSinceStartup + 120f; } catch { } }

		internal static Champion Find(Monster m)
		{
			if (m == null) return null;
			long key = Elites.Key(m);
			foreach (var c in sAlive) if (c.Key == key) return c;
			return null;
		}

		internal static void Remove(Champion c) { sAlive.Remove(c); Ended(c); }

		internal static void Tick(float now, List<Player> players)
		{
			float dt = sLastTick < 0f ? 0f : Mathf.Clamp(now - sLastTick, 0f, 5f);
			sLastTick = now;
			Tend(now);

			int every = Mathf.Clamp(CorruptedRealmPlugin.ChampionEvery.Value, 0, 240);
			if (every <= 0 || !Story.Guarded) return;
			float full = every * 60f;
			if (State.NextChampionIn < 0f || State.NextChampionIn > full) { State.NextChampionIn = full; State.Dirty = true; }
			if (players.Count == 0) return;
			State.NextChampionIn -= dt;
			State.Dirty = true;
			if (State.NextChampionIn > 0f) return;
			State.NextChampionIn = full;
			string why;
			var target = PickTarget(players);
			if (target == null) { ModKit.Dbg("champion: everyone connected is in a safe area, dead or not in the world yet"); State.NextChampionIn = 120f; return; }
			if (!Send(target, "", out why)) { ModKit.Dbg("champion not sent: " + why); State.NextChampionIn = 120f; }
		}

		// Champions that were sent: empower the new ones once the game has finished setting
		// them up, forget the ones that are gone, and send home the ones nobody is fighting.
		private static void Tend(float now)
		{
			if (sMine.Count > 0)
			{
				List<ulong> old = null;
				foreach (var kv in sMine) if (now >= kv.Value) { if (old == null) old = new List<ulong>(); old.Add(kv.Key); }
				if (old != null) foreach (var id in old) sMine.Remove(id);
			}
			for (int i = sAlive.Count - 1; i >= 0; i--)
			{
				var c = sAlive[i];
				bool gone = false;
				try { gone = c.Monster == null || c.Object == null || !c.Object.IsSpawned || c.Monster.IsDead(); } catch { gone = true; }
				if (gone) { sAlive.RemoveAt(i); Ended(c); continue; }
				try
				{
					if (!c.Empowered && now >= c.EmpowerAt)
					{
						c.Empowered = true;
						// The game rolls for an empowerment of its own while it sets a monster up.
						// A champion that already has one keeps it; the others get the one picked
						// for them. The champion is named after what it carries, then announced.
						if (!c.Monster.IsEmpowered && c.Modifier != null) c.Monster.ApplyEmpowerment(c.Modifier);
						string kind = "Empowered";
						try { var mod = c.Monster.ActiveEmpowermentModifier; if (mod != null && !string.IsNullOrWhiteSpace(mod.DisplayName)) kind = mod.DisplayName.Trim(); } catch { }
						c.Title = c.Name + " the " + kind;
						if (CorruptedRealmPlugin.AnnounceChampions.Value) Realm.Tell(CorruptedRealmPlugin.MsgChampionAppears.Value, c.Target, "", c.Title, Realm.WithArticle(c.MonsterName), c.Area);
						ModKit.Say("A champion came for " + c.Target + " in " + c.Area + ": " + c.Title + ", " + Realm.WithArticle(c.MonsterName) + ".");
						ModKit.Dbg("champion " + c.Title + ": empowered=" + c.Monster.IsEmpowered + " level=" + c.Monster.Level.Value);
					}
					float leave = Mathf.Clamp(CorruptedRealmPlugin.ChampionLeaveAfter.Value, 5, 120) * 60f;
					if (now - c.SentAt < leave) continue;
					bool fighting = false; try { fighting = c.Monster.IsEngaged.Value; } catch { }
					if (fighting) continue;
					sAlive.RemoveAt(i); Ended(c);
					c.Object.Despawn(true);
					if (CorruptedRealmPlugin.AnnounceChampions.Value) Realm.Tell(CorruptedRealmPlugin.MsgChampionLeaves.Value, "", "", c.Title, c.MonsterName, "");
					ModKit.Say("The champion " + c.Title + " left unfought.");
				}
				catch (Exception e) { ModKit.Dbg("champion upkeep: " + e.Message); sAlive.RemoveAt(i); Ended(c); }
			}
		}

		private static bool IsSafe(Areas.Scene scene)
		{
			if (scene == Areas.Scene.None || scene == Areas.Scene.MainMenu) return true;
			string name = scene.ToString();
			foreach (var part in (CorruptedRealmPlugin.ChampionSafeScenes.Value ?? "").Split(','))
				if (part.Trim().Length > 0 && string.Equals(part.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
			return false;
		}

		private static Player PickTarget(List<Player> players)
		{
			var ok = new List<Player>();
			foreach (var p in players)
			{
				try { if (p.Dead.Value || IsSafe(p.Scene.Value)) continue; } catch { continue; }
				ok.Add(p);
			}
			return ok.Count == 0 ? null : ok[sRandom.Next(ok.Count)];
		}

		private static SceneHandler HandlerFor(Areas.Scene scene)
		{
			float now = Time.realtimeSinceStartup;
			if (sHandlers == null || now - sHandlersAt > 60f)
			{
				sHandlers = new Dictionary<int, SceneHandler>();
				sHandlersAt = now;
				var all = UnityEngine.Object.FindObjectsOfType<SceneHandler>();
				foreach (var h in all) { try { if (h != null && h.IsSpawned) sHandlers[(int)h.Scene] = h; } catch { } }
			}
			SceneHandler found;
			return sHandlers.TryGetValue((int)scene, out found) && found != null ? found : null;
		}

		// null = this kind can be a champion; else the plain reason it cannot.
		private static readonly Dictionary<int, string> sRefusals = new Dictionary<int, string>();
		private static string Refusal(MonsterType type)
		{
			string r;
			if (sRefusals.TryGetValue((int)type, out r)) return r;
			bool found, stages;
			var cfg = Kinds.Of(type, out found, out stages);
			if (!found) r = "This version of the game has no " + Realm.Humanize(type.ToString()) + " to spawn.";
			else if (cfg == null) r = null; // unread: the monster itself is asked after it is spawned
			else if (stages || Kinds.StoryOrBoss(cfg)) r = Realm.Humanize(type.ToString()) + " is a boss or is tied to the world's story, so it cannot be a champion.";
			else r = null;
			sRefusals[(int)type] = r;
			if (r != null) ModKit.Dbg("champion kind refused: " + type + " (" + r + ")");
			return r;
		}

		private static bool TryType(string name, out MonsterType type)
		{
			type = default(MonsterType);
			name = (name ?? "").Trim();
			if (name.Length == 0 || char.IsDigit(name[0])) return false;
			return Enum.TryParse(name, true, out type) && Enum.IsDefined(typeof(MonsterType), type);
		}

		internal static EmpowermentModifier PickModifier()
		{
			var em = EmpowermentManager.Singleton;
			var all = em != null ? em.AllModifiers : null;
			if (all == null) return null;
			var ok = new List<EmpowermentModifier>();
			for (int i = 0; i < all.Count; i++) { var mod = all[i]; try { if (mod != null && mod.InProduction && mod.Type != EmpowermentType.None) ok.Add(mod); } catch { } }
			return ok.Count == 0 ? null : ok[sRandom.Next(ok.Count)];
		}

		// Sends a champion after a player. typeName empty = one of the configured types.
		internal static bool Send(Player target, string typeName, out string why)
		{
			why = "";
			if (target == null) { why = "There is nobody to send it after."; return false; }
			if (sAlive.Count >= Mathf.Clamp(CorruptedRealmPlugin.ChampionMaxAlive.Value, 1, 5)) { why = "There are already " + sAlive.Count + " champions out."; return false; }
			MonsterType type;
			if (!string.IsNullOrWhiteSpace(typeName))
			{
				if (!TryType(typeName, out type)) { why = "'" + typeName.Trim() + "' is not a monster type the game knows."; return false; }
			}
			else
			{
				var types = new List<MonsterType>();
				foreach (var part in (CorruptedRealmPlugin.ChampionTypes.Value ?? "").Split(',')) { MonsterType t; if (TryType(part, out t) && Refusal(t) == null) types.Add(t); }
				if (types.Count == 0) { why = "Champion/Types names no monster type that can be a champion."; return false; }
				type = types[sRandom.Next(types.Count)];
			}
			if (!Story.Guarded) { why = "Champions are off on this game version."; return false; }
			string refusal = Refusal(type);
			if (refusal != null) { why = refusal; return false; }
			Areas.Scene scene; Vector3 at;
			try { scene = target.Scene.Value; at = target.transform.position; } catch { why = "That player is not in the world yet."; return false; }
			if (IsSafe(scene)) { why = ModKit.NameOf(target) + " is in a safe area."; return false; }
			var handler = HandlerFor(scene);
			if (handler == null) { why = "The area " + scene + " is not loaded on the server."; return false; }

			// A point on walkable ground a short way from the player: the game's own picker for
			// its runtime spawns, asked for a spot round a point seven units out.
			double angle = sRandom.NextDouble() * Math.PI * 2.0;
			var ring = at + new Vector3((float)Math.Cos(angle) * 7f, (float)Math.Sin(angle) * 7f, 0f);
			Vector3 spot = ring;
			try { var p = ArenaManager.RandomArenaPoint(ring, 3f); if (!float.IsNaN(p.x) && !float.IsNaN(p.y) && Vector3.Distance(p, at) < 25f) spot = p; }
			catch (Exception e) { ModKit.Dbg("spawn point: " + e.Message); }

			var modifier = PickModifier();
			string name = sNames[sRandom.Next(sNames.Length)];

			var no = handler.SpawnMonsterInstance(type, spot, name, true, true, InGameEvent.None, null, true);
			var monster = no != null ? no.GetComponent<Monster>() : null;
			if (monster == null) { why = "The game did not spawn a " + type + " there."; return false; }
			// The same question once more, of the monster itself, in case the prefab could not be read.
			bool story = true;
			try { story = Kinds.StoryOrBoss(monster.MonsterConfiguration) || monster.GetComponentInChildren<BossInvulnerability>(true) != null; } catch { }
			if (story) { try { no.Despawn(true); } catch { } why = Realm.Humanize(type.ToString()) + " is a boss or is tied to the world's story, so it cannot be a champion."; return false; }
			ulong id = 0; try { id = no.NetworkObjectId; } catch { }
			if (id != 0) sMine[id] = float.MaxValue;

			var c = new Champion { Key = Elites.Key(monster), Id = id, Monster = monster, Object = no, Name = name, Title = name, Modifier = modifier, SentAt = Time.realtimeSinceStartup };
			c.EmpowerAt = c.SentAt + 1.5f;
			c.MonsterName = Realm.NameOfType(monster, type);
			c.Target = ModKit.NameOf(target); c.Area = Realm.AreaName(scene);
			sAlive.Add(c);
			State.ChampionsSent++; State.Dirty = true;
			return true;
		}
	}

	internal static class Realm
	{
		internal static int PlayerCount;
		internal static long Made;
		private static float sNextTick, sNextCensus;
		private static readonly HashSet<int> sTypesLogged = new HashSet<int>();
		private static readonly Dictionary<long, float> sSeenDeaths = new Dictionary<long, float>();

		internal static void Tick()
		{
			if (ModKit.Off) return;
			try
			{
				ModKit.Pump();
				Story.BetweenFrames();
				float now = Time.realtimeSinceStartup;
				if (now < sNextTick) return;
				sNextTick = now + 1f;
				if (!ModKit.OnServer()) return;
				Drops.Tick(now);
				var players = ModKit.Players();
				PlayerCount = players.Count;
				Rolls.Tick(now);
				Champions.Tick(now, players);
				Elites.Prune(now);
				State.SaveIfDue(now);
				if (now >= sNextCensus) { sNextCensus = now + 30f; Census(); }
			}
			catch (Exception e) { ModKit.Fail("tick", e); }
		}

		// A Debug line for whoever is proving the mod in a lab: how many of the monsters the
		// server has loaded are empowered right now.
		private static void Census()
		{
			try
			{
				var all = Monster.All;
				if (all == null) return;
				int alive = 0, elite = 0, allied = 0;
				for (int i = 0; i < all.Count; i++)
				{
					var m = all[i];
					if (m == null) continue;
					try
					{
						if (m.IsPlayerAllied) { allied++; continue; }
						if (m.IsDead()) continue;
						alive++;
						if (m.IsEmpowered) elite++;
					}
					catch { }
				}
				ModKit.Dbg("census: hostile alive=" + alive + " empowered=" + elite + " (" + (alive > 0 ? (100.0 * elite / alive).ToString("0.0", CultureInfo.InvariantCulture) : "0") + "%) empowered since start=" + Made + " second rolls=" + Rolls.Rolled + " won=" + Rolls.Won + " wild rolls=" + Rolls.WildRolled + " won=" + Rolls.WildWon + " champions out=" + Champions.AliveCount + " players=" + PlayerCount);
				for (int i = 0; i < all.Count; i++)
				{
					var m = all[i]; if (m == null) continue;
					try
					{
						var cfg = m.MonsterConfiguration; if (cfg == null) continue;
						if (!sTypesLogged.Add((int)cfg.MonsterType)) continue;
						ModKit.Dbg("census type " + cfg.MonsterType + ": can be empowered=" + cfg.CanBeEmpowered + " chance=" + cfg.EmpowermentChance.ToString("0.##", CultureInfo.InvariantCulture) + "% pool=" + (cfg.EmpowermentPool != null ? cfg.EmpowermentPool.Count : 0) + " tier dial=" + DifficultyManager.GetEmpowermentChanceMultiplier(cfg).ToString("0.##", CultureInfo.InvariantCulture));
					}
					catch { }
				}
			}
			catch (Exception e) { ModKit.Dbg("census: " + e.Message); }
		}

		// ---- names -------------------------------------------------------------------------
		internal static string TypeOf(Monster m)
		{
			try { var cfg = m.MonsterConfiguration; if (cfg != null) return cfg.MonsterType.ToString(); } catch { }
			return "Unknown";
		}

		// "WildwoodWolfAlpha" -> "Wildwood Wolf Alpha". The words a player would not say (the
		// tier and variant tags in the game's type names) are dropped.
		internal static string Humanize(string typeName)
		{
			if (string.IsNullOrEmpty(typeName)) return "monster";
			foreach (var tag in new[] { "Empowered", "Deed", "Farmlands", "CorruptedBeastKey", "FalseLead" })
				if (typeName.EndsWith(tag, StringComparison.Ordinal) && typeName.Length > tag.Length) typeName = typeName.Substring(0, typeName.Length - tag.Length);
			var sb = new StringBuilder();
			for (int i = 0; i < typeName.Length; i++)
			{
				char ch = typeName[i];
				if (char.IsDigit(ch)) continue;
				if (i > 0 && char.IsUpper(ch) && !char.IsUpper(typeName[i - 1])) sb.Append(' ');
				sb.Append(ch);
			}
			string s = sb.ToString().Trim();
			if (s.EndsWith(" T", StringComparison.Ordinal) || s.EndsWith(" B", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 2);
			return s.Length == 0 ? "monster" : s;
		}

		// The name the game shows for the monster where the server can read it, else the type
		// name in words.
		internal static string NameOfType(Monster m, MonsterType type)
		{
			try
			{
				var lm = LocalizationManager.Singleton;
				var cfg = m != null ? m.MonsterConfiguration : null;
				string s = lm != null ? (cfg != null ? lm.GetMonsterName(cfg) : lm.GetMonsterName(type)) : null;
				if (!string.IsNullOrWhiteSpace(s))
				{
					s = s.Trim();
					// The game names the monster types it can empower "<name> Empowered"; the kind of
					// empowerment is said separately in the mod's lines, so that word is dropped.
					if (s.EndsWith(" Empowered", StringComparison.OrdinalIgnoreCase) && s.Length > 10) s = s.Substring(0, s.Length - 10).Trim();
					bool looksLikeAKey = s.IndexOf('_') >= 0 || s.IndexOf('.') >= 0 || s == type.ToString() && s.IndexOf(' ') < 0 && s.Length > 12;
					if (!looksLikeAKey && s.Length > 0 && s.Length <= 48) return s;
				}
			}
			catch { }
			return Humanize(type.ToString());
		}

		internal static string NameOf(Monster m)
		{
			MonsterType type = default(MonsterType);
			try { var cfg = m.MonsterConfiguration; if (cfg != null) type = cfg.MonsterType; } catch { }
			return NameOfType(m, type);
		}

		internal static string Count(long n, string noun) { return n.ToString("N0", CultureInfo.InvariantCulture) + " " + noun + (n == 1 ? "" : "s"); }

		internal static string WithArticle(string noun)
		{
			if (string.IsNullOrEmpty(noun)) return "a monster";
			if (noun.StartsWith("The ", StringComparison.OrdinalIgnoreCase)) return noun;
			return ("AEIOUaeiou".IndexOf(noun[0]) >= 0 ? "an " : "a ") + noun;
		}

		internal static string AreaName(Areas.Scene scene)
		{
			try { var s = Areas.ConvertAreasToText(scene); if (!string.IsNullOrWhiteSpace(s)) return s.Trim(); } catch { }
			return Humanize(scene.ToString());
		}

		internal static void Tell(string template, string player, string elite, string champion, string monster, string area)
		{
			if (string.IsNullOrWhiteSpace(template)) return;
			string text = template.Replace("{player}", player ?? "").Replace("{elite}", elite ?? "").Replace("{champion}", champion ?? "").Replace("{monster}", monster ?? "").Replace("{area}", area ?? "");
			ModKit.Broadcast(text);
		}

		// ---- kills -------------------------------------------------------------------------
		// The server credits every monster death here, with the player who gets the credit.
		internal static void MonsterDeath(Player killer, Monster monster)
		{
			if (ModKit.Off) return;
			try
			{
				if (monster == null || !ModKit.OnServer()) return;
				float now = Time.realtimeSinceStartup;
				long key = Elites.Key(monster);
				float seen;
				if (sSeenDeaths.TryGetValue(key, out seen) && now - seen < 10f) return;
				if (sSeenDeaths.Count > 256) sSeenDeaths.Clear();
				sSeenDeaths[key] = now;

				var champion = Champions.Find(monster);
				bool elite = champion != null || Elites.IsElite(monster, true);
				if (!elite) { ModKit.Dbg("kill: " + TypeOf(monster) + " (ordinary)"); return; }
				bool byPlayer = false;
				try { byPlayer = killer != null && killer.OwnerClientId != NetworkManager.ServerClientId; } catch { }
				string who = byPlayer ? ModKit.NameOf(killer) : "";

				if (champion != null)
				{
					Champions.Remove(champion);
					if (!byPlayer) { ModKit.Say("The champion " + champion.Title + " died with no player to credit."); return; }
					State.ChampionsSlain++; State.ElitesSlain++; State.Dirty = true;
					if (CorruptedRealmPlugin.AnnounceChampions.Value) Tell(CorruptedRealmPlugin.MsgChampionSlain.Value, who, "", champion.Title, champion.MonsterName, "");
					ModKit.Say(who + " slew the champion " + champion.Title + ".");
					return;
				}
				if (!byPlayer) return;
				State.ElitesSlain++; State.Dirty = true;
				var mark = Elites.MarkOf(monster);
				string kind = mark != null ? mark.Kind : "Empowered";
				string name = NameOf(monster);
				string what = WithArticle(kind + " " + name);
				ModKit.Dbg("elite kill: " + who + " -> " + TypeOf(monster) + " (" + kind + ") total=" + State.ElitesSlain);
				if (CorruptedRealmPlugin.AnnounceKills.Value) Tell(CorruptedRealmPlugin.MsgEliteKill.Value, who, what, "", name, "");
			}
			catch (Exception e) { ModKit.Fail("kill", e); }
		}

		// ---- chat --------------------------------------------------------------------------
		private static bool IsAdmin(string name)
		{
			if (string.IsNullOrEmpty(name)) return false;
			foreach (var part in (CorruptedRealmPlugin.AdminNames.Value ?? "").Split(','))
				if (part.Trim().Length > 0 && string.Equals(part.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
			return false;
		}

		internal static bool Chat(Message message)
		{
			if (ModKit.Off) return true;
			try
			{
				if (!CorruptedRealmPlugin.Commands.Value || !ModKit.OnServer()) return true;
				string verb, rest; ulong source;
				if (!ModKit.IsCommand(message, out verb, out rest, out source)) return true;
				if (verb != "realm" && verb != "champion") return true;
				var me = ModKit.PlayerOf(source);
				if (me == null) return true;
				if (verb == "realm") { foreach (var line in RealmLines()) ModKit.Whisper(source, line, false); return false; }

				if (!IsAdmin(ModKit.NameOf(me))) { ModKit.Whisper(source, "Only the server's admins can call a champion.", false); return false; }
				// /champion            one of the configured types, after the caller
				// /champion <type>     that monster type, after the caller
				string why;
				if (Champions.Send(me, rest, out why)) ModKit.Whisper(source, "A champion is on its way to you.", false);
				else ModKit.Whisper(source, "No champion was sent. " + why, false);
				return false;
			}
			catch (Exception e) { ModKit.Fail("chat", e); return true; }
		}

		private static IEnumerable<string> RealmLines()
		{
			var inv = CultureInfo.InvariantCulture;
			float chance = Dials.Clamp(CorruptedRealmPlugin.EliteChance.Value, 1f, 10f);
			int levels = Mathf.Clamp(CorruptedRealmPlugin.EliteLevels.Value, 0, 20);
			float wild = Dials.Clamp(CorruptedRealmPlugin.WildChance.Value, 0f, 25f);
			yield return "Corrupted Realm: empowered monsters are " + chance.ToString("0.##", inv) + "x as common"
				+ (wild > 0f ? ", and " + wild.ToString("0.##", inv) + "% of ordinary monsters are empowered too." : ".");
			yield return "They " + (levels > 0 ? "gain " + levels + (levels == 1 ? " level and " : " levels and ") : "")
				+ "pay " + Dials.Clamp(CorruptedRealmPlugin.EliteXp.Value, 1f, 5f).ToString("0.##", inv) + "x XP, "
				+ Dials.Clamp(CorruptedRealmPlugin.EliteGold.Value, 1f, 5f).ToString("0.##", inv) + "x gold and "
				+ Dials.Clamp(CorruptedRealmPlugin.EliteLoot.Value, 1f, 5f).ToString("0.##", inv) + "x drops.";
			string second = "Slain on this server: " + Count(State.ElitesSlain, "empowered monster") + ", " + Count(State.ChampionsSlain, "champion") + ".";
			int every = Mathf.Clamp(CorruptedRealmPlugin.ChampionEvery.Value, 0, 240);
			if (every > 0 && State.NextChampionIn >= 0f)
			{
				int minutes = Mathf.Max(1, Mathf.CeilToInt(State.NextChampionIn / 60f));
				second += " The next champion comes in about " + minutes + (minutes == 1 ? " minute." : " minutes.");
			}
			yield return second;
			float per = Dials.Clamp(CorruptedRealmPlugin.PerPlayer.Value, 0f, 0.5f);
			if (per > 0f && PlayerCount > 1)
				yield return "With " + PlayerCount + " players on, monsters hit " + (1f + per * (PlayerCount - 1)).ToString("0.##", inv) + "x as hard.";
		}
	}
}
