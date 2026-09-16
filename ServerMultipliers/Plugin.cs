using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Unity.Netcode;

namespace WaygateMods.ServerMultipliers
{
	// Server-side multipliers for a Dimraeth server. Everything here is computed on the
	// server and replicated, so players install nothing.
	//
	//   XP, Gold, LootChance, MonsterRespawnTime  scale the game's own difficulty dials at
	//   the point where the server reads them (DifficultyManager.Get*Multiplier). A monster
	//   that gives 12 XP gives 36 with XP = 3.
	//   DayLength                                 scales GameConfig.TimeSpeed, the seconds the
	//   world clock waits between minutes. 2 makes a day last twice as long.
	//
	// On a client the patches are inert: every one checks that this process is the server.
	[BepInPlugin(PluginId, "Server Multipliers", Version)]
	public class ServerMultipliersPlugin : BasePlugin
	{
		public const string PluginId = "com.humangenome.waygatemods.servermultipliers";
		public const string Version = "1.0.0";

		internal static ManualLogSource Logger;
		internal static ConfigEntry<float> Xp;
		internal static ConfigEntry<float> Gold;
		internal static ConfigEntry<float> LootChance;
		internal static ConfigEntry<float> MonsterRespawnTime;
		internal static ConfigEntry<float> DayLength;
		internal static ConfigEntry<bool> LogApplied;

		public override void Load()
		{
			Logger = Log;
			Xp = Config.Bind("Multipliers", "XP", 1f, "XP from monster kills. 1 = the game's own value. Range 0.1 to 20.");
			Gold = Config.Bind("Multipliers", "Gold", 1f, "Gold dropped by monsters. 1 = the game's own value. Range 0.1 to 20.");
			LootChance = Config.Bind("Multipliers", "LootChance", 1f, "Chance of item, rune and gold drops. 1 = the game's own value. Range 0.1 to 20.");
			MonsterRespawnTime = Config.Bind("Multipliers", "MonsterRespawnTime", 1f, "Time before a killed monster returns. 0.5 = twice as fast. Range 0.1 to 20.");
			DayLength = Config.Bind("Multipliers", "DayLength", 1f, "Length of a game day. 2 = days last twice as long, 0.5 = half. Range 0.1 to 20.");
			LogApplied = Config.Bind("Logging", "LogApplied", true, "Log a line each time a multiplier changes a value (XP and gold on every kill).");

			try
			{
				new Harmony(PluginId).PatchAll(typeof(ServerMultipliersPlugin).Assembly);
				Logger.LogInfo("[multipliers] " + Version + " xp=" + Clamp(Xp.Value) + " gold=" + Clamp(Gold.Value) + " loot=" + Clamp(LootChance.Value)
					+ " respawn=" + Clamp(MonsterRespawnTime.Value) + " dayLength=" + Clamp(DayLength.Value));
			}
			catch (Exception e) { Logger.LogError("[multipliers] patching failed: " + e); }
		}

		internal static float Clamp(float v)
		{
			if (float.IsNaN(v) || v <= 0f) return 1f;
			return v < 0.1f ? 0.1f : (v > 20f ? 20f : v);
		}

		internal static bool OnServer()
		{
			try { var nm = NetworkManager.Singleton; return nm != null && nm.IsServer; } catch { return false; }
		}

		internal static string MonsterName(MonsterConfiguration config)
		{
			try { return config != null && !string.IsNullOrEmpty(config.MonsterName) ? config.MonsterName : "monster"; } catch { return "monster"; }
		}
	}

	[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.GetXPMultiplier))]
	internal static class XpPatch
	{
		private static void Postfix(MonsterConfiguration config, ref float __result)
		{
			float k = ServerMultipliersPlugin.Clamp(ServerMultipliersPlugin.Xp.Value);
			if (k == 1f || !ServerMultipliersPlugin.OnServer()) return;
			float was = __result;
			__result = was * k;
			if (ServerMultipliersPlugin.LogApplied.Value) ServerMultipliersPlugin.Logger.LogInfo("[multipliers] xp x" + k + " for " + ServerMultipliersPlugin.MonsterName(config) + ": " + was + " -> " + __result);
		}
	}

	[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.GetGoldMultiplier))]
	internal static class GoldPatch
	{
		private static void Postfix(MonsterConfiguration config, ref float __result)
		{
			float k = ServerMultipliersPlugin.Clamp(ServerMultipliersPlugin.Gold.Value);
			if (k == 1f || !ServerMultipliersPlugin.OnServer()) return;
			float was = __result;
			__result = was * k;
			if (ServerMultipliersPlugin.LogApplied.Value) ServerMultipliersPlugin.Logger.LogInfo("[multipliers] gold x" + k + " for " + ServerMultipliersPlugin.MonsterName(config) + ": " + was + " -> " + __result);
		}
	}

	[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.GetLootChanceMultiplier))]
	internal static class LootPatch
	{
		private static void Postfix(ref float __result)
		{
			float k = ServerMultipliersPlugin.Clamp(ServerMultipliersPlugin.LootChance.Value);
			if (k == 1f || !ServerMultipliersPlugin.OnServer()) return;
			__result *= k;
		}
	}

	[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.GetRespawnTimeMultiplier))]
	internal static class RespawnPatch
	{
		private static void Postfix(ref float __result)
		{
			float k = ServerMultipliersPlugin.Clamp(ServerMultipliersPlugin.MonsterRespawnTime.Value);
			if (k == 1f || !ServerMultipliersPlugin.OnServer()) return;
			__result *= k;
		}
	}

	// The world clock. TimeManager's update loop waits GameConfig.TimeSpeed * 3 seconds
	// between game minutes and reads the static on every pass, so scaling it once when the
	// server's TimeManager spawns is enough. Clients read the replicated clock.
	[HarmonyPatch(typeof(TimeManager), nameof(TimeManager.OnNetworkSpawn))]
	internal static class DayLengthPatch
	{
		private static float sBase = -1f;
		private static void Postfix(TimeManager __instance)
		{
			try
			{
				if (__instance == null || !__instance.IsServer) return;
				float k = ServerMultipliersPlugin.Clamp(ServerMultipliersPlugin.DayLength.Value);
				if (sBase < 0f) sBase = GameConfig.TimeSpeed;
				GameConfig.TimeSpeed = sBase * k;
				ServerMultipliersPlugin.Logger.LogInfo("[multipliers] day length x" + k + ": TimeSpeed " + sBase + " -> " + GameConfig.TimeSpeed + " (" + (sBase * k * 3f).ToString("F2") + " s per game minute)");
			}
			catch (Exception e) { ServerMultipliersPlugin.Logger.LogWarning("[multipliers] day length: " + e.Message); }
		}
	}
}
