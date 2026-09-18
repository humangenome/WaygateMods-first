using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using WaygateMods.Kit;

namespace WaygateMods.WaygateTravel
{
	// Typed travel commands for co-op groups:
	//
	//   /summon            ask every member of your party to come to you
	//   /tpa <name>        ask one player if you may come to them
	//   /tpaccept /tpdeny  answer the request you were sent
	//   /home              go to your respawn point
	//   /back              go back to where you were before your last trip
	//   /where <name>      which area a player is in
	//
	// Server-side only. A move is the game's own placement call, the one that puts a player
	// in the world when they join or respawn, sent to that one player. Nothing is written to
	// a character; the world saves positions as it always does.
	[BepInPlugin(PluginId, "Waygate Travel", Version)]
	public class WaygateTravelPlugin : BasePlugin
	{
		public const string PluginId = "com.humangenome.waygatemods.waygatetravel";
		public const string ModId = "HumanGenome-WaygateTravel";
		public const string Version = "1.0.0";

		internal static ConfigEntry<bool> SummonOn, TpaOn, HomeOn, BackOn, WhereOn;
		internal static ConfigEntry<int> SummonCooldown, TpaCooldown, HomeCooldown, BackCooldown, RequestTimeout;
		internal static ConfigEntry<bool> RefuseInCombat, RefuseLockedScene, GamePrompt;
		internal static ConfigEntry<string> AdminNames;

		public override void Load()
		{
			SummonOn = Config.Bind("Commands", "Summon", true, "/summon: ask your party to come to you.");
			TpaOn = Config.Bind("Commands", "Tpa", true, "/tpa <name>: ask one player if you may come to them.");
			HomeOn = Config.Bind("Commands", "Home", true, "/home: go to your respawn point.");
			BackOn = Config.Bind("Commands", "Back", true, "/back: return to where you were before your last trip.");
			WhereOn = Config.Bind("Commands", "Where", true, "/where <name>: which area a player is in.");
			SummonCooldown = Config.Bind("Cooldowns", "SummonSeconds", 300, "Seconds before the same player can /summon again. 0 to 3600.");
			TpaCooldown = Config.Bind("Cooldowns", "TpaSeconds", 120, "Seconds before the same player can /tpa again. 0 to 3600.");
			HomeCooldown = Config.Bind("Cooldowns", "HomeSeconds", 300, "Seconds before the same player can /home again. 0 to 3600.");
			BackCooldown = Config.Bind("Cooldowns", "BackSeconds", 60, "Seconds before the same player can /back again. 0 to 3600.");
			RequestTimeout = Config.Bind("Rules", "RequestTimeoutSeconds", 60, "How long a /summon or /tpa request waits for an answer. 15 to 300.");
			RefuseInCombat = Config.Bind("Rules", "RefuseInCombat", true, "No travel out of a fight or into one.");
			RefuseLockedScene = Config.Bind("Rules", "RefuseLockedAreas", true, "No travel into an area a bounty has locked.");
			GamePrompt = Config.Bind("Rules", "GamePrompt", false, "/summon also shows the game's own accept or decline banner to party members. The chat request is always sent.");
			AdminNames = Config.Bind("Rules", "NoCooldownNames", "", "Character names that skip cooldowns, separated by commas.");

			var harmony = new Harmony(PluginId);
			ModKit.Init(Log, harmony, "Waygate Travel");
			ModKit.WatchConfig(Config);
			try
			{
				ModKit.Patch(typeof(ChatSystem), "SendMessageToServerServerRpc", null, typeof(Travel), nameof(Travel.Chat), null, true, "reading chat commands");
				ModKit.Patch(typeof(TransitionManager), "Update", null, typeof(Travel), null, nameof(Travel.Tick), true, "the server clock");
				if (!ModKit.Off) ModKit.Say("Waygate Travel " + Version + " is on. Players can type " + Travel.CommandList() + ".");
			}
			catch (Exception e) { ModKit.Dbg("load: " + e); ModKit.TurnOff("it could not start"); }
		}
	}

	internal static class Travel
	{
		private enum Kind { Tpa, Summon }
		private sealed class Request { internal ulong From; internal string FromName = ""; internal Kind Kind; internal float Expires; }
		private struct Spot { internal Areas.Scene Scene; internal Vector3 Position; }

		private static readonly Dictionary<ulong, Request> sPending = new Dictionary<ulong, Request>();
		private static readonly Dictionary<string, float> sCooldowns = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, Spot> sBack = new Dictionary<string, Spot>(StringComparer.OrdinalIgnoreCase);
		private static float sNextTick;

		internal static string CommandList()
		{
			var l = new List<string>();
			if (WaygateTravelPlugin.SummonOn.Value) l.Add("/summon");
			if (WaygateTravelPlugin.TpaOn.Value) l.Add("/tpa <name>");
			if (WaygateTravelPlugin.HomeOn.Value) l.Add("/home");
			if (WaygateTravelPlugin.BackOn.Value) l.Add("/back");
			if (WaygateTravelPlugin.WhereOn.Value) l.Add("/where <name>");
			return l.Count == 0 ? "nothing (every command is switched off)" : string.Join(", ", l);
		}

		internal static void Tick()
		{
			if (ModKit.Off) return;
			try
			{
				ModKit.Pump();
				float now = Time.realtimeSinceStartup;
				if (now < sNextTick) return;
				sNextTick = now + 2f;
				if (sPending.Count == 0) return;
				var gone = new List<ulong>();
				foreach (var kv in sPending) if (now > kv.Value.Expires) gone.Add(kv.Key);
				foreach (var id in gone)
				{
					var r = sPending[id]; sPending.Remove(id);
					Reply(r.From, "Your request got no answer.");
				}
			}
			catch (Exception e) { ModKit.Fail("tick", e); }
		}

		// A typed line this mod answers is not relayed to the other players.
		[HarmonyPriority(Priority.First)]
		internal static bool Chat(Message message)
		{
			if (ModKit.Off) return true;
			try
			{
				if (!ModKit.OnServer()) return true;
				string verb, rest; ulong source;
				if (!ModKit.IsCommand(message, out verb, out rest, out source)) return true;
				switch (verb)
				{
					case "summon": if (!WaygateTravelPlugin.SummonOn.Value) return true; Summon(source); return false;
					case "tpa": if (!WaygateTravelPlugin.TpaOn.Value) return true; Tpa(source, rest); return false;
					case "tpaccept": case "tpyes": Answer(source, true); return false;
					case "tpdeny": case "tpno": Answer(source, false); return false;
					case "home": if (!WaygateTravelPlugin.HomeOn.Value) return true; Home(source); return false;
					case "back": if (!WaygateTravelPlugin.BackOn.Value) return true; Back(source); return false;
					case "where": if (!WaygateTravelPlugin.WhereOn.Value) return true; Where(source, rest); return false;
					case "travel": Reply(source, "Travel commands: " + CommandList() + ". Answer a request with /tpaccept or /tpdeny."); return false;
					default: return true;
				}
			}
			catch (Exception e) { ModKit.Fail("chat", e); return true; }
		}

		private static void Reply(ulong clientId, string text) { ModKit.Whisper(clientId, text, false); }

		// ---- rules -------------------------------------------------------------------------
		private static bool IsAdmin(string name)
		{
			var list = WaygateTravelPlugin.AdminNames.Value ?? "";
			foreach (var part in list.Split(',')) if (part.Trim().Length > 0 && string.Equals(part.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
			return false;
		}

		private static bool OnCooldown(Player p, string verb, int seconds, out int left)
		{
			left = 0;
			seconds = Mathf.Clamp(seconds, 0, 3600);
			if (seconds == 0 || IsAdmin(ModKit.NameOf(p))) return false;
			float until, now = Time.realtimeSinceStartup;
			if (sCooldowns.TryGetValue(ModKit.HashOf(p) + "|" + verb, out until) && now < until) { left = Mathf.CeilToInt(until - now); return true; }
			return false;
		}

		private static void StartCooldown(Player p, string verb, int seconds)
		{
			seconds = Mathf.Clamp(seconds, 0, 3600);
			if (seconds > 0) sCooldowns[ModKit.HashOf(p) + "|" + verb] = Time.realtimeSinceStartup + seconds;
		}

		private static bool Dead(Player p) { try { return p.Dead.Value || p.IsDead(); } catch { return false; } }
		private static bool Busy(Player p) { try { return p.Teleporting.Value; } catch { return false; } }

		// The game raises this flag on a player while monsters are fighting them (it drives the
		// battle music). 1 = in combat.
		private static bool InCombat(Player p)
		{
			if (!WaygateTravelPlugin.RefuseInCombat.Value) return false;
			try { float v = p.BattleMusicActive.Value; return v > 0.5f && v < 1.5f; } catch { return false; }
		}

		private static bool Locked(Areas.Scene scene)
		{
			if (!WaygateTravelPlugin.RefuseLockedScene.Value) return false;
			try
			{
				var dm = DeedManager.Singleton;
				if (dm == null) return false;
				DeedDefinition deed; Quest quest;
				return dm.IsSceneLockedByActiveDeed(scene, out deed, out quest);
			}
			catch (Exception e) { ModKit.Dbg("locked area check: " + e.Message); return false; }
		}

		private static string Words(Areas.Scene scene)
		{
			var s = scene.ToString(); var sb = new StringBuilder();
			for (int i = 0; i < s.Length; i++)
			{
				if (i > 0 && char.IsUpper(s[i]) && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1])))) sb.Append(' ');
				sb.Append(s[i]);
			}
			return sb.ToString();
		}

		// Why this player cannot leave right now, or null.
		private static string CannotLeave(Player p)
		{
			if (Dead(p)) return "You cannot travel while dead.";
			if (Busy(p)) return "You are already travelling.";
			if (InCombat(p)) return "You cannot travel out of a fight.";
			return null;
		}

		// ---- the move ----------------------------------------------------------------------
		private static bool Move(Player mover, Areas.Scene scene, Vector3 position, string whereText)
		{
			var sm = SpawnManager.Singleton;
			if (sm == null) { Reply(mover.OwnerClientId, "Travel is not ready yet. Try again in a moment."); return false; }
			var hash = ModKit.HashOf(mover);
			try { if (hash.Length > 0) sBack[hash] = new Spot { Scene = mover.Scene.Value, Position = mover.transform.position }; } catch { }
			sm.LoadPlayerLocationClientRpc(scene, position, mover.OwnerClientId, false, false);
			ModKit.Say(ModKit.NameOf(mover) + " travelled to " + whereText + ".");
			return true;
		}

		private static bool MoveTo(Player mover, Player target)
		{
			Areas.Scene scene; Vector3 pos;
			try { scene = target.Scene.Value; pos = target.transform.position; } catch { return false; }
			if (scene == Areas.Scene.None || scene == Areas.Scene.MainMenu) { Reply(mover.OwnerClientId, ModKit.NameOf(target) + " is still loading in."); return false; }
			var ring = UnityEngine.Random.insideUnitCircle * 1.5f;
			return Move(mover, scene, new Vector3(pos.x + ring.x, pos.y + ring.y, pos.z), ModKit.NameOf(target) + " in " + Words(scene));
		}

		// ---- commands ----------------------------------------------------------------------
		private static void Summon(ulong source)
		{
			var me = ModKit.PlayerOf(source);
			if (me == null) return;
			int left;
			if (OnCooldown(me, "summon", WaygateTravelPlugin.SummonCooldown.Value, out left)) { Reply(source, "You can summon again in " + left + " seconds."); return; }
			if (Dead(me)) { Reply(source, "You cannot summon while dead."); return; }
			if (InCombat(me)) { Reply(source, "You cannot summon your party into a fight."); return; }

			var members = new List<Player>();
			try
			{
				var party = me.Party;
				var ids = party != null ? party.PartyMemberIDs : null;
				if (ids != null)
					for (int i = 0; i < ids.Count; i++)
					{
						ulong id = ids[i];
						if (id == source) continue;
						var p = ModKit.PlayerOf(id);
						if (p != null) members.Add(p);
					}
			}
			catch (Exception e) { ModKit.Dbg("party read: " + e.Message); }
			if (members.Count == 0) { Reply(source, "Nobody else is in your party. Use /tpa <name> to ask one player."); return; }

			string myName = ModKit.NameOf(me);
			float expires = Time.realtimeSinceStartup + Mathf.Clamp(WaygateTravelPlugin.RequestTimeout.Value, 15, 300);
			foreach (var p in members)
			{
				sPending[p.OwnerClientId] = new Request { From = source, FromName = myName, Kind = Kind.Summon, Expires = expires };
				Reply(p.OwnerClientId, myName + " is calling the party to them. Type /tpaccept to go or /tpdeny to stay.");
			}
			if (WaygateTravelPlugin.GamePrompt.Value)
			{
				try { var pos = me.transform.position; me.Party.ServerSendTeleportInvites(me, Party.TeleportAnchor.Requester, new Vector2(pos.x, pos.y), 3f, 0f); }
				catch (Exception e) { ModKit.Dbg("game prompt: " + e.Message); }
			}
			StartCooldown(me, "summon", WaygateTravelPlugin.SummonCooldown.Value);
			Reply(source, "Summon sent to " + members.Count + (members.Count == 1 ? " party member." : " party members."));
			ModKit.Say(myName + " summoned their party (" + members.Count + ").");
		}

		private static void Tpa(ulong source, string who)
		{
			var me = ModKit.PlayerOf(source);
			if (me == null) return;
			if (who.Length == 0) { Reply(source, "Type /tpa and a player's name."); return; }
			var target = ModKit.FindByName(who);
			if (target == null) { Reply(source, "No player here is called '" + who + "'."); return; }
			if (target.OwnerClientId == source) { Reply(source, "You are already there."); return; }
			int left;
			if (OnCooldown(me, "tpa", WaygateTravelPlugin.TpaCooldown.Value, out left)) { Reply(source, "You can ask again in " + left + " seconds."); return; }
			var why = CannotLeave(me);
			if (why != null) { Reply(source, why); return; }
			Request open;
			if (sPending.TryGetValue(target.OwnerClientId, out open) && Time.realtimeSinceStartup < open.Expires) { Reply(source, ModKit.NameOf(target) + " is already answering a request. Try again shortly."); return; }

			string myName = ModKit.NameOf(me);
			sPending[target.OwnerClientId] = new Request { From = source, FromName = myName, Kind = Kind.Tpa, Expires = Time.realtimeSinceStartup + Mathf.Clamp(WaygateTravelPlugin.RequestTimeout.Value, 15, 300) };
			StartCooldown(me, "tpa", WaygateTravelPlugin.TpaCooldown.Value);
			Reply(target.OwnerClientId, myName + " asks to travel to you. Type /tpaccept or /tpdeny.");
			Reply(source, "Asked " + ModKit.NameOf(target) + ". Waiting for their answer.");
		}

		private static void Answer(ulong source, bool yes)
		{
			Request r;
			if (!sPending.TryGetValue(source, out r) || Time.realtimeSinceStartup > r.Expires) { sPending.Remove(source); Reply(source, "You have no travel request to answer."); return; }
			sPending.Remove(source);
			var me = ModKit.PlayerOf(source); var other = ModKit.PlayerOf(r.From);
			if (me == null) return;
			if (other == null) { Reply(source, r.FromName + " has left."); return; }
			if (!yes) { Reply(r.From, ModKit.NameOf(me) + " said no."); Reply(source, "Declined."); return; }

			Player mover = r.Kind == Kind.Tpa ? other : me, dest = r.Kind == Kind.Tpa ? me : other;
			var why = CannotLeave(mover);
			if (why != null) { Reply(mover.OwnerClientId, why); if (mover != me) Reply(source, ModKit.NameOf(mover) + " cannot travel right now."); else Reply(r.From, ModKit.NameOf(me) + " cannot travel right now."); return; }
			if (Dead(dest)) { Reply(mover.OwnerClientId, ModKit.NameOf(dest) + " is dead. Try again when they are back."); return; }
			if (InCombat(dest)) { Reply(mover.OwnerClientId, ModKit.NameOf(dest) + " is in a fight. Try again when it is over."); return; }
			Areas.Scene scene = Areas.Scene.None; try { scene = dest.Scene.Value; } catch { }
			Areas.Scene from = Areas.Scene.None; try { from = mover.Scene.Value; } catch { }
			if (scene != from && Locked(scene)) { Reply(mover.OwnerClientId, Words(scene) + " is locked by a bounty in progress."); return; }
			if (MoveTo(mover, dest)) { if (mover != me) Reply(source, ModKit.NameOf(mover) + " is on the way."); else Reply(r.From, ModKit.NameOf(me) + " is on the way."); }
		}

		private static void Home(ulong source)
		{
			var me = ModKit.PlayerOf(source);
			if (me == null) return;
			int left;
			if (OnCooldown(me, "home", WaygateTravelPlugin.HomeCooldown.Value, out left)) { Reply(source, "You can go home again in " + left + " seconds."); return; }
			var why = CannotLeave(me);
			if (why != null) { Reply(source, why); return; }
			Areas.Scene scene; Vector3 pos;
			if (!SpawnPoint(me, out scene, out pos)) { Reply(source, "The server has no respawn point on record for you yet."); return; }
			if (Move(me, scene, pos, "their respawn point in " + Words(scene))) StartCooldown(me, "home", WaygateTravelPlugin.HomeCooldown.Value);
		}

		private static void Back(ulong source)
		{
			var me = ModKit.PlayerOf(source);
			if (me == null) return;
			int left;
			if (OnCooldown(me, "back", WaygateTravelPlugin.BackCooldown.Value, out left)) { Reply(source, "You can go back again in " + left + " seconds."); return; }
			var why = CannotLeave(me);
			if (why != null) { Reply(source, why); return; }
			Spot spot;
			if (!sBack.TryGetValue(ModKit.HashOf(me), out spot)) { Reply(source, "There is nowhere to go back to yet."); return; }
			Areas.Scene from = Areas.Scene.None; try { from = me.Scene.Value; } catch { }
			if (spot.Scene != from && Locked(spot.Scene)) { Reply(source, Words(spot.Scene) + " is locked by a bounty in progress."); return; }
			if (Move(me, spot.Scene, spot.Position, "where they were in " + Words(spot.Scene))) StartCooldown(me, "back", WaygateTravelPlugin.BackCooldown.Value);
		}

		private static void Where(ulong source, string who)
		{
			if (who.Length == 0) { Reply(source, "Type /where and a player's name."); return; }
			var target = ModKit.FindByName(who);
			if (target == null) { Reply(source, "No player here is called '" + who + "'."); return; }
			Areas.Scene scene = Areas.Scene.None; try { scene = target.Scene.Value; } catch { }
			Reply(source, ModKit.NameOf(target) + " is in " + Words(scene) + (Dead(target) ? " (dead)." : "."));
		}

		// The respawn point the world keeps for this character.
		private static bool SpawnPoint(Player p, out Areas.Scene scene, out Vector3 pos)
		{
			scene = Areas.Scene.None; pos = Vector3.zero;
			try
			{
				string hash = ModKit.HashOf(p);
				var spd = ServerPersistentData.Singleton;
				if (hash.Length == 0 || spd == null || spd.PlayerData == null) return false;
				for (int i = 0; i < spd.PlayerData.Count; i++)
				{
					var d = spd.PlayerData[i];
					if (d == null) continue;
					string h = ""; try { h = d.Hash.ToString(); } catch { }
					if (!string.Equals(h, hash, StringComparison.OrdinalIgnoreCase)) continue;
					scene = d.SpawnArea;
					Vector3 v = d.SpawnLocation; pos = v;
					return scene != Areas.Scene.None && scene != Areas.Scene.MainMenu;
				}
			}
			catch (Exception e) { ModKit.Dbg("respawn point: " + e.Message); }
			return false;
		}
	}
}
