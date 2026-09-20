using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using WaygateMods.Kit;

namespace WaygateMods.DarkNights
{
	// Nights are dangerous, and every Nth night is a blood moon.
	//
	// From dusk to dawn the server scales the game's own difficulty dials at the point where
	// it reads them: monster damage, how fast monster specials come back, how many monsters
	// spawn empowered, and what kills pay. On a blood moon the dials go up again, the weather
	// turns to storm, every dead monster returns at dusk, monsters that spawn or return that
	// night are larger and carry a shield until dawn, and nobody can sleep through it.
	//
	// Server-side only. Nothing is written to the world or to a character: the dials are
	// multiplied per call, so with the mod removed the game reads its own values again.
	// Monster health and speed are left alone on purpose. The game re-reads the health dial
	// every second on both sides, so a dial that changes at dusk would re-scale living
	// monsters and a player's health bar could disagree with the server.
	[BepInPlugin(PluginId, "Dark Nights", Version)]
	public class DarkNightsPlugin : BasePlugin
	{
		public const string PluginId = "com.humangenome.waygatemods.darknights";
		public const string ModId = "HumanGenome-DarkNights";
		public const string Version = "1.0.1";

		internal static ConfigEntry<float> NightDamage, NightCooldowns, NightElites, NightXp, NightLoot;
		internal static ConfigEntry<int> BloodEvery, WarnGameMinutes, ShieldPercent;
		internal static ConfigEntry<float> BloodDamage, BloodCooldowns, BloodElites, BloodXp, BloodLoot;
		internal static ConfigEntry<bool> BloodStorm, BloodRespawn, BloodGiants, BloodShield, BloodBlockSleep;
		internal static ConfigEntry<bool> AnnounceNights;
		internal static ConfigEntry<string> MsgDusk, MsgDawn, MsgWarn, MsgRise, MsgPass, MsgNoSleep;

		public override void Load()
		{
			NightDamage = Config.Bind("Night", "Damage", 1.5f, "Damage monsters deal at night. 1 = the game's own value. 1 to 5.");
			NightCooldowns = Config.Bind("Night", "Cooldowns", 1.25f, "How fast monster special attacks come back at night. 1 to 3.");
			NightElites = Config.Bind("Night", "Elites", 2f, "How much more often a monster spawns empowered at night. Only the kinds the game can empower (their own chance is 33%, so 3 empowers every one of them). 1 to 3.");
			NightXp = Config.Bind("Night", "XP", 1.25f, "XP from kills at night. 1 to 5.");
			NightLoot = Config.Bind("Night", "Loot", 1.25f, "Drop chance and gold at night. 1 to 5.");
			AnnounceNights = Config.Bind("Night", "Announce", true, "A chat line at dusk and at dawn on ordinary nights.");

			BloodEvery = Config.Bind("BloodMoon", "EveryNights", 7, "Every Nth night is a blood moon. 0 = never.");
			BloodDamage = Config.Bind("BloodMoon", "Damage", 2f, "Damage monsters deal under a blood moon. 1 to 5.");
			BloodCooldowns = Config.Bind("BloodMoon", "Cooldowns", 1.5f, "How fast monster special attacks come back under a blood moon. 1 to 3.");
			BloodElites = Config.Bind("BloodMoon", "Elites", 3f, "How much more often a monster spawns empowered under a blood moon. Only the kinds the game can empower. 1 to 3.");
			BloodXp = Config.Bind("BloodMoon", "XP", 2f, "XP from kills under a blood moon. 1 to 5.");
			BloodLoot = Config.Bind("BloodMoon", "Loot", 2f, "Drop chance and gold under a blood moon. 1 to 5.");
			BloodStorm = Config.Bind("BloodMoon", "Storm", true, "Storm weather everywhere until dawn.");
			BloodRespawn = Config.Bind("BloodMoon", "RespawnAll", true, "Every dead monster returns when the blood moon rises.");
			BloodGiants = Config.Bind("BloodMoon", "Giants", true, "Monsters that spawn or return under a blood moon are larger until dawn. The game draws a larger monster at one and a half times its size; the size cannot be chosen.");
			BloodShield = Config.Bind("BloodMoon", "Shield", true, "Monsters that spawn or return under a blood moon carry a shield. It fades away by dawn.");
			ShieldPercent = Config.Bind("BloodMoon", "ShieldPercent", 50, "How strong that shield is, as a share of the monster's health. 10 to 200.");
			BloodBlockSleep = Config.Bind("BloodMoon", "BlockSleep", true, "Nobody can sleep the blood moon away.");
			WarnGameMinutes = Config.Bind("BloodMoon", "WarnGameMinutes", 60, "Game minutes of warning before a blood moon rises. 0 = no warning. Up to 180.");

			MsgDusk = Config.Bind("Messages", "Dusk", "Night falls. The monsters grow bold.", "Chat line at dusk on an ordinary night. Empty = none.");
			MsgDawn = Config.Bind("Messages", "Dawn", "Dawn. The night's danger has passed.", "Chat line at dawn after an ordinary night. Empty = none.");
			MsgWarn = Config.Bind("Messages", "BloodMoonWarning", "The sky reddens. A blood moon rises at dusk. Find shelter or find your friends.", "Chat line before a blood moon. Empty = none.");
			MsgRise = Config.Bind("Messages", "BloodMoonRises", "The blood moon rises. The dead return, and nothing sleeps tonight.", "Chat line when a blood moon rises. Empty = none.");
			MsgPass = Config.Bind("Messages", "BloodMoonPasses", "The blood moon passes. You lived.", "Chat line at dawn after a blood moon. Empty = none.");
			MsgNoSleep = Config.Bind("Messages", "NoSleep", "Nobody sleeps under a blood moon.", "Chat line for a player who tries to sleep under a blood moon.");

			var harmony = new Harmony(PluginId);
			ModKit.Init(Log, harmony, "Dark Nights");
			ModKit.WatchConfig(Config);
			try
			{
				State.Open(Path.Combine(Paths.ConfigPath, ModId));
				ModKit.Patch(typeof(TransitionManager), "Update", null, typeof(Night), null, nameof(Night.Tick), true, "the server clock");
				var dm = typeof(DifficultyManager);
				ModKit.Patch(dm, "GetMonsterDamageDealtMultiplier", null, typeof(Dials), null, nameof(Dials.Damage), false, "night damage");
				ModKit.Patch(dm, "GetCooldownRecoveryMultiplier", null, typeof(Dials), null, nameof(Dials.Cooldowns), false, "night aggression");
				// The game's empowerment roll reads its chance dial straight off the object DialsFor
				// returns (its own getter for that dial has no caller), so that is where the dial goes.
				ModKit.Patch(dm, "DialsFor", new[] { typeof(MonsterConfiguration) }, typeof(Dials), null, nameof(Dials.Resolved), false, "more empowered monsters at night");
				ModKit.Patch(dm, "GetXPMultiplier", null, typeof(Dials), null, nameof(Dials.Xp), false, "night XP");
				ModKit.Patch(dm, "GetGoldMultiplier", null, typeof(Dials), null, nameof(Dials.Loot), false, "night gold");
				ModKit.Patch(dm, "GetLootChanceMultiplier", null, typeof(Dials), null, nameof(Dials.Loot), false, "night loot");
				// The game clears a monster's empowerment right before it rolls for one, at set-up and
				// on its return from death: the cue for a monster that has just entered the night.
				ModKit.Patch(typeof(Monster), "ClearEmpowerment", null, typeof(Dress), nameof(Dress.Entering), null, false, "blood moon giants and shields");
				ModKit.Patch(typeof(SleepManager), "RegisterSleepServerRpc", null, typeof(Night), nameof(Night.Sleep), null, false, "the blood moon sleep rule");
				if (!ModKit.Off)
				{
					int every = Mathf.Max(0, BloodEvery.Value);
					ModKit.Say("Dark Nights " + Version + " is on: nights hit " + Dials.Clamp(NightDamage.Value, 5f).ToString("0.##", CultureInfo.InvariantCulture) + "x as hard"
						+ (every > 0 ? ", a blood moon every " + every + " nights (the next one is " + (State.NightsUntilBloodMoon(every) == 1 ? "the coming night" : State.NightsUntilBloodMoon(every) + " nights away") + ")." : ", blood moons off."));
				}
			}
			catch (Exception e) { ModKit.Dbg("load: " + e); ModKit.TurnOff("it could not start"); }
		}
	}

	// What the mod remembers between restarts: how many nights it has seen, and whether the
	// server stopped in the middle of one. A plain text file, three lines.
	internal static class State
	{
		private static string sFile = "";
		internal static long Nights;
		internal static bool InNight, InBloodMoon;

		internal static void Open(string dir)
		{
			Directory.CreateDirectory(dir);
			sFile = Path.Combine(dir, "nights.txt");
			if (!File.Exists(sFile)) return;
			try
			{
				foreach (var line in File.ReadAllLines(sFile))
				{
					int eq = line.IndexOf('='); if (eq <= 0) continue;
					string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
					if (k == "nights") long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out Nights);
					else if (k == "in_night") InNight = v == "true";
					else if (k == "in_blood_moon") InBloodMoon = v == "true";
				}
				if (Nights < 0) Nights = 0;
			}
			catch (Exception e) { ModKit.Dbg("state read: " + e.Message); Nights = 0; InNight = false; InBloodMoon = false; }
		}

		internal static void Save()
		{
			try
			{
				string tmp = sFile + ".tmp";
				File.WriteAllLines(tmp, new[] { "nights=" + Nights.ToString(CultureInfo.InvariantCulture), "in_night=" + (InNight ? "true" : "false"), "in_blood_moon=" + (InBloodMoon ? "true" : "false") });
				if (File.Exists(sFile)) File.Replace(tmp, sFile, null); else File.Move(tmp, sFile);
			}
			catch (Exception e) { ModKit.Fail("state write", e); }
		}

		// Nights until the next blood moon, counting the coming night as 1.
		internal static long NightsUntilBloodMoon(int every)
		{
			if (every <= 0) return 0;
			return every - (Nights % every);
		}
	}

	internal static class Night
	{
		internal static bool IsNight, IsBloodMoon;
		private static float sNextTick;
		private static bool sClockKnown, sWarned;
		private static Dictionary<Kingdom, Weathers> sWeatherBefore;

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
				bool night; GameTime time;
				if (!ModKit.TryClock(out night, out time)) return;
				Clock.Sample(now, time);
				Dress.Tick(now, time);

				if (!sClockKnown)
				{
					sClockKnown = true;
					// The server came up in the middle of a night it had already counted: carry on
					// with the same night instead of counting it twice.
					if (night && State.InNight) { IsNight = true; IsBloodMoon = State.InBloodMoon; if (IsBloodMoon) ModKit.Say("The server restarted under a blood moon. It lasts until dawn."); return; }
					if (!night && State.InNight) { State.InNight = false; State.InBloodMoon = false; State.Save(); }
					IsNight = false; IsBloodMoon = false;
				}

				if (night && !IsNight) Dusk();
				else if (!night && IsNight) Dawn();
				else if (!night) Warn(time);
			}
			catch (Exception e) { ModKit.Fail("tick", e); }
		}

		private static bool NextNightIsBloodMoon()
		{
			int every = Mathf.Max(0, DarkNightsPlugin.BloodEvery.Value);
			return every > 0 && (State.Nights + 1) % every == 0;
		}

		private static void Warn(GameTime time)
		{
			if (sWarned || !NextNightIsBloodMoon()) return;
			int minutes = Mathf.Clamp(DarkNightsPlugin.WarnGameMinutes.Value, 0, 180);
			if (minutes == 0) return;
			var tm = TimeManager.Singleton;
			if (tm == null) return;
			int total = time.Hour * 60 + time.Minute + minutes;
			var ahead = new GameTime(time.Year, time.Month, time.Day, (total / 60) % 24, total % 60);
			if (!tm.DetermineIfNight(ahead)) return;
			sWarned = true;
			Tell(DarkNightsPlugin.MsgWarn.Value);
			ModKit.Say("Blood moon warning sent. It rises at dusk.");
		}

		private static void Dusk()
		{
			IsNight = true; sWarned = false; Dials.ForgetCopies(); Dress.Dressed = 0;
			State.Nights++;
			IsBloodMoon = false;
			int every = Mathf.Max(0, DarkNightsPlugin.BloodEvery.Value);
			if (every > 0 && State.Nights % every == 0) IsBloodMoon = true;
			State.InNight = true; State.InBloodMoon = IsBloodMoon; State.Save();

			if (!IsBloodMoon)
			{
				if (DarkNightsPlugin.AnnounceNights.Value) Tell(DarkNightsPlugin.MsgDusk.Value);
				long left = every > 0 ? every - State.Nights % every : 0;
				ModKit.Say("Night " + State.Nights + " has fallen." + (every > 0 ? (left == 1 ? " The next night is a blood moon." : " Blood moon in " + left + " nights.") : ""));
				return;
			}
			Tell(DarkNightsPlugin.MsgRise.Value);
			int back = 0;
			if (DarkNightsPlugin.BloodRespawn.Value)
			{
				try { var mm = MonsterManager.Singleton; if (mm != null) back = mm.RespawnAllDeadMonsters(); }
				catch (Exception e) { ModKit.Dbg("respawn all: " + e.Message); }
			}
			if (DarkNightsPlugin.BloodStorm.Value) Storm();
			ModKit.Say("Night " + State.Nights + ": the blood moon has risen." + (back > 0 ? " " + back + " dead monsters returned." : ""));
		}

		private static void Dawn()
		{
			bool wasBlood = IsBloodMoon;
			IsNight = false; IsBloodMoon = false; Dials.ForgetCopies();
			State.InNight = false; State.InBloodMoon = false; State.Save();
			if (wasBlood)
			{
				Dress.Dawn();
				RestoreWeather();
				Tell(DarkNightsPlugin.MsgPass.Value);
				ModKit.Say("The blood moon has passed.");
			}
			else if (DarkNightsPlugin.AnnounceNights.Value) Tell(DarkNightsPlugin.MsgDawn.Value);
		}

		private static void Tell(string text) { if (!string.IsNullOrWhiteSpace(text)) ModKit.Broadcast(text); }

		// Storm in every kingdom until dawn. The replicated weather list is what every client
		// follows; the game's own ChangeWeather refuses inside its start-of-world sunny period,
		// so the list is written when the call did not take. The saved world weather is not
		// touched: at dawn the weather that was there is put back.
		private static void Storm()
		{
			try
			{
				var wm = WeatherManager.Singleton;
				if (wm == null) return;
				sWeatherBefore = new Dictionary<Kingdom, Weathers>();
				foreach (Kingdom k in Enum.GetValues(typeof(Kingdom)))
				{
					if (k == Kingdom.None || k == Kingdom.Sanctum) continue;
					try
					{
						var was = wm.CurrentWeather(k);
						sWeatherBefore[k] = was;
						SetWeather(wm, k, Weathers.Stormy);
					}
					catch (Exception e) { ModKit.Dbg("storm " + k + ": " + e.Message); }
				}
			}
			catch (Exception e) { ModKit.Dbg("storm: " + e.Message); }
		}

		private static void RestoreWeather()
		{
			if (sWeatherBefore == null) return;
			try
			{
				var wm = WeatherManager.Singleton;
				if (wm != null) foreach (var kv in sWeatherBefore) { try { if (wm.CurrentWeather(kv.Key) == Weathers.Stormy) SetWeather(wm, kv.Key, kv.Value == Weathers.None ? Weathers.Sunny : kv.Value); } catch { } }
			}
			catch (Exception e) { ModKit.Dbg("weather restore: " + e.Message); }
			sWeatherBefore = null;
		}

		private static void SetWeather(WeatherManager wm, Kingdom k, Weathers w)
		{
			wm.ChangeWeather(k, w);
			if (wm.CurrentWeather(k) == w) return;
			var list = wm.WeatherStates; int idx = (int)k;
			if (list != null && idx >= 0 && idx < list.Count) list[idx] = (int)w;
		}

		// A player lying down under a blood moon is told no, and the game's own registration
		// is skipped, so the night cannot be slept away.
		[HarmonyPriority(Priority.First)]
		internal static bool Sleep(ulong playerId)
		{
			if (ModKit.Off) return true;
			try
			{
				if (!IsBloodMoon || !DarkNightsPlugin.BloodBlockSleep.Value || !ModKit.OnServer()) return true;
				ModKit.Whisper(playerId, DarkNightsPlugin.MsgNoSleep.Value, false);
				var p = ModKit.PlayerOf(playerId);
				ModKit.Say((p != null ? ModKit.NameOf(p) : "A player") + " tried to sleep under the blood moon.");
				return false;
			}
			catch (Exception e) { ModKit.Fail("sleep", e); return true; }
		}
	}

	internal static class Dials
	{
		internal static float Clamp(float v, float max) { if (float.IsNaN(v) || v < 1f) return 1f; return v > max ? max : v; }

		private static float Pick(ConfigEntry<float> night, ConfigEntry<float> blood, float max)
		{
			if (!Night.IsNight) return 1f;
			return Clamp(Night.IsBloodMoon ? blood.Value : night.Value, max);
		}

		private static void Scale(ref float result, ConfigEntry<float> night, ConfigEntry<float> blood, float max, string what)
		{
			if (ModKit.Off) return;
			try
			{
				float k = Pick(night, blood, max);
				if (k == 1f || !ModKit.OnServer()) return;
				float was = result;
				result *= k;
				Trace(what, was, result);
			}
			catch (Exception e) { ModKit.Fail(what, e); }
		}

		// One Debug line per dial every 30 s, for whoever reads the log with Debug enabled.
		private static readonly Dictionary<string, float> sTraced = new Dictionary<string, float>();
		private static void Trace(string what, float was, float now)
		{
			float t = Time.realtimeSinceStartup, last;
			if (sTraced.TryGetValue(what, out last) && t - last < 30f) return;
			sTraced[what] = t;
			ModKit.Dbg("dial " + what + (Night.IsBloodMoon ? " (blood moon) " : " (night) ") + was.ToString("0.###", CultureInfo.InvariantCulture) + " -> " + now.ToString("0.###", CultureInfo.InvariantCulture));
		}

		internal static void Damage(ref float __result) { Scale(ref __result, DarkNightsPlugin.NightDamage, DarkNightsPlugin.BloodDamage, 5f, "damage"); }
		internal static void Cooldowns(ref float __result) { Scale(ref __result, DarkNightsPlugin.NightCooldowns, DarkNightsPlugin.BloodCooldowns, 3f, "cooldowns"); }

		// More empowered monsters. The game keeps one shared set of dials per monster kind and
		// its roll reads the chance straight off that object, so the object is never edited: at
		// night the roll is handed a copy whose chance is scaled. Every other value in the copy is
		// the game's own, so the other dials (each scaled where the game reads it) are not applied
		// twice. Only kinds the game can empower have a chance to scale.
		private sealed class Copy { internal DifficultyManager.ResolvedDials Dials; internal float Factor, GameChance; }
		private static readonly Dictionary<IntPtr, Copy> sCopies = new Dictionary<IntPtr, Copy>();

		internal static void ForgetCopies() { sCopies.Clear(); }

		internal static void Resolved(ref DifficultyManager.ResolvedDials __result)
		{
			if (ModKit.Off) return;
			try
			{
				if (__result == null || !Night.IsNight) return;
				float k = Pick(DarkNightsPlugin.NightElites, DarkNightsPlugin.BloodElites, 3f);
				if (k == 1f || !ModKit.OnServer()) return;
				float game = __result.EmpowermentChanceMultiplier;
				Copy c;
				if (!sCopies.TryGetValue(__result.Pointer, out c) || c.Factor != k || c.GameChance != game)
				{
					if (sCopies.Count > 128) sCopies.Clear();
					var clone = __result.MemberwiseClone().Cast<DifficultyManager.ResolvedDials>();
					clone.EmpowermentChanceMultiplier = game * k;
					c = new Copy { Dials = clone, Factor = k, GameChance = game };
					sCopies[__result.Pointer] = c;
				}
				__result = c.Dials;
				Trace("elites", game, game * k);
			}
			catch (Exception e) { ModKit.Fail("elites", e); }
		}
		internal static void Xp(ref float __result) { Scale(ref __result, DarkNightsPlugin.NightXp, DarkNightsPlugin.BloodXp, 5f, "xp"); }
		internal static void Loot(ref float __result) { Scale(ref __result, DarkNightsPlugin.NightLoot, DarkNightsPlugin.BloodLoot, 5f, "loot"); }

	}

	// Monsters that enter a blood moon night (spawned, or back from death) are dressed for it a
	// moment later: larger, and carrying a shield. Both are ordinary game effects added the way
	// the game adds them, and both end at dawn by their own clocks, so nothing has to be undone.
	//
	// The shield is the effect the game's own shield spells use (a shield that runs down over
	// its time): the game writes the monster's shield from it, amount x time left / time. The
	// plain Shield effect does not set a shield, and the run-down effect without an end gives a
	// shield that is not a number, so the time is always finite here.
	internal static class Dress
	{
		private sealed class Due { internal Monster M; internal float At; }
		private static readonly Dictionary<ulong, Due> sDue = new Dictionary<ulong, Due>();
		private static readonly List<ulong> sReady = new List<ulong>();
		internal static long Dressed;
		private static readonly List<Monster> sWorn = new List<Monster>();

		// Dawn: what the blood moon put on comes off, also when the night was cut short (slept
		// away, or the clock was set), so "until dawn" holds whatever the effects' own clocks say.
		internal static void Dawn()
		{
			int off = 0;
			foreach (var m in sWorn)
			{
				try
				{
					if (m == null || !m.IsSpawned || m.IsDead()) continue;
					var fx = m.GetComponent<Effects>(); if (fx == null) continue;
					bool any = false;
					if (Effects.IsEffectOnObject(m, Effect.ShieldDownOverTime)) { fx.RemoveEffectFromObject(Effect.ShieldDownOverTime, (Spell)0); any = true; }
					if (Effects.IsEffectOnObject(m, Effect.LargeSize)) { fx.RemoveEffectFromObject(Effect.LargeSize, (Spell)0); any = true; }
					if (any) off++;
				}
				catch (Exception e) { ModKit.Dbg("undressing a monster: " + e.Message); }
			}
			if (off > 0) ModKit.Dbg("dawn: the blood moon's size and shield taken off " + off + " monsters");
			sWorn.Clear(); sDue.Clear();
		}

		internal static void Entering(Monster __instance)
		{
			if (ModKit.Off) return;
			try
			{
				if (__instance == null || !Night.IsBloodMoon || !ModKit.OnServer()) return;
				if (!DarkNightsPlugin.BloodGiants.Value && !DarkNightsPlugin.BloodShield.Value) return;
				if (sDue.Count > 4096) sDue.Clear();
				sDue[__instance.NetworkObjectId] = new Due { M = __instance, At = Time.realtimeSinceStartup + 1f };
			}
			catch (Exception e) { ModKit.Fail("blood moon monsters", e); }
		}

		internal static void Tick(float now, GameTime time)
		{
			if (sDue.Count == 0) return;
			if (!Night.IsBloodMoon) { sDue.Clear(); return; }
			sReady.Clear();
			foreach (var kv in sDue) if (now >= kv.Value.At) sReady.Add(kv.Key);
			if (sReady.Count == 0) return;
			float seconds = Clock.SecondsUntilDawn(time);
			int done = 0;
			foreach (var key in sReady)
			{
				var m = sDue[key].M; sDue.Remove(key);
				try { if (One(m, seconds)) { done++; if (sWorn.Count < 8192) sWorn.Add(m); } } catch (Exception e) { ModKit.Dbg("dressing a monster: " + e.Message); }
			}
			if (done > 0) { Dressed += done; ModKit.Dbg("blood moon: " + done + " monsters made larger and/or shielded for " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s (" + Dressed + " tonight)"); }
		}

		private static bool One(Monster m, float seconds)
		{
			if (m == null || !m.IsSpawned || m.IsDead() || m.IsPlayerAllied) return false;
			var fx = m.GetComponent<Effects>();
			if (fx == null) return false;
			bool any = false;
			if (DarkNightsPlugin.BloodGiants.Value && !Effects.IsEffectOnObject(m, Effect.LargeSize))
			{
				// the game draws every monster that carries this effect at its own fixed large size
				// (one and a half times); the number on the effect is not read for that
				fx.AddEffectToObject(new EffectValues(Effect.LargeSize, (Spell)0, seconds, 999, 1.5f, 0f, 0UL, -1));
				any = true;
			}
			if (DarkNightsPlugin.BloodShield.Value && !Effects.IsEffectOnObject(m, Effect.ShieldDownOverTime))
			{
				float max = m.MaxHealth.Value;
				float amount = max * Mathf.Clamp(DarkNightsPlugin.ShieldPercent.Value, 10, 200) / 100f;
				if (amount >= 1f && !float.IsNaN(amount) && !float.IsInfinity(amount) && seconds >= 1f)
				{
					fx.AddEffectToObject(new EffectValues(Effect.ShieldDownOverTime, (Spell)0, seconds, 999, 1f, amount, 0UL, -1));
					any = true;
				}
			}
			return any;
		}
	}

	// How long until dawn, in real seconds: the clock's own pace is measured while it runs (an
	// owner may have changed the day length), and the game is asked which minute stops being night.
	internal static class Clock
	{
		private static float sSecondsPerGameMinute = 1680f / 1440f;
		private static float sLastReal = -1f, sAccReal; private static int sLastMinute = -1, sAccMinutes;

		internal static void Sample(float now, GameTime time)
		{
			int minute = time.Hour * 60 + time.Minute;
			if (sLastMinute >= 0)
			{
				int d = (minute - sLastMinute + 1440) % 1440; float real = now - sLastReal;
				if (d > 5 || real <= 0f || real > 10f) { sAccReal = 0f; sAccMinutes = 0; }   // the clock was set, or the night was slept away
				else
				{
					sAccReal += real; sAccMinutes += d;
					if (sAccMinutes >= 30) { float pace = sAccReal / sAccMinutes; if (pace > 0.05f && pace < 60f) sSecondsPerGameMinute = pace; sAccReal = 0f; sAccMinutes = 0; }
				}
			}
			sLastMinute = minute; sLastReal = now;
		}

		internal static float SecondsUntilDawn(GameTime time)
		{
			int minutes = 0;
			try
			{
				var tm = TimeManager.Singleton;
				int start = time.Hour * 60 + time.Minute;
				for (minutes = 5; minutes <= 16 * 60; minutes += 5)
				{
					int total = start + minutes;
					if (!tm.DetermineIfNight(new GameTime(time.Year, time.Month, time.Day, (total / 60) % 24, total % 60))) break;
				}
			}
			catch { minutes = 360; }
			return Mathf.Clamp(minutes * sSecondsPerGameMinute, 30f, 3600f);
		}
	}
}
