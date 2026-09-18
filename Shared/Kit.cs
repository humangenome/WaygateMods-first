using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Unity.Netcode;
using UnityEngine;

namespace WaygateMods.Kit
{
	// Shared by the server mods in this repository. It is compiled into each mod (a mod zip
	// carries one dll), so there is no shared assembly to install.
	//
	// What it gives a mod:
	//   - patches applied one by one, so a game method that is missing after a game update
	//     switches one feature off instead of failing the whole mod
	//   - a fail-closed switch: after repeated errors the mod removes its patches, writes one
	//     plain line and stays off until the next start. It never stops the server.
	//   - chat: a server line to everyone, or a line only one player sees
	//   - two log levels with a rule: Info and Warning lines are written for the server owner
	//     in plain words (hosting panels show them in the console), Debug lines are for
	//     whoever is reading BepInEx\LogOutput.log with Debug enabled
	internal static class ModKit
	{
		private static ManualLogSource sLog;
		private static Harmony sHarmony;
		private static string sName = "Mod";
		private static int sErrors;
		private static float sErrorWindowStart;
		private const int MaxErrors = 5;
		private const float ErrorWindowSeconds = 300f;

		internal static bool Off { get; private set; }

		internal static void Init(ManualLogSource log, Harmony harmony, string displayName)
		{
			sLog = log; sHarmony = harmony; sName = displayName;
		}

		// A line for the server owner. Plain words only.
		internal static void Say(string text) { try { sLog.LogInfo(text); } catch { } }
		// A line for a developer. Not shown in a hosting panel's console.
		internal static void Dbg(string text) { try { sLog.LogDebug(text); } catch { } }

		// An error inside a patch or the tick. Five inside five minutes turns the mod off.
		internal static void Fail(string where, Exception e)
		{
			Dbg("error in " + where + ": " + (e != null ? e.ToString() : "?"));
			float now = 0f; try { now = Time.realtimeSinceStartup; } catch { }
			if (now - sErrorWindowStart > ErrorWindowSeconds) { sErrorWindowStart = now; sErrors = 0; }
			sErrors++;
			if (sErrors >= MaxErrors) TurnOff("it hit repeated errors, most likely after a game update");
		}

		internal static void TurnOff(string plainReason)
		{
			if (Off) return;
			Off = true;
			try { if (sHarmony != null) sHarmony.UnpatchSelf(); } catch { }
			try { sLog.LogWarning(sName + " turned itself off: " + plainReason + ". The server keeps running without it."); } catch { }
		}

		// Patch one game method. Returns false when the method does not exist in this game
		// build or the patch does not apply; a required patch that fails turns the mod off.
		internal static bool Patch(Type type, string method, Type[] args, Type patchClass, string prefix, string postfix, bool required, string feature)
		{
			if (Off) return false;
			try
			{
				MethodBase target = args != null ? AccessTools.Method(type, method, args) : AccessTools.Method(type, method);
				if (target == null) throw new MissingMethodException(type.Name + "." + method);
				HarmonyMethod pre = prefix != null ? new HarmonyMethod(AccessTools.Method(patchClass, prefix)) : null;
				HarmonyMethod post = postfix != null ? new HarmonyMethod(AccessTools.Method(patchClass, postfix)) : null;
				if (pre != null && prefix != null && pre.method == null) throw new MissingMethodException(patchClass.Name + "." + prefix);
				if (post != null && postfix != null && post.method == null) throw new MissingMethodException(patchClass.Name + "." + postfix);
				sHarmony.Patch(target, pre, post);
				Dbg("patched " + type.Name + "." + method);
				return true;
			}
			catch (Exception e)
			{
				Dbg("patch " + type.Name + "." + method + " failed: " + e.Message);
				if (required) TurnOff("this game version changed something it depends on (" + feature + ")");
				else try { sLog.LogWarning(sName + ": " + feature + " is not available on this game version and was left off."); } catch { }
				return false;
			}
		}

		internal static bool OnServer()
		{
			try { var nm = NetworkManager.Singleton; return nm != null && nm.IsServer && nm.IsListening; } catch { return false; }
		}

		// ---- players -----------------------------------------------------------------------
		// The real players on the server: the game's own list, minus anything that is not a
		// spawned object owned by a remote client (the server keeps no character of its own).
		internal static List<Player> Players()
		{
			var list = new List<Player>();
			try
			{
				var vm = VisibilityManager.Singleton;
				var all = vm != null ? vm.ConnectedPlayers : null;
				if (all == null) return list;
				for (int i = 0; i < all.Count; i++)
				{
					var p = all[i];
					if (p == null) continue;
					try { if (!p.IsSpawned || p.OwnerClientId == NetworkManager.ServerClientId) continue; } catch { continue; }
					list.Add(p);
				}
			}
			catch (Exception e) { Dbg("players: " + e.Message); }
			return list;
		}

		internal static Player PlayerOf(ulong clientId)
		{
			if (clientId == NetworkManager.ServerClientId) return null;
			foreach (var p in Players())
			{
				try { if (p.OwnerClientId == clientId) return p; } catch { }
			}
			return null;
		}

		internal static string NameOf(Player p)
		{
			try { var n = p.Name.Value.ToString(); if (!string.IsNullOrEmpty(n)) return n; } catch { }
			return "";
		}

		internal static string HashOf(Player p)
		{
			try { var h = p.Hash.Value.ToString(); if (!string.IsNullOrEmpty(h)) return h; } catch { }
			return "";
		}

		internal static Player FindByName(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) return null;
			name = name.Trim();
			Player partial = null; int partials = 0;
			foreach (var p in Players())
			{
				var n = NameOf(p);
				if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return p;
				if (n.StartsWith(name, StringComparison.OrdinalIgnoreCase)) { partial = p; partials++; }
			}
			return partials == 1 ? partial : null;
		}

		// ---- chat --------------------------------------------------------------------------
		// A ClientRpc sent from a connected player's chat component reaches every client. The
		// server's own objects are not visible to clients, so nothing is sent from those.
		private static ChatSystem AnyChat()
		{
			foreach (var p in Players())
			{
				try { var cs = p.GetComponent<ChatSystem>(); if (cs != null) return cs; } catch { }
			}
			return null;
		}

		private static Message NewMessage(string text)
		{
			var m = new Message();
			m.Text = text ?? "";
			m.Username = "Server";
			m.Color = Color.white;
			m.Bold = false;
			m.MsgType = ChatMessageType.Server;
			m.Locale = ChatLocale.Everyone;
			m.Source = NetworkManager.ServerClientId;
			m.Destination = 0;
			try { m.LocalizationKey = ""; } catch { }
			try { m.LocalizationArgs = new Il2CppStringArray(0); } catch { }
			return m;
		}

		// Chat leaves through an outbox that the mod's tick empties. A chat command is read
		// inside the game's handling of the player's own chat RPC, and a ClientRpc called on the
		// same chat component at that moment runs on the server instead of being sent (the
		// component is still marked as executing an RPC). One frame later it is sent normally.
		private struct Outgoing { internal bool ToAll; internal ulong ClientId; internal string Text; }
		private static readonly Queue<Outgoing> sOutbox = new Queue<Outgoing>();

		// A [Server] line on every player's screen.
		internal static void Broadcast(string text)
		{
			if (string.IsNullOrWhiteSpace(text) || Off) return;
			if (sOutbox.Count < 200) sOutbox.Enqueue(new Outgoing { ToAll = true, Text = text.Trim() });
		}

		// A [Server] line only one player sees. The game filters a Direct message on each
		// client: it is shown when the receiving player is its source or its destination, so
		// both are set to the recipient. Every other client drops it.
		internal static void Whisper(ulong clientId, string text, bool toEveryoneInstead)
		{
			if (toEveryoneInstead) { Broadcast(text); return; }
			if (string.IsNullOrWhiteSpace(text) || Off) return;
			if (sOutbox.Count < 200) sOutbox.Enqueue(new Outgoing { ToAll = false, ClientId = clientId, Text = text.Trim() });
		}

		// ---- settings --------------------------------------------------------------------
		// A hosting panel rewrites the mod's cfg file when the owner changes a setting. The file
		// is looked at every few seconds and read again when it changed, so most settings apply
		// without a restart.
		private static ConfigFile sConfig;
		private static DateTime sConfigStamp;
		private static float sNextConfigLook;

		internal static void WatchConfig(ConfigFile config)
		{
			sConfig = config;
			try { sConfigStamp = System.IO.File.GetLastWriteTimeUtc(config.ConfigFilePath); } catch { }
		}

		private static bool sConfigChanging;

		private static void LookAtConfig()
		{
			if (sConfig == null) return;
			float now = Time.realtimeSinceStartup;
			if (now < sNextConfigLook) return;
			sNextConfigLook = now + 4f;
			try
			{
				var stamp = System.IO.File.GetLastWriteTimeUtc(sConfig.ConfigFilePath);
				// A writer may touch the file more than once. Read it when it has stopped changing.
				if (stamp != sConfigStamp) { sConfigStamp = stamp; sConfigChanging = true; return; }
				if (!sConfigChanging) return;
				sConfigChanging = false;
				sConfig.Reload();
				// Reading the file makes BepInEx write it back (it adds the keys the writer left
				// out). That write is ours, not a new change.
				sConfigStamp = System.IO.File.GetLastWriteTimeUtc(sConfig.ConfigFilePath);
				Say(sName + " picked up new settings.");
			}
			catch (Exception e) { Dbg("settings reload: " + e.Message); }
		}

		// Call from the mod's tick, every frame. Sends what is waiting.
		internal static void Pump()
		{
			if (Off) return;
			LookAtConfig();
			if (sOutbox.Count == 0) return;
			try
			{
				int budget = 8;
				while (sOutbox.Count > 0 && budget-- > 0)
				{
					var o = sOutbox.Dequeue();
					if (o.ToAll)
					{
						var cs = AnyChat();
						if (cs == null) { Dbg("chat line dropped, nobody is connected: " + o.Text); continue; }
						cs.AcceptServerChatMessageClientRpc(NewMessage(o.Text), false);
						continue;
					}
					var p = PlayerOf(o.ClientId);
					var chat = p != null ? p.GetComponent<ChatSystem>() : null;
					if (chat == null) { Dbg("reply dropped, client " + o.ClientId + " has left"); continue; }
					var m = NewMessage(o.Text);
					m.Locale = ChatLocale.Direct;
					m.Source = o.ClientId;
					m.Destination = o.ClientId;
					chat.AcceptServerChatMessageClientRpc(m, false);
				}
			}
			catch (Exception e) { Fail("chat send", e); }
		}

		// ---- chat commands -----------------------------------------------------------------
		// True when the message is a player's typed line that starts with '/'. The game keeps
		// /party and /reply for itself; a mod never claims those.
		internal static bool IsCommand(Message message, out string verb, out string rest, out ulong source)
		{
			verb = ""; rest = ""; source = 0;
			try
			{
				if (message == null) return false;
				if (message.MsgType != ChatMessageType.Player) return false;
				source = message.Source;
				if (source == NetworkManager.ServerClientId) return false;
				var text = (message.Text ?? "").Trim();
				if (text.Length < 2 || text[0] != '/') return false;
				int sp = text.IndexOf(' ');
				verb = (sp < 0 ? text.Substring(1) : text.Substring(1, sp - 1)).ToLowerInvariant();
				rest = sp < 0 ? "" : text.Substring(sp + 1).Trim();
				return verb.Length > 0;
			}
			catch { return false; }
		}

		// ---- clock -------------------------------------------------------------------------
		internal static bool TryClock(out bool isNight, out GameTime time)
		{
			isNight = false; time = null;
			try
			{
				var tm = TimeManager.Singleton;
				if (tm == null) return false;
				time = tm.CurrentGameTime;
				if (time == null) return false;
				isNight = tm.IsNight;
				return true;
			}
			catch { return false; }
		}
	}
}
