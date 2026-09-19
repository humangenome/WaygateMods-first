using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Unity.Netcode;
using UnityEngine;
using WaygateMods.Kit;

namespace WaygateMods.BountyBoardPlus
{
	// The game's bounty boards, tuned for a server that runs all day: new postings on a real
	// time clock, bounty cooldowns off or shortened, the tier ceiling raised, more postings per
	// board, chat lines for new postings, tier unlocks, finished bounties and boss kills, and
	// /bounties to read the board from anywhere.
	//
	// Server-side only. The board a player opens is sent by the server, and so are the tiers
	// and the cooldown list, so a player needs nothing installed. The world save is only ever
	// changed by the game's own bounty records; the mod changes what the server answers, per
	// call, so with the mod removed the game answers with its own values again.
	[BepInPlugin(PluginId, "Bounty Board Plus", Version)]
	public class BountyBoardPlusPlugin : BasePlugin
	{
		public const string PluginId = "com.humangenome.waygatemods.bountyboardplus";
		public const string ModId = "HumanGenome-BountyBoardPlus";
		public const string Version = "1.0.0";

		internal static ConfigEntry<int> RefreshEveryMinutes, ExtraSlots, CapBonus;
		internal static ConfigEntry<float> CooldownFactor;
		internal static ConfigEntry<bool> AnnounceRefresh, AnnounceTierUnlocks, AnnounceBounties, AnnounceBossKills, AnnounceMiniBosses;
		internal static ConfigEntry<bool> Commands, PrivateReplies;
		internal static ConfigEntry<string> MsgRefresh, MsgTierUnlock, MsgBountyDone, MsgBossKill;

		public override void Load()
		{
			RefreshEveryMinutes = Config.Bind("Board", "RefreshEveryMinutes", 15, "New postings on every board every this many real minutes, on top of the game's own refresh (once per game day and after each finished bounty). 0 = the game's own refresh only. 5 to 1440.");
			ExtraSlots = Config.Bind("Board", "ExtraSlots", 2, "Extra postings on each board. 0 to 7; a board never holds more than 10.");
			CooldownFactor = Config.Bind("Cooldowns", "Factor", 0f, "How long a finished bounty stays off the board for the player who did it. 0 = no cooldown, 0.5 = half the game's days (rounded up), 1 = the game's own cooldown.");
			CapBonus = Config.Bind("Tiers", "CapBonus", 2, "Added to the world's tier ceiling. A bounty still opens its tiers one by one, and never goes past its own last tier. 0 to 9.");

			AnnounceRefresh = Config.Bind("Announcements", "Refresh", true, "A chat line when this mod posts new bounties.");
			AnnounceTierUnlocks = Config.Bind("Announcements", "TierUnlocks", true, "A chat line when a bounty's next tier opens for the world.");
			AnnounceBounties = Config.Bind("Announcements", "Bounties", true, "A chat line when a player finishes a bounty.");
			AnnounceBossKills = Config.Bind("Announcements", "BossKills", true, "A chat line when a boss dies.");
			AnnounceMiniBosses = Config.Bind("Announcements", "MiniBosses", false, "Count mini-bosses as bosses for that line.");

			Commands = Config.Bind("Chat", "Commands", true, "Players can type /bounties in chat.");
			PrivateReplies = Config.Bind("Chat", "PrivateReplies", true, "true: only the player who asked sees the answer. false: everyone sees it.");

			MsgRefresh = Config.Bind("Messages", "Refresh", "New bounties are posted on the {board} board.", "Chat line for new postings. {board} = the board's name. Empty = none.");
			MsgTierUnlock = Config.Bind("Messages", "TierUnlock", "{bounty}: tier {tier} is now open.", "Chat line for a tier unlock. {bounty}, {tier}. Empty = none.");
			MsgBountyDone = Config.Bind("Messages", "BountyDone", "{player} finished the bounty {bounty}{tier}.", "Chat line for a finished bounty. {player}, {bounty}, {tier} (reads ' (tier 3)' or nothing). Empty = none.");
			MsgBossKill = Config.Bind("Messages", "BossKill", "{player} felled {boss}{tier}.", "Chat line for a boss kill. {player}, {boss}, {tier} (reads ' (tier 3 bounty)' or nothing). Empty = none.");

			var harmony = new Harmony(PluginId);
			ModKit.Init(Log, harmony, "Bounty Board Plus");
			ModKit.WatchConfig(Config);
			try
			{
				ModKit.Patch(typeof(TransitionManager), "Update", null, typeof(Hooks), null, nameof(Hooks.Tick), true, "the server clock");
				ModKit.Patch(typeof(DeedManager), "IsDeedOnCooldownFor", new[] { typeof(DeedBoardType), typeof(DeedDefinition), typeof(long), typeof(string) }, typeof(Hooks), nameof(Hooks.CooldownBefore), nameof(Hooks.CooldownAfter), false, "bounty cooldowns");
				ModKit.Patch(typeof(DeedManager), "SendCooldownDataClientRpc", null, typeof(Hooks), nameof(Hooks.CooldownList), null, false, "the cooldown list on the board");
				ModKit.Patch(typeof(DeedManager), "GetWorldTierCap", null, typeof(Hooks), null, nameof(Hooks.TierCap), false, "the tier ceiling");
				ModKit.Patch(typeof(DeedManager), "UnlockNextWorldDeedTier", null, typeof(Hooks), null, nameof(Hooks.TierUnlocked), false, "tier unlock lines");
				ModKit.Patch(typeof(DeedManager), "RecordDeedCompletionForQuest", null, typeof(Hooks), nameof(Hooks.BountyBefore), nameof(Hooks.BountyAfter), false, "finished bounty lines");
				ModKit.Patch(typeof(QuestManager), "RegisterMonsterDeath", new[] { typeof(Player), typeof(Monster), typeof(InGameEvent) }, typeof(Hooks), null, nameof(Hooks.MonsterDeath), false, "boss kill lines");
				ModKit.Patch(typeof(ChatSystem), "SendMessageToServerServerRpc", null, typeof(Hooks), nameof(Hooks.Chat), null, false, "the /bounties chat command");
				if (!ModKit.Off) ModKit.Say("Bounty Board Plus " + Version + " is on: " + Hooks.SettingsLine() + (Commands.Value ? " Players can type /bounties." : ""));
			}
			catch (Exception e) { ModKit.Dbg("load: " + e); ModKit.TurnOff("it could not start"); }
		}

		internal static float Factor { get { float f = CooldownFactor.Value; if (float.IsNaN(f)) return 1f; return Mathf.Clamp(f, 0f, 1f); } }
		internal static int Bonus { get { return Mathf.Clamp(CapBonus.Value, 0, 9); } }
		internal static int Slots { get { return Mathf.Clamp(ExtraSlots.Value, 0, 7); } }
		internal static int EveryMinutes { get { int m = RefreshEveryMinutes.Value; return m <= 0 ? 0 : Mathf.Clamp(m, 5, 1440); } }
	}

	internal static class Hooks
	{
		private const int BoardMaxSlots = 10, TierMax = 10;

		private static float sNextTick;
		private static bool sReady;
		private static long sClockSlot = -1;
		private static int sAppliedSlots = -1, sAppliedBonus = -1, sAppliedEvery = -1;
		private static float sAppliedFactor = -1f;
		private static int sGameCap = -1;
		private static readonly Dictionary<int, int> sGameSlots = new Dictionary<int, int>();
		private static readonly Dictionary<ulong, float> sSeenMonsters = new Dictionary<ulong, float>();

		internal static string SettingsLine()
		{
			int every = BountyBoardPlusPlugin.EveryMinutes; float f = BountyBoardPlusPlugin.Factor;
			return (every > 0 ? "new postings every " + every + " minutes" : "the game's own refresh")
				+ ", " + (f <= 0f ? "no cooldowns" : f >= 1f ? "the game's cooldowns" : "cooldowns at " + Pct(f))
				+ ", tier ceiling +" + BountyBoardPlusPlugin.Bonus
				+ ", " + BountyBoardPlusPlugin.Slots + " extra postings per board.";
		}

		private static string Pct(float f) { return ((int)Math.Round(f * 100f)).ToString(CultureInfo.InvariantCulture) + "%"; }

		// ---- the tick --------------------------------------------------------------------------
		internal static void Tick()
		{
			if (ModKit.Off) return;
			try
			{
				ModKit.Pump();
				float now = Time.realtimeSinceStartup;
				if (now < sNextTick) return;
				sNextTick = now + 1f;
				if (!ModKit.OnServer()) { sReady = false; return; }
				var dm = DeedManager.Singleton;
				if (dm == null || !dm.IsSpawned || dm.DeedBoards == null) return;

				int every = BountyBoardPlusPlugin.EveryMinutes, slots = BountyBoardPlusPlugin.Slots, bonus = BountyBoardPlusPlugin.Bonus;
				float factor = BountyBoardPlusPlugin.Factor;

				if (!sReady)
				{
					sReady = true; sClockSlot = ClockSlot(every);
					sAppliedEvery = every; sAppliedSlots = slots; sAppliedBonus = bonus; sAppliedFactor = factor;
					Facts(dm, "at start, before this mod touched anything");
					// The game filled its boards when the world started, with its own slot count and
					// cooldowns. Post again so the settings hold from the first minute.
					Refresh(dm, false, "start");
					if (bonus > 0) SyncTiers(dm);
					return;
				}

				bool boardSettingsChanged = slots != sAppliedSlots || Math.Abs(factor - sAppliedFactor) > 0.0001f;
				if (boardSettingsChanged)
				{
					sAppliedSlots = slots; sAppliedFactor = factor;
					Refresh(dm, false, "new settings");
				}
				if (bonus != sAppliedBonus) { sAppliedBonus = bonus; SyncTiers(dm); }
				if (every != sAppliedEvery) { sAppliedEvery = every; sClockSlot = ClockSlot(every); }

				if (every > 0)
				{
					long slot = ClockSlot(every);
					if (slot != sClockSlot) { sClockSlot = slot; Refresh(dm, true, "clock"); }
				}
			}
			catch (Exception e) { ModKit.Fail("tick", e); }
		}

		// The clock is cut from real time (UTC) in blocks of N minutes, so a restart does not move
		// the next posting time and nothing has to be remembered.
		private static long ClockSlot(int everyMinutes)
		{
			if (everyMinutes <= 0) return -1;
			long minutes = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
			return minutes / everyMinutes;
		}

		private static int MinutesToNextPosting(int everyMinutes)
		{
			if (everyMinutes <= 0) return -1;
			long seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			long block = everyMinutes * 60L;
			long left = block - (seconds % block);
			return (int)Math.Max(1, (left + 59) / 60);
		}

		private static void SyncTiers(DeedManager dm)
		{
			try { dm.RefreshTierCapIfChanged(); ModKit.Dbg("tier ceiling: the game says " + sGameCap + ", players are sent " + dm.GetWorldTierCap()); }
			catch (Exception e) { ModKit.Fail("tier sync", e); }
		}

		private static void Refresh(DeedManager dm, bool announce, string why)
		{
			var boards = dm.DeedBoards;
			int done = 0; string firstName = "";
			for (int i = 0; i < boards.Count; i++)
			{
				var def = boards[i];
				if (def == null || !def.InProduction) continue;
				ApplySlots(def);
				dm.RefreshBoard(def);
				done++;
				if (firstName.Length == 0) firstName = BoardName(def);
				if (announce && BountyBoardPlusPlugin.AnnounceRefresh.Value && ModKit.Players().Count > 0)
				{
					var line = Fill(BountyBoardPlusPlugin.MsgRefresh.Value, "{board}", BoardName(def));
					if (line.Length > 0) ModKit.Broadcast(line);
				}
			}
			Facts(dm, "after posting (" + why + ")");
			if (announce && done > 0) ModKit.Say("New bounties posted on " + (done == 1 ? "the " + firstName + " board" : done + " boards") + ".");
		}

		private static void ApplySlots(DeedBoardDefinition def)
		{
			int key = (int)def.BoardType;
			int game;
			if (!sGameSlots.TryGetValue(key, out game)) { game = def.AvailableSlots; sGameSlots[key] = game; }
			int want = Mathf.Clamp(game + BountyBoardPlusPlugin.Slots, 1, Math.Max(game, BoardMaxSlots));
			if (def.AvailableSlots != want) { ModKit.Dbg("board " + BoardName(def) + ": postings " + def.AvailableSlots + " -> " + want + " (the game's own: " + game + ")"); def.AvailableSlots = want; }
		}

		private static string BoardName(DeedBoardDefinition def)
		{
			try { var n = def.BoardName; if (!string.IsNullOrWhiteSpace(n)) return n.Trim(); } catch { }
			try { return Words(def.BoardType.ToString()); } catch { }
			return "bounty";
		}

		// Debug lines for whoever reads the log with Debug enabled: what the server holds right now.
		private static void Facts(DeedManager dm, string when)
		{
			try
			{
				var boards = dm.DeedBoards;
				for (int i = 0; i < boards.Count; i++)
				{
					var def = boards[i];
					if (def == null) continue;
					var sb = new StringBuilder();
					sb.Append("facts ").Append(when).Append(": board '").Append(BoardName(def)).Append("' type=").Append(def.BoardType.ToString())
						.Append(" production=").Append(def.InProduction).Append(" daily=").Append(def.RefreshDaily).Append(" hour=").Append(def.RefreshHour)
						.Append(" slots=").Append(def.AvailableSlots).Append(" boardTierCap=").Append(def.BoardTierCap)
						.Append(" pool=").Append(def.AvailableDeeds != null ? def.AvailableDeeds.Count : 0);
					var active = dm.GetActiveDeedsForBoard(def.BoardType);
					sb.Append(" posted=").Append(active != null ? active.Count : 0).Append(" [");
					for (int k = 0; active != null && k < active.Count; k++)
					{
						var d = active[k]; if (d == null) continue;
						if (k > 0) sb.Append(", ");
						sb.Append(d.name).Append(':').Append(d.DeedType.ToString()).Append(":cd").Append(d.InGameDaysCooldown).Append(":tier").Append(dm.GetUnlockedTierForDeed(d)).Append('/').Append(d.IsTiered ? d.GetTierCount() : 1);
					}
					sb.Append(']');
					ModKit.Dbg(sb.ToString());
				}
				int sent = dm.GetWorldTierCap();
				ModKit.Dbg("facts " + when + ": tier ceiling game=" + sGameCap + " sent=" + sent + " base=" + dm.BaseTierCap + " day=" + dm.GetCurrentInGameDay());
			}
			catch (Exception e) { ModKit.Dbg("facts: " + e.Message); }
		}

		// ---- cooldowns -------------------------------------------------------------------------
		// The game asks one question in three places (filling a board, answering a player who
		// opens one, finishing a quest): is this bounty cooling down for this player on this day?
		// It answers with days since the last completion < the bounty's cooldown days. A shorter
		// cooldown is the same question asked on a later day; no cooldown is the answer no.
		internal static void CooldownBefore(DeedDefinition deed, ref long currentDay)
		{
			if (ModKit.Off) return;
			try
			{
				float f = BountyBoardPlusPlugin.Factor;
				if (f <= 0f || f >= 1f || deed == null || !ModKit.OnServer()) return;
				int days = deed.InGameDaysCooldown;
				if (days <= 0) return;
				currentDay += days - Shortened(days, f);
			}
			catch (Exception e) { ModKit.Fail("cooldown", e); }
		}

		internal static void CooldownAfter(DeedDefinition deed, ref bool __result)
		{
			if (ModKit.Off) return;
			try
			{
				if (!__result || BountyBoardPlusPlugin.Factor > 0f || !ModKit.OnServer()) return;
				__result = false;
				ModKit.Dbg("cooldown waived: " + (deed != null ? deed.name : "?"));
			}
			catch (Exception e) { ModKit.Fail("cooldown", e); }
		}

		private static int Shortened(int days, float factor) { return Mathf.Clamp((int)Math.Ceiling(days * (double)factor - 0.0001), 1, days); }

		// What a player's board shows under "on cooldown": the server sends the bounty's place in
		// the board's pool and the days left. The days are rewritten to match the answer above.
		internal static void CooldownList(DeedManager __instance, DeedBoardType boardType, ref Il2CppStructArray<int> cooldownDeedIndices, ref Il2CppStructArray<int> remainingDays)
		{
			if (ModKit.Off) return;
			try
			{
				float f = BountyBoardPlusPlugin.Factor;
				if (f >= 1f || __instance == null || cooldownDeedIndices == null || remainingDays == null || !ModKit.OnServer()) return;
				// The same call arrives a second time when the server's own copy of the message is
				// handled. Only the outgoing one is rewritten.
				if ((int)__instance.__rpc_exec_stage == 1) return;
				int n = Math.Min(cooldownDeedIndices.Length, remainingDays.Length);
				if (n == 0) return;
				var keepIdx = new List<int>(); var keepDays = new List<int>();
				if (f > 0f)
				{
					var def = __instance.GetBoardDefinition(boardType);
					var pool = def != null ? def.AvailableDeeds : null;
					for (int i = 0; i < n; i++)
					{
						int idx = cooldownDeedIndices[i], left = remainingDays[i];
						var deed = pool != null && idx >= 0 && idx < pool.Count ? pool[idx] : null;
						int days = deed != null ? deed.InGameDaysCooldown : 0;
						if (days > 0) left -= days - Shortened(days, f);
						if (left <= 0) continue;
						keepIdx.Add(idx); keepDays.Add(left);
					}
				}
				var a = new Il2CppStructArray<int>(keepIdx.Count); var b = new Il2CppStructArray<int>(keepIdx.Count);
				for (int i = 0; i < keepIdx.Count; i++) { a[i] = keepIdx[i]; b[i] = keepDays[i]; }
				ModKit.Dbg("cooldown list for a player: " + n + " -> " + keepIdx.Count + " at factor " + f.ToString("0.##", CultureInfo.InvariantCulture) + (keepDays.Count > 0 ? ", days left " + string.Join(",", keepDays) : ""));
				cooldownDeedIndices = a; remainingDays = b;
			}
			catch (Exception e) { ModKit.Fail("cooldown list", e); }
		}

		// ---- tiers -----------------------------------------------------------------------------
		// The ceiling is what the world's story progress allows. Each bounty still opens its own
		// tiers one by one underneath it, and the game clamps to the bounty's last tier.
		internal static void TierCap(ref int __result)
		{
			if (ModKit.Off) return;
			try
			{
				sGameCap = __result;
				int bonus = BountyBoardPlusPlugin.Bonus;
				if (bonus <= 0 || !ModKit.OnServer()) return;
				__result = Math.Min(TierMax, __result + bonus);
			}
			catch (Exception e) { ModKit.Fail("tier ceiling", e); }
		}

		internal static void TierUnlocked(DeedDefinition deed, int completedTier, bool __result)
		{
			if (ModKit.Off) return;
			try
			{
				if (!__result || deed == null || !ModKit.OnServer()) return;
				int tier = completedTier + 1;
				try { var spd = ServerPersistentData.Singleton; if (spd != null) { int t = spd.GetWorldDeedTier(deed.name); if (t > 0) tier = t; } } catch { }
				string name = DeedName(deed);
				ModKit.Say("Tier " + tier + " of " + name + " is now open.");
				if (!BountyBoardPlusPlugin.AnnounceTierUnlocks.Value) return;
				var line = Fill(Fill(BountyBoardPlusPlugin.MsgTierUnlock.Value, "{bounty}", name), "{tier}", tier.ToString(CultureInfo.InvariantCulture));
				if (line.Length > 0) ModKit.Broadcast(line);
			}
			catch (Exception e) { ModKit.Fail("tier unlock", e); }
		}

		// ---- finished bounties -----------------------------------------------------------------
		internal sealed class Accepted { internal string Deed = "", Hash = ""; internal int Tier; internal bool Tiered; }

		// The game forgets which bounty a quest belonged to while it records the completion, so
		// it is read first.
		internal static void BountyBefore(DeedManager __instance, Quest quest, string completingPlayerHash, out Accepted __state)
		{
			__state = null;
			if (ModKit.Off) return;
			try
			{
				if (__instance == null || !ModKit.OnServer()) return;
				string deedName, playerHash; int tier; DeedBoardType board;
				if (!__instance.TryGetAcceptedDeedInfo(quest, completingPlayerHash, out deedName, out tier, out board, out playerHash)) return;
				if (string.IsNullOrEmpty(deedName)) return;
				var deed = __instance.FindDeedByName(deedName);
				__state = new Accepted { Deed = deed != null ? DeedName(deed) : Words(deedName), Tier = tier, Tiered = deed != null && deed.IsTiered, Hash = !string.IsNullOrEmpty(completingPlayerHash) ? completingPlayerHash : (playerHash ?? "") };
			}
			catch (Exception e) { ModKit.Fail("bounty", e); }
		}

		internal static void BountyAfter(Accepted __state)
		{
			if (ModKit.Off || __state == null) return;
			try
			{
				string who = "";
				foreach (var p in ModKit.Players()) if (string.Equals(ModKit.HashOf(p), __state.Hash, StringComparison.OrdinalIgnoreCase)) { who = ModKit.NameOf(p); break; }
				string tier = __state.Tiered && __state.Tier > 0 ? " (tier " + __state.Tier + ")" : "";
				ModKit.Say((who.Length > 0 ? who : "A player") + " finished the bounty " + __state.Deed + tier + ".");
				if (!BountyBoardPlusPlugin.AnnounceBounties.Value || who.Length == 0) return;
				var line = Fill(Fill(Fill(BountyBoardPlusPlugin.MsgBountyDone.Value, "{player}", who), "{bounty}", __state.Deed), "{tier}", tier);
				if (line.Length > 0) ModKit.Broadcast(line);
			}
			catch (Exception e) { ModKit.Fail("bounty", e); }
		}

		// ---- boss kills ------------------------------------------------------------------------
		internal static void MonsterDeath(Player killer, Monster monster, InGameEvent inGameEvent)
		{
			if (ModKit.Off) return;
			try
			{
				if (killer == null || monster == null || !ModKit.OnServer()) return;
				if (!BountyBoardPlusPlugin.AnnounceBossKills.Value) return;
				if (killer.OwnerClientId == NetworkManager.ServerClientId) return;
				var cfg = monster.MonsterConfiguration;
				if (cfg == null) return;
				var danger = cfg.DangerLevel;
				bool boss = danger == DangerLevel.Boss || (danger == DangerLevel.MiniBoss && BountyBoardPlusPlugin.AnnounceMiniBosses.Value);
				if (!boss) return;
				float now = Time.realtimeSinceStartup;
				ulong id = monster.NetworkObjectId;
				float seen;
				if (sSeenMonsters.TryGetValue(id, out seen) && now - seen < 10f) return;
				if (sSeenMonsters.Count > 64) sSeenMonsters.Clear();
				sSeenMonsters[id] = now;

				string name = ""; try { name = cfg.MonsterName; } catch { }
				if (string.IsNullOrWhiteSpace(name)) name = Words(cfg.MonsterType.ToString());
				string tier = "";
				try
				{
					var dm = DeedManager.Singleton;
					if (dm != null && inGameEvent != InGameEvent.None && dm.HasActiveDeedTier(inGameEvent)) { int t = dm.GetActiveDeedTier(inGameEvent); if (t > 0) tier = " (tier " + t + " bounty)"; }
				}
				catch { }
				string who = ModKit.NameOf(killer);
				if (who.Length == 0) return;
				var line = Fill(Fill(Fill(BountyBoardPlusPlugin.MsgBossKill.Value, "{player}", who), "{boss}", name.Trim()), "{tier}", tier);
				ModKit.Say(who + " felled " + name.Trim() + tier + ".");
				if (line.Length > 0) ModKit.Broadcast(line);
			}
			catch (Exception e) { ModKit.Fail("boss kill", e); }
		}

		// ---- /bounties -------------------------------------------------------------------------
		[HarmonyPriority(Priority.First)]
		internal static bool Chat(Message message)
		{
			if (ModKit.Off) return true;
			try
			{
				if (!BountyBoardPlusPlugin.Commands.Value || !ModKit.OnServer()) return true;
				string verb, rest; ulong source;
				if (!ModKit.IsCommand(message, out verb, out rest, out source)) return true;
				if (verb != "bounties" && verb != "bounty") return true;
				bool everyone = !BountyBoardPlusPlugin.PrivateReplies.Value;
				foreach (var line in BoardLines(source)) ModKit.Whisper(source, line, everyone);
				return false;
			}
			catch (Exception e) { ModKit.Fail("chat", e); return true; }
		}

		private static List<string> BoardLines(ulong source)
		{
			var lines = new List<string>();
			var dm = DeedManager.Singleton;
			if (dm == null || dm.DeedBoards == null) { lines.Add("The bounty boards are not up yet. Try again in a moment."); return lines; }
			var asker = ModKit.PlayerOf(source);
			string hash = asker != null ? ModKit.HashOf(asker) : "";
			long day = dm.GetCurrentInGameDay();
			int every = BountyBoardPlusPlugin.EveryMinutes;
			var boards = dm.DeedBoards;
			for (int i = 0; i < boards.Count; i++)
			{
				var def = boards[i];
				if (def == null || !def.InProduction) continue;
				var active = dm.GetActiveDeedsForBoard(def.BoardType);
				int count = active != null ? active.Count : 0;
				var head = BoardName(def) + " board: " + (count == 0 ? "nothing posted" : count == 1 ? "1 bounty" : count + " bounties") + "."
					+ (every > 0 ? " New postings in " + MinutesToNextPosting(every) + " min." : "");
				lines.Add(head);
				var sb = new StringBuilder();
				for (int k = 0; k < count; k++)
				{
					var d = active[k]; if (d == null) continue;
					string part = (k + 1) + ". " + DeedName(d) + " [" + d.DeedType.ToString() + (d.IsTiered ? ", tier " + Math.Max(1, dm.GetUnlockedTierForDeed(d)) + " of " + d.GetTierCount() : "") + "]";
					bool cooling = false;
					try { cooling = hash.Length > 0 && dm.IsDeedOnCooldownFor(def.BoardType, d, day, hash); } catch { }
					if (cooling) part += " (cooling down for you)";
					if (sb.Length > 0 && sb.Length + part.Length + 2 > 150) { lines.Add(sb.ToString()); sb.Clear(); }
					if (sb.Length > 0) sb.Append(", ");
					sb.Append(part);
				}
				if (sb.Length > 0) lines.Add(sb.ToString());
			}
			if (lines.Count == 0) lines.Add("This world has no bounty board yet.");
			return lines;
		}

		// ---- words -----------------------------------------------------------------------------
		private static string DeedName(DeedDefinition deed)
		{
			try { var n = deed.GetLocalizedName(); if (!string.IsNullOrWhiteSpace(n) && n.IndexOf('_') < 0) return n.Trim(); } catch { }
			try { var n = deed.DeedName; if (!string.IsNullOrWhiteSpace(n)) return n.Trim(); } catch { }
			try { return Words(deed.name); } catch { }
			return "a bounty";
		}

		// "GuildHall" -> "Guild Hall"
		private static string Words(string s)
		{
			if (string.IsNullOrEmpty(s)) return "";
			var sb = new StringBuilder(s.Length + 8);
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (c == '_') { sb.Append(' '); continue; }
				if (i > 0 && char.IsUpper(c) && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]) && char.IsUpper(s[i - 1])))) sb.Append(' ');
				sb.Append(c);
			}
			return sb.ToString().Trim();
		}

		private static string Fill(string template, string token, string value)
		{
			if (string.IsNullOrWhiteSpace(template)) return "";
			return template.Replace(token, value ?? "").Trim();
		}
	}
}
