using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace WaygateMods.MotdAnnounce
{
	// A message of the day and a repeating announcement, sent as [Server] chat lines.
	//
	// The game has no server-originated chat of its own beyond join and leave notices. It
	// does have the path those notices use: ChatSystem.CreateServerNetworkMessage builds a
	// Server-type message and relays it to every connected client. This mod calls it on a
	// connected player's ChatSystem a few seconds after they join (so their chat window
	// exists to receive it) and again on a timer for everyone.
	//
	// Server-side only. On a client nothing here runs.
	[BepInPlugin(PluginId, "Message of the Day", Version)]
	public class MotdAnnouncePlugin : BasePlugin
	{
		public const string PluginId = "com.humangenome.waygatemods.motdannounce";
		public const string Version = "1.0.0";

		internal static ManualLogSource Logger;
		internal static ConfigEntry<string> Motd;
		internal static ConfigEntry<int> JoinDelaySeconds;
		internal static ConfigEntry<string> Announcement;
		internal static ConfigEntry<int> AnnounceEveryMinutes;

		public override void Load()
		{
			Logger = Log;
			Motd = Config.Bind("Messages", "Motd", "Welcome, {player}. Have fun and play fair.", "Sent as a [Server] line when a player joins. {player} is the joining character's name. Empty = off.");
			JoinDelaySeconds = Config.Bind("Messages", "JoinDelaySeconds", 8, "Seconds after a join before the message is sent (the player's chat must be up). 3 to 60.");
			Announcement = Config.Bind("Messages", "Announcement", "", "Sent as a [Server] line to everyone on a timer. Empty = off.");
			AnnounceEveryMinutes = Config.Bind("Messages", "AnnounceEveryMinutes", 30, "Minutes between announcements. 1 to 1440.");
			try
			{
				new Harmony(PluginId).PatchAll(typeof(MotdAnnouncePlugin).Assembly);
				Logger.LogInfo("[motd] " + Version + " motd=" + (string.IsNullOrWhiteSpace(Motd.Value) ? "off" : "on") + " announcement=" + (string.IsNullOrWhiteSpace(Announcement.Value) ? "off" : "every " + AnnounceEveryMinutes.Value + " min"));
			}
			catch (Exception e) { Logger.LogError("[motd] patching failed: " + e); }
		}
	}

	internal static class Sender
	{
		internal static readonly Dictionary<ulong, float> Pending = new Dictionary<ulong, float>();
		internal static readonly HashSet<ulong> Connected = new HashSet<ulong>();
		private static float sNextAnnounce = -1f;

		private static bool OnServer()
		{
			try { var nm = NetworkManager.Singleton; return nm != null && nm.IsServer && nm.IsListening; } catch { return false; }
		}

		private static string PlayerName(Player p)
		{
			try { var n = p.Name.Value.ToString(); if (!string.IsNullOrEmpty(n)) return n; } catch { }
			return "adventurer";
		}

		// The chat component of a connected player's object. A ClientRpc sent from it reaches
		// every client that can see that player, which is every client.
		private static ChatSystem ChatOf(ulong clientId, out Player player)
		{
			player = null;
			try
			{
				var nm = NetworkManager.Singleton;
				if (nm == null) return null;
				var po = nm.ConnectedClients[clientId]?.PlayerObject;
				if (po == null || !po.IsSpawned) return null;
				player = po.GetComponent<Player>();
				return po.GetComponent<ChatSystem>();
			}
			catch { return null; }
		}

		private static bool Send(ChatSystem chat, string text)
		{
			if (chat == null || string.IsNullOrWhiteSpace(text)) return false;
			chat.CreateServerNetworkMessage(text.Trim());
			return true;
		}

		internal static void Tick()
		{
			if (!OnServer()) return;
			float now = Time.realtimeSinceStartup;
			if (Pending.Count > 0)
			{
				var due = new List<ulong>();
				foreach (var kv in Pending) if (now >= kv.Value) due.Add(kv.Key);
				foreach (var id in due)
				{
					Pending.Remove(id);
					var text = MotdAnnouncePlugin.Motd.Value ?? "";
					if (string.IsNullOrWhiteSpace(text)) continue;
					Player p;
					var chat = ChatOf(id, out p);
					if (chat == null) { MotdAnnouncePlugin.Logger.LogInfo("[motd] client " + id + " has no player object yet, retrying in 3 s"); Pending[id] = now + 3f; continue; }
					var line = text.Replace("{player}", PlayerName(p));
					if (Send(chat, line)) MotdAnnouncePlugin.Logger.LogInfo("[motd] sent to client " + id + " (" + PlayerName(p) + "): '" + line + "'");
				}
			}
			var ann = MotdAnnouncePlugin.Announcement.Value ?? "";
			if (string.IsNullOrWhiteSpace(ann)) return;
			int every = Mathf.Clamp(MotdAnnouncePlugin.AnnounceEveryMinutes.Value, 1, 1440);
			if (sNextAnnounce < 0f) sNextAnnounce = now + every * 60f;
			if (now < sNextAnnounce) return;
			sNextAnnounce = now + every * 60f;
			try
			{
				foreach (var id in new List<ulong>(Connected))
				{
					if (id == NetworkManager.ServerClientId) continue;
					Player p;
					var chat = ChatOf(id, out p);
					if (chat == null) continue;
					if (Send(chat, ann)) MotdAnnouncePlugin.Logger.LogInfo("[motd] announcement sent: '" + ann.Trim() + "'");
					return;
				}
				MotdAnnouncePlugin.Logger.LogInfo("[motd] announcement skipped: nobody connected");
			}
			catch (Exception e) { MotdAnnouncePlugin.Logger.LogWarning("[motd] announcement: " + e.Message); }
		}
	}

	[HarmonyPatch(typeof(CustomNetworkManager), "OnClientConnected")]
	internal static class JoinPatch
	{
		private static void Postfix(ulong clientId)
		{
			try
			{
				var nm = NetworkManager.Singleton;
				if (nm == null || !nm.IsServer || clientId == NetworkManager.ServerClientId) return;
				int delay = Mathf.Clamp(MotdAnnouncePlugin.JoinDelaySeconds.Value, 3, 60);
				Sender.Connected.Add(clientId);
				Sender.Pending[clientId] = Time.realtimeSinceStartup + delay;
			}
			catch (Exception e) { MotdAnnouncePlugin.Logger.LogWarning("[motd] join: " + e.Message); }
		}
	}

	[HarmonyPatch(typeof(CustomNetworkManager), "OnClientDisconnected")]
	internal static class LeavePatch
	{
		private static void Postfix(ulong clientId)
		{
			try { Sender.Pending.Remove(clientId); Sender.Connected.Remove(clientId); } catch { }
		}
	}

	// A tick that exists in every scene the game runs: the menu manager's Update.
	[HarmonyPatch(typeof(TransitionManager), "Update")]
	internal static class TickPatch
	{
		private static void Postfix()
		{
			try { Sender.Tick(); }
			catch (Exception e) { MotdAnnouncePlugin.Logger.LogWarning("[motd] tick: " + e.Message); }
		}
	}
}
