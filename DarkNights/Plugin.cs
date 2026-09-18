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
	// turns to storm, every dead monster returns at dusk, monsters that spawn that night are
	// larger and shielded, and nobody can sleep through it.
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
		public const string Version = "1.0.0";

		internal static ConfigEntry<float> NightDamage, NightCooldowns, NightElites, NightXp, NightLoot;
		internal static ConfigEntry<int> BloodEvery, WarnGameMinutes;
		internal static ConfigEntry<float> BloodDamage, BloodCooldowns, BloodElites, BloodXp, BloodLoot, GiantSize;
		internal static ConfigEntry<bool> BloodStorm, BloodRespawn, BloodGiants, BloodShield, BloodBlockSleep;
		internal static ConfigEntry<bool> AnnounceNights;
		internal static ConfigEntry<string> MsgDusk, MsgDawn, MsgWarn, MsgRise, MsgPass, MsgNoSleep;

		public override void Load()
		{
			NightDamage = Config.Bind("Night", "Damage", 1.5f, "Damage monsters deal at night. 1 = the game's own value. 1 to 5.");
			NightCooldowns = Config.Bind("Night", "Cooldowns", 1.25f, "How fast monster special attacks come back at night. 1 to 3.");
			NightElites = Config.Bind("Night", "Elites", 2f, "How much more often a monster spawns empowered at night. 1 to 10.");
			NightXp = Config.Bind("Night", "XP", 1.25f, "XP from kills at night. 1 to 5.");
			NightLoot = Config.Bind("Night", "Loot", 1.25f, "Drop chance and gold at night. 1 to 5.");
			AnnounceNights = Config.Bind("Night", "Announce", true, "A chat line at dusk and at dawn on ordinary nights.");

			BloodEvery = Config.Bind("BloodMoon", "EveryNights", 7, "Every Nth night is a blood moon. 0 = never.");
			BloodDamage = Config.Bind("BloodMoon", "Damage", 2f, "Damage monsters deal under a blood moon. 1 to 5.");
			BloodCooldowns = Config.Bind("BloodMoon", "Cooldowns", 1.5f, "How fast monster special attacks come back under a blood moon. 1 to 3.");
			BloodElites = Config.Bind("BloodMoon", "Elites", 4f, "How much more often a monster spawns empowered under a blood moon. 1 to 10.");
			BloodXp = Config.Bind("BloodMoon", "XP", 2f, "XP from kills under a blood moon. 1 to 5.");
			BloodLoot = Config.Bind("BloodMoon", "Loot", 2f, "Drop chance and gold under a blood moon. 1 to 5.");
			BloodStorm = Config.Bind("BloodMoon", "Storm", true, "Storm weather everywhere until dawn.");
			BloodRespawn = Config.Bind("BloodMoon", "RespawnAll", true, "Every dead monster returns when the blood moon rises.");
			BloodGiants = Config.Bind("BloodMoon", "Giants", true, "Monsters that spawn under a blood moon are larger.");
			GiantSize = Config.Bind("BloodMoon", "GiantSize", 1.4f, "How much larger. 1.1 to 2.");
			BloodShield = Config.Bind("BloodMoon", "Shield", true, "Monsters that spawn under a blood moon start with a shield.");
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
				ModKit.Patch(dm, "GetEmpowermentChanceMultiplier", null, typeof(Dials), null, nameof(Dials.Elites), false, "more elites at night");
				ModKit.Patch(dm, "GetXPMultiplier", null, typeof(Dials), null, nameof(Dials.Xp), false, "night XP");
				ModKit.Patch(dm, "GetGoldMultiplier", null, typeof(Dials), null, nameof(Dials.Loot), false, "night gold");
				ModKit.Patch(dm, "GetLootChanceMultiplier", null, typeof(Dials), null, nameof(Dials.Loot), false, "night loot");
				ModKit.Patch(dm, "GetExtraStartingEffects", null, typeof(Dials), null, nameof(Dials.StartingEffects), false, "blood moon giants and shields");
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
			IsNight = true; sWarned = false;
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
			IsNight = false; IsBloodMoon = false;
			State.InNight = false; State.InBloodMoon = false; State.Save();
			if (wasBlood)
			{
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

		// One Debug line per dial every 30 s, for whoever is proving the mod in a lab.
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
		internal static void Elites(ref float __result) { Scale(ref __result, DarkNightsPlugin.NightElites, DarkNightsPlugin.BloodElites, 10f, "elites"); }
		internal static void Xp(ref float __result) { Scale(ref __result, DarkNightsPlugin.NightXp, DarkNightsPlugin.BloodXp, 5f, "xp"); }
		internal static void Loot(ref float __result) { Scale(ref __result, DarkNightsPlugin.NightLoot, DarkNightsPlugin.BloodLoot, 5f, "loot"); }

		// Monsters that spawn under a blood moon carry extra starting effects. The game hands
		// back the list that belongs to its difficulty settings, so that list is never touched:
		// a new list is returned with the game's entries first.
		internal static void StartingEffects(ref Il2CppSystem.Collections.Generic.List<MonsterStartingEffect> __result)
		{
			if (ModKit.Off) return;
			try
			{
				if (!Night.IsBloodMoon || !ModKit.OnServer()) return;
				bool giants = DarkNightsPlugin.BloodGiants.Value, shield = DarkNightsPlugin.BloodShield.Value;
				if (!giants && !shield) return;
				var list = new Il2CppSystem.Collections.Generic.List<MonsterStartingEffect>();
				if (__result != null) for (int i = 0; i < __result.Count; i++) list.Add(__result[i]);
				if (giants) list.Add(New(Effect.LargeSize, Mathf.Clamp(DarkNightsPlugin.GiantSize.Value, 1.1f, 2f)));
				if (shield) list.Add(New(Effect.Shield, 0.5f));
				__result = list;
				Trace("spawn effects", 0f, list.Count);
			}
			catch (Exception e) { ModKit.Fail("starting effects", e); }
		}

		private static MonsterStartingEffect New(Effect effect, float multiplier)
		{
			var e = new MonsterStartingEffect();
			e.Effect = effect; e.Duration = 0f; e.Multiplier = multiplier; e.Additive = 0f;
			return e;
		}
	}
}
